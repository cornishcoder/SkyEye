using Android.Content;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
#if SKYEYE_HYBRID_PRODUCTION
    public bool HasMoreControls => true;
    public bool IsLab => false;
#else
    public bool HasMoreControls => false;
    public bool IsLab => true;
#endif
    public Task MoreControlsAsync() => MainThread.InvokeOnMainThreadAsync(() =>
    {
#if SKYEYE_HYBRID_PRODUCTION
        if (!active || disposed || EditingTable || TransferringClip) return;
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("Android activity required.");
        // Starting the private native activity backgrounds/suspends this surface.
        // Only the address is forwarded. Credentials stay in the existing store;
        // the native controls recheck the camera before admitting any mutation.
        using var intent = new Intent(activity, typeof(SkyEye.Companion.MainActivity));
        if (!string.IsNullOrWhiteSpace(client.Address)) intent.PutExtra("camera", client.Address);
        activity.StartActivity(intent);
#endif
    });

    string? RestoreSharedHostCredential()
    {
        // More controls can claim/release a lease while this surface is paused.
        // Refresh/dispatch still require a fresh heartbeat; storage is not proof.
        var settings = Android.App.Application.Context.GetSharedPreferences("skyeye", FileCreationMode.Private)!;
        var savedCamera = settings.GetString("camera", "");
        if (string.IsNullOrWhiteSpace(client.Address) || string.IsNullOrWhiteSpace(savedCamera)) return null;
        var changed = !string.Equals(savedCamera, client.Address, StringComparison.OrdinalIgnoreCase);
        // Bind the restored token to its endpoint before ConnectCamera compares
        // native identities. Otherwise switching back would erase the new lease.
        if (changed) client.Connect(savedCamera);
        client.HostToken = settings.GetString("host", null);
        return changed ? savedCamera : null;
    }

    async Task ResumeCameraAsync(CancellationToken token)
    {
        try
        {
            var changedCamera = RestoreSharedHostCredential();
            if (changedCamera is not null) await ConnectCoreAsync(changedCamera);
            if (!token.IsCancellationRequested && active && !disposed) await PollAsync(token);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (active && !disposed) ShowError("Camera settings changed. Reconnect to check its current state."); }
    }
}
