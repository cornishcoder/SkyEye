#if SKYEYE_PERFORMANCE
using System.Net;
using System.Text.Json;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    LocalHttpServer? performanceServer;
    public void EnablePerformance()
    {
        if (performanceServer is not null) return;
        PipelineDiagnostics.Enabled = true;
        player?.SetDiagnostics(true);
        performanceServer = new(8768, async (r, ct) =>
        {
            if (r.Method == "POST" && r.Path == "/connect")
            { await ConnectAsync(SavedAddress); return HttpResponse.Json(new { connected = app.State.Camera.Reachability.ToString() }); }
            if (r.Method == "POST" && r.Path == "/host")
            {
                await ControlCameraAsync(new(app.State.Version, new ClaimCameraHost(app.State.Camera.Scope!, HostName)));
                return HttpResponse.Json(new { host = app.State.Camera.CanControl });
            }
            if (r.Method == "POST" && r.Path == "/capture")
            {
                var action = r.Json<PerformanceCapture>().Action;
                if (action is not (CaptureAction.Stop or CaptureAction.Start)) return HttpResponse.Json(new { error = "Unsupported development action" }, 400);
                await ControlCameraAsync(new(app.State.Version, new ChangeCapture(app.State.Camera.Scope!, action)));
                return HttpResponse.Json(new { capture = app.State.Camera.Capture.ToString() });
            }
            if (r.Method == "POST" && r.Path == "/observe")
            {
                return await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    if (player is null || table.State.Source is null) return HttpResponse.Json(new { error = "No active source" }, 409);
                    var result = await app.DispatchAsync(new(Guid.NewGuid(), CommandOrigin.Sky, app.State.Version,
                        new ObserveTable(table.State.Source, table.State.Generation, MediaGeneration())), ct);
                    return HttpResponse.Json(result);
                });
            }
            if (r.Method != "GET" || r.Path != "/metrics") return HttpResponse.Json(new { error = "Not found" }, 404);
            return await MainThread.InvokeOnMainThreadAsync(() => HttpResponse.Json(new
            {
                measuredAt = DateTimeOffset.UtcNow,
                pipeline = PipelineDiagnostics.Snapshot(),
                player = JsonSerializer.Deserialize<JsonElement>(player?.PlaybackMetrics() ?? "{}"),
                annotations = JsonSerializer.Deserialize<JsonElement>(player?.AnnotationMetrics() ?? "{}"),
                encoder = JsonSerializer.Deserialize<JsonElement>(shotRelay?.Metrics ?? "{}"),
                presentation = app.State.Presentation,
                capture = app.State.Camera.Capture.ToString(),
                host = app.State.Camera.CanControl,
                calibrated = HasTableCalibration
                ,observation = app.State.Observation
            }));
        }, IPAddress.Loopback);
        performanceServer.Start();
    }
    sealed record PerformanceCapture(CaptureAction Action);
    void StopPerformance() { if (performanceServer is not null) _ = performanceServer.DisposeAsync(); performanceServer = null; PipelineDiagnostics.Enabled = false; }
}
#endif
