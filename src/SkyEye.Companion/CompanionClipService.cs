using SkyEye.Core;
using SkyEye.Core.Application;

namespace SkyEye.Companion;

public sealed record CameraClipCatalog(Guid ConnectionId, string CameraIdentity, Clip[] Clips);
public sealed class CompanionClipService(CameraClient current, Func<CameraConnection> connection, ClipLibrary library, IPortraitClipExporter exporter)
{
    public ClipLibrary Library => library;
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
