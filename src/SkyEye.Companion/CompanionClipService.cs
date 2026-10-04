using SkyEye.Core;
using SkyEye.Core.Application;

namespace SkyEye.Companion;

public sealed record CameraClipCatalog(Guid ConnectionId, string CameraIdentity, Clip[] Clips);
public sealed class CompanionClipService(CameraClient current, Func<CameraConnection> connection, ClipLibrary library, IPortraitClipExporter exporter)
{
    public ClipLibrary Library => library;
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
