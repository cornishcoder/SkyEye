namespace SkyEye.Core.Application;

public sealed partial class AppCoordinator
{
    readonly ICameraControlEffect? cameraControls;
    readonly SemaphoreSlim cameraSerial = new(1);
    readonly Dictionary<Guid, (CommandEnvelope Envelope, Task<CommandReceipt> Pending)> effectPending = [];

    // Called while the canonical writer is held. Only admission mutates here;
    // network work uses a separate queue and never blocks preference commands.
    Task<CommandReceipt> BeginCameraCommand(CommandEnvelope envelope, CancellationToken cancellation)
    {
        if (effectPending.TryGetValue(envelope.OperationId, out var pending))
            return pending.Envelope == envelope ? DuplicateAsync(pending.Pending)
                : Task.FromResult(Receipt(envelope, CommandOutcome.Conflict, "Operation ID was already used for a different request."));
        CommandReceipt? rejection = null;
        var command = (CameraCommand)envelope.Command;
        if (cameraControls is null) rejection = Receipt(envelope, CommandOutcome.Rejected, "Camera control is unavailable.");
        else if (envelope.ExpectedVersion != state.Version) rejection = Receipt(envelope, CommandOutcome.Stale, "The app changed. Refresh before trying again.");
        else if (command is RefreshCamera && envelope.Origin != CommandOrigin.System)
            rejection = Receipt(envelope, CommandOutcome.Rejected, "Camera observations are supplied by the native adapter.");
        else if (command is ConnectCamera connect)
        {
            try
            {
                var identity = CameraEndpoint.Identity(connect.Address);
                CommitCamera(new(Guid.NewGuid(), identity, CameraReachability.Connecting, null, null, CameraOwnership.Unknown, null, 0, null, 0));
            }
            catch (ArgumentException e) { rejection = Receipt(envelope, CommandOutcome.Rejected, e.Message); }
        }
        else rejection = CheckCameraAdmission(envelope, state.Camera);
        if (rejection is not null) { Remember(envelope, rejection); return Task.FromResult(rejection); }
        var admitted = state.Camera;
        var completion = new TaskCompletionSource<CommandReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        effectPending.Add(envelope.OperationId, (envelope, completion.Task));
        _ = RunCameraCommandAsync(envelope, admitted, completion, cancellation);
        return completion.Task;
    }

    CommandReceipt? CheckCameraAdmission(CommandEnvelope envelope, CameraConnection connection)
    {
        var command = (CameraCommand)envelope.Command;
        if (command is RefreshCamera refresh)
            return refresh.ConnectionId != connection.ConnectionId || connection.ConnectionId == Guid.Empty
                || connection.Reachability is CameraReachability.Disconnected or CameraReachability.Connecting
                ? Receipt(envelope, CommandOutcome.Stale, "This camera connection is no longer current.") : null;
        var target = command switch { ClaimCameraHost c => c.Scope, ReleaseCameraHost c => c.Scope, ChangeCapture c => c.Scope, _ => null };
        if (target is null || target != connection.Scope) return Receipt(envelope, CommandOutcome.Stale, "Camera session or ownership changed. Refresh before trying again.");
        if (command is ClaimCameraHost claim)
        {
            if (string.IsNullOrWhiteSpace(claim.Owner) || claim.Owner.Length > 80) return Receipt(envelope, CommandOutcome.Rejected, "Choose a host name of 1–80 characters.");
            return connection.Ownership == CameraOwnership.Available ? null : Receipt(envelope, CommandOutcome.Rejected, "This camera already has a host.");
        }
        if (!connection.CanControl) return Receipt(envelope, CommandOutcome.Rejected, "Become host before controlling capture.");
        if (command is ReleaseCameraHost) return null;
        if (command is not ChangeCapture capture || !Enum.IsDefined(capture.Action)) return Receipt(envelope, CommandOutcome.Rejected, "Unsupported camera control.");
        bool unchanged = capture.Action == CaptureAction.Start && connection.Capture == CaptureState.Active
            || capture.Action == CaptureAction.Stop && connection.Capture is CaptureState.Ready or CaptureState.Stopped;
        // Even an apparent no-op must verify current native authority and scope.
        bool allowed = unchanged || capture.Action switch
        {
            CaptureAction.Start => connection.Armed == true && connection.Capture is CaptureState.Ready or CaptureState.Stopped,
            CaptureAction.Resume => connection.Armed == true && connection.Capture == CaptureState.Paused,
            CaptureAction.Pause => connection.Capture == CaptureState.Active,
            CaptureAction.Stop => connection.Capture is CaptureState.Active or CaptureState.Paused or CaptureState.Faulted,
            _ => false
        };
        return allowed ? null : Receipt(envelope, CommandOutcome.Rejected, "Capture cannot make that transition. Check the camera and its current state.");
    }

