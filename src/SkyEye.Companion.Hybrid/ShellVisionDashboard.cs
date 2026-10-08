using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using SkyEye.Companion.Hybrid.Probes;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using Point = SkyEye.Core.Point;

namespace SkyEye.Companion.Hybrid;

sealed record VisionDashboardBall(string Id, string Kind, double ClassifierScore,
    double X, double Y, double RadiusX, double RadiusY)
{
    // Keep the v1 field while an already-open dashboard tab is still running its old script.
    public double Confidence => ClassifierScore;
}
sealed record VisionDashboardCandidate(string Id, string Pattern, double X, double Y,
    double RadiusX, double RadiusY, double? BodyDarkFraction, double? RimDarkFraction,
    double? BoundarySupport, double? CandidateBoundarySupport);
sealed record VisionDashboardSample(int Sequence, DateTimeOffset CapturedAt, double FrameAgeMs,
    long FrameId, long SourceTimeUs, int ClassifiedBallCount, int CircleCandidateCount, int UnclassifiedCandidateCount,
    bool Uncertain, double ElapsedMs, double? ReferenceBrightness, double? MinimumGain, double? MaximumGain, string[] Warnings)
{
    // Keep the v1 field for already-open dashboard tabs during an app update.
    public int BallCount => ClassifiedBallCount;
    public CueBallRecoveryTelemetry? CueBallRecovery { get; init; }
}

/// <summary>Opt-in, read-only laptop view over the latest sequential vision diagnostic.</summary>
public sealed partial class ShellSession
{
    const string VisionDashboardAsset = "vision-diagnostics-dashboard.html";
    const string VisionScenarioAsset = "vision-scenario-lab.html";
    const int VisionDashboardAssetVersion = 6;
    const int VisionScenarioAssetVersion = 3;
    const int VisionDashboardPort = 8770;
    readonly DashboardScenarioWorkspace dashboardScenario;
    LocalHttpServer? visionDashboardServer;
    bool visionDashboardStartedDiagnostics;
    CancellationTokenSource? visionDashboardMaintenance;
    Task? visionDashboardMaintenanceTask;
    public bool VisionDashboardActive => visionDashboardServer is not null;
    public string VisionDashboardUrl { get; private set; } = "";
    public string VisionScenarioUrl { get; private set; } = "";
    public string VisionDashboardStatus { get; private set; } = "Laptop dashboard is off.";

    public Task StartVisionDashboardAsync() => MainThread.InvokeOnMainThreadAsync(StartVisionDashboardCoreAsync);

    async Task StartVisionDashboardCoreAsync()
    {
        if (VisionDashboardActive) return;
        if (!CanAnalyzeTable || !HasTableCalibration || EditingTable)
        {
            VisionDashboardStatus = "Open the active calibrated live table first.";
            Changed?.Invoke();
            return;
        }
        if (await Permissions.RequestAsync<LocalNetworkPermission>() != PermissionStatus.Granted)
        {
            VisionDashboardStatus = "Allow local-network access to serve the dashboard to your laptop.";
            Changed?.Invoke();
            return;
        }

        var address = LocalWifiAddress();
        if (address is null)
        {
            VisionDashboardStatus = "Connect the Pixel and laptop to Wi-Fi, then try again.";
            Changed?.Invoke();
            return;
        }

        byte[] html, scenarioHtml;
        try
        {
            html = await ReadDashboardAssetAsync(VisionDashboardAsset);
            scenarioHtml = await ReadDashboardAssetAsync(VisionScenarioAsset);
        }
        catch (Exception)
        {
            VisionDashboardStatus = "The dashboard page is missing from this app build.";
            Changed?.Invoke();
            return;
        }

        bool startedHere = false;
        if (!VisionDiagnosticsActive)
        {
            if (!table.StartVisionDiagnostics(out var startMessage))
            {
                VisionDashboardStatus = startMessage;
                Changed?.Invoke();
                return;
            }
            startedHere = true;
        }

        try
        {
            var server = new LocalHttpServer(VisionDashboardPort,
                (request, cancellation) => HandleVisionDashboardRequestAsync(request, cancellation, html, scenarioHtml),
                IPAddress.Any);
            server.Start();
            visionDashboardServer = server;
            visionDashboardStartedDiagnostics = startedHere;
            VisionDashboardUrl = $"http://{address}:{server.Port}/?dashboard={VisionDashboardAssetVersion}";
            VisionScenarioUrl = $"http://{address}:{server.Port}/scenario?view={VisionScenarioAssetVersion}";
            VisionDashboardStatus = "Dashboard is live on your local Wi-Fi. Any device on this network can access it while SkyEye is open.";
            visionDashboardMaintenance = new();
            visionDashboardMaintenanceTask = MaintainVisionDashboardSamplingAsync(visionDashboardMaintenance.Token);
        }
        catch (Exception)
        {
            visionDashboardServer = null;
            VisionDashboardUrl = "";
            VisionScenarioUrl = "";
            if (startedHere) await table.StopVisionDiagnosticsAsync();
            VisionDashboardStatus = "The Pixel could not open a local dashboard port. Stop another dashboard and try again.";
        }
        Changed?.Invoke();
    }

