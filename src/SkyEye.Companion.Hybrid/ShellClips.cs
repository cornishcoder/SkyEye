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
    CompanionCorrectedClips correctedClips = null!;
    CameraClipCatalog? catalog;
    ShellClipChoice[] remoteClips = [], localClips = [];
    string? clipCatalogError;
    CancellationTokenSource? transferCancellation;
    long catalogGeneration;
    bool preparingCorrected;
    public IReadOnlyList<ShellClipChoice> RemoteClips => remoteClips;
    public IReadOnlyList<ShellClipChoice> LocalClips => localClips;
    public bool LoadingClips { get; private set; }
    public bool TransferringClip => transferCancellation is not null || preparingCorrected;
    public string ClipStatus { get; private set; } = "Your saved shots, ready to watch again.";
    public bool CanLoadClips => app.State.Camera.Reachability == CameraReachability.Reachable;
    public bool PhoneSavedClipSelected => selection.Kind == ViewingSource.LocalClip;
    public bool PhoneIsPlayingSavedClip => PhoneSavedClipSelected && Playing;

    void InitializeClips()
    {
        var context = Android.App.Application.Context;
        clips = new(client, () => app.State.Camera,
            new ClipLibrary(Path.Combine(context.FilesDir!.AbsolutePath, "clips")), new AndroidPortraitClipExporter(context));
        correctedClips=new(client,()=>app.State.Camera,clips,Path.Combine(context.FilesDir!.AbsolutePath,"corrected-clips"),new AndroidCorrectedClipRenderer(context),annotations,()=>player?.AnnotationMetrics()??"{}",port:IsLab?8768:8767);
        RefreshLocalClips();
    }
    void RefreshLocalClips() => localClips = correctedClips.Local().Concat(clips.Library.Local().Select(c=>c.Clip)).DistinctBy(c=>c.Id).OrderByDescending(c=>c.CreatedAt)
        .Select(c => new ShellClipChoice(Guid.Empty, c.Id, c.Name, c.Duration, true)).ToArray();
    void InvalidateClipCatalog(Guid connection)
    {
        if (catalog is null || catalog.ConnectionId == connection) return;
        catalog = null; remoteClips = [];
    }
    public Task LoadClipsAsync() => LoadClipsAsync(CancellationToken.None);
    public Task LoadClipsAsync(CancellationToken operationCancellation) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!active || foreground is null || LoadingClips || !CanLoadClips) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(foreground.Token, operationCancellation);
        var token = linked.Token; var generation = ++catalogGeneration;
        clipCatalogError = null;
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
        catch (Exception) { if (!token.IsCancellationRequested && !disposed) { clipCatalogError = "Camera clips could not load. Check the connection and refresh."; ShowError("Camera clips could not load. Check the connection and refresh."); } }
        finally { if (generation == catalogGeneration) { LoadingClips = false; if (!disposed) Publish(); } }
    });
    public Task DownloadClipAsync(ShellClipChoice choice) => DownloadClipAsync(choice, CancellationToken.None);
    public Task DownloadClipAsync(ShellClipChoice choice, CancellationToken operationCancellation) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!active || disposed || TransferringClip) return;
        var admitted = catalog;
        var clip = admitted?.ConnectionId == choice.ConnectionId ? admitted.Clips.FirstOrDefault(c => c.Id == choice.Id) : null;
        if (admitted is null || clip is null) { ShowError("Refresh camera clips before downloading this shot."); return; }
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(clipLifetime.Token, operationCancellation);
        transferCancellation = cancellation; var lastProgress = TimeSpan.FromSeconds(-1);
        DismissNotice(); ClipStatus = "Downloading clip…"; Publish();
        try
        {
            await correctedClips.SavedAsync(admitted, clip, progress => MainThread.BeginInvokeOnMainThread(() =>
            {
                if (disposed || transferCancellation != cancellation || cancellation.IsCancellationRequested) return;
                if (progress.Stage == ClipTransferStage.Downloading && clock.Elapsed - lastProgress < TimeSpan.FromMilliseconds(500)) return;
                lastProgress = clock.Elapsed;
                ClipStatus = progress.Stage switch
                {
                    ClipTransferStage.Downloading => $"Downloading {progress.Bytes * 100 / progress.TotalBytes}%",
                    ClipTransferStage.Preparing => "Preparing corrected clip…",
                    _ => "Corrected clip available locally, with room audio."
                };
                if (active) Publish();
            }), cancellation.Token);
            if (disposed || cancellation.IsCancellationRequested) return;
            RefreshLocalClips();
            remoteClips = remoteClips.Select(c => c.Id == clip.Id && c.ConnectionId == admitted.ConnectionId ? c with { Available = true } : c).ToArray();
            ClipStatus = "Corrected clip available locally, with room audio. Short stable gaps hold the correction; long geometry gaps keep the original view.";
        }
        catch (OperationCanceledException) { if (!disposed) ClipStatus = "Download cancelled. You can retry this clip."; }
        catch (Exception) { if (!disposed) { ClipStatus = "Clip unavailable. You can retry the download."; ShowError("The clip could not be prepared. Check the camera and retry."); } }
        finally { if (transferCancellation == cancellation) transferCancellation = null; if (!disposed) Publish(); }
    });
    public void CancelClipTransfer() => transferCancellation?.Cancel();
    public Task PlayClipAsync(string id) => PlayClipAsync(id, CancellationToken.None);
    public Task PlayClipAsync(string id, CancellationToken operationCancellation) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!active || disposed || foreground is null || TransferringClip) return;
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(foreground.Token,clipLifetime.Token,operationCancellation);
        transferCancellation=cancellation;var token=cancellation.Token;var previous=selection.Generation;preparingCorrected=true;ClipStatus="Preparing corrected playback…";Publish();
        try{var local=await correctedClips.LocalAsync(id,token);if(token.IsCancellationRequested||disposed||!active||previous!=selection.Generation)return;
            Stop();var generation=selection.Begin(ViewingSource.LocalClip);selection.Observe(generation,ViewingSource.LocalClip,new Uri(local.Portrait).AbsoluteUri);
            ClipStatus="Prepared clip ready, with room audio.";
            Message="Playing corrected footage · with room audio.";PlayCurrent();SetSheet(false);RefreshLocalClips();Publish();}
        catch(OperationCanceledException){if(!disposed)ClipStatus="Preparation cancelled. You can retry this clip.";}
        catch(Exception){ShowError("Connect the original camera to prepare corrected footage, then retry.");}
        finally{if(transferCancellation==cancellation)transferCancellation=null;preparingCorrected=false;if(!disposed)Publish();}
    });
    public Task ShareClipAsync(string id) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (!active || disposed || foreground is null || TransferringClip) return;
        using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(foreground.Token,clipLifetime.Token);
        transferCancellation=cancellation;var token=cancellation.Token;preparingCorrected=true;ClipStatus="Preparing corrected share file…";Publish();
        CorrectedClip local;
        try{local=await correctedClips.LocalAsync(id,token);ClipStatus="Corrected share file ready. Short stable gaps hold the correction; long geometry gaps keep the original view.";}catch(OperationCanceledException){if(!disposed)ClipStatus="Preparation cancelled. You can retry this clip.";return;}catch(Exception){ShowError("Connect the original camera to prepare a corrected share file.");return;}finally{if(transferCancellation==cancellation)transferCancellation=null;preparingCorrected=false;if(!disposed)Publish();}
        if(!active||disposed||token.IsCancellationRequested)return;
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("Android activity required.");
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(activity, activity.PackageName + ".files", new Java.IO.File(local.Portrait));
        using var intent = new Intent(Intent.ActionSend).SetType("video/mp4").PutExtra(Intent.ExtraStream, uri).AddFlags(ActivityFlags.GrantReadUriPermission);
        using var chooser = Intent.CreateChooser(intent, "Share your shot"); activity.StartActivity(chooser);
    });
}
