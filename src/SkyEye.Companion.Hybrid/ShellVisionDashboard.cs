using System.Net;
using System.Net.NetworkInformation;
using System.Text.Json;
using SkyEye.Companion.Hybrid.Probes;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;
using SkyEye.Core.Rules;
using SkyEye.GameState;
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
sealed record VisionDashboardOutcomeReviewRequest(Guid CandidateId, string Action,
    int? Reds = null, int? Yellows = null, int? CueBalls = null, int? BlackBalls = null);
public sealed record VisionDashboardOutcomeReviewResponse(bool Accepted, string Message, long? GameVersion = null);

/// <summary>Opt-in laptop view for sequential vision evidence and explicit settled-count review.</summary>
public sealed partial class ShellSession
{
    const string VisionDashboardAsset = "vision-diagnostics-dashboard.html";
    const string VisionScenarioAsset = "vision-scenario-lab.html";
    const int VisionDashboardAssetVersion = 16;
    const int VisionScenarioAssetVersion = 5;
    const int VisionDashboardPort = 8770;
    readonly DashboardScenarioWorkspace dashboardScenario;
    LocalHttpServer? visionDashboardServer;
    int visionDashboardStarting;
    bool visionDashboardStartedDiagnostics;
    bool visionDashboardSamplingPaused;
    CancellationTokenSource? visionDashboardMaintenance;
    Task? visionDashboardMaintenanceTask;
    public bool VisionDashboardActive => visionDashboardServer is not null;
    public string VisionDashboardUrl { get; private set; } = "";
    public string VisionScenarioUrl { get; private set; } = "";
    public string VisionDashboardStatus { get; private set; } = "Laptop dashboard is off.";

