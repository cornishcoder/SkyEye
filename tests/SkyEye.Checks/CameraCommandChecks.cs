using SkyEye.Core;
using SkyEye.Core.Application;

static class CameraCommandChecks
{
    static void Check(bool value, string reason) { if (!value) throw new Exception(reason); }
    static CommandEnvelope Request(AppCoordinator app, SkyEyeCommand command, CommandOrigin origin = CommandOrigin.Touch)
        => new(Guid.NewGuid(), origin, app.State.Version, command);
    public static async Task Run()
    {
        var port = new FixturePort(); var app = new AppCoordinator(cameraControls: port);
        var empty = app.State;
        Check((await app.DispatchAsync(Request(app, new ConnectCamera("http://camera:8765/?secret=no")))).Outcome == CommandOutcome.Rejected && port.Calls == 0 && app.State == empty, "invalid endpoint never starts connection");
        var connect = Request(app, new ConnectCamera("camera:8765"));
        Check((await app.DispatchAsync(connect)).Succeeded && app.State.Camera.Reachability == CameraReachability.Reachable && app.State.Camera.Ownership == CameraOwnership.Available, "only observed connect result becomes reachable");
        Check((await app.DispatchAsync(connect)).Duplicate && port.Calls == 1, "completed connect replay cannot change epoch");
        Check((await app.DispatchAsync(Request(app, new ChangeCapture(app.State.Camera.Scope!, CaptureAction.Start)))).Outcome == CommandOutcome.Rejected && port.Calls == 1, "guest rejected before effect");
        Check((await app.DispatchAsync(Request(app, new RefreshCamera(app.State.Camera.ConnectionId), CommandOrigin.Sky))).Outcome == CommandOutcome.Rejected, "Sky cannot supply camera observations");
        await app.DispatchAsync(Request(app, new ClaimCameraHost(app.State.Camera.Scope!, "fixture")));
        Check(app.State.Camera.CanControl && app.State.Camera.HostGeneration == 1, "acknowledged host result controls capture");
        var start = Request(app, new ChangeCapture(app.State.Camera.Scope!, CaptureAction.Start));
        await app.DispatchAsync(start);
        Check(app.State.Camera.Capture == CaptureState.Active, "capture starts from observed result");
        await app.DispatchAsync(Request(app, new SetTargetGroup(AdviceTargetGroup.Red)));
        long version = app.State.Version; int before = port.Calls;
        Check((await app.DispatchAsync(start)).Duplicate && port.Calls == before && app.State.Version == version && app.State.Advice.TargetGroup == AdviceTargetGroup.Red, "duplicate capture receipt never rolls back newer state");
        Check((await app.DispatchAsync(Request(app, new ChangeCapture(((ChangeCapture)start.Command).Scope, CaptureAction.Stop)))).Outcome == CommandOutcome.Stale && port.Calls == before, "old capture/session scope cannot stop newer capture");
        Check((await app.DispatchAsync(Request(app, new ChangeCapture(app.State.Camera.Scope!, CaptureAction.Resume)))).Outcome == CommandOutcome.Rejected, "resume requires paused capture");

        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        port.Before = async (command, _) => { if (command is ChangeCapture { Action: CaptureAction.Pause }) { entered.TrySetResult(); await release.Task; } };
        var pauseRequest = Request(app, new ChangeCapture(app.State.Camera.Scope!, CaptureAction.Pause));
        var pause = app.DispatchAsync(pauseRequest); await entered.Task;
        var duplicatePause = app.DispatchAsync(pauseRequest);
        var conflict = await app.DispatchAsync(pauseRequest with { Command = new ShowShotChoices(true) });
        Check(conflict.Outcome == CommandOutcome.Conflict, "pending camera ID cannot become a preference");
        var queuedStop = app.DispatchAsync(Request(app, new ChangeCapture(app.State.Camera.Scope!, CaptureAction.Stop)));
        var shown = await app.DispatchAsync(Request(app, new ShowShotChoices(true))).WaitAsync(TimeSpan.FromSeconds(2));
        Check(shown.Succeeded, "slow capture effect does not block preferences");
        release.SetResult();
        Check((await pause).Succeeded && (await duplicatePause).Duplicate && (await queuedStop).Outcome == CommandOutcome.Stale
            && app.State.Camera.Capture == CaptureState.Paused && app.State.Presentation.ShotChoicesEnabled, "queued effects revalidate scope after prior mutation");
        port.Before = null;
        await app.DispatchAsync(Request(app, new ChangeCapture(app.State.Camera.Scope!, CaptureAction.Resume)));
        await app.DispatchAsync(Request(app, new ChangeCapture(app.State.Camera.Scope!, CaptureAction.Stop)));
        await app.DispatchAsync(Request(app, new ReleaseCameraHost(app.State.Camera.Scope!)));
        Check(!app.State.Camera.CanControl && app.State.Camera.Capture == CaptureState.Stopped && app.State.Camera.Ownership == CameraOwnership.Available, "release removes ownership without guessing capture changes");

        var refreshEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var refreshRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        port.Before = async (command, _) => { if (command is RefreshCamera) { refreshEntered.SetResult(); await refreshRelease.Task; } };
        var oldEpoch = app.State.Camera.ConnectionId;
        var oldRefresh = app.DispatchAsync(Request(app, new RefreshCamera(oldEpoch), CommandOrigin.System)); await refreshEntered.Task;
        var reconnect = app.DispatchAsync(Request(app, new ConnectCamera("camera:8765")));
        Check(app.State.Camera.ConnectionId != oldEpoch && app.State.Camera.Reachability == CameraReachability.Connecting && !app.State.Camera.CanControl, "same-address reconnect invalidates old authority immediately");
        refreshRelease.SetResult();
        Check((await oldRefresh).Outcome == CommandOutcome.Stale && (await reconnect).Succeeded && app.State.Camera.Ownership == CameraOwnership.Available
            && app.State.Advice.TargetGroup == AdviceTargetGroup.Red, "late old response cannot restore ownership or preferences");
        port.Before = null;
        await app.DispatchAsync(Request(app, new ClaimCameraHost(app.State.Camera.Scope!, "fixture")));
        port.Expire = true;
        await app.DispatchAsync(Request(app, new RefreshCamera(app.State.Camera.ConnectionId), CommandOrigin.System));
        Check(app.State.Camera.Ownership == CameraOwnership.Available && !app.State.Camera.CanControl, "observed host expiry removes authority");
        port.Expire = false;
        await app.DispatchAsync(Request(app, new ClaimCameraHost(app.State.Camera.Scope!, "fixture")));
        port.Fail = true;
        var uncertain = await app.DispatchAsync(Request(app, new ChangeCapture(app.State.Camera.Scope!, CaptureAction.Start)));
        Check(uncertain.Outcome == CommandOutcome.Uncertain && app.State.Camera.Reachability == CameraReachability.Unreachable
            && app.State.Camera.Capture is null && !app.State.Camera.CanControl, "unknown remote outcome never guesses stopped or owned state");
        port.Fail = false;
        await app.DispatchAsync(Request(app, new RefreshCamera(app.State.Camera.ConnectionId), CommandOrigin.System));
        Check(app.State.Camera.Reachability == CameraReachability.Reachable, "fresh observation restores reachability");

        var badPort = new FixturePort { InvalidObservation = true }; var invalid = new AppCoordinator(cameraControls: badPort);
        Check((await invalid.DispatchAsync(Request(invalid, new ConnectCamera("camera:8765")))).Outcome == CommandOutcome.Uncertain
            && !invalid.State.Camera.CanControl && invalid.State.Camera.Capture is null, "invalid native observation fails closed");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await app.DispatchAsync(Request(app, new ConnectCamera("other:8765")), cancelled.Token); throw new Exception("cancel ignored"); }
        catch (OperationCanceledException) { }
        await PendingSaveConflict();
        Console.WriteLine("PASS canonical camera command admission, authority/transition gates, duplicates, queued scope checks, responsive preferences, reconnect/late replies, expiry and unknown outcomes");
    }

    static async Task PendingSaveConflict()
    {
        var root = Path.Combine(Path.GetTempPath(), "skyeye-command-crossing-" + Guid.NewGuid().ToString("N"));
        var effect = new HeldSave(); var port = new FixturePort();
        var app = new AppCoordinator(clipSaves: new(root, effect), cameraControls: port);
        try
        {
            await app.DispatchAsync(Request(app, new ConnectCamera("camera:8765")));
            var c = app.State.Camera;
            var saveRequest = Request(app, new SaveClip(30, new(c.CameraIdentity!, c.SessionId!, 1, c.CaptureRevision)));
            var save = app.DispatchAsync(saveRequest); await effect.Entered.Task;
            var conflict = await app.DispatchAsync(saveRequest with { Command = new ConnectCamera("other:8765") });
            Check(conflict.Outcome == CommandOutcome.Conflict && port.Calls == 1, "queued save ID cannot be reused by a camera effect");
            effect.Release.SetResult(); Check((await save).Succeeded, "reserved save remains intact after conflict");
        }
        finally
        {
            if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Unexpected fixture path");
            Directory.Delete(root, recursive: true);
        }
    }
    sealed class HeldSave : IClipSaveEffect
    {
        public TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously), Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<Clip> SaveAsync(Guid id, SaveClip command, CancellationToken cancellation)
        { Entered.SetResult(); await Release.Task; return new(Guid.NewGuid().ToString("N"), "fixture", 29.8, 1234, new string('B', 64), DateTimeOffset.UtcNow); }
    }
    sealed class FixturePort : ICameraControlEffect
    {
        public int Calls; public bool Expire, Fail, InvalidObservation;
        public Func<CameraCommand, CancellationToken, Task>? Before;
        public async Task<CameraControlResult> ExecuteAsync(Guid id, CameraCommand command, CameraConnection admitted, CancellationToken cancellation)
        {
            Calls++; if (Before is not null) await Before(command, cancellation);
            if (Fail) throw new HttpRequestException("fixture transport failure");
            var observed = command switch
            {
                ConnectCamera => admitted with { Reachability = CameraReachability.Reachable, Armed = true, Capture = CaptureState.Stopped,
                    Ownership = CameraOwnership.Available, SessionId = Guid.NewGuid().ToString("N"), CaptureRevision = 1 },
                ClaimCameraHost => admitted with { Ownership = CameraOwnership.Owned, HostOwner = "fixture", HostGeneration = admitted.HostGeneration + 1, CaptureRevision = admitted.CaptureRevision + 1 },
                ReleaseCameraHost => admitted with { Ownership = CameraOwnership.Available, HostOwner = null, HostGeneration = 0, CaptureRevision = admitted.CaptureRevision + 1 },
                ChangeCapture c => admitted with { Capture = c.Action switch { CaptureAction.Start or CaptureAction.Resume => CaptureState.Active, CaptureAction.Pause => CaptureState.Paused, _ => CaptureState.Stopped },
                    CaptureRevision = admitted.CaptureRevision + 1, SessionId = c.Action == CaptureAction.Start ? Guid.NewGuid().ToString("N") : admitted.SessionId },
                _ => admitted with { Reachability = CameraReachability.Reachable, Armed = true, Capture = admitted.Capture ?? CaptureState.Stopped,
                    Ownership = Expire ? CameraOwnership.Available : admitted.Ownership == CameraOwnership.Unknown ? CameraOwnership.Available : admitted.Ownership,
                    HostOwner = Expire ? null : admitted.HostOwner, HostGeneration = Expire || admitted.Ownership == CameraOwnership.Unknown ? 0 : admitted.HostGeneration }
            };
            if (InvalidObservation) observed = observed with { ConnectionId = Guid.NewGuid() };
            return new(observed, CommandOutcome.Applied, "Fixture observed.");
        }
    }
}
