using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;
using G = SkyEye.GameState;

static class GameIntegrationChecks
{
    public static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "skyeye-game-app-" + Guid.NewGuid().ToString("N"));
        try
        {
            var clock = new Clock(); var frames = new Frames(clock); var vision = new Vision();
            using var table = new TableAnalysisSession(frames, vision, new Calibration(), clock);
            var store = new GameJournalStore(root);
            var app = new AppCoordinator(cameraControls: new Camera(), observations: new TableObservationEffect(() => table, () => 1, () => true), gameJournal: store);
            using var binding = app.Subscribe(s => table.Bind(s.Camera, "open"));
            await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, app.State.Version, new ConnectCamera("camera:8765")));
            table.RestoreCalibration();
            G.GameInputEnvelope Input(G.GameInput input) => new(Guid.NewGuid(), "match", "frame", clock.GetUtcNow(), input, Provenance: G.Provenance.PlayerConfirmed);
            async Task<CommandReceipt> Send(G.GameInputEnvelope input) => await app.DispatchAsync(new(input.Id, CommandOrigin.Touch, app.State.Version, new SubmitGameInput(input)));
            var match = Input(new G.StartMatch([new("rich", "Rich"), new("mike", "Mike")], 3));
            Check((await Send(match)).Succeeded, "Host declares match through the existing application writer");
            Check((await Send(Input(new G.StartFrame("rich")))).Succeeded, "Frame starts through typed domain command");
            async Task Observe() => Check((await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, app.State.Version,
                new ObserveTable(table.State.Source!, table.State.Generation, 1)))).Succeeded, "Requested observation remains successful");
            await Observe(); clock.Advance(3); await Observe();
            var actual = app.State.Observation!;
            Check(actual.GameEvidence?.Balls.Length == 2 && actual.GameEvidence.Source.Calibration == table.State.CalibrationId,
                "Observation effect captures matching immutable balls, calibration and timestamp");
            Check(app.State.Game.Table.Remaining(G.BallColour.Yellow) == 1 && app.State.GameTableCurrent,
                "Existing settled analysis updates canonical game projection");
            var yellow = app.State.Game.Table.Balls.Values.Single(b => b.Colour == G.BallColour.Yellow);
            Check(yellow.Position == new G.TablePosition(.5, .5), "Application's 2:1 coordinate space translates to the normalized domain rectangle");
            var stableId = yellow.Id;
            clock.Advance(3); await Observe();
            Check(app.State.Game.Table.Balls.ContainsKey(stableId), "Unchanged uniquely associated layout retains identity");
            vision.Shift = true; clock.Advance(3); await Observe();
            Check(!app.State.GameTableCurrent && app.State.Game.Table.Uncertain && app.State.Game.Table.Remaining(G.BallColour.Yellow) is null,
                "Untracked movement stays ambiguous rather than inventing ball identity or pot");
            vision.Unknown = true; clock.Advance(3); await Observe();
            Check(app.State.Observation!.GameEvidence!.Balls.Any(b => b.Colour == G.BallColour.Unknown), "Unknown CV colours do not fail the observation effect");
            vision.Unknown = false; vision.Shift = false; clock.Advance(3); await Observe(); clock.Advance(3); await Observe();
            var rebuilt = new AppCoordinator(cameraControls: new Camera(), gameJournal: store);
            Check(JsonSerializer.Serialize(rebuilt.State.Game) == JsonSerializer.Serialize(app.State.Game) && !rebuilt.State.GameTableCurrent,
                "Restart restores match history but never marks recovered imagery as current live state");
            await rebuilt.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, 0, new ConnectCamera("camera:8765")));
            var retry = await rebuilt.DispatchAsync(new(match.Id, CommandOrigin.Touch, 0, new SubmitGameInput(match)));
            Check(retry.Succeeded && retry.Duplicate, "Durable game input duplicate survives a changed app version/restart");
            var failed = new AppCoordinator(cameraControls: new Camera(), gameJournal: new FailedJournal());
            await failed.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, 0, new ConnectCamera("camera:8765")));
            var version = failed.State.Version;
            var refusal = await failed.DispatchAsync(new(match.Id, CommandOrigin.Touch, version, new SubmitGameInput(match)));
            Check(!refusal.Succeeded && failed.State.Game.Match is null && failed.State.Version == version, "Failed journal write publishes no canonical transition");
            var stale = Input(new G.PlayerCorrection(new G.ChangeTurn("mike")));
            Check((await app.DispatchAsync(new(stale.Id, CommandOrigin.Touch, 0, new SubmitGameInput(stale)))).Outcome == CommandOutcome.Stale,
                "Stale application command cannot mutate game state");
            Check((await app.DispatchAsync(new(stale.Id, CommandOrigin.Vision, app.State.Version, new SubmitGameInput(stale)))).Outcome == CommandOutcome.Rejected,
                "Perception cannot impersonate player correction");
            Check((await new AppCoordinator().DispatchAsync(new(match.Id, CommandOrigin.Touch, 0, new SubmitGameInput(match)))).Outcome == CommandOutcome.Rejected,
                "Guest/unowned app cannot mutate game");
            Console.WriteLine("PASS game integration: matching existing observations, normalized positions, conservative identities, unknown colours, host/stale fences, restart freshness and failed-save atomicity");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    sealed class Clock : TimeProvider
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(int seconds) => now = now.AddSeconds(seconds);
    }
    sealed class Frames(Clock clock) : ITableFrameSource
    {
        public Task<TableFrame> ReadAsync(CameraScope source, CancellationToken cancellation)
            => Task.FromResult(new TableFrame([1], clock.GetUtcNow(), "fixture") { SourceTimeUs = clock.GetUtcNow().ToUnixTimeMilliseconds() * 1000 });
    }
    sealed class Vision : ITableAnalyzer
    {
        public bool Shift, Unknown;
        public VisionResult Analyze(byte[] jpeg, TableCalibration calibration, CancellationToken cancellation = default)
            => new([new("transient-cue", "cue", new(.4, .5), .9), new("transient-yellow", Unknown ? "unknown" : "yellow", new(Shift ? 1.4 : 1, .5), .9)], Unknown, [], 1, 2, true, "fixture", [], 1, false, []);
        public Point[]? FindCorners(byte[] jpeg) => null;
    }
    sealed class Calibration : ICalibrationStore
    {
        public Point[]? Load(string cameraIdentity) => [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        public void Save(string cameraIdentity, Point[] corners) { }
    }
    sealed class Camera : ICameraControlEffect
    {
        public Task<CameraControlResult> ExecuteAsync(Guid id, CameraCommand command, CameraConnection admitted, CancellationToken cancellation)
            => Task.FromResult(new CameraControlResult(admitted with { Reachability = CameraReachability.Reachable, Armed = true, Capture = CaptureState.Active,
                Ownership = CameraOwnership.Owned, HostOwner = "fixture", HostGeneration = 1, SessionId = Guid.NewGuid().ToString("N"), CaptureRevision = 1 }, CommandOutcome.Applied, "Fixture"));
    }
    sealed class FailedJournal : IGameJournal
    {
        public G.GameSession Load() => G.GameSession.Empty;
        public void Save(G.GameSession previous, G.GameSession next) => throw new IOException("Fixture storage failure");
    }
}
