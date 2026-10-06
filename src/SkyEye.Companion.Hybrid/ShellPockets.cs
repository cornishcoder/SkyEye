using System.Text.Json;
using SkyEye.Core.Analysis;
using SkyEye.Vision.Android;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    readonly VisionAnalyzer pocketDetector = new();
    CancellationTokenSource? pocketLifetime;
    PocketLayout? automaticPockets;
    DateTimeOffset pocketsCheckedAt;
    string? pocketCamera;
    public bool AutomaticPocketsCurrent => automaticPockets is not null && pocketCamera == app.State.Camera.CameraIdentity
        && (DateTimeOffset.UtcNow - pocketsCheckedAt).TotalSeconds < 30;
    long mediaGeneration;
    long MediaGeneration()
    {
        if (!MainThread.IsMainThread) return Interlocked.Read(ref mediaGeneration);
        using var metrics = JsonDocument.Parse(player!.PlaybackMetrics()!);
        var value = metrics.RootElement.GetProperty("mediaGeneration").GetInt64();
        Interlocked.Exchange(ref mediaGeneration, value); return value;
    }
    void EnsurePocketLoop()
    {
        if (!active || foreground is null || player is null || !Playing || !FrameTableRequested || EditingTable || selection.Kind != ViewingSource.Live || pocketLifetime is not null) return;
        pocketLifetime = CancellationTokenSource.CreateLinkedTokenSource(foreground.Token);
        _ = DetectPocketsAsync(player, MediaGeneration(), app.State.Camera.CameraIdentity, pocketLifetime.Token);
    }
    async Task DetectPocketsAsync(UK.CO.Skyeye.Companion.Nativebridge.PlayerBridge owner, long media, string? camera, CancellationToken token)
    {
        PocketLayout? previous = null;
        bool Same(PocketLayout a, PocketLayout b) => a.Corners.Zip(b.Corners).All(p => (p.First - p.Second).Length < .015);
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (!active || player != owner || MediaGeneration() != media || app.State.Camera.CameraIdentity != camera) return;
                PocketLayout? found = null;
                if (!EditingTable && Playing)
                {
                    // Display-only raw texture pixels: no host lease, snapshot endpoint,
                    // game timestamp or calibration save is involved.
                    try
                    {
                        var jpeg = owner.SnapshotForFraming();
                        if (jpeg is not null) found = await Task.Run(() => pocketDetector.FindPockets(jpeg, token), token);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception) { found = null; }
                }
                token.ThrowIfCancellationRequested();
                if (!active || player != owner || MediaGeneration() != media || app.State.Camera.CameraIdentity != camera) return;
                if (found is not null && previous is not null && Same(found, previous))
                {
                    if (automaticPockets is null || !Same(found, automaticPockets)) automaticPockets = found;
                    pocketCamera = camera; pocketsCheckedAt = DateTimeOffset.UtcNow;
                }
                else { automaticPockets = null; pocketCamera = null; }
                previous = found; Publish();
                await Task.Delay(automaticPockets is null && found is not null ? 2000 : 10000, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception)
        {
            if (!token.IsCancellationRequested) { automaticPockets = null; pocketCamera = null; Publish(); }
        }
    }
    void StopPocketFraming()
    {
        pocketLifetime?.Cancel(); pocketLifetime?.Dispose(); pocketLifetime = null;
        automaticPockets = null; pocketCamera = null;
        player?.SetTvSceneTransform(""); lastTvFraming = ""; lastScene = null;
    }
}