    public Task StartVisionDashboardAsync() => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        if (VisionDashboardActive || Interlocked.CompareExchange(ref visionDashboardStarting, 1, 0) != 0) return;
        try { await StartVisionDashboardCoreAsync(); }
        finally { Interlocked.Exchange(ref visionDashboardStarting, 0); }
    });

    async Task StartVisionDashboardCoreAsync()
    {
        if (VisionDashboardActive) return;
        bool canServeDashboard = CanAnalyzeTable && HasTableCalibration && !EditingTable;
#if !SKYEYE_HYBRID_PRODUCTION
        canServeDashboard = active && !disposed;
#endif
        if (!canServeDashboard)
        {
#if !SKYEYE_HYBRID_PRODUCTION
            VisionDashboardStatus = "Open SkyEye's Lab app to start its development dashboard.";
#else
            VisionDashboardStatus = "Open the active calibrated live table first.";
#endif
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
        if (!VisionDiagnosticsActive && CanStartVisionDiagnostics)
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
            visionDashboardSamplingPaused = false;
            VisionDashboardUrl = $"http://{address}:{server.Port}/?dashboard={VisionDashboardAssetVersion}";
            VisionScenarioUrl = $"http://{address}:{server.Port}/scenario?view={VisionScenarioAssetVersion}";
            VisionDashboardStatus = VisionDiagnosticsActive
                ? "Dashboard and live sampling are running on your local Wi-Fi."
                : "Dashboard is live; start a calibrated live table to begin sampling.";
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
                    if (cancellation.IsCancellationRequested || disposed || !VisionDashboardActive || !active || visionDashboardSamplingPaused) return;
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
        if (request.Path == "/api/live-match/review" && request.Method == "POST")
        {
            if (request.Body.Length > 4096) return HttpResponse.Json(new VisionDashboardOutcomeReviewResponse(false, "Review request is too large."), 413);
            if (!request.Headers.GetValueOrDefault("Content-Type", "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                return HttpResponse.Json(new VisionDashboardOutcomeReviewResponse(false, "Send the review as JSON."), 415);
            VisionDashboardOutcomeReviewRequest command;
            try { command = request.Json<VisionDashboardOutcomeReviewRequest>(); }
            catch (JsonException) { return HttpResponse.Json(new VisionDashboardOutcomeReviewResponse(false, "The review request is not valid JSON."), 400); }
            catch (ArgumentException e) { return HttpResponse.Json(new VisionDashboardOutcomeReviewResponse(false, e.Message), 400); }
            var result = await ReviewLiveOutcomeAsync(command, cancellation);
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
                var activityLifecycle = WithSettledOutcomeRulesAdvice(MotionActivityMetrics, current.Game);
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
                    activityLifecycle,
                    motion = MotionMetrics is { } motion ? new
                    {
                        active = motion.StopReason is null && motion.Processed > 0,
                        motion.StopReason,
                        motion.Processed,
                        motion.RequestedHz,
                        sourceTimeUs = motion.Latest?.SourceTimeUs,
                        analysis = motion.Analysis,
                        activityLifecycle,
                        regions = motion.Latest?.Regions.Select(region => new
                        {
                            region.Id, region.TemplateStatus, region.TemplateDxPixels, region.TemplateDyPixels,
                            region.TemplateResidual, region.Features, region.Supported,
                            region.IlluminationNormalizedChangedPixelFraction
                        }).ToArray()
                    } : null,
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

    public SettledTableCandidateDisplay? LiveOutcomeProposalForVoice()
    {
        var state = app.State;
        return WithSettledOutcomeRulesAdvice(MotionActivityMetrics, state.Game).Candidate;
    }

    MotionActivityDisplay WithSettledOutcomeRulesAdvice(MotionActivityDisplay activity, GameSnapshot state)
    {
        if (activity.Candidate is not { } candidate) return activity;
        ActivityWindowContext? context;
        SettledTableEvidence? after;
        lock (motionActivityGate)
        {
            context = motionActivityCandidateContext;
            after = motionActivityCandidateAfter;
        }
        if (context is null || after is null) return activity;
        var comparison = SettledStateReconciler.Compare(context, after);
        var advice = SettledOutcomeRulesAdvisor.Evaluate(state, comparison.Comparison);
        return activity with { Candidate = candidate with { RulesAdvice = advice } };
    }

    async Task<VisionDashboardOutcomeReviewResponse> ReviewLiveOutcomeAsync(
        VisionDashboardOutcomeReviewRequest request, CancellationToken cancellation,
        CommandOrigin origin = CommandOrigin.Touch)
    {
        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            cancellation.ThrowIfCancellationRequested();
            ActivityWindowContext? context;
            SettledTableEvidence? after;
            lock (motionActivityGate)
            {
                if (motionActivityCandidate is not { } candidate || candidate.ActivityWindowId != request.CandidateId
                    || motionActivityCandidateContext is null || motionActivityCandidateAfter is null)
                    return new VisionDashboardOutcomeReviewResponse(false, "This table-change proposal is no longer available. Wait for a fresh settled result.");
                context = motionActivityCandidateContext;
                after = motionActivityCandidateAfter;
            }

            var currentSample = table.VisionDiagnostics is { Active: true, LiveFrame: { } liveFrame }
                ? liveFrame.Sample : null;
            var sampleAgeMs = currentSample is null ? double.PositiveInfinity
                : (DateTimeOffset.UtcNow - currentSample.CapturedAt).TotalMilliseconds;
            var currentCounts = currentSample is { Detection.Uncertain: false, Detection.Obstructed: false }
                ? CountRecognizedBalls(currentSample.Detection.Balls) : null;
            bool latestMatchesProposal = currentSample is not null && after.SourceTimeUs is { } afterSourceTime
                && currentSample.Source == after.Source && currentSample.SourceTimeUs >= afterSourceTime
                && double.IsFinite(sampleAgeMs) && sampleAgeMs is >= -200 and <= 2500
                && currentCounts == after.Counts;
            if (!latestMatchesProposal)
            {
                ExpireLiveOutcomeCandidate(request.CandidateId);
                return new VisionDashboardOutcomeReviewResponse(false, "The live view no longer matches this proposal. It was cleared; wait for a fresh settled result.");
            }

            var canonical = app.CaptureGameSession();
            if (!context.Game.IsCompatibleWith(canonical.State)
                || canonical.State.Table.Source != context.Before.Source
                || canonical.State.Table.LastSettled?.EvidenceId != context.Before.EvidenceId
                || !SettledStateReconciler.Compare(context, after).Ready)
            {
                ExpireLiveOutcomeCandidate(request.CandidateId);
                return new VisionDashboardOutcomeReviewResponse(false, "The match or before-table changed. The old proposal was cleared; wait for a fresh result.");
            }

            GameInput input;
            string confirmation;
            switch (request.Action?.Trim().ToLowerInvariant())
            {
                case "accept":
                    input = new AcceptSettledInventory(1, context, after);
                    confirmation = "Observed table counts accepted. No pocket, foul or turn was inferred.";
                    break;
                case "correct":
                    if (!ValidLiveInventoryCounts(request.Reds, request.Yellows, request.CueBalls, request.BlackBalls))
                        return new VisionDashboardOutcomeReviewResponse(false, "Enter valid counts: 0–7 reds and yellows, and 0 or 1 cue ball and black.");
                    input = new CorrectSettledInventory(1, context, after,
                        new(request.Reds, request.Yellows, request.CueBalls, request.BlackBalls));
                    confirmation = "Corrected counts saved with the original detector evidence.";
                    break;
                case "reject":
                    input = new RejectSettledInventory(1, context, after);
                    confirmation = "Proposal rejected. The canonical inventory and match score are unchanged.";
                    break;
                default:
                    return new VisionDashboardOutcomeReviewResponse(false, "Choose accept, correct or reject.");
            }

            cancellation.ThrowIfCancellationRequested();
            var current = app.State;
            var id = Guid.NewGuid();
            var at = DateTimeOffset.UtcNow < after.CapturedAt ? after.CapturedAt : DateTimeOffset.UtcNow;
            var envelope = new GameInputEnvelope(id, context.Game.MatchId, context.Game.FrameId, at,
                input, after.Source, after.SourceTimeUs, Provenance.PlayerConfirmed);
            var receipt = await app.DispatchAsync(new(id, origin, current.Version,
                new SubmitGameInput(envelope, canonical.State.Version)), cancellation);
            if (!receipt.Succeeded)
                return new VisionDashboardOutcomeReviewResponse(false, receipt.Message);

            lock (motionActivityGate)
            {
                if (motionActivityCandidate?.ActivityWindowId == request.CandidateId)
                {
                    motionActivityCandidate = null;
                    motionActivityCandidateContext = null;
                    motionActivityCandidateAfter = null;
                    motionActivityLifecycle = null;
                    motionActivityVerificationPending = false;
                    motionActivityVerificationAttempted = false;
                    motionActivityLastSourceTimeUs = null;
                    motionActivityStatus = input is AcceptSettledInventory or CorrectSettledInventory
                        ? "Reviewed table counts are current; watching for the next settled change."
                        : "Proposal rejected; the current match inventory stays in place.";
                    ResetSettledDifferenceCandidate();
                }
            }
            Changed?.Invoke();
            return new VisionDashboardOutcomeReviewResponse(true, confirmation, app.State.Game.Version);
        }).ConfigureAwait(false);
    }

    Task<VisionDashboardOutcomeReviewResponse> ReviewLiveOutcomeFromSkyAsync(Guid candidateId, string action,
        int? reds, int? yellows, int? cueBalls, int? blackBalls, CancellationToken cancellation)
        => ReviewLiveOutcomeAsync(new(candidateId, action, reds, yellows, cueBalls, blackBalls), cancellation, CommandOrigin.Sky);

    void ExpireLiveOutcomeCandidate(Guid candidateId)
    {
        lock (motionActivityGate)
        {
            if (motionActivityCandidate?.ActivityWindowId != candidateId) return;
            motionActivityCandidate = null;
            motionActivityCandidateContext = null;
            motionActivityCandidateAfter = null;
            motionActivityLifecycle = null;
            motionActivityVerificationPending = false;
            motionActivityStatus = "The proposal expired because its match or baseline changed. Waiting for fresh evidence.";
            ResetSettledDifferenceCandidate();
        }
        Changed?.Invoke();
    }

    static bool ValidLiveInventoryCounts(int? reds, int? yellows, int? cueBalls, int? blackBalls)
        => reds is >= 0 and <= 7 && yellows is >= 0 and <= 7 && cueBalls is 0 or 1 && blackBalls is 0 or 1;

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
        visionDashboardSamplingPaused = false;
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
