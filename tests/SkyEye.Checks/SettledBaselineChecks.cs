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
            var effect = new Observation { PackedRack = true };
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
                && baseline.Counts == new G.SettledTableCounts(7, 7, 1, 1), $"Verified fresh full inventory establishes counts despite changed old positions: {first.Outcome}, {first.Message}, {app.State.GameError}");
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
                && app.State.Game.Table.Remaining(G.BallColour.Yellow) == 7 && !app.State.GameTableCurrent,
                "A later count change holds A for outcome review instead of silently changing canonical inventory");
            if (!G.SettledTableEvidence.TryCapture(before, out var liveBefore)
                || !G.ActivityWindowContext.TryBegin(Guid.NewGuid(), before, liveBefore.SourceTimeUs!.Value + 1, out var liveContext))
                throw new Exception("The live app retains a current A and frozen review context.");
            var liveAfter = new G.SettledTableEvidence(Guid.NewGuid(), liveBefore.Source,
                liveBefore.SourceTimeUs!.Value + 20_000_000, DateTimeOffset.UtcNow,
                new(0, 0, 1, 0), Settled: true, Clear: true)
            { BoundedStillnessVerified = true, StableClearSampleCount = 3 };
            var acceptId = Guid.NewGuid();
            var acceptEnvelope = new G.GameInputEnvelope(acceptId, before.Match!.Id, before.Frame!.Id,
                liveAfter.CapturedAt.AddMilliseconds(1), new G.AcceptSettledInventory(1, liveContext, liveAfter),
                liveAfter.Source, liveAfter.SourceTimeUs, G.Provenance.PlayerConfirmed);
            var acceptReceipt = await app.DispatchAsync(new(acceptId, CommandOrigin.Touch, app.State.Version,
                new SubmitGameInput(acceptEnvelope, before.Version)));
            Check(acceptReceipt.Succeeded && app.State.GameTableCurrent
                && app.State.Game.Table.Remaining(G.BallColour.Yellow) == 0
                && store.Load().History.Any(item => item.Event is G.SettledInventoryAcceptedEvent),
                "AppCoordinator persists reviewed A/B before publishing the accepted current inventory.");
            var acceptedGame = app.CaptureGameSession();
            if (!G.ActivityWindowContext.TryBegin(Guid.NewGuid(), acceptedGame.State,
                    liveAfter.SourceTimeUs!.Value + 1, out var correctionContext))
                throw new Exception("A reviewed layout becomes the correction path's new trusted A.");
            var misreadAfter = new G.SettledTableEvidence(Guid.NewGuid(), liveAfter.Source,
                liveAfter.SourceTimeUs.Value + 20_000_000, DateTimeOffset.UtcNow,
                new(0, 1, 1, 0), Settled: true, Clear: true)
            { BoundedStillnessVerified = true, StableClearSampleCount = 3 };
            var correctionId = Guid.NewGuid();
            var correctionEnvelope = new G.GameInputEnvelope(correctionId, acceptedGame.State.Match!.Id,
                acceptedGame.State.Frame!.Id, misreadAfter.CapturedAt.AddMilliseconds(1),
                new G.CorrectSettledInventory(1, correctionContext, misreadAfter, new(0, 0, 1, 0)),
                misreadAfter.Source, misreadAfter.SourceTimeUs, G.Provenance.PlayerConfirmed);
            var correctionReceipt = await app.DispatchAsync(new(correctionId, CommandOrigin.Touch, app.State.Version,
                new SubmitGameInput(correctionEnvelope, acceptedGame.State.Version)));
            Check(correctionReceipt.Succeeded && app.State.GameTableCurrent
                && app.State.Game.Table.Remaining(G.BallColour.Yellow) == 0
                && app.State.Game.Table.LastSettled?.EvidenceId == correctionId
                && store.Load().History.Any(item => item.InputId == correctionId
                    && item.Event is G.SettledInventoryCorrectedEvent { Context: not null, Observed: not null }),
                "AppCoordinator saves a corrected count with the observed B evidence and marks the verified table current.");
            var invalid = history.SetItem(history.IndexOf(established), established with
            { Event = establishedEvent with { Evidence = establishedEvent.Evidence with { BoundedStillnessVerified = false } } });
            Throws(() => G.GameSession.Restore(invalid));
            Console.WriteLine("PASS settled baseline/review: source-reset counts, trusted-A hold, durable accept/correct with evidence, current-state publication and proof/origin fences");
            await PackedRackBaselineCheck();
        }
        finally
        {
            if (Path.GetDirectoryName(Path.GetFullPath(root)) != Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar)
                || !Path.GetFileName(root).StartsWith("skyeye-baseline-")) throw new InvalidOperationException("Unsafe test cleanup path.");
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    static async Task PackedRackBaselineCheck()
    {
        var effect = new Observation { PackedRack = true };
        var app = new AppCoordinator(cameraControls: new Camera(), observations: effect);
        await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, app.State.Version, new ConnectCamera("camera:8765")));
        G.GameInputEnvelope Input(G.GameInput input) => new(Guid.NewGuid(), "packed-match", "packed-frame",
            DateTimeOffset.UtcNow, input, Provenance: G.Provenance.PlayerConfirmed);
        async Task<CommandReceipt> Send(G.GameInput input)
        {
            var envelope = Input(input);
            return await app.DispatchAsync(new(envelope.Id, CommandOrigin.Touch, app.State.Version, new SubmitGameInput(envelope)));
        }
        Check((await Send(new G.StartMatch([new("rich", "Rich"), new("mike", "Mike")], 3))).Succeeded,
            "Start a separate packed-rack baseline match");
        Check((await Send(new G.StartFrame("rich"))).Succeeded, "Start the packed-rack baseline frame");
        var receipt = await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, app.State.Version,
            new ObserveTable(app.State.Camera.Scope!, 1, 1, Mode: ObservationMode.VerifyForAdvice)));
        Check(receipt.Succeeded && receipt.Observation is { Outcome: ObservationOutcome.Ready, Quality: LayoutQuality.Clear }
            && app.State.GameTableCurrent && G.SettledTableEvidence.TryCapture(app.State.Game, out var baseline)
            && baseline.Counts == new G.SettledTableCounts(7, 7, 1, 1),
            $"A verified packed-rack receipt publishes the observed complete aggregate baseline as current ({receipt.Outcome}: {receipt.Message}; observation {receipt.Observation?.Outcome}/{receipt.Observation?.Reason}; current {app.State.GameTableCurrent}; game error {app.State.GameError}; baseline {G.SettledTableEvidence.TryCapture(app.State.Game, out var debugBaseline)} {debugBaseline?.Counts})");
        Check(app.State.Game.Table.Balls.Count == 0 && app.State.Game.Shot is null
            && app.State.Game.Frame is { Pots.Count: 0 }
            && app.State.Game.Table.GroupCounts[G.BallColour.Red].Value == 7
            && app.State.Game.Table.GroupCounts[G.BallColour.Yellow].Value == 7,
            "The canonical baseline retains counts only; it creates no detector identities, pot, shot, or legal ruling");
        Console.WriteLine("PASS packed-rack baseline: clear/still inventory is canonical while planner geometry remains unavailable");
    }
    static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    static void Throws(Action action) { try { action(); } catch (ArgumentException) { return; } throw new Exception("Expected invalid baseline refusal"); }
    sealed class Observation : ITableObservationEffect
    {
        public bool YellowPresent = true;
        public bool PackedRack { get; init; }
        int sequence;
        public bool IsCurrent(ObserveTable request) => true;
        public Task<TableObservation> ExecuteAsync(ObserveTable request, CancellationToken cancellation) => ExecuteAsync(request, Guid.NewGuid(), cancellation);
        public Task<TableObservation> ExecuteAsync(ObserveTable request, Guid operationId, CancellationToken cancellation)
        {
            var pts = ++sequence * 10_000_000L;
            var now = DateTimeOffset.UtcNow;
            var balls = PackedRack ? PackedRackFixture.Balls.ToList()
                : new List<Ball> { new("detected-cue", "cue", new(1.4, .7), .9) };
            if (!PackedRack && YellowPresent) balls.Add(new("detected-yellow", "yellow", new(1.2, .8), .9));
            if (PackedRack && !YellowPresent)
            {
                var yellow = balls.FindLastIndex(ball => ball.Kind == "yellow");
                if (yellow >= 0) balls.RemoveAt(yellow);
            }
            bool incompleteEssentialInventory = balls.Count(ball => ball.Kind == "cue") != 1 || balls.Count(ball => ball.Kind == "black") != 1;
            var vision = new VisionResult(balls.ToArray(), incompleteEssentialInventory, [], 1, balls.Count, true, "fixture", [], 1, false, []);
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
                    balls.Select(b => new G.ObservedBall(b.Id, Enum.TryParse<G.BallColour>(b.Kind, true, out var colour) ? colour : G.BallColour.Unknown,
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
