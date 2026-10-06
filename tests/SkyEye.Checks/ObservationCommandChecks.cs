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
        Console.WriteLine("PASS scoped observation commands: bounded admission, touch/Sky, duplicate, receipt timing, source/media changes and cancellation");
    }
    sealed class Observation : ITableObservationEffect
    {
        public bool Current = true; public int Calls;
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public void Reset() { Current = true; Entered = new(TaskCreationOptions.RunContinuationsAsynchronously); Release = new(TaskCreationOptions.RunContinuationsAsynchronously); }
        public bool IsCurrent(ObserveTable request) => Current;
        public async Task<TableObservation> ExecuteAsync(ObserveTable request, CancellationToken cancellation)
        {
            Calls++; Entered.TrySetResult(); await Release.Task; // deliberately ignore cancellation
            return new(request.Source, request.AnalysisGeneration, request.MediaGeneration, "fixture", DateTimeOffset.UtcNow, 100, 1, 1, 1, 2, 1, ObservationOutcome.Unsettled, "fixture");
        }
    }
    sealed class Camera : ICameraControlEffect
    {
        public Task<CameraControlResult> ExecuteAsync(Guid id, CameraCommand command, CameraConnection admitted, CancellationToken cancellation)
            => Task.FromResult(new CameraControlResult(admitted with { Reachability = CameraReachability.Reachable, Armed = true, Capture = CaptureState.Active,
                Ownership = CameraOwnership.Owned, HostOwner = "fixture", HostGeneration = 1, SessionId = Guid.NewGuid().ToString("N"), CaptureRevision = 1 }, CommandOutcome.Applied, "Fixture observed."));
    }
}