    async Task MaintainVisionDashboardSamplingAsync(CancellationToken cancellation)
    {
        try
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(3), cancellation).ConfigureAwait(false);
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (cancellation.IsCancellationRequested || disposed || !VisionDashboardActive || !active) return;
                    if (VisionDiagnosticsActive || !CanStartVisionDiagnostics) return;
                    if (table.StartVisionDiagnostics(out var message))
                    {
                        visionDashboardStartedDiagnostics = true;
                        VisionDashboardStatus = "Dashboard is live; fresh sampling restarted after an interruption.";
                    }
                    else VisionDashboardStatus = "Dashboard is available; waiting for fresh sampling. " + message;
                    Changed?.Invoke();
                }).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }

    static async Task<byte[]> ReadDashboardAssetAsync(string assetName)
    {
        await using var asset = await FileSystem.OpenAppPackageFileAsync(assetName);
        using var content = new MemoryStream();
        await asset.CopyToAsync(content);
        return content.ToArray();
    }

    async Task<HttpResponse> HandleVisionDashboardRequestAsync(HttpRequest request, CancellationToken cancellation,
        byte[] html, byte[] scenarioHtml)
    {
        if (request.Path == "/api/scenario/reset" && request.Method == "POST")
        {
            if (request.Body.Length > 4096) return HttpResponse.Json(new { error = "Scenario command is too large." }, 413);
            if (!request.Headers.GetValueOrDefault("Content-Type", "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                return HttpResponse.Json(new { error = "Send the scenario command as JSON." }, 415);
            DashboardScenarioResetRequest command;
            try { command = request.Json<DashboardScenarioResetRequest>(); }
            catch (JsonException) { return HttpResponse.Json(new { error = "The scenario command is not valid JSON." }, 400); }
            catch (ArgumentException e) { return HttpResponse.Json(new { error = e.Message }, 400); }
            var result = dashboardScenario.Reset(command);
            return HttpResponse.Json(result, result.Accepted ? 200 : 409);
        }
        if (request.Path == "/api/scenario/command" && request.Method == "POST")
        {
            if (request.Body.Length > 8192) return HttpResponse.Json(new { error = "Scenario command is too large." }, 413);
            if (!request.Headers.GetValueOrDefault("Content-Type", "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                return HttpResponse.Json(new { error = "Send the scenario command as JSON." }, 415);
            DashboardScenarioCommandRequest command;
            try { command = request.Json<DashboardScenarioCommandRequest>(); }
            catch (JsonException) { return HttpResponse.Json(new { error = "The scenario command is not valid JSON." }, 400); }
            catch (ArgumentException e) { return HttpResponse.Json(new { error = e.Message }, 400); }
            var result = dashboardScenario.Apply(command);
            return HttpResponse.Json(result, result.Accepted ? 200 : 409);
        }
        if (request.Path == "/api/scenario/review" && request.Method == "POST")
        {
            if (request.Body.Length > 8192) return HttpResponse.Json(new { error = "Scenario review is too large." }, 413);
            if (!request.Headers.GetValueOrDefault("Content-Type", "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                return HttpResponse.Json(new { error = "Send the scenario review as JSON." }, 415);
            DashboardScenarioReviewRequest command;
            try { command = request.Json<DashboardScenarioReviewRequest>(); }
            catch (JsonException) { return HttpResponse.Json(new { error = "The scenario review is not valid JSON." }, 400); }
            catch (ArgumentException e) { return HttpResponse.Json(new { error = e.Message }, 400); }
            var result = dashboardScenario.Resolve(command);
            return HttpResponse.Json(result, result.Accepted ? 200 : 409);
        }
        if (request.Path == "/api/scenario/rules" && request.Method == "POST")
        {
            if (request.Body.Length > 8192) return HttpResponse.Json(new { error = "Scenario rule facts are too large." }, 413);
            if (!request.Headers.GetValueOrDefault("Content-Type", "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                return HttpResponse.Json(new { error = "Send scenario rule facts as JSON." }, 415);
            DashboardScenarioRuleRequest command;
            try { command = request.Json<DashboardScenarioRuleRequest>(); }
            catch (JsonException) { return HttpResponse.Json(new { error = "Scenario rule facts are not valid JSON." }, 400); }
            catch (ArgumentException e) { return HttpResponse.Json(new { error = e.Message }, 400); }
            var result = dashboardScenario.EvaluateRules(command);
            return HttpResponse.Json(result, result.Accepted ? 200 : 409);
        }
        if ((request.Path is "/api/scenario/live/start" or "/api/scenario/live/stop") && request.Method == "POST")
        {
            if (request.Body.Length > 4096) return HttpResponse.Json(new { error = "Camera review command is too large." }, 413);
            if (!request.Headers.GetValueOrDefault("Content-Type", "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                return HttpResponse.Json(new { error = "Send the camera review command as JSON." }, 415);
            DashboardScenarioLiveStartRequest command;
            try { command = request.Json<DashboardScenarioLiveStartRequest>(); }
            catch (JsonException) { return HttpResponse.Json(new { error = "The camera review command is not valid JSON." }, 400); }
            catch (ArgumentException e) { return HttpResponse.Json(new { error = e.Message }, 400); }
            if (request.Path.EndsWith("/stop", StringComparison.Ordinal))
            {
                var stopped = dashboardScenario.StopCameraReview(command);
                return HttpResponse.Json(stopped, stopped.Accepted ? 200 : 409);
            }
            var started = await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var current = app.State;
                if (!current.HasFreshGameTable(DateTimeOffset.UtcNow))
                    return new DashboardScenarioLiveResponse(false, "Take a fresh live table observation before copying the match into the scenario.", dashboardScenario.Snapshot());
                var session = app.CaptureGameSession();
                if (session.State.Version != current.Game.Version)
                    return new DashboardScenarioLiveResponse(false, "The live match changed during capture. Refresh and try again.", dashboardScenario.Snapshot());
                return dashboardScenario.StartCameraReview(command, session, table.VisionDiagnostics);
            });
            return HttpResponse.Json(started, started.Accepted ? 200 : 409);
        }
        if (request.Path == "/api/scenario/live/samples" && request.Method == "POST")
        {
            if (request.Body.Length > 4096) return HttpResponse.Json(new { error = "Camera sample cursor is too large." }, 413);
            if (!request.Headers.GetValueOrDefault("Content-Type", "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                return HttpResponse.Json(new { error = "Send the camera sample cursor as JSON." }, 415);
            DashboardScenarioLiveSampleRequest command;
            try { command = request.Json<DashboardScenarioLiveSampleRequest>(); }
            catch (JsonException) { return HttpResponse.Json(new { error = "The camera sample cursor is not valid JSON." }, 400); }
            catch (ArgumentException e) { return HttpResponse.Json(new { error = e.Message }, 400); }
            var reviewed = dashboardScenario.ReviewCameraSamples(command, table.VisionDiagnostics);
            return HttpResponse.Json(reviewed, reviewed.Accepted ? 200 : 409);
        }

        if (request.Method != "GET") return HttpResponse.Json(new { error = "This route is read-only." }, 405);
        if (request.Path == "/") return new(200, "text/html; charset=utf-8", html);
        if (request.Path == "/scenario") return new(200, "text/html; charset=utf-8", scenarioHtml);
        if (request.Path == "/api/scenario") return HttpResponse.Json(dashboardScenario.Snapshot());
        if (request.Path == "/api/live-match")
        {
            return await MainThread.InvokeOnMainThreadAsync(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                var diagnostic = table.VisionDiagnostics;
                var live = diagnostic.Active ? diagnostic.LiveFrame : null;
                var detection = live?.Sample.Detection;
                var current = app.State;
                return HttpResponse.Json(new
                {
                    measuredAt = DateTimeOffset.UtcNow,
                    frame = live is null ? null : new { live.Sample.Sequence, live.Sample.FrameAgeMs },
                    diagnosticSessionId = diagnostic.SessionId,
                    detection = detection is null ? null : new
                    {
                        detection.Uncertain,
                        balls = detection.Balls.Where(ball => ball.Kind != "unknown")
                            .Select(ball => new { ball.Id, ball.Kind }).ToArray()
                    },
                    current.GameTableCurrent,
                    current.GameError,
                    game = current.Game,
                    match = current.Match
                });
            });
        }
        if (request.Path == "/api/frame")
        {
            var diagnostic = table.VisionDiagnostics;
            var live = diagnostic.Active ? diagnostic.LiveFrame : null;
            if (live is null) return HttpResponse.Json(new { error = "Waiting for the first analyzed frame" }, 404);
            if (request.Query.TryGetValue("sessionId", out var requestedSession)
                && !requestedSession.Equals(diagnostic.SessionId.ToString(), StringComparison.OrdinalIgnoreCase))
                return HttpResponse.Json(new { error = "A new diagnostic session is available" }, 409);
            if (request.Query.TryGetValue("sequence", out var requestedSequence)
                && int.TryParse(requestedSequence, out var expectedSequence)
                && live.Sample.Sequence != expectedSequence)
                return HttpResponse.Json(new { error = "A newer analyzed frame is available", sequence = live.Sample.Sequence }, 409);
            return new(200, "image/jpeg", live.Jpeg, Headers: new Dictionary<string, string>
            { ["X-Diagnostic-Sequence"] = live.Sample.Sequence.ToString() });
        }
        if (request.Path == "/api/snapshot")
        {
            return await MainThread.InvokeOnMainThreadAsync(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                var diagnostic = table.VisionDiagnostics;
                var live = diagnostic.Active ? diagnostic.LiveFrame : null;
                var sample = live?.Sample;
                var calibration = table.State.Calibration;
                var detection = sample?.Detection;
                var detectedBalls = detection is null || calibration is null ? []
                    : detection.Balls.Where(ball => ball.Kind != "unknown").Select(ball =>
                    {
                        var centre = calibration.Unmap(ball.Position);
                        var horizontal = calibration.Unmap(ball.Position + new Point(PoolGeometry.Radius, 0));
                        var vertical = calibration.Unmap(ball.Position + new Point(0, PoolGeometry.Radius));
                        return new VisionDashboardBall(ball.Id, ball.Kind, ball.Confidence,
                            centre.X, centre.Y, Math.Abs(horizontal.X - centre.X), Math.Abs(vertical.Y - centre.Y));
                    }).ToArray();
                var candidates = detection is null || calibration is null ? []
                    : detection.Circles.Select(circle =>
                    {
                        var tablePoint = new Point(circle.Centre.Y / 1000d, 1 - circle.Centre.X / 1000d);
                        var centre = calibration.Unmap(tablePoint);
                        var horizontal = calibration.Unmap(tablePoint + new Point(circle.Radius / 1000d, 0));
                        var vertical = calibration.Unmap(tablePoint + new Point(0, circle.Radius / 1000d));
                        return new VisionDashboardCandidate(circle.BallId, circle.Pattern, centre.X, centre.Y,
                            Math.Abs(horizontal.X - centre.X), Math.Abs(vertical.Y - centre.Y),
                            circle.BodyDarkFraction, circle.RimDarkFraction,
                            circle.BoundarySupport, circle.CandidateBoundarySupport);
                    }).ToArray();
                var samples = diagnostic.Samples.TakeLast(24).Select(item => new VisionDashboardSample(
                    item.Sequence, item.CapturedAt, item.FrameAgeMs, item.FrameId, item.SourceTimeUs,
                    item.Detection.Balls.Count(ball => ball.Kind != "unknown"), item.Detection.CircleCount,
                    item.Detection.Balls.Count(ball => ball.Kind == "unknown"), item.Detection.Uncertain, item.Detection.ElapsedMs,
                    item.Detection.Illumination?.ReferenceBrightness,
                    item.Detection.Illumination?.MinimumGain, item.Detection.Illumination?.MaximumGain,
                    item.Detection.Warnings)
                    { CueBallRecovery = item.Detection.Recognition?.CueBallRecovery }).ToArray();
                var tracked = table.State.Table;
                var camera = app.State.Camera;
                var current = app.State;
                return HttpResponse.Json(new
                {
                    active = diagnostic.Active,
                    status = diagnostic.Status,
                    totalSamples = diagnostic.TotalSamples,
                    sessionId = diagnostic.SessionId,
                    measuredAt = DateTimeOffset.UtcNow,
                    frame = sample is null ? null : new
                    {
                        sequence = sample.Sequence, sample.CapturedAt, sample.FrameAgeMs, sample.MediaGeneration,
                        sample.FrameId, sample.SourceTimeUs, sample.FrameSource,
                        width = live!.Width, height = live.Height
                    },
                    detection = detection is null ? null : new
                    {
                        detection.Uncertain, detection.Warnings, detection.ElapsedMs, detection.CircleCount,
                        unclassifiedCount = detection.Balls.Count(ball => ball.Kind == "unknown"),
                        detection.Normalized, detection.Runtime, detection.ColourGains, detection.ClothHue,
                        detection.Obstructed, detection.Recognition, detection.VerificationReason,
                        detection.Illumination, detection.Circles,
                        balls = detectedBalls, candidates
                    },
                    samples,
                    camera = new { reachability = camera.Reachability.ToString(), capture = camera.Capture.ToString(), camera.CanControl },
                    calibration = new { table.State.CalibrationId, corners = calibration?.Corners },
                    trackedTable = tracked,
                    observation = current.Observation,
                    gameTableCurrent = current.GameTableCurrent,
                    gameError = current.GameError,
                    game = current.Game,
                    match = current.Match
                });
            });
        }
        return HttpResponse.Json(new { error = "Not found" }, 404);
    }

    public async Task StopVisionDashboardAsync()
    {
        bool stopDiagnostics = visionDashboardStartedDiagnostics;
        await StopVisionDashboardServerAsync().ConfigureAwait(false);
        visionDashboardStartedDiagnostics = false;
        if (stopDiagnostics) await table.StopVisionDiagnosticsAsync().ConfigureAwait(false);
        if (!disposed) await MainThread.InvokeOnMainThreadAsync(() =>
        {
            VisionDashboardStatus = "Laptop dashboard stopped.";
            Changed?.Invoke();
        });
    }

    async Task StopVisionDashboardServerAsync()
    {
        var maintenance = visionDashboardMaintenance;
        var maintenanceTask = visionDashboardMaintenanceTask;
        visionDashboardMaintenance = null;
        visionDashboardMaintenanceTask = null;
        maintenance?.Cancel();
        var server = visionDashboardServer;
        visionDashboardServer = null;
        VisionDashboardUrl = "";
        VisionScenarioUrl = "";
        visionDashboardStartedDiagnostics = false;
        if (server is not null) await server.DisposeAsync().ConfigureAwait(false);
        if (maintenanceTask is not null) await maintenanceTask.ConfigureAwait(false);
        maintenance?.Dispose();
    }

    void CloseVisionDashboard()
    {
        if (visionDashboardServer is null) return;
        _ = StopVisionDashboardServerAsync();
        visionDashboardStartedDiagnostics = false;
        VisionDashboardStatus = "Dashboard stopped because SkyEye was paused.";
    }

    static string? LocalWifiAddress()
    {
        try
        {
            var adapters = NetworkInterface.GetAllNetworkInterfaces()
                .Where(adapter => adapter.OperationalStatus == OperationalStatus.Up)
                .OrderByDescending(adapter => adapter.NetworkInterfaceType == NetworkInterfaceType.Wireless80211
                    || adapter.Name.Contains("wlan", StringComparison.OrdinalIgnoreCase)
                    || adapter.Name.Contains("wifi", StringComparison.OrdinalIgnoreCase));
            foreach (var adapter in adapters)
            foreach (var item in adapter.GetIPProperties().UnicastAddresses)
                if (item.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork
                    && !IPAddress.IsLoopback(item.Address) && !item.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                    return item.Address.ToString();
        }
        catch (NetworkInformationException) { }
        catch (PlatformNotSupportedException) { }
        return null;
    }
}
