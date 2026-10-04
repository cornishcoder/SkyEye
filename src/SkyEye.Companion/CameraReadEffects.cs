using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;

namespace SkyEye.Companion;

public sealed class CameraClipTransfer : IClipTransfer
{
    readonly CameraClient request;
    public CameraClipTransfer(CameraClient current) => request = current.CopyForViewing();
    public async Task<Clip[]> ListAsync(CancellationToken cancellation)
    {
        var clips = await request.ClipsAsync(cancellation).ConfigureAwait(false);
        foreach (var clip in clips) ClipLibrary.Validate(clip);
        return clips;
    }
    public Task DownloadAsync(Clip clip, string destination, Action<long> progress, CancellationToken cancellation)
        => request.DownloadAsync(clip, destination, progress, cancellation);
    public void Dispose() => request.Dispose();
}

public sealed class CameraTableFrameSource(CameraClient current, Func<CameraConnection> connection) : ITableFrameSource
{
    public async Task<TableFrame> ReadAsync(CameraScope source, CancellationToken cancellation)
    {
        CheckCurrent(source);
        using var request = current.CopyForHost();
        if (CameraEndpoint.Identity(request.Address) != source.CameraIdentity || request.HostToken is null)
            throw new InvalidOperationException("The camera connection changed. Request a fresh table view.");
        await CheckRemoteAsync(request, source, cancellation).ConfigureAwait(false);
        await request.PostAsync("host/heartbeat", new { }, cancellation).ConfigureAwait(false);
        var frame = await request.SnapshotAsync(cancellation).ConfigureAwait(false);
        // Check both sides of the snapshot: a capture change during the request
        // must not publish pixels under a previous session/calibration scope.
        await CheckRemoteAsync(request, source, cancellation).ConfigureAwait(false);
        CheckCurrent(source);
        return new(frame.Jpeg, frame.CapturedAt, frame.Timeline);
    }
    void CheckCurrent(CameraScope source)
    {
        var camera = connection();
        if (!camera.CanControl || camera.Capture != CaptureState.Active || camera.Scope != source)
            throw new InvalidOperationException("An active camera and current host control are needed for table advice.");
    }
    static async Task CheckRemoteAsync(CameraClient request, CameraScope source, CancellationToken cancellation)
    {
        var status = await request.StatusAsync(cancellation).ConfigureAwait(false);
        if (status.GetProperty("state").GetString() != "Active" || request.SaveScope != new ClipSaveScope(source.CameraIdentity, source.SessionId, source.HostGeneration, source.CaptureRevision))
            throw new InvalidOperationException("Camera session or ownership changed. Request a fresh table view.");
    }
}
