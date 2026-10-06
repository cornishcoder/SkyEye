using System.Text.Json;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    long mediaGeneration;
    readonly FramingGeometryFence framingGeometry = new();
    public bool AutomaticPocketsCurrent => table.State.Calibration is not null;
    long MediaGeneration()
    {
        if (!MainThread.IsMainThread) return Interlocked.Read(ref mediaGeneration);
        using var metrics = JsonDocument.Parse(player!.PlaybackMetrics()!);
        var value = metrics.RootElement.GetProperty("mediaGeneration").GetInt64();
        Interlocked.Exchange(ref mediaGeneration, value); return value;
    }
    string GeometryKey()
    {
        int width = 1920, height = 1080; string configuration = "fixed";
        if (player is not null)
        {
            using var metrics = JsonDocument.Parse(player.PlaybackMetrics()!);
            width = metrics.RootElement.GetProperty("width").GetInt32(); height = metrics.RootElement.GetProperty("height").GetInt32();
        }
        if (effects.Latest?.Status.TryGetProperty("geometryId", out var id) == true) configuration = id.GetString() ?? "unknown";
        return $"{width}x{height}:{configuration}";
    }
    // This checks source geometry only. Maintaining correction performs no perception.
    void EnsurePocketLoop()
    {
        if (selection.Kind != ViewingSource.Live || player is null) return;
        using var metrics = JsonDocument.Parse(player.PlaybackMetrics()!);
        if (framingGeometry.Observe(metrics.RootElement.GetProperty("width").GetInt32(), metrics.RootElement.GetProperty("height").GetInt32(), GeometryKey().Split(':', 2)[1]))
        { table.InvalidateCalibration(); shotMessage = "Stream geometry changed. Recalibrate the table."; }
    }
    void StopPocketFraming()
    {
        player?.SetTvSceneTransform(""); lastTvFraming = ""; lastScene = null;
    }
}