    async Task RunCameraCommandAsync(CommandEnvelope envelope, CameraConnection admitted, TaskCompletionSource<CommandReceipt> completion, CancellationToken cancellation)
    {
        CameraControlResult? result = null; CommandReceipt? skipped = null; bool entered = false;
        try
        {
            await cameraSerial.WaitAsync(cancellation).ConfigureAwait(false); entered = true;
            await serial.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                // Recheck the connection and capture scope after waiting for prior
                // camera work. Preference changes alone do not invalidate admission.
                if (state.Camera.ConnectionId != admitted.ConnectionId)
                    skipped = Receipt(envelope, CommandOutcome.Stale, "A newer camera connection replaced this request.");
                else if (envelope.Command is not ConnectCamera)
                    skipped = CheckCameraAdmission(envelope, state.Camera);
            }
            finally { serial.Release(); }
            if (skipped is null)
                result = await cameraControls!.ExecuteAsync(envelope.OperationId, (CameraCommand)envelope.Command, admitted, cancellation).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Cancellation/transport loss can race a remote mutation. Never
            // substitute a guessed stopped/owned state or automatically resend.
            result = new(admitted.Unreachable(), CommandOutcome.Uncertain, "Camera result is unknown. Refresh its observed status before trying a new request.");
        }
        finally
        {
            await serial.WaitAsync().ConfigureAwait(false);
            try
            {
                CommandReceipt receipt;
                if (state.Camera.ConnectionId != admitted.ConnectionId)
                    receipt = Receipt(envelope, CommandOutcome.Stale, "A newer camera connection replaced this result.");
                else if (skipped is not null) receipt = skipped;
                else
                {
                    if (result is null || !ValidCameraResult(result, admitted))
                        result = new(admitted.Unreachable(), CommandOutcome.Uncertain, "Camera returned an invalid observation. Reconnect to check its state.");
                    CommitCamera(result.Connection);
                    receipt = Receipt(envelope, result.Outcome, result.Message);
                }
                effectPending.Remove(envelope.OperationId); Remember(envelope, receipt); completion.TrySetResult(receipt);
            }
            finally { serial.Release(); if (entered) cameraSerial.Release(); }
        }
    }

    void CommitCamera(CameraConnection connection)
    {
        if (state.Camera == connection) return;
        Volatile.Write(ref state, state with { Version = checked(state.Version + 1), Camera = connection }); Notify(state);
    }
    static async Task<CommandReceipt> DuplicateAsync(Task<CommandReceipt> pending) => (await pending.ConfigureAwait(false)) with { Duplicate = true };
    Task<CommandReceipt> BeginSaveCommand(CommandEnvelope envelope, CancellationToken cancellation)
    {
        var completion = new TaskCompletionSource<CommandReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
        effectPending.Add(envelope.OperationId, (envelope, completion.Task));
        _ = CompleteSaveAsync(envelope, state.Version, completion, cancellation);
        return completion.Task;
    }
    async Task CompleteSaveAsync(CommandEnvelope envelope, long version, TaskCompletionSource<CommandReceipt> completion, CancellationToken cancellation)
    {
        CommandReceipt result;
        try { result = await clipSaves!.ExecuteAsync(envelope, version, cancellation).ConfigureAwait(false); }
        catch (OperationCanceledException) { result = new(envelope.OperationId, CommandOutcome.Failed, version, "Save cancelled before dispatch."); }
        catch (Exception) { result = new(envelope.OperationId, CommandOutcome.Uncertain, version, "Save result is unknown. Check camera clips before trying a new request."); }
        await serial.WaitAsync().ConfigureAwait(false);
        try { effectPending.Remove(envelope.OperationId); completion.TrySetResult(result); }
        finally { serial.Release(); }
    }
    static bool ValidCameraResult(CameraControlResult result, CameraConnection admitted)
    {
        var c = result.Connection;
        if (c is null || c.ConnectionId != admitted.ConnectionId || c.CameraIdentity != admitted.CameraIdentity
            || !Enum.IsDefined(c.Reachability) || !Enum.IsDefined(c.Ownership) || c.CaptureRevision < 0 || c.HostGeneration < 0
            || result.Message is not { Length: > 0 and <= 500 } || result.Outcome is not (CommandOutcome.Applied or CommandOutcome.Unchanged or CommandOutcome.Stale or CommandOutcome.Failed or CommandOutcome.Uncertain)) return false;
        if (c.Reachability == CameraReachability.Unreachable) return c.Armed is null && c.Capture is null && c.Ownership == CameraOwnership.Unknown;
        return c.Reachability == CameraReachability.Reachable && c.Armed is not null && c.Capture is { } capture && Enum.IsDefined(capture)
            && Guid.TryParseExact(c.SessionId, "N", out _) && (c.HostOwner is null || c.HostOwner.Length <= 80)
            && (c.Ownership == CameraOwnership.Available && c.HostGeneration == 0 || c.Ownership is CameraOwnership.Owned or CameraOwnership.Guest && c.HostGeneration > 0);
    }
}
