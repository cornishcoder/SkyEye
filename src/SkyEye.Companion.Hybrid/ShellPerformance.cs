#if SKYEYE_PERFORMANCE
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;
using G = SkyEye.GameState;

namespace SkyEye.Companion.Hybrid;

public sealed record PerformanceControlCommand(string Command, JsonElement Arguments);

public sealed partial class ShellSession
{
    LocalHttpServer? performanceServer;
    public void EnablePerformance()
    {
        if (performanceServer is not null) return;
        PipelineDiagnostics.Enabled = true;
        player?.SetDiagnostics(true);
        // Lab reserves 8768 for its corrected-clip relay; diagnostics use a separate loopback port.
        // Keep development controls on device loopback; laptop access is through
        // an explicit ADB forward rather than an unauthenticated Wi-Fi port.
        performanceServer = new(8769, async (r, ct) =>
        {
            if (r.Method == "GET" && r.Path == "/ping") return HttpResponse.Json(new { diagnostic = true, package = Android.App.Application.Context.PackageName });
            if (r.Method == "GET" && r.Path == "/control/status")
                return await MainThread.InvokeOnMainThreadAsync(() => HttpResponse.Json(PerformanceControlSnapshot()));
            if (r.Method == "GET" && r.Path == "/control/scenario")
                return HttpResponse.Json(dashboardScenario.Snapshot());
            if (r.Method == "GET" && r.Path == "/control/label-frame")
                return await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    var frame = VisionDiagnosticLabelFrame;
                    return frame is null ? HttpResponse.Json(new { error = "No fresh diagnostic frame is waiting to be labelled." }, 404)
                        : HttpResponse.Json(new { frame.SessionId, frame.RecordingSessionId, frame.SampleId, frame.CapturedAt, frame.FrameAgeMs,
                            frame.FrameId, frame.SourceTimeUs, frame.Width, frame.Height, imageDataUrl = "data:image/jpeg;base64," + Convert.ToBase64String(frame.Jpeg) });
                });
            if (r.Method == "GET" && r.Path == "/control/commands")
                return HttpResponse.Json(new { commands = PerformanceControlCommands });
            if (r.Method == "POST" && r.Path == "/control/command")
            {
                if (r.Body.Length > 65536) return HttpResponse.Json(new { error = "Control command is too large." }, 413);
                if (!r.Headers.GetValueOrDefault("Content-Type", "").StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                    return HttpResponse.Json(new { error = "Send the control command as JSON." }, 415);
                PerformanceControlCommand command;
                try { command = r.Json<PerformanceControlCommand>(); }
                catch (JsonException) { return HttpResponse.Json(new { error = "The control command is not valid JSON." }, 400); }
                catch (ArgumentException e) { return HttpResponse.Json(new { error = e.Message }, 400); }
                return await RunPerformanceControlCommandAsync(command, ct);
            }
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
                    var diagnostics = table.VisionDiagnostics;
                    bool resumeDiagnostics = diagnostics.Active;
                    if (resumeDiagnostics && (diagnostics.LabelFrame is not null || diagnostics.LabelCapturePending))
                        return HttpResponse.Json(new { error = "Finish the labelled frame before refreshing table evidence." }, 409);
                    if (resumeDiagnostics && VisionDashboardActive) visionDashboardSamplingPaused = true;
                    table.VerificationCapture = r.Query.GetValueOrDefault("capture") == "1" ? CaptureVerificationPixels : null;
                    try
                    {
                        // The one-shot observer and sequential dashboard sampler share vision resources.
                        if (resumeDiagnostics) await table.StopVisionDiagnosticsAsync();
                        var result = await ExecuteApplicationCommandAsync(new(Guid.NewGuid(), CommandOrigin.Sky, app.State.Version,
                            new ObserveTable(table.State.Source, table.State.Generation, MediaGeneration(), IncludeChoices: r.Query.GetValueOrDefault("choices") == "1",
                                Mode: r.Query.GetValueOrDefault("verify") == "1" ? ObservationMode.VerifyForAdvice : ObservationMode.Single)), ct);
                        return HttpResponse.Json(result);
                    }
                    finally
                    {
                        table.VerificationCapture = null;
                        if (resumeDiagnostics)
                        {
                            if (!disposed && CanStartVisionDiagnostics && table.StartVisionDiagnostics(out var status))
                            {
                                VisionDiagnosticsStatus = status;
                                if (VisionDashboardActive) VisionDashboardStatus = "Dashboard is live; fresh sampling resumed.";
                            }
                            else if (!disposed && VisionDashboardActive)
                                VisionDashboardStatus = "Dashboard is available; waiting for fresh sampling.";
                            visionDashboardSamplingPaused = false;
                            Changed?.Invoke();
                        }
                    }
                });
            }
            if (r.Method == "POST" && r.Path == "/motion-feed")
            {
                try
                {
                    bool enabled = r.Query.GetValueOrDefault("enabled") == "1";
                    var seedMode = r.Query.GetValueOrDefault("seed", "confirmed");
                    if (seedMode is not ("confirmed" or "fresh-clear-candidate"))
                        return HttpResponse.Json(new { error = "Unsupported motion diagnostic seed mode." }, 400);
                    await SetMotionFeedAsync(enabled, int.Parse(r.Query.GetValueOrDefault("hz", "30")),
                        allowUnconfirmedDiagnosticSeed: seedMode == "fresh-clear-candidate");
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
                ,analysis = new
                {
                    sessionMode = sessionMode.ToString(),
                    viewingSource = selection.Kind.ToString(),
                    active,
                    mediaGeneration = MediaGeneration(),
                    generation = table.State.Generation,
                    hasSource = table.State.Source is not null,
                    hasCalibration = table.State.Calibration is not null,
                    hasPendingWork = table.HasPendingWork,
                    verificationProgress = table.State.VerificationProgress,
                    diagnosticsActive = table.VisionDiagnostics.Active,
                    diagnosticsStatus = table.VisionDiagnostics.Status
                }
                ,scene = JsonSerializer.Deserialize<JsonElement>(player?.SceneMetrics() ?? "{}")
                ,copy = JsonSerializer.Deserialize<JsonElement>(player?.DecodedCopyMetrics() ?? "{}")
            }));
        }, IPAddress.Loopback);
        performanceServer.Start();
    }

    static readonly string[] PerformanceControlCommands =
    [
        "camera.connect", "camera.discover", "camera.select", "camera.claim-host", "camera.release-host", "camera.capture.start", "camera.capture.pause", "camera.capture.resume", "camera.capture.stop",
        "scenario.reset", "scenario.command", "scenario.review", "scenario.rules", "scenario.live.start", "scenario.live.samples", "scenario.live.stop",
        "session.start-live", "session.start-dark", "session.watch-live", "session.end", "view.fixture", "view.local-clip",
        "dashboard.start", "dashboard.stop", "diagnostics.start", "diagnostics.stop", "vision.observe", "vision.verify", "vision.motion.start", "vision.motion.stop",
        "vision.label.capture", "vision.label.colour", "vision.label.visibility", "vision.label.lighting", "vision.label.motion", "vision.label.notes", "vision.label.scene-tag",
        "vision.label.box", "vision.label.rename", "vision.label.remove-last", "vision.label.clear", "vision.label.confirm-empty", "vision.label.confirm-all", "vision.label.save", "vision.label.discard", "vision.label.finish",
        "table.calibrate", "table.correct", "table.apply", "table.cancel", "table.group", "table.corner.add", "table.corner.move", "table.corner.undo", "table.corner.reset",
        "table.ball.select", "table.ball.kind", "table.ball.add-mode", "table.ball.place", "table.ball.delete",
        "match.start", "match.next-frame", "match.turn", "match.colours", "match.foul", "match.winner", "match.undo", "match.finish", "match.declare-shot",
        "shot.choices.show", "shot.choices.cancel-check", "shot.cue-path",
        "presentation.display", "presentation.pin-shot", "presentation.show-shot", "presentation.annotation", "presentation.clear-annotations", "presentation.table-correction",
        "clips.load", "clips.download", "clips.play", "clips.share", "clips.play-tv", "clips.cancel", "replay.recent.request", "replay.recent.watch", "tv.choose", "tv.play-live", "tv.stop", "tv.replay.control",
        "sky.key.setup", "sky.key.remove", "sky.start", "sky.mute.toggle", "sky.end", "scene.check.toggle", "checks.annotation-journal", "checks.annotated-export",
        "ui.navigate", "ui.controls.open", "ui.controls.close", "ui.controls.reveal", "ui.notice.dismiss", "ui.more-controls", "presentation.guides.toggle"
    ];

    static readonly JsonSerializerOptions ControlJson = new(JsonSerializerDefaults.Web)
    { PropertyNameCaseInsensitive = true, Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    object PerformanceControlSnapshot()
    {
        var diagnostic = table.VisionDiagnostics;
        var sample = diagnostic.Active ? diagnostic.LiveFrame?.Sample : null;
        var camera = app.State.Camera;
        var layout = table.State.Table;
        return new
        {
            measuredAt = DateTimeOffset.UtcNow,
            appVersion = app.State.Version,
            camera = new { reachability = camera.Reachability.ToString(), capture = camera.Capture?.ToString(), canControl = camera.CanControl,
                cameraIdentity = camera.CameraIdentity, discovering = FindingCamera, discovered = Cameras },
            ui = new { screen = UiScreen, controlsVisible = ControlsVisible, sheetOpen = SheetOpen, frameGuides = FrameGuides },
            appActive = active && !disposed, sessionMode = sessionMode.ToString(), viewingSource = selection.Kind.ToString(), playing = Playing, hasSource = HasSource,
            calibrated = HasTableCalibration, tableStatus = TableStatus,
            table = layout is null ? null : new { layout.Version, layout.Confirmed, layout.Settled, layout.Occluded, layout.Group,
                layout.CalibrationId, ballCount = layout.Balls.Length, capturedAt = layout.CapturedAt,
                corners = table.State.Calibration?.Corners, balls = layout.Balls },
            tableEdit = new { mode = edit.Mode.ToString(), canApply = CanApplyTableEdit, tableVersion = edit.TableVersion,
                corners = edit.Corners, balls = edit.Balls, selected = edit.Selected, addingBall = edit.AddingBall, kind = edit.Kind,
                hint = EditingTable ? EditorHint : null },
            game = app.State.Game, gameError = app.State.GameError,
            match = app.State.Match,
            targetGroup = app.State.Advice.TargetGroup.ToString(),
            presentation = app.State.Presentation,
            sceneCheck = new { active = SceneCheck, status = SceneCheckStatus },
            dashboard = new { active = VisionDashboardActive, status = VisionDashboardStatus, url = VisionDashboardUrl, scenarioUrl = VisionScenarioUrl },
            diagnostics = new
            {
                active = diagnostic.Active, status = diagnostic.Status, totalSamples = diagnostic.TotalSamples, sessionId = diagnostic.SessionId,
                latest = sample is null ? null : new { sample.Sequence, sample.FrameId, sample.SourceTimeUs, sample.FrameAgeMs, ballCount = sample.Detection.Balls.Count(ball => ball.Kind != "unknown"), uncertain = sample.Detection.Uncertain }
            },
            labeling = new { status = VisionDiagnosticLabelStatus, sessionId = VisionDiagnosticSessionId,
                recordingSessionId = VisionDiagnosticRecordingSessionId, frameReady = VisionDiagnosticLabelFrame is not null,
                capturePending = VisionDiagnosticLabelCapturePending, marks = VisionDiagnosticMarks.Count, saved = VisionDiagnosticSavedLabels },
            motion = MotionMetrics, sky = new { state = SkyState.ToString(), connected = SkyConnected, muted = SkyMuted,
                hasPersonalKey = PersonalKeyConfigured, status = SkyStatus },
            tv = new { connected = TvConnected, status = TvStatus, replayRequest = TvReplayRequest, replayPosition = TvReplayPosition, replayDuration = TvReplayDuration },
            clips = new { status = ClipStatus, loading = LoadingClips, transferring = TransferringClip,
                remote = RemoteClips, local = LocalClips }
        };
    }

    static T ReadControlArgument<T>(JsonElement args, string name)
    {
        if (args.ValueKind == JsonValueKind.Object)
            foreach (var property in args.EnumerateObject())
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    var value = property.Value.Deserialize<T>(ControlJson);
                    if (value is null) throw new ArgumentException($"Argument '{name}' cannot be null.");
                    return value;
                }
        throw new ArgumentException($"Missing required argument '{name}'.");
    }

    static T ReadOptionalControlArgument<T>(JsonElement args, string name, T fallback)
    {
        if (args.ValueKind == JsonValueKind.Object)
            foreach (var property in args.EnumerateObject())
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    var value = property.Value.Deserialize<T>(ControlJson);
                    return value is null ? fallback : value;
                }
        return fallback;
    }

    static TEnum ReadControlEnum<TEnum>(JsonElement args, string name, TEnum fallback) where TEnum : struct, Enum
    {
        var text = ReadOptionalControlArgument(args, name, "");
        if (text.Length == 0) return fallback;
        return Enum.TryParse<TEnum>(text, true, out var value) && (typeof(TEnum).IsDefined(typeof(FlagsAttribute), false) || Enum.IsDefined(value))
            ? value : throw new ArgumentException($"Argument '{name}' has an unsupported value.");
    }

    ShellClipChoice FindControlClip(string id) => RemoteClips.Concat(LocalClips).FirstOrDefault(clip => clip.Id == id)
        ?? throw new ArgumentException("Refresh the clip list and use a current clip ID.");

    Task<HttpResponse> RunPerformanceControlCommandAsync(PerformanceControlCommand request, CancellationToken cancellation) =>
        MainThread.InvokeOnMainThreadAsync(() => RunPerformanceControlCommandCoreAsync(request, cancellation));

    async Task<HttpResponse> RunPerformanceControlCommandCoreAsync(PerformanceControlCommand request, CancellationToken cancellation)
    {
        var command = request.Command?.Trim().ToLowerInvariant();
        var args = request.Arguments;
        string? message = null;
        object? result = null;
        bool succeeded = true;
        bool scenarioCommand = command?.StartsWith("scenario.", StringComparison.Ordinal) == true;
        if ((!active || disposed) && (!scenarioCommand || command is "scenario.live.start" or "scenario.live.samples" or "scenario.live.stop"))
            return HttpResponse.Json(new { succeeded = false, command, message = "Unlock the Pixel and bring SkyEye Lab to the foreground before sending app controls.", state = PerformanceControlSnapshot() }, 409);
        long version = ReadOptionalControlArgument(args, "expectedVersion", app.State.Version);
        try
        {
            switch (command)
            {
                case "camera.connect":
                    await ConnectAsync(ReadOptionalControlArgument(args, "address", SavedAddress));
                    succeeded = app.State.Camera.Reachability == CameraReachability.Reachable;
                    if (!succeeded) message = "The requested camera address is not reachable.";
                    break;
                case "camera.discover": await FindCamerasAsync(); break;
                case "camera.select":
                {
                    var selection = ReadOptionalControlArgument(args, "address", "");
                    var name = ReadOptionalControlArgument(args, "name", "");
                    var cameraChoice = Cameras.FirstOrDefault(camera =>
                        (selection.Length > 0 && string.Equals(camera.Address, selection, StringComparison.OrdinalIgnoreCase)) ||
                        (name.Length > 0 && string.Equals(camera.Name, name, StringComparison.OrdinalIgnoreCase)));
                    if (cameraChoice is null) throw new ArgumentException("Discover cameras first, then select a current camera by address or name.");
                    await ConnectAsync(cameraChoice.Address);
                    succeeded = app.State.Camera.Reachability == CameraReachability.Reachable;
                    if (!succeeded) message = "The selected camera is not reachable.";
                    break;
                }
                case "camera.claim-host":
                case "camera.release-host":
                {
                    var camera = await MainThread.InvokeOnMainThreadAsync(() => app.State.Camera);
                    if (camera.Scope is not { } scope) { succeeded = false; message = "Connect to a reachable camera first."; break; }
                    if (command == "camera.claim-host" && !camera.CanControl) await ControlCameraAsync(new(app.State.Version, new ClaimCameraHost(scope, HostName)));
                    if (command == "camera.release-host" && camera.CanControl) await ControlCameraAsync(new(app.State.Version, new ReleaseCameraHost(scope)));
                    succeeded = command == "camera.claim-host" ? app.State.Camera.CanControl : !app.State.Camera.CanControl;
                    if (!succeeded) message = "The camera did not confirm the requested host change.";
                    break;
                }
                case "camera.capture.start":
                case "camera.capture.pause":
                case "camera.capture.resume":
                case "camera.capture.stop":
                {
                    var camera = await MainThread.InvokeOnMainThreadAsync(() => app.State.Camera);
                    if (camera.Scope is not { } scope || !camera.CanControl) { succeeded = false; message = "Capture changes require host control of a connected camera."; break; }
                    var action = command switch { "camera.capture.start" => CaptureAction.Start, "camera.capture.pause" => CaptureAction.Pause, "camera.capture.resume" => CaptureAction.Resume, _ => CaptureAction.Stop };
                    await ControlCameraAsync(new(app.State.Version, new ChangeCapture(scope, action)));
                    succeeded = action switch { CaptureAction.Start or CaptureAction.Resume => app.State.Camera.Capture == CaptureState.Active, CaptureAction.Pause => app.State.Camera.Capture == CaptureState.Paused, _ => app.State.Camera.Capture is CaptureState.Stopped or CaptureState.Ready };
                    if (!succeeded) message = "The camera has not confirmed the requested capture state.";
                    break;
                }
                case "session.start-live": await StartLiveSessionAsync(); succeeded = SessionMode == TableSessionMode.Live && HasSource; break;
                case "session.start-dark": await StartDarkSessionAsync(); succeeded = SessionMode == TableSessionMode.Dark; break;
                case "session.watch-live": await WatchLiveAsync(cancellation); succeeded = CanAnalyzeTable && HasSource; break;
                case "session.end": await EndSessionAsync(); succeeded = SessionMode == TableSessionMode.None; break;
                case "view.fixture": await FixtureAsync(); succeeded = HasSource; break;
                case "view.local-clip": await PlayClipAsync(ReadControlArgument<string>(args, "id"), cancellation); succeeded = selection.Kind == ViewingSource.LocalClip; break;
                case "dashboard.start": await StartVisionDashboardAsync(); succeeded = VisionDashboardActive; break;
                case "dashboard.stop": await StopVisionDashboardAsync(); succeeded = !VisionDashboardActive; break;
                case "diagnostics.start": await StartVisionDiagnosticsAsync(); succeeded = VisionDiagnosticsActive; if (!succeeded) message = VisionDiagnosticsStatus; break;
                case "diagnostics.stop": await StopVisionDiagnosticsAsync(); succeeded = !VisionDiagnosticsActive; break;
                case "vision.observe": await ObserveTableAsync(); succeeded = table.State.Observation is not null; break;
                case "vision.verify":
                {
                    var response = await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        if (player is null || table.State.Source is null) return false;
                        var result = await ExecuteApplicationCommandAsync(new(Guid.NewGuid(), CommandOrigin.System, app.State.Version,
                            new ObserveTable(table.State.Source, table.State.Generation, MediaGeneration(), IncludeChoices: true, Mode: ObservationMode.VerifyForAdvice)), cancellation);
                        return result.Succeeded;
                    });
                    succeeded = response;
                    if (!succeeded) message = TableStatus;
                    break;
                }
                case "vision.motion.start":
                    await SetMotionFeedAsync(true, ReadOptionalControlArgument(args, "hz", 30), ReadOptionalControlArgument(args, "allowUnconfirmed", false));
                    succeeded = MotionMetrics is { Active: > 0 }; break;
                case "vision.motion.stop": await SetMotionFeedAsync(false); succeeded = MotionMetrics is not { Active: > 0 }; break;
                case "shot.choices.show":
                {
                    var target = ReadControlEnum(args, "target", SceneTarget.Both);
                    await ShowRequestedChoicesAsync(version, target);
                    var shown = target switch
                    {
                        SceneTarget.Phone => app.State.Presentation.Phone.Mode == ShotDisplayMode.Cycling,
                        SceneTarget.Tv => app.State.Presentation.Tv.Mode == ShotDisplayMode.Cycling,
                        _ => app.State.Presentation.Phone.Mode == ShotDisplayMode.Cycling && app.State.Presentation.Tv.Mode == ShotDisplayMode.Cycling
                    };
                    succeeded = shown;
                    if (!succeeded) message = CandidateStatus;
                    break;
                }
                case "shot.choices.cancel-check":
                    succeeded = CheckingTable;
                    CancelTableVerification();
                    if (!succeeded) message = "No candidate-shot table check is running.";
                    break;
                case "shot.cue-path":
                {
                    var receipt = await ShowCuePathAsync(version, ReadControlArgument<long>(args, "tableVersion"), ReadControlArgument<string>(args, "id"));
                    succeeded = receipt.Succeeded; message = receipt.Message; break;
                }
                case "vision.label.capture": RequestVisionDiagnosticLabelFrame(); succeeded = VisionDiagnosticLabelCapturePending; break;
                case "vision.label.colour": SetVisionDiagnosticLabelColour(ReadControlArgument<string>(args, "value")); break;
                case "vision.label.visibility": SetVisionDiagnosticLabelVisibility(ReadControlArgument<string>(args, "value")); break;
                case "vision.label.lighting": SetVisionDiagnosticLabelLighting(ReadControlArgument<string>(args, "value")); break;
                case "vision.label.motion": SetVisionDiagnosticLabelMotion(ReadControlArgument<string>(args, "value")); break;
                case "vision.label.notes": SetVisionDiagnosticLabelNotes(ReadOptionalControlArgument(args, "value", "")); break;
                case "vision.label.scene-tag": ToggleVisionDiagnosticSceneTag(ReadControlArgument<string>(args, "tag"), ReadOptionalControlArgument(args, "selected", true)); break;
                case "vision.label.box": AddVisionDiagnosticBox(ReadControlArgument<double>(args, "x1"), ReadControlArgument<double>(args, "y1"), ReadControlArgument<double>(args, "x2"), ReadControlArgument<double>(args, "y2")); break;
                case "vision.label.rename": RenameVisionDiagnosticBall(ReadControlArgument<string>(args, "currentId"), ReadControlArgument<string>(args, "id")); break;
                case "vision.label.remove-last": RemoveLastVisionDiagnosticBox(); break;
                case "vision.label.clear": ClearVisionDiagnosticBoxes(); break;
                case "vision.label.confirm-empty": SetVisionDiagnosticEmptyFrameConfirmed(ReadOptionalControlArgument(args, "confirmed", true)); break;
                case "vision.label.confirm-all": SetVisionDiagnosticAllBallsLabelledConfirmed(ReadOptionalControlArgument(args, "confirmed", true)); break;
                case "vision.label.save": await SaveVisionDiagnosticLabelsAsync(); succeeded = VisionDiagnosticLabelStatus.StartsWith("Saved ", StringComparison.Ordinal); message = VisionDiagnosticLabelStatus; break;
                case "vision.label.discard": DiscardVisionDiagnosticLabelFrame(); break;
                case "vision.label.finish": FinishVisionDiagnosticLabelSession(); succeeded = VisionDiagnosticLabelSessionFinalized; message = VisionDiagnosticLabelStatus; break;
                case "scenario.reset":
                {
                    var current = dashboardScenario.Snapshot();
                    var reset = dashboardScenario.Reset(new(ReadOptionalControlArgument(args, "scenarioId", current.ScenarioId),
                        ReadOptionalControlArgument(args, "expectedVersion", current.Version), ReadOptionalControlArgument(args, "preset", "empty")));
                    succeeded = reset.Accepted; message = reset.Message; result = reset; break;
                }
                case "scenario.command":
                {
                    var current = dashboardScenario.Snapshot();
                    var scenarioRequest = new DashboardScenarioCommandRequest(
                        ReadOptionalControlArgument(args, "scenarioId", current.ScenarioId), ReadOptionalControlArgument(args, "expectedVersion", current.Version),
                        ReadOptionalControlArgument(args, "operationId", Guid.NewGuid()), ReadControlArgument<string>(args, "action"),
                        ReadOptionalControlArgument<string?>(args, "playerOne", null), ReadOptionalControlArgument<string?>(args, "playerTwo", null),
                        ReadOptionalControlArgument(args, "target", 3), ReadOptionalControlArgument<string?>(args, "playerId", null),
                        ReadOptionalControlArgument<string?>(args, "colour", null), ReadOptionalControlArgument<string?>(args, "ballId", null),
                        ReadOptionalControlArgument<double?>(args, "x", null), ReadOptionalControlArgument<double?>(args, "y", null),
                        ReadOptionalControlArgument<double?>(args, "confidence", null), ReadOptionalControlArgument<bool?>(args, "foul", null),
                        ReadOptionalControlArgument(args, "settled", true), ReadOptionalControlArgument(args, "uncertain", false),
                        ReadOptionalControlArgument<string?>(args, "shotId", null), ReadOptionalControlArgument<string?>(args, "pocket", null));
                    var applied = dashboardScenario.Apply(scenarioRequest);
                    succeeded = applied.Accepted; message = applied.Message; result = applied; break;
                }
                case "scenario.review":
                {
                    var current = dashboardScenario.Snapshot();
                    var reviewed = dashboardScenario.Resolve(new(ReadOptionalControlArgument(args, "scenarioId", current.ScenarioId),
                        ReadOptionalControlArgument(args, "expectedVersion", current.Version), ReadControlArgument<Guid>(args, "proposalId"), ReadControlArgument<string>(args, "choiceId")));
                    succeeded = reviewed.Accepted; message = reviewed.Message; result = reviewed; break;
                }
                case "scenario.rules":
                {
                    var current = dashboardScenario.Snapshot();
                    var ruleRequest = new DashboardScenarioRuleRequest(ReadOptionalControlArgument(args, "scenarioId", current.ScenarioId),
                        ReadOptionalControlArgument(args, "expectedVersion", current.Version), ReadOptionalControlArgument(args, "operationId", Guid.NewGuid()),
                        ReadControlArgument<string>(args, "legality"), ReadOptionalControlArgument(args, "redsPotted", 0),
                        ReadOptionalControlArgument(args, "yellowsPotted", 0), ReadOptionalControlArgument(args, "blackPotted", false),
                        ReadOptionalControlArgument(args, "cueBallPotted", false), ReadOptionalControlArgument(args, "cueBallOffTable", false),
                        ReadOptionalControlArgument<string?>(args, "firstContact", null), ReadOptionalControlArgument<bool?>(args, "callRequired", null),
                        ReadOptionalControlArgument<string?>(args, "calledGroup", null), ReadOptionalControlArgument<int?>(args, "redsBefore", null),
                        ReadOptionalControlArgument<int?>(args, "yellowsBefore", null), ReadOptionalControlArgument(args, "break", false),
                        ReadOptionalControlArgument<bool?>(args, "legalBreak", null));
                    var evaluated = dashboardScenario.EvaluateRules(ruleRequest);
                    succeeded = evaluated.Accepted; message = evaluated.Message; result = evaluated; break;
                }
                case "scenario.live.start":
                case "scenario.live.stop":
                {
                    var current = dashboardScenario.Snapshot();
                    var startRequest = new DashboardScenarioLiveStartRequest(ReadOptionalControlArgument(args, "scenarioId", current.ScenarioId),
                        ReadOptionalControlArgument(args, "expectedVersion", current.Version));
                    DashboardScenarioLiveResponse liveResponse;
                    if (command == "scenario.live.stop") liveResponse = dashboardScenario.StopCameraReview(startRequest);
                    else liveResponse = await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        var liveState = app.State;
                        var gameSession = app.CaptureGameSession();
                        if (gameSession.State.Version != liveState.Game.Version)
                            return new(false, "The live match changed during capture. Refresh and try again.", dashboardScenario.Snapshot());
                        return dashboardScenario.StartCameraReview(startRequest, gameSession, table.VisionDiagnostics);
                    });
                    succeeded = liveResponse.Accepted; message = liveResponse.Message; result = liveResponse; break;
                }
                case "scenario.live.samples":
                {
                    var current = dashboardScenario.Snapshot();
                    var liveSession = ReadControlArgument<Guid>(args, "sessionId");
                    var reviewed = dashboardScenario.ReviewCameraSamples(new(ReadOptionalControlArgument(args, "scenarioId", current.ScenarioId),
                        ReadOptionalControlArgument(args, "expectedVersion", current.Version), liveSession, ReadControlArgument<int>(args, "throughSequence")), table.VisionDiagnostics);
                    succeeded = reviewed.Accepted; message = reviewed.Message; result = reviewed; break;
                }
                case "table.calibrate": await CalibrateTableAsync(); succeeded = EditingTable && edit.Mode == TableEditMode.Calibration; break;
                case "table.correct": await CorrectTableAsync(); succeeded = EditingTable && edit.Mode == TableEditMode.Correction; break;
                case "table.apply":
                    if (!CanApplyTableEdit) { succeeded = false; message = EditorHint; break; }
                    await ApplyTableEditAsync(); succeeded = !EditingTable; message = succeeded ? null : EditorHint; break;
                case "table.cancel": CancelTableEdit(); succeeded = !EditingTable; break;
                case "table.group":
                    await SetTableGroupAsync(version, ReadControlEnum(args, "group", AdviceTargetGroup.Open));
                    succeeded = app.State.Advice.TargetGroup == ReadControlEnum(args, "group", AdviceTargetGroup.Open); break;
                case "table.corner.add": EditTable(draft => draft.AddCorner(ReadControlArgument<SkyEye.Core.Point>(args, "point"))); break;
                case "table.corner.move": EditTable(draft => draft.MoveCorner(ReadControlArgument<int>(args, "index"), ReadControlArgument<SkyEye.Core.Point>(args, "point"))); break;
                case "table.corner.undo": EditTable(draft => draft.UndoCorner()); break;
                case "table.corner.reset": EditTable(draft => draft.ResetCorners()); break;
                case "table.ball.select": EditTable(draft => draft.Select(ReadControlArgument<int>(args, "index"))); break;
                case "table.ball.kind": EditTable(draft => draft.SetKind(ReadControlArgument<string>(args, "kind"))); break;
                case "table.ball.add-mode": EditTable(draft => draft.AddBallMode()); break;
                case "table.ball.place": EditTable(draft => draft.Place(ReadControlArgument<SkyEye.Core.Point>(args, "point"))); break;
                case "table.ball.delete": EditTable(draft => draft.DeleteSelected()); break;
                case "match.start": succeeded = await StartManualMatchAsync(ReadControlArgument<string>(args, "firstPlayer"), ReadControlArgument<string>(args, "secondPlayer"), ReadControlArgument<int>(args, "target"), version); message = MatchError; break;
                case "match.next-frame": succeeded = await StartNextFrameAsync(version, ReadControlArgument<string>(args, "player")); message = MatchError; break;
                case "match.turn": succeeded = await SetMatchTurnAsync(version, ReadControlArgument<string>(args, "player")); message = MatchError; break;
                case "match.colours":
                {
                    var colourText = ReadOptionalControlArgument<string?>(args, "colour", null);
                    G.BallColour? colour = colourText is null ? null : Enum.TryParse<G.BallColour>(colourText, true, out var parsedColour) && Enum.IsDefined(parsedColour)
                        ? parsedColour : throw new ArgumentException("Argument 'colour' has an unsupported value.");
                    succeeded = await DeclareMatchColoursAsync(version, ReadOptionalControlArgument<string?>(args, "player", null), colour); message = MatchError; break;
                }
                case "match.foul": succeeded = await SetMatchFoulAsync(version, ReadControlArgument<bool>(args, "foul")); message = MatchError; break;
                case "match.winner": succeeded = await SetFrameWinnerAsync(version, ReadControlArgument<string>(args, "frame"), ReadControlArgument<string>(args, "player")); message = MatchError; break;
                case "match.undo": succeeded = await UndoMatchDecisionAsync(version, ReadControlArgument<Guid>(args, "inputId")); message = MatchError; break;
                case "match.finish": succeeded = await FinishManualMatchAsync(version); message = MatchError; break;
                case "match.declare-shot": succeeded = await RecordDeclaredShotAsync(version, ReadControlArgument<string>(args, "match"), ReadControlArgument<string>(args, "frame"), ReadControlArgument<G.DeclaredShot>(args, "facts"), ReadOptionalControlArgument<Guid?>(args, "corrects", null)); message = MatchError; break;
                case "presentation.display":
                {
                    var target = ReadControlEnum(args, "target", SceneTarget.Both);
                    var mode = ReadControlEnum(args, "mode", ShotDisplayMode.Live);
                    var receipt = await ExecuteApplicationCommandAsync(new(Guid.NewGuid(), CommandOrigin.Touch, version, new SetShotDisplay(target, mode)), cancellation);
                    succeeded = receipt.Succeeded; message = receipt.Message; break;
                }
                case "presentation.pin-shot":
                {
                    var receipt = await ExecuteApplicationCommandAsync(new(Guid.NewGuid(), CommandOrigin.Touch, version,
                        new PinShot(ReadControlEnum(args, "target", SceneTarget.Both), ReadControlArgument<long>(args, "tableVersion"), ReadControlArgument<string>(args, "id"))), cancellation);
                    succeeded = receipt.Succeeded; message = receipt.Message; break;
                }
                case "presentation.show-shot":
                {
                    var receipt = await ExecuteApplicationCommandAsync(new(Guid.NewGuid(), CommandOrigin.Touch, version,
                        new ShowShot(ReadControlEnum(args, "target", SceneTarget.Both), ReadControlArgument<long>(args, "tableVersion"), ReadControlArgument<int>(args, "number"))), cancellation);
                    succeeded = receipt.Succeeded; message = receipt.Message; break;
                }
                case "presentation.annotation":
                {
                    var receipt = await ExecuteApplicationCommandAsync(new(Guid.NewGuid(), CommandOrigin.Touch, version,
                        new ShowAnnotation(ReadControlEnum(args, "target", SceneTarget.Both), ReadControlArgument<long>(args, "tableVersion"),
                            ReadControlArgument<string>(args, "id"), ReadControlEnum(args, "annotations", ShotAnnotations.All))), cancellation);
                    succeeded = receipt.Succeeded; message = receipt.Message; break;
                }
                case "presentation.clear-annotations":
                {
                    var receipt = await ExecuteApplicationCommandAsync(new(Guid.NewGuid(), CommandOrigin.Touch, version,
                        new ClearAnnotations(ReadControlEnum(args, "target", SceneTarget.Both))), cancellation);
                    succeeded = receipt.Succeeded; message = receipt.Message; break;
                }
                case "presentation.table-correction":
                {
                    var correctionTarget = ReadControlEnum(args, "target", SceneTarget.Phone);
                    var enabled = ReadControlArgument<bool>(args, "enabled");
                    SceneTarget[] targets = correctionTarget == SceneTarget.Both ? [SceneTarget.Phone, SceneTarget.Tv] : [correctionTarget];
                    foreach (var target in targets)
                    {
                        var receipt = await ExecuteApplicationCommandAsync(new(Guid.NewGuid(), CommandOrigin.Touch, app.State.Version,
                            new SetTableCorrection(target, enabled)), cancellation);
                        if (!receipt.Succeeded) { succeeded = false; message = receipt.Message; break; }
                    }
                    break;
                }
                case "presentation.guides.toggle": ToggleFrameGuides(); break;
                case "clips.load": await LoadClipsAsync(cancellation); break;
                case "clips.download": await DownloadClipAsync(FindControlClip(ReadControlArgument<string>(args, "id")), cancellation); succeeded = !TransferringClip; message = ClipStatus; break;
                case "clips.play": await PlayClipAsync(ReadControlArgument<string>(args, "id"), cancellation); succeeded = selection.Kind == ViewingSource.LocalClip; break;
                case "clips.share": await ShareClipAsync(ReadControlArgument<string>(args, "id")); break;
                case "clips.play-tv": await PlayClipOnTvAsync(FindControlClip(ReadControlArgument<string>(args, "id")), cancellation); succeeded = TvReplaySelected; break;
                case "clips.cancel": CancelClipTransfer(); break;
                case "replay.recent.request": await RecentReplayAsync(ReadOptionalControlArgument(args, "seconds", 30), cancellation); succeeded = PreparingRecentReplay || RecentReplayStatus.Contains("available", StringComparison.OrdinalIgnoreCase); message = RecentReplayStatus; break;
                case "replay.recent.watch": WatchRecentReplay(); succeeded = TvReplaySelected; break;
                case "tv.choose": await ChooseTvAsync(); break;
                case "tv.play-live": PlayTv(); break;
                case "tv.stop": StopTv(); break;
                case "tv.replay.control":
                {
                    var requestId = ReadControlArgument<string>(args, "request");
                    ControlTvReplay(requestId, ReadControlEnum<TvReplayAction>(args, "action", TvReplayAction.Pause), ReadOptionalControlArgument(args, "seconds", 0L));
                    succeeded = TvReplayRequest == requestId && !string.IsNullOrWhiteSpace(TvCommandStatus);
                    if (!succeeded) message = "The current TV replay cannot accept that control.";
                    break;
                }
                case "sky.key.setup": await SetupKeyAsync(); break;
                case "sky.key.remove": await RemoveSkyKeyAsync(); succeeded = !PersonalKeyConfigured && !SkyConnected; break;
                case "sky.start": await StartSkyAsync(); succeeded = SkyConnected; message = SkyStatus; break;
                case "sky.mute.toggle": ToggleSkyMute(); break;
                case "sky.end": await EndSkyAsync(); succeeded = !SkyConnected; break;
                case "scene.check.toggle": await ToggleSceneCheckAsync(); break;
                case "checks.annotation-journal": await CheckAnnotationJournalAsync(); break;
                case "checks.annotated-export": await CheckAnnotatedExportAsync(); break;
                case "ui.navigate": succeeded = await NavigateFromControlAsync(ReadControlArgument<string>(args, "screen")); break;
                case "ui.controls.open": SetSheet(true); break;
                case "ui.controls.close": SetSheet(false); break;
                case "ui.controls.reveal": RevealControls(); break;
                case "ui.notice.dismiss": DismissNotice(); break;
                case "ui.more-controls": await MoreControlsAsync(); break;
                default:
                    return HttpResponse.Json(new { error = "Unsupported command.", commands = PerformanceControlCommands }, 400);
            }
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or JsonException or FormatException)
        {
            succeeded = false; message = e.Message;
        }

        if (!succeeded && string.IsNullOrWhiteSpace(message)) message = "The requested app action did not reach its expected state.";
        return await MainThread.InvokeOnMainThreadAsync(() => HttpResponse.Json(new
        {
            succeeded, command, message, result, state = PerformanceControlSnapshot()
        }, succeeded ? 200 : 409));
    }

    sealed record PerformanceCapture(CaptureAction Action);
    sealed record PerformanceSceneCommand(string Name, SceneTarget Target = SceneTarget.Both, ShotDisplayMode Mode = ShotDisplayMode.Live,
        long TableVersion = 0, int Number = 0, string ShotId = "", ShotAnnotations Annotations = ShotAnnotations.All, bool Enabled = false,
        long? ExpectedVersion = null, Guid OperationId = default, CommandOrigin Origin = CommandOrigin.Sky);
    void StopPerformance() { if (performanceServer is not null) _ = performanceServer.DisposeAsync(); performanceServer = null; PipelineDiagnostics.Enabled = false; }
}
#endif
