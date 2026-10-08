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
            if (r.Method == "GET" && r.Path == "/ping") return HttpResponse.Json(new { diagnostic = true, package = Android.App.Application.Context.PackageName });
            if (r.Method == "POST" && r.Path == "/connect")
            { await ConnectAsync(SavedAddress); return HttpResponse.Json(new { connected = app.State.Camera.Reachability.ToString() }); }
            if (r.Method == "POST" && r.Path == "/fixture")
            { await FixtureAsync(); return HttpResponse.Json(new { source = "labelled local fixture" }); }
            if (r.Method == "POST" && r.Path == "/live")
            { await WatchLiveAsync(); return HttpResponse.Json(new { source = "live camera" }); }
            if (r.Method == "POST" && r.Path == "/restart-tv-decoder")
                return await MainThread.InvokeOnMainThreadAsync(() => HttpResponse.Json(new
                { restarted = BackgroundTvService.Instance?.RestartDecoderForCheck() == true }));
            if (r.Method == "POST" && r.Path == "/local")
            {
                var choice = LocalClips.FirstOrDefault();
                if (choice is null) return HttpResponse.Json(new { error = "No saved local clip" }, 409);
                await PlayClipAsync(choice.Id);
                return HttpResponse.Json(new { source = "saved local clip", playback = Playback });
            }
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
                    table.VerificationCapture = r.Query.GetValueOrDefault("capture") == "1" ? CaptureVerificationPixels : null;
                    try
                    {
                        var result = await ExecuteApplicationCommandAsync(new(Guid.NewGuid(), CommandOrigin.Sky, app.State.Version,
                            new ObserveTable(table.State.Source, table.State.Generation, MediaGeneration(), IncludeChoices: r.Query.GetValueOrDefault("choices") == "1",
                                Mode: r.Query.GetValueOrDefault("verify") == "1" ? ObservationMode.VerifyForAdvice : ObservationMode.Single)), ct);
                        return HttpResponse.Json(result);
                    }
                    finally { table.VerificationCapture = null; }
                });
            }
            if (r.Method == "POST" && r.Path == "/motion-feed")
            {
                try
                {
                    bool enabled = r.Query.GetValueOrDefault("enabled") == "1";
                    await SetMotionFeedAsync(enabled, int.Parse(r.Query.GetValueOrDefault("hz", "30")));
                    if (MotionMetrics is { } metrics) return HttpResponse.Json(metrics);
                    return HttpResponse.Json(new { active = enabled ? 1 : 0, pending = 0, status = enabled ? "Starting" : "Stopped; no samples collected" });
                }
                catch (Exception e) when (e is InvalidOperationException or ArgumentException or FormatException)
                { return HttpResponse.Json(new { error = e.Message }, 409); }
            }
            if (r.Method == "POST" && r.Path == "/game-check")
            {
                try { return await MainThread.InvokeOnMainThreadAsync(() => CheckGameStateAsync(ct)); }
                catch (Exception e) { return HttpResponse.Json(new { error = e.Message, exception = e.GetType().FullName, inner = e.InnerException?.Message }, 500); }
            }
            if (r.Method == "POST" && r.Path == "/command")
            {
                var input = r.Json<PerformanceSceneCommand>();
                SkyEyeCommand command = input.Name switch
                {
                    "set_phone_presentation" => new SetShotDisplay(SceneTarget.Phone, input.Mode),
                    "set_tv_presentation" => new SetShotDisplay(SceneTarget.Tv, input.Mode),
                    "show_shot_choices" => new SetShotDisplay(input.Target, ShotDisplayMode.Cycling),
                    "show_shot" => new ShowShot(input.Target, input.TableVersion, input.Number),
                    "show_annotation" => new ShowAnnotation(input.Target, input.TableVersion, input.ShotId, input.Annotations),
                    "clear_annotations" => new ClearAnnotations(input.Target),
                    "set_table_correction" => new SetTableCorrection(input.Target, input.Enabled),
                    _ => throw new ArgumentException("Unsupported presentation command")
                };
                var result = await ExecuteApplicationCommandAsync(new(input.OperationId == Guid.Empty ? Guid.NewGuid() : input.OperationId, input.Origin,
                    input.ExpectedVersion ?? app.State.Version, command), ct);
                return HttpResponse.Json(result);
            }
            if (r.Method != "GET" || r.Path != "/metrics") return HttpResponse.Json(new { error = "Not found" }, 404);
            return await MainThread.InvokeOnMainThreadAsync(() => HttpResponse.Json(new
            {
                measuredAt = DateTimeOffset.UtcNow,
                pipeline = PipelineDiagnostics.Snapshot(),
                player = JsonSerializer.Deserialize<JsonElement>(player?.PlaybackMetrics() ?? "{}"),
                annotations = JsonSerializer.Deserialize<JsonElement>(player?.AnnotationMetrics() ?? "{}"),
                framing = JsonSerializer.Deserialize<JsonElement>(player?.FramingMetrics() ?? "{}"),
                encoder = JsonSerializer.Deserialize<JsonElement>(BackgroundTvService.Instance?.Metrics ?? shotRelay?.Metrics ?? "{}"),
                cast = JsonSerializer.Deserialize<JsonElement>(cast?.Metrics() ?? "{}"),
                presentation = app.State.Presentation,
                version = app.State.Version, choices = app.State.ShotChoices,
                vision = table.State.Vision is { } vision ? new { vision.CircleCount, vision.Warnings, vision.ElapsedMs } : null,
                capture = app.State.Camera.Capture.ToString(),
                host = app.State.Camera.CanControl,
                calibrated = HasTableCalibration
                ,observation = app.State.Observation
                ,motion = MotionMetrics
                ,calibration = table.State.Calibration?.Corners
                ,table = table.State.Table
                ,candidateStatus = CandidateStatus
                ,scene = JsonSerializer.Deserialize<JsonElement>(player?.SceneMetrics() ?? "{}")
                ,copy = JsonSerializer.Deserialize<JsonElement>(player?.DecodedCopyMetrics() ?? "{}")
            }));
        }, IPAddress.Loopback);
        performanceServer.Start();
    }
    sealed record PerformanceCapture(CaptureAction Action);
    sealed record PerformanceSceneCommand(string Name, SceneTarget Target = SceneTarget.Both, ShotDisplayMode Mode = ShotDisplayMode.Live,
        long TableVersion = 0, int Number = 0, string ShotId = "", ShotAnnotations Annotations = ShotAnnotations.All, bool Enabled = false,
        long? ExpectedVersion = null, Guid OperationId = default, CommandOrigin Origin = CommandOrigin.Sky);
    void StopPerformance() { if (performanceServer is not null) _ = performanceServer.DisposeAsync(); performanceServer = null; PipelineDiagnostics.Enabled = false; }
}
#endif
