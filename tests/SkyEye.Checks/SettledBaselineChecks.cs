using System.Collections.Immutable;
using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;
using G = SkyEye.GameState;

static class SettledBaselineChecks
{
    public static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "skyeye-baseline-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new GameJournalStore(root);
            var journal = new Journal(store);
            var effect = new Observation();
            var app = new AppCoordinator(cameraControls: new Camera(), observations: effect, gameJournal: journal);
            await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, 0, new ConnectCamera("camera:8765")));
            async Task<CommandReceipt> Send(G.GameInput input, G.EvidenceSource? source = null, long? pts = null,
                CommandOrigin origin = CommandOrigin.Touch)
            {
                var id = Guid.NewGuid();
                return await app.DispatchAsync(new(id, origin, app.State.Version, new SubmitGameInput(
                    new(id, "match", "frame", DateTimeOffset.UtcNow, input, source, pts))));
            }
            Check((await Send(new G.StartMatch([new("rich", "Rich"), new("mike", "Mike")], 3))).Succeeded, "Start baseline fixture match");
            Check((await Send(new G.StartFrame("rich"))).Succeeded, "Start baseline fixture frame");
            var oldSource = new G.EvidenceSource(app.State.Camera.CameraIdentity!, "fixture", 1, "old-calibration", app.State.Camera.ConnectionId.ToString("N"));
            Check((await Send(new G.TableObservation([new("old-yellow", G.BallColour.Yellow, new(.1, .1), .9),
                new("old-cue", G.BallColour.Cue, new(.2, .1), .9)], true), oldSource, 1)).Succeeded, "Seed legacy physical identities");
            var frame = app.State.Game.Frame;
            async Task<CommandReceipt> Observe() => await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, app.State.Version,
                new ObserveTable(app.State.Camera.Scope!, 1, 1, Mode: ObservationMode.VerifyForAdvice)));
            var oldVersion = app.State.Game.Version;
            journal.Fail = true;
            await Observe();
            Check(app.State.Game.Version == oldVersion && app.State.Game.Table.Source == oldSource && app.State.GameError is not null,
                "Failed save publishes neither source selection nor baseline");
            journal.Fail = false;
            var first = await Observe();
            Check(first.Succeeded && app.State.GameTableCurrent && G.SettledTableEvidence.TryCapture(app.State.Game, out var baseline)
                && baseline.Counts == new G.SettledTableCounts(0, 1, 1, 0), $"Verified fresh source establishes counts despite changed old positions: {first.Outcome}, {first.Message}, {app.State.GameError}");
            Check(app.State.Game.Frame == frame && app.State.Game.Frame!.Pots.Count == 0 && app.State.Game.Shot is null,
                "Starting physical inventory declares no pot, turn, foul, group or score");
            Check(app.State.Game.Table.Balls.Values.All(b => b.Position is null
                && (b.Colour == G.BallColour.Cue || b.Presence.Value == G.BallPresence.Unknown)),
                "Old same-colour identities and positions are not assigned to new sightings");
            var saved = store.Load();
            Check(saved.History.Any(e => e.Event is G.SettledBaselineEstablishedEvent && e.Provenance == G.Provenance.Observed)
                && JsonSerializer.Serialize(saved.State) == JsonSerializer.Serialize(app.State.Game), "Observed baseline is durable and rebuilds identically");
            var rebuilt = new AppCoordinator(gameJournal: store);
            Check(!rebuilt.State.GameTableCurrent && G.SettledTableEvidence.TryCapture(rebuilt.State.Game, out _),
                "Restart retains a comparison anchor but never claims old imagery is live");
            var history = saved.History;
            var established = history.Last(e => e.Event is G.SettledBaselineEstablishedEvent);
            var establishedEvent = (G.SettledBaselineEstablishedEvent)established.Event;
            var duplicate = saved.Accept(new(established.InputId, established.MatchId, established.FrameId, established.At,
                new G.EstablishSettledBaseline(1, establishedEvent.Evidence), established.Source, established.SourceTimeUs));
            Check(duplicate.Outcome == G.InputOutcome.Duplicate, "Baseline retries do not apply twice");
            var overwrite = saved.Accept(new(Guid.NewGuid(), "match", "frame", DateTimeOffset.UtcNow,
                new G.EstablishSettledBaseline(1, establishedEvent.Evidence), established.Source, established.SourceTimeUs));
            Check(!overwrite.Accepted, "An existing trusted A cannot be replaced by baseline establishment");
            Check(!(await Send(new G.EstablishSettledBaseline(1, establishedEvent.Evidence), established.Source,
                established.SourceTimeUs, CommandOrigin.Sky)).Succeeded, "A voice command cannot impersonate verified starting evidence");
            var beforeRefresh = app.State.Game;
            Check((await Observe()).Succeeded && app.State.GameTableCurrent && app.State.Game.Version == beforeRefresh.Version
                && app.State.Game.Table.LastSettled == beforeRefresh.Table.LastSettled,
                "An unchanged check confirms current counts without replacing A or writing duplicate observations");
            effect.YellowPresent = false;
            var before = app.State.Game;
            Check((await Observe()).Succeeded && app.State.Game.Version == before.Version && app.State.Game.Table.LastSettled == before.Table.LastSettled
                && app.State.Game.Table.Remaining(G.BallColour.Yellow) == 1 && !app.State.GameTableCurrent,
                "A later count change holds A for outcome review instead of silently changing canonical inventory");
            var invalid = history.SetItem(history.IndexOf(established), established with
            { Event = establishedEvent with { Evidence = establishedEvent.Evidence with { BoundedStillnessVerified = false } } });
            Throws(() => G.GameSession.Restore(invalid));
            Console.WriteLine("PASS settled baseline: source-reset counts, no invented identities/outcomes, durable replay/duplicates, trusted-A hold and proof/origin fences");
        }
        finally
        {
            if (Path.GetDirectoryName(Path.GetFullPath(root)) != Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)
                || !Path.GetFileName(root).StartsWith("skyeye-baseline-")) throw new InvalidOperationException("Unsafe test cleanup path.");
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Throws(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected invalid baseline refusal"); }
    sealed class Observation : ITableObservationEffect
    {
        public bool YellowPresent = true;
        int sequence;
        public bool IsCurrent(ObserveTable request) => true;
        public Task<TableObservation> ExecuteAsync(ObserveTable request, CancellationToken cancellation) => ExecuteAsync(request, Guid.NewGuid(), cancellation);
        public Task<TableObservation> ExecuteAsync(ObserveTable request, Guid operationId, CancellationToken cancellation)
        {
            var pts = ++sequence * 10_000_000L;
            var now = DateTimeOffset.UtcNow;
            var balls = new List<Ball> { new("detected-cue", "cue", new(1.4, .7), .9) };
            if (YellowPresent) balls.Add(new("detected-yellow", "yellow", new(1.2, .8), .9));
            var vision = new VisionResult(balls.ToArray(), false, [], 1, balls.Count, true, "fixture", [], 1, false, []);
            var samples = Enumerable.Range(0, 5).Select(i => new VerificationSample(sequence * 10 + i, pts + i * 500000,
                1, 1, 1, 1, 1, new(TableMotion.Still, ObservationReason.None, true, 0, 0, 0, vision)) { CapturedAt = now }).ToArray();
            var evidence = new VerificationEvidence(operationId, ObservationPolicy.VerificationConfiguration, "new-calibration", "open",
                1, pts, samples[^1].SourceTimeUs, 5, 500, 1, samples);
            return Task.FromResult(new TableObservation(request.Source, 1, 1, "fixture", now, samples[^1].SourceTimeUs,
                1, 1, 1, 1, sequence, ObservationOutcome.Ready, "fixture")
            {
                Motion = TableMotion.Still, Quality = LayoutQuality.Clear, Reason = ObservationReason.None, Verification = evidence,
                DecodedReceipt = new(1, samples[^1].FrameId, samples[^1].SourceTimeUs, sequence, 640, 360),
                DetectionEvidence = new(samples[^1].FrameId, samples[^1].SourceTimeUs, 1),
                GameEvidence = new(new(request.Source.CameraIdentity!, "fixture", 2, "new-calibration", request.Source.ConnectionId.ToString("N")),
                    balls.Select(b => new G.ObservedBall(b.Id, b.Kind == "cue" ? G.BallColour.Cue : G.BallColour.Yellow,
                        new(b.Position.X / 2, b.Position.Y), b.Confidence)).ToImmutableArray())
            });
        }
    }
    sealed class Camera : ICameraControlEffect
    {
        public Task<CameraControlResult> ExecuteAsync(Guid id, CameraCommand command, CameraConnection admitted, CancellationToken cancellation)
            => Task.FromResult(new CameraControlResult(admitted with { Reachability = CameraReachability.Reachable, Armed = true, Capture = CaptureState.Active,
                Ownership = CameraOwnership.Owned, HostOwner = "fixture", HostGeneration = 1, SessionId = Guid.NewGuid().ToString("N"), CaptureRevision = 1 }, CommandOutcome.Applied, "Fixture"));
    }
    sealed class Journal(GameJournalStore store) : IGameJournal
    {
        public bool Fail;
        public G.GameSession Load() => store.Load();
        public void Save(G.GameSession previous, G.GameSession next)
        { if (Fail) throw new IOException("Fixture save failure"); store.Save(previous, next); }
    }
}
