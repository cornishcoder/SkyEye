using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;

static class ObservationCommandChecks
{
    public static async Task Run()
    {
        void Check(bool yes, string reason) { if (!yes) throw new Exception(reason); }
        var effect = new Observation(); var camera = new Camera(); var app = new AppCoordinator(cameraControls: camera, observations: effect);
        await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, 0, new ConnectCamera("camera:8765")));
        CommandEnvelope Request(CommandOrigin origin = CommandOrigin.Sky) => new(Guid.NewGuid(), origin, app.State.Version, new ObserveTable(app.State.Camera.Scope!, 1, 1));
        var request = Request(); var pending = app.DispatchAsync(request); var duplicate = app.DispatchAsync(request);
        await effect.Entered.Task;
        Check(app.State.Observation is null, "pending work is not a published observation");
        effect.Release.TrySetResult();
        var receipt = await pending;
        Check(receipt.Succeeded && receipt.Observation?.Outcome == ObservationOutcome.Unsettled && effect.Calls == 1 && (await duplicate).Duplicate,
            "one explicit request/duplicate performs one bounded observation and reports honest settlement");
        Check(app.State.Observation?.Timeline == "fixture" && receipt.Observation.DurationMs == 2 && receipt.Observation.FrameAgeMs == 1,
            "receipt preserves frame scope, age and timing");
        var replay = await app.DispatchAsync(request);
        Check(replay.Duplicate && effect.Calls == 1, "replayed request never re-observes");
        effect.Reset(); var late = app.DispatchAsync(Request(CommandOrigin.Touch)); await effect.Entered.Task;
        await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, app.State.Version, new ConnectCamera("other:8765")));
        effect.Release.TrySetResult();
        Check((await late).Outcome == CommandOutcome.Stale && app.State.Observation is null, "changed source rejects late publication");
        effect.Reset(); using var cancellation = new CancellationTokenSource();
        var cancelled = app.DispatchAsync(Request(), cancellation.Token); await effect.Entered.Task;
        cancellation.Cancel(); effect.Release.TrySetResult();
        Check(!(await cancelled).Succeeded && app.State.Observation is null, "ignored native cancellation cannot publish a result");
        effect.Reset(); var changedMedia = app.DispatchAsync(Request()); await effect.Entered.Task;
        effect.Current = false; effect.Release.TrySetResult();
        Check(!(await changedMedia).Succeeded && app.State.Observation is null, "media/calibration generation fences result");
        effect.Reset(); effect.Ready = true;
        var noProof = app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, app.State.Version,
            new ObserveTable(app.State.Camera.Scope!, 1, 1, Mode: ObservationMode.VerifyForAdvice)));
        await effect.Entered.Task; effect.Release.TrySetResult();
        Check((await noProof).Outcome == CommandOutcome.Failed && app.State.Observation is null,
            "A legacy ready flag without independent verification evidence cannot publish as verified advice");
        effect.Reset(); effect.Ready = true; effect.IncludeProof = true;
        var verifiedEnvelope = new CommandEnvelope(Guid.NewGuid(), CommandOrigin.Touch, app.State.Version,
            new ObserveTable(app.State.Camera.Scope!, 1, 1, Mode: ObservationMode.VerifyForAdvice));
        var verifiedPending = app.DispatchAsync(verifiedEnvelope); await effect.Entered.Task; effect.Release.TrySetResult();
        var verifiedReceipt = await verifiedPending;
        var report = app.LatestVerificationReport;
        Check(verifiedReceipt.Succeeded && report is { Historical: true, IsCurrent: false, PublishedAsCurrent: true,
                Outcome: ObservationOutcome.Ready, Motion: TableMotion.Still, Quality: LayoutQuality.Clear,
                Reason: ObservationReason.None, IndependentFrames: 5, CandidateSpanIndependentFrames: 5,
                StartSourceTimeUs: 1_000_000, EndSourceTimeUs: 3_000_000, Samples.Length: 5,
                FinalDetection: { Total: 3, CueBalls: 1, Reds: 1, BlackBalls: 1 } },
            "The bounded verifier report keeps its exact five-frame proof and compact detection counts");
        var stop = await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, app.State.Version,
            new ChangeCapture(app.State.Camera.Scope!, CaptureAction.Stop)));
        Check(stop.Succeeded && app.State.Camera.Capture == CaptureState.Stopped && app.State.Observation is null
            && ReferenceEquals(app.LatestVerificationReport, report) && report is { Historical: true, IsCurrent: false },
            "Stopping clears current observation state but preserves one explicitly historical metadata report");
        Console.WriteLine("PASS scoped observation commands: bounded admission, touch/Sky, duplicate, receipt timing, source/media changes and cancellation");
    }
    sealed class Observation : ITableObservationEffect
    {
        public bool Current = true, Ready, IncludeProof; public int Calls;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Reset() { Current = true; Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); Release = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        public bool IsCurrent(ObserveTable request) => Current;
        public Task<TableObservation> ExecuteAsync(ObserveTable request, Guid operationId, CancellationToken cancellation) => Execute(request, operationId, cancellation);
        public async Task<TableObservation> ExecuteAsync(ObserveTable request, CancellationToken cancellation)
            => await Execute(request, Guid.NewGuid(), cancellation);
        async Task<TableObservation> Execute(ObserveTable request, Guid operationId, CancellationToken cancellation)
        {
            Calls++; Entered.TrySetResult(); await Release.Task; // deliberately ignore cancellation
            var now = DateTimeOffset.UtcNow;
            var result = new TableObservation(request.Source, request.AnalysisGeneration, request.MediaGeneration,
                "fixture", now, 3_000_000, 1, 1, 1, 2, 1,
                Ready ? ObservationOutcome.Ready : ObservationOutcome.Unsettled, "decoded-live-verification");
            if (!IncludeProof || request.Mode != ObservationMode.VerifyForAdvice) return result;
            var vision = new VisionResult([new("cue", "cue", new(1.8, .5), 1), new("red", "red", new(1, .5), 1),
                new("black", "black", new(.2, .8), 1)], false, [], 1, 3, true, "fixture", [1, 1, 1], 100, false, []);
            var samples = Enumerable.Range(0, 5).Select(index => new VerificationSample(200 + index,
                1_000_000 + index * 500_000, 1, 1, 1, 1, 1,
                new(TableMotion.Still, ObservationReason.None, true, 0, .005, 0, vision)) { CapturedAt = now }).ToArray();
            return result with
            {
                Motion = TableMotion.Still, Quality = LayoutQuality.Clear, Reason = ObservationReason.None,
                DecodedReceipt = new(1, samples[^1].FrameId, samples[^1].SourceTimeUs, 3_000_000_000, 1920, 1080),
                DetectionEvidence = new(samples[^1].FrameId, samples[^1].SourceTimeUs, 1),
                Verification = new(operationId, ObservationPolicy.VerificationConfiguration, "calibration", "open", 1,
                    samples[0].SourceTimeUs, samples[^1].SourceTimeUs, samples.Length, 500, 1, samples)
                { CandidateSpanStartSampleIndex = 0, CandidateSpanIndependentFrames = samples.Length }
            };
        }
    }
    sealed class Camera : ICameraControlEffect
    {
        public Task<CameraControlResult> ExecuteAsync(Guid id, CameraCommand command, CameraConnection admitted, CancellationToken cancellation)
            => Task.FromResult(new CameraControlResult(admitted with { Reachability = CameraReachability.Reachable, Armed = true,
                Capture = command is ChangeCapture { Action: CaptureAction.Stop } ? CaptureState.Stopped : CaptureState.Active,
                Ownership = CameraOwnership.Owned, HostOwner = "fixture", HostGeneration = 1, SessionId = Guid.NewGuid().ToString("N"), CaptureRevision = 1 }, CommandOutcome.Applied, "Fixture observed."));
    }
}
