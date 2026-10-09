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
            Check(app.State.Observation.CapturedAt > table.State.Table!.CapturedAt && app.State.Game.Table.Uncertain,
                "Fresh unclear evidence retains the previous layout timestamp without claiming a current clear table");
            vision.Unknown = false; vision.Shift = false; clock.Advance(3); await Observe(); clock.Advance(3); await Observe();
            // The future tracker supplies stable shot/pocket identities through this typed seam.
            // This sequence is declared evidence, not a claim that today's CV detects pots.
            var gameSource = app.State.Game.Table.Source!;
            var yellowId = app.State.Game.Table.Balls.Values.Single(b => b.Colour == G.BallColour.Yellow).Id;
            async Task Signal(G.GameInput signal, double? confidence = null)
            {
                clock.Advance(1);
                var evidence = Input(signal) with { Source = gameSource, SourceTimeUs = clock.GetUtcNow().ToUnixTimeMilliseconds() * 1000,
                    Provenance = G.Provenance.Observed, Confidence = confidence };
                Check((await Send(evidence)).Succeeded, "Declared tracker evidence uses the shared application command surface");
            }
            await Signal(new G.ShotStarted("declared-shot"));
            Check(!app.State.GameTableCurrent, "Motion invalidates live settled-table projection");
            await Signal(new G.PossiblePocketEvent("declared-shot", yellowId, G.Pocket.BottomRight), .9);
            await Signal(new G.ShotEnded("declared-shot"));
            var cue = app.State.Game.Table.Balls.Values.Single(b => b.Colour == G.BallColour.Cue);
            await Signal(new G.TableObservation([new(cue.Id, cue.Colour, cue.Position, .9)], true));
            Check(app.State.Game.Frame!.Pots.Count == 1 && app.State.Game.Table.Remaining(G.BallColour.Yellow) == 0,
                "Full application vertical slice records the declared tracker pot");
            var pot = store.Load().History.Single(e => e.Event is G.BallPottedEvent);
            Check((await Send(Input(new G.PlayerCorrection(new G.SetBallPresence(yellowId, G.BallPresence.OnTable, pot.EventId))))).Succeeded,
                "Application correction appends its amendment durably");
            Check(app.State.Game.Frame.Pots.Count == 0 && app.State.Game.Table.Remaining(G.BallColour.Yellow) == 1,
                "Canonical application state repairs the declared pot/visit");
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
            Check((await new AppCoordinator().DispatchAsync(new(match.Id, CommandOrigin.Touch, 0, new SubmitGameInput(match)))).Succeeded,
                "A declared local match can start independently of camera connection");
            Check(!app.State.HasFreshGameTable(clock.GetUtcNow().AddSeconds(7)), "A retained table cannot claim fresh observation indefinitely");
            frames.Hold();
            var pending = Observe(); await frames.Entered.Task;
            Check((await Send(Input(new G.PlayerCorrection(new G.SetFrameWinner("rich"))))).Succeeded, "Resolve the previous frame while observation is pending");
            var nextFrame = Input(new G.StartFrame("mike")) with { FrameId = "next-frame" };
            Check((await Send(nextFrame)).Succeeded, "Start a new frame without erasing match score");
            var newFrameVersion = app.State.Game.Version;
            frames.Release.TrySetResult(); await pending;
            Check(app.State.Game.Frame!.Id == "next-frame" && app.State.Game.Table.Balls.Count == 0 && app.State.Game.Version == newFrameVersion,
                "Observation admitted for the previous frame cannot seed a replacement frame");
            Console.WriteLine("PASS game integration: matching existing observations, normalized positions, conservative identities, unknown colours, host/stale fences, restart freshness and failed-save atomicity");
        }
        finally
        {
            if (Path.GetDirectoryName(Path.GetFullPath(root)) != Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)
                || !Path.GetFileName(root).StartsWith("skyeye-game-app-")) throw new InvalidOperationException("Unsafe test cleanup path.");
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
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
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        bool held;
        public void Hold() => held = true;
        public async Task<TableFrame> ReadAsync(CameraScope source, CancellationToken cancellation)
        {
            if (held) { Entered.TrySetResult(); await Release.Task.WaitAsync(cancellation); }
            return new([1], clock.GetUtcNow(), "fixture") { SourceTimeUs = clock.GetUtcNow().ToUnixTimeMilliseconds() * 1000 };
        }
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
