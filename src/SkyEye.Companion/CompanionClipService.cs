using SkyEye.Core;
using SkyEye.Core.Application;
using System.Text.Json;

namespace SkyEye.Companion;

public sealed record CameraClipCatalog(Guid ConnectionId, string CameraIdentity, Clip[] Clips);
public sealed record CameraRecentReplay(Guid ConnectionId, string CameraIdentity, RecentReplayAsset Asset, long ReceivedTimestamp);
public sealed class CompanionClipService(CameraClient current, Func<CameraConnection> connection, ClipLibrary library, IPortraitClipExporter exporter)
{
    public ClipLibrary Library => library;
    public async Task<CameraRecentReplay> RecentAsync(int seconds, CancellationToken cancellation)
    {
        var admitted = connection(); Check(admitted.ConnectionId, admitted.CameraIdentity);
        if (!RecentReplay.Durations.Contains(seconds)) throw new ArgumentException("Unsupported recent replay duration.");
        using var request = current.CopyForViewing();
        // Always take a fresh camera clock sample; never use delayed phone/TV PTS.
        var status = await request.StatusAsync(cancellation).ConfigureAwait(false);
        Check(admitted.ConnectionId, admitted.CameraIdentity);
        if (!status.TryGetProperty("recentReplayVersion", out var version) || version.GetInt32() != 1)
            throw new InvalidOperationException("Update SkyEye Camera to use recent replay.");
        if (status.GetProperty("state").GetString() != "Active") throw new InvalidOperationException("Start camera recording before requesting recent replay.");
        var command = new RecentReplayRequest(Guid.NewGuid().ToString("N"), status.GetProperty("timeline").GetString()!, seconds, status.GetProperty("sourceTimeUs").GetInt64());
        RecentReplay.Validate(command);
        var result = await request.PostAsync("replays/recent?access=" + Uri.EscapeDataString(request.ReadToken), command, cancellation).ConfigureAwait(false);
        var asset = result.Deserialize<RecentReplayAsset>(JsonDefaults.Options) ?? throw new InvalidOperationException("Missing recent replay acknowledgement.");
        ClipLibrary.Validate(asset.Clip);
        if (asset.Timeline != command.Timeline || asset.EndUs != command.EndUs || asset.StartUs < 0 || asset.StartUs >= asset.EndUs
            || asset.EndUs - asset.StartUs > (seconds + 4) * 1_000_000L || asset.Clip.Duration > seconds + 5
            || asset.Clip.Bytes > RecentReplayStore.MaximumBytes || asset.ExpiresAt - asset.Clip.CreatedAt != RecentReplayStore.Retention)
            throw new InvalidOperationException("Camera returned invalid recent replay metadata.");
        Check(admitted.ConnectionId, admitted.CameraIdentity);
        return new(admitted.ConnectionId, admitted.CameraIdentity!, asset, System.Diagnostics.Stopwatch.GetTimestamp());
    }
    public string TvSource(CameraRecentReplay replay)
    {
        Check(replay.ConnectionId, replay.CameraIdentity);
        ClipLibrary.Validate(replay.Asset.Clip);
        if (System.Diagnostics.Stopwatch.GetElapsedTime(replay.ReceivedTimestamp) >= RecentReplayStore.Retention - TimeSpan.FromSeconds(45)
            || string.IsNullOrEmpty(current.ReadToken)) throw new InvalidOperationException("Temporary replay expired. Request recent footage again.");
        return current.MediaUrl("/replays/" + replay.Asset.Clip.Id + "?access=" + Uri.EscapeDataString(current.ReadToken));
    }
    // Resolve only a validated catalog member on the still-current camera. Read
    // credentials stay native; the UI supplies an ID, never a media address.
    public string TvSource(CameraClipCatalog catalog, Clip clip)
    {
        Check(catalog.ConnectionId, catalog.CameraIdentity);
        if (!catalog.Clips.Contains(clip) || !Guid.TryParseExact(clip.Id, "N", out _)
            || !double.IsFinite(clip.Duration) || clip.Duration <= 0 || clip.Duration > long.MaxValue / 1000d
            || string.IsNullOrEmpty(current.ReadToken))
            throw new InvalidOperationException("Refresh camera clips before playing this shot.");
        return current.MediaUrl("/clips/" + clip.Id + "?access=" + Uri.EscapeDataString(current.ReadToken));
    }
    public async Task<CameraClipCatalog> LoadAsync(CancellationToken cancellation)
    {
        var admitted = connection(); Check(admitted.ConnectionId, admitted.CameraIdentity);
        using var transfer = new CameraClipTransfer(current);
        var clips = await transfer.ListAsync(cancellation).ConfigureAwait(false);
        Check(admitted.ConnectionId, admitted.CameraIdentity);
        return new(admitted.ConnectionId, admitted.CameraIdentity!, clips);
    }
    public async Task<LocalClip> DownloadAsync(CameraClipCatalog catalog, Clip clip, Action<ClipTransferProgress>? progress, CancellationToken cancellation)
    {
        Check(catalog.ConnectionId, catalog.CameraIdentity);
        if (!catalog.Clips.Contains(clip)) throw new InvalidOperationException("Refresh camera clips before downloading.");
        using var transfer = new CameraClipTransfer(current);
        return await library.DownloadAsync(clip, transfer, exporter, progress, cancellation).ConfigureAwait(false);
    }
    void Check(Guid id, string? identity)
    {
        var actual = connection();
        if (id == Guid.Empty || identity is null || actual.ConnectionId != id || actual.CameraIdentity != identity
            || actual.Reachability != CameraReachability.Reachable || CameraEndpoint.Identity(current.Address) != identity)
            throw new InvalidOperationException("The camera connection changed. Refresh its clips.");
    }
}
