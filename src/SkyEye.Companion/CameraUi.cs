using Android.Content;
using Android.Views;
using SkyEye.Core;
using SkyEye.Core.Application;

namespace SkyEye.Companion;

public sealed partial class MainActivity
{
    Task ConnectAsync() => ActionAsync(async () =>
    {
        var receipt = await RunCameraUiAsync(new ConnectCamera(address.Text ?? ""));
        if (lifetime.IsCancellationRequested) return;
        if (receipt.Succeeded)
        {
            RestoreCalibration();
            GetSharedPreferences("skyeye", FileCreationMode.Private)!.Edit()!.PutString("camera", client.Address)!.Apply();
            await LoadClipsAsync();
        }
        StartPolling(); // An unreachable connection can recover on later observations.
    });

    void HostCommand(bool claimHost) => _ = ActionAsync(async () =>
    {
        if (renderedAppState.Camera.Scope is not { } scope) { Say("Connect to the camera before requesting control."); return; }
        await RunCameraUiAsync(claimHost ? new ClaimCameraHost(scope, Android.OS.Build.Model ?? "SkyEye companion") : new ReleaseCameraHost(scope));
    });

    void CaptureCommand(CaptureAction action) => _ = ActionAsync(async () =>
    {
        if (renderedAppState.Camera.Scope is not { } scope) { Say("Connect and become host before controlling capture."); return; }
        await RunCameraUiAsync(new ChangeCapture(scope, action));
    });

    async Task<CommandReceipt> RunCameraUiAsync(CameraCommand command)
    {
        var receipt = await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Touch, renderedAppState.Version, command), lifetime.Token);
        if (!lifetime.IsCancellationRequested)
        {
            ApplyAppState();
            RenderNativeCameraView();
            Say(receipt.Message);
        }
        return receipt;
    }

    async Task RefreshAsync()
    {
        var snapshot = app.State;
        if (snapshot.Camera.ConnectionId == Guid.Empty) return;
        await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.System, snapshot.Version, new RefreshCamera(snapshot.Camera.ConnectionId)), lifetime.Token);
        if (lifetime.IsCancellationRequested) return;
        ApplyAppState();
        // Telemetry can change without changing control state or its version.
        RenderNativeCameraView();
    }

    async Task RefreshOnResumeAsync()
    {
        try { await RefreshAsync(); }
        catch (OperationCanceledException) { }
        catch { Say("Camera status could not be refreshed."); }
    }

    void ApplyCameraState(CameraConnection previous, CameraConnection next)
    {
        bool newConnection = previous.ConnectionId != next.ConnectionId;
        bool changedCamera = previous.CameraIdentity is not null && previous.CameraIdentity != next.CameraIdentity;
        if (changedCamera)
        {
            calibration = null; tableView.Calibration = null; tableView.Picking = false; tableView.Picks.Clear();
        }
        if (newConnection || previous.CanControl != next.CanControl || previous.Capture != next.Capture)
        {
            PauseCandidateOverlay();
            InvalidateTable(changedCamera ? "Camera changed; calibrate and check its table." : "A fresh active camera view and host control are needed for table advice.");
            if (overlayEnabled) StartCandidateOverlay();
        }
        if (previous.CanControl && !next.CanControl) _ = EndMaeveAsync();
        controls.Visibility = next.CanControl ? ViewStates.Visible : ViewStates.Gone;
        release.Visibility = controls.Visibility;
        claim.Visibility = next.CanControl ? ViewStates.Gone : ViewStates.Visible;
        claim.Enabled = next.Reachability == CameraReachability.Reachable && next.Ownership == CameraOwnership.Available;
        status.Text = next.Reachability switch
        {
            CameraReachability.Connecting => "Connecting to camera…",
            CameraReachability.Unreachable => "Camera unreachable. Recording may continue; reconnect to check.",
            CameraReachability.Reachable => $"{next.Capture} · " + OwnershipText(next),
            _ => "Connect to your armed camera"
        };
        RenderNativeCameraView();
        ConfigureCandidateCast(next.CanControl);
        if (!offline)
        {
            if (next.Reachability != CameraReachability.Reachable || next.Capture != CaptureState.Active)
            {
                player.Stop();
                viewerStatus.Text = next.Capture is { } capture ? "Capture is " + capture.ToString().ToLowerInvariant() : "Waiting for camera status…";
            }
            else if (newConnection || previous.Reachability != CameraReachability.Reachable || previous.Capture != CaptureState.Active)
            {
                player.SetMuted(true); player.Play(stream);
            }
        }
    }

    static string OwnershipText(CameraConnection camera) => camera.Ownership switch
    {
        CameraOwnership.Owned => "You control this session",
        CameraOwnership.Available => "Host available",
        CameraOwnership.Guest => "Host: " + camera.HostOwner,
        _ => "Host control unknown"
    };

    void RenderNativeCameraView()
    {
        if (cameraEffects.Latest is not { } view || view.Connection != app.State.Camera) return;
        stream = view.Stream;
        var state = view.Status;
        status.Text = $"{view.Connection.Capture} · Battery {state.GetProperty("battery").GetInt32()}% · {state.GetProperty("temperatureC").GetDouble():0.0}°C\nReplay available: {state.GetProperty("historySeconds").GetDouble():0}s · " + OwnershipText(view.Connection);
        ConfigureCandidateCast(view.Connection.CanControl);
    }
}
