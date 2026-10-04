using SkyEye.Core;
using SkyEye.Core.Application;
using Android.Content;

namespace SkyEye.Companion.Hybrid;

// Only clip IDs and display metadata cross the Razor boundary. Native services
// own verified files, private paths and frozen camera/read credentials.
public sealed record ShellClipChoice(Guid ConnectionId, string Id, string Name, double Duration, bool Available);

public sealed partial class ShellSession
{
    readonly CancellationTokenSource clipLifetime = new();
    CompanionClipService clips = null!;
    CameraClipCatalog? catalog;
    ShellClipChoice[] remoteClips = [], localClips = [];
    CancellationTokenSource? transferCancellation;
    long catalogGeneration;
    public IReadOnlyList<ShellClipChoice> RemoteClips => remoteClips;
    public IReadOnlyList<ShellClipChoice> LocalClips => localClips;
    public bool LoadingClips { get; private set; }
    public bool TransferringClip => transferCancellation is not null;
    public string ClipStatus { get; private set; } = "Your saved shots, ready to watch again.";
    public bool CanLoadClips => app.State.Camera.Reachability == CameraReachability.Reachable;

    void InitializeClips()
    {
        var context = Android.App.Application.Context;
        clips = new(client, () => app.State.Camera,
            new ClipLibrary(Path.Combine(context.FilesDir!.AbsolutePath, "clips")), new AndroidPortraitClipExporter(context));
        RefreshLocalClips();
    }
    void RefreshLocalClips() => localClips = clips.Library.Local().OrderByDescending(c => c.Clip.CreatedAt)
        .Select(c => new ShellClipChoice(Guid.Empty, c.Clip.Id, c.Clip.Name, c.Clip.Duration, true)).ToArray();
    void InvalidateClipCatalog(Guid connection)
    {
        if (catalog is null || catalog.ConnectionId == connection) return;
        catalog = null; remoteClips = [];
    }
    public Task LoadClipsAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!active || foreground is null || LoadingClips || !CanLoadClips) return;
        var token = foreground.Token; var generation = ++catalogGeneration;
        LoadingClips = true; ClipStatus = "Loading camera clips…"; Publish();
        try
        {
            var result = await clips.LoadAsync(token);
            if (token.IsCancellationRequested || disposed || result.ConnectionId != app.State.Camera.ConnectionId) return;
            catalog = result;
            remoteClips = result.Clips.OrderByDescending(c => c.CreatedAt)
                .Select(c => new ShellClipChoice(result.ConnectionId, c.Id, c.Name, c.Duration, clips.Library.Available(c))).ToArray();
            ClipStatus = remoteClips.Length == 0 ? "Saved camera clips will appear here." : "Choose a shot to watch on TV or keep on this phone.";
        }
        catch (OperationCanceledException) { }
        catch (Exception) { if (!token.IsCancellationRequested && !disposed) ShowError("Camera clips could not load. Check the connection and refresh."); }
        finally { if (generation == catalogGeneration) { LoadingClips = false; if (!disposed) Publish(); } }
    });
    public Task DownloadClipAsync(ShellClipChoice choice) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!active || disposed || TransferringClip) return;
        var admitted = catalog;
        var clip = admitted?.ConnectionId == choice.ConnectionId ? admitted.Clips.FirstOrDefault(c => c.Id == choice.Id) : null;
        if (admitted is null || clip is null) { ShowError("Refresh camera clips before downloading this shot."); return; }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(clipLifetime.Token);
        transferCancellation = cancellation; var lastProgress = TimeSpan.FromSeconds(-1);
        DismissNotice(); ClipStatus = "Downloading clip…"; Publish();
        try
        {
            await clips.DownloadAsync(admitted, clip, progress => MainThread.BeginInvokeOnMainThread(() =>
            {
                if (disposed || transferCancellation != cancellation || cancellation.IsCancellationRequested) return;
                if (progress.Stage == ClipTransferStage.Downloading && clock.Elapsed - lastProgress < TimeSpan.FromMilliseconds(500)) return;
                lastProgress = clock.Elapsed;
                ClipStatus = progress.Stage switch
                {
                    ClipTransferStage.Downloading => $"Downloading {progress.Bytes * 100 / progress.TotalBytes}%",
                    ClipTransferStage.Preparing => "Download verified. Preparing portrait clip…",
                    _ => "Portrait clip available locally, with room audio."
                };
                if (active) Publish();
            }), cancellation.Token);
            if (disposed || cancellation.IsCancellationRequested) return;
            RefreshLocalClips();
            remoteClips = remoteClips.Select(c => c.Id == clip.Id && c.ConnectionId == admitted.ConnectionId ? c with { Available = true } : c).ToArray();
            ClipStatus = "Portrait clip available locally, with room audio.";
        }
        catch (OperationCanceledException) { if (!disposed) ClipStatus = "Download cancelled. You can retry this clip."; }
        catch (Exception) { if (!disposed) { ClipStatus = "Clip unavailable. You can retry the download."; ShowError("The clip could not be prepared. Check the camera and retry."); } }
        finally { if (transferCancellation == cancellation) transferCancellation = null; if (!disposed) Publish(); }
    });
    public void CancelClipTransfer() => transferCancellation?.Cancel();
    public void PlayClip(string id)
    {
        if (!MainThread.IsMainThread) { MainThread.BeginInvokeOnMainThread(() => PlayClip(id)); return; }
        if (!active || disposed) return;
        var local = clips.Library.Local().FirstOrDefault(c => c.Clip.Id == id);
        if (local is null) { ShowError("This clip is no longer available. Download it again."); RefreshLocalClips(); return; }
        Stop(); var generation = selection.Begin(ViewingSource.LocalClip);
        selection.Observe(generation, ViewingSource.LocalClip, new Uri(local.VideoPath).AbsoluteUri);
        Message = "Playing your saved shot, with room audio."; PlayCurrent(); SetSheet(false); Publish();
    }
    public Task ShareClipAsync(string id) => MainThread.InvokeOnMainThreadAsync(() =>
    {
        if (!active || disposed) return;
        var local = clips.Library.Local().FirstOrDefault(c => c.Clip.Id == id);
        if (local is null) { ShowError("This clip is no longer available. Download it again."); return; }
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("Android activity required.");
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(activity, activity.PackageName + ".files", new Java.IO.File(local.VideoPath));
        using var intent = new Intent(Intent.ActionSend).SetType("video/mp4").PutExtra(Intent.ExtraStream, uri).AddFlags(ActivityFlags.GrantReadUriPermission);
        using var chooser = Intent.CreateChooser(intent, "Share your shot"); activity.StartActivity(chooser);
    });
}
