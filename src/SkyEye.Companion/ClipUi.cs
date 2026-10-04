using Android.Content;
using SkyEye.Core;
using SkyEye.Core.Application;

namespace SkyEye.Companion;

public sealed partial class MainActivity
{
    async Task LoadClipsAsync()
    {
        var catalog = await clipService.LoadAsync(lifetime.Token);
        if (lifetime.IsCancellationRequested || catalog.ConnectionId != app.State.Camera.ConnectionId) return;
        remoteClips.RemoveAllViews();
        foreach (var clip in catalog.Clips.Reverse())
        {
            remoteClips.AddView(Label($"{clip.Name} · {clip.Duration:0}s · {clip.Bytes / 1048576.0:0.0} MB", 14));
            Add(remoteClips, clipService.Library.Available(clip) ? "Downloaded · play below" : "Download portrait clip", () => _ = ActionAsync(() => DownloadAsync(catalog, clip)));
        }
        if (catalog.Clips.Length == 0) remoteClips.AddView(Label("Saved clips appear here.", 14));
    }
    async Task DownloadAsync(CameraClipCatalog catalog, Clip clip)
    {
        var lastProgress = DateTimeOffset.MinValue;
        await clipService.DownloadAsync(catalog, clip, progress =>
        {
            if (progress.Stage == ClipTransferStage.Preparing) Say("Download verified. Preparing portrait clip…");
            else if (progress.Stage == ClipTransferStage.Downloading && (DateTimeOffset.UtcNow - lastProgress).TotalMilliseconds > 500)
            { lastProgress = DateTimeOffset.UtcNow; Say($"Downloading {progress.Bytes * 100 / progress.TotalBytes}%"); }
        }, lifetime.Token);
        if (lifetime.IsCancellationRequested) return;
        Say("Portrait clip available locally, with room audio."); RefreshLocal();
    }
    void RefreshLocal()
    {
        localClips.RemoveAllViews();
        foreach (var local in clipService.Library.Local())
        {
            var clip = local.Clip; var video = local.VideoPath;
            localClips.AddView(Label(clip.Name + " · available offline", 14)); var row = Row(localClips);
            Add(row, "Play", () => { offline = true; player.SetPortrait(false); player.SetMuted(false); player.Play(Android.Net.Uri.FromFile(new Java.IO.File(video))!.ToString()!); });
            Add(row, "Share", () =>
            {
                var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(this, PackageName + ".files", new Java.IO.File(video));
                var intent = new Intent(Intent.ActionSend).SetType("video/mp4").PutExtra(Intent.ExtraStream, uri).AddFlags(ActivityFlags.GrantReadUriPermission);
                StartActivity(Intent.CreateChooser(intent, "Share your shot"));
            });
        }
    }
}
