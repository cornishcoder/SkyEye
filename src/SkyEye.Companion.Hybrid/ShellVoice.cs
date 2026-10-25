using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using SkyEye.Companion;
using SkyEye.Core;
using SkyEye.Core.Analysis;
using SkyEye.Core.Application;
using SkyEye.GameState;

namespace SkyEye.Companion.Hybrid;

public sealed partial class ShellSession
{
    SkyVoiceSession? sky;
    SkyVoiceState skyState = SkyVoiceState.Disconnected;
    string skyMessage = "Sky is not connected.";
    readonly SkyVoiceTranscript voiceTranscript = new();
    string skyToolStatus = "";
    SceneTarget voiceShotTarget = SceneTarget.Both;
    Guid voiceOperationScope;

    public event Action<string>? SkyAgentTurnRequested;
    public event Action? SkyAgentTurnInterrupted;

    public SkyVoiceState SkyState => skyState;
    public string SkyStatus => skyMessage;
    public string SkyTranscript => voiceTranscript.Text;
    public string SkyToolStatus => skyToolStatus;
    public bool SkyConnected => sky?.IsReady == true;
    public bool SkyMuted => sky?.IsMuted == true;
    public bool CanStartSky => active && !disposed && sky is null;
    public bool CanMuteSky => sky?.IsReady == true;
    public bool CanEndSky => sky is not null && skyState != SkyVoiceState.Ending;
    public bool CanEditSkyKey => sky is null;
    public void ReportSkyAgentProjectionFailure()
    {
        skyToolStatus = "Sky's conversation panel could not update. Voice remains connected.";
        Changed?.Invoke();
    }

    public Task StartSkyAsync() => MainThread.InvokeOnMainThreadAsync(StartSkyCoreAsync);
    async Task StartSkyCoreAsync()
    {
        if (!active || disposed) return;
        if (sky is not null) { Message = "Sky is already connected or connecting."; Changed?.Invoke(); return; }

        string? key;
        try { key = SecretStore.Get(Android.App.Application.Context, "openai"); }
        catch
        {
            SetSkyStatus(SkyVoiceState.Error, "The saved key could not be opened. Enter it again on this phone.");
            return;
        }
        if (string.IsNullOrWhiteSpace(key))
        {
            Message = "Set your personal key before connecting to Sky."; Publish();
            await SetupKeyAsync();
            return;
        }

        PermissionStatus permission;
        try { permission = await Permissions.RequestAsync<Permissions.Microphone>(); }
        catch { permission = PermissionStatus.Denied; }
        if (!active || disposed) return;
        if (permission != PermissionStatus.Granted)
        {
            SetSkyStatus(SkyVoiceState.Error, "Microphone access is required to talk to Sky.");
            Message = "Allow microphone access in Android settings, then connect again."; Publish();
            return;
        }

        SkyVoiceSession? session = null;
        session = new SkyVoiceSession(Android.App.Application.Context, key, static _ => { }, update: update => OnSkyUpdate(session, update),
            includeSceneCommands: true, correlatedTools: ExecuteVoiceToolAsync);
        // Install the owner before any asynchronous callbacks can arrive.
        sky = session;
        voiceOperationScope = Guid.NewGuid();
        voiceTranscript.BeginSession(); skyToolStatus = "";
        SetSkyStatus(SkyVoiceState.Connecting, "Connecting Sky…");
        bool serviceStarted = false;
        try
        {
            Android.App.Application.Context.StartForegroundService(new Android.Content.Intent(Android.App.Application.Context, typeof(VoiceService)));
            serviceStarted = true;
            await session.StartAsync();
        }
        catch (OperationCanceledException) when (sky != session) { }
        catch (Exception)
        {
            try { await session.CloseAsync(); } catch { }
            if (sky == session)
            {
                sky = null;
                if (serviceStarted) Android.App.Application.Context.StopService(new Android.Content.Intent(Android.App.Application.Context, typeof(VoiceService)));
                SetSkyStatus(SkyVoiceState.Error, "Sky could not connect. Check the key, network, and microphone permission.");
                Message = "Sky could not connect. Your camera session is unchanged."; Publish();
            }
        }
    }

    public void ToggleSkyMute()
    {
        if (sky is null) return;
        bool muted = !sky.IsMuted;
        if (!sky.SetMuted(muted))
        {
            SetSkyStatus(SkyVoiceState.Interrupted, "Sky is reconnecting; microphone control is unavailable.");
            return;
        }
    }

    public async Task EndSkyAsync()
    {
        var session = sky;
        if (session is null) return;
        SetSkyStatus(SkyVoiceState.Ending, "Ending Sky…");
        try { await session.CloseAsync(); }
        finally
        {
            if (sky == session) sky = null;
            Android.App.Application.Context.StopService(new Android.Content.Intent(Android.App.Application.Context, typeof(VoiceService)));
            if (skyState == SkyVoiceState.Ending) SetSkyStatus(SkyVoiceState.Disconnected, "Sky ended. Camera recording continues.");
        }
    }

    public async Task RemoveSkyKeyAsync()
    {
        if (!active || disposed) return;
        if (sky is not null) await EndSkyAsync();
        RemoveKey();
    }

    void OnSkyUpdate(SkyVoiceSession? session, SkyVoiceUpdate update)
    {
        void Apply()
        {
            if (session is not null && sky != session) return;
            if (update.Message.Length > 0)
            {
                skyState = update.State; skyMessage = update.Message;
                if (update.State == SkyVoiceState.Error) Message = update.Message;
            }
            if (update.Transcript is { Length: > 0 } transcript)
            {
                if (update.IsUserTranscript) voiceTranscript.AddUserTranscript(transcript, update.TranscriptFinal);
                else voiceTranscript.AddAssistantTranscript(update.ResponseId, update.TranscriptEventType, transcript);
                if (update.IsUserTranscript && update.TranscriptFinal && agentClient.BeginTurn(transcript) is not null)
                    SkyAgentTurnRequested?.Invoke(transcript);
                else if (!update.IsUserTranscript && !update.TranscriptFinal)
                    agentClient.PublishAssistantText(update.ResponseId, transcript);
            }
            if (update.ToolName is { Length: > 0 } toolName)
            {
                skyToolStatus = update.ToolOutcome switch
                {
                    "requested" or "started" => $"Checking: {VoiceToolLabel(toolName)}…",
                    "completed" => $"Done: {VoiceToolLabel(toolName)}.",
                    "cancelled" => $"Cancelled: {VoiceToolLabel(toolName)}.",
                    _ => $"Unavailable: {VoiceToolLabel(toolName)}."
                };
                if (update.ToolOutcome == "requested" && update.ToolCallId is { } requestedCall && update.ToolArgumentsJson is { } arguments)
                    agentClient.PublishToolCall(update.ResponseId, requestedCall, toolName, arguments);
                else if (update.ToolOutcome is "completed" or "cancelled" or "unavailable"
                    && update.ToolCallId is { } completedCall && update.ToolResultJson is { } result)
                    agentClient.PublishToolResult(completedCall, result);
            }
            if (update.EventKind == "response_started") voiceTranscript.BeginResponse(update.ResponseId);
            else if (update.EventKind == "response_finished") voiceTranscript.FinishResponse(update.ResponseId, completesTurn: false);
            else if (update.EventKind == "response_completed") voiceTranscript.FinishResponse(update.ResponseId, completesTurn: true);
            else if (update.EventKind == "response_cancelled") voiceTranscript.DiscardResponse(update.ResponseId);
            if (update.EventKind == "user_interrupted")
            {
                voiceTranscript.InterruptResponse(update.ResponseId);
                agentClient.InterruptCurrent();
                SkyAgentTurnInterrupted?.Invoke();
            }
            else if (update.EventKind == "session_interrupted")
            {
                voiceTranscript.EndSession();
                agentClient.InterruptCurrent();
                SkyAgentTurnInterrupted?.Invoke();
            }
            if (update.EventKind == "response_cancelled") agentClient.InterruptResponse(update.ResponseId);
            else if (update.EventKind == "response_started") agentClient.StartResponse(update.ResponseId);
            else if (update.EventKind == "response_finished") agentClient.FinishResponse(update.ResponseId, completesTurn: false);
            else if (update.EventKind == "response_completed") agentClient.FinishResponse(update.ResponseId, completesTurn: true);
            Changed?.Invoke();
        }
        if (MainThread.IsMainThread) Apply(); else MainThread.BeginInvokeOnMainThread(Apply);
    }

    void SetSkyStatus(SkyVoiceState state, string message)
    {
        skyState = state; skyMessage = message; Changed?.Invoke();
    }

    static string VoiceToolLabel(string name) => name switch
    {
        "get_table_state" => "table state",
        "start_vision_diagnostics" or "stop_vision_diagnostics" or "get_vision_diagnostics" => "vision diagnostics",
        "calibrate_table" => "table corner review",
        "apply_table_calibration" => "table calibration and initial check",
        "get_camera_state" => "camera state",
        "discover_cameras" => "nearby cameras",
        "connect_camera" => "camera connection",
        "host_live_session" => "live session setup",
        "claim_camera_host" => "host control",
        "release_camera_host" => "host release",
        "start_capture" => "camera capture",
        "pause_capture" => "camera pause",
        "resume_capture" => "camera resume",
        "stop_capture" => "camera capture stop",
        "choose_chromecast" => "Chromecast picker",
        "cast_live_table" => "live Chromecast playback",
        "stop_cast" => "Chromecast disconnect",
        "suggest_shots" => "fresh shot choices",
        "explain_shot" => "shot explanation",
        "select_shot_by_number" or "select_shot_by_id" => "shot selection",
        "clear_shots" => "shot paths",
        "get_match_state" => "match state",
        "review_settled_table_change" => "settled table review",
        "start_match" or "start_frame" or "finish_match" => "match setup",
        "declare_match_colours" => "player group declaration",
        "correct_match_state" => "match correction",
        "undo_match_decision" => "match undo",
        "record_declared_shot" => "declared shot outcome",
        "save_recent_clip" => "camera clip save",
        "get_saved_clips" => "saved clips",
        "play_saved_clip" => "saved clip playback",
        "replay_recent" => "recent TV replay",
        "control_tv_replay" => "TV replay control",
        "return_to_live" => "live table playback",
        _ => "table request"
    };

    Task<object> ExecuteVoiceToolAsync(string callId, string name, JsonElement arguments, CancellationToken cancellation) => name switch
    {
        "get_table_state" => Task.FromResult(ReadVoiceTableState(cancellation)),
        "start_vision_diagnostics" => StartVoiceVisionDiagnosticsAsync(cancellation),
        "get_vision_diagnostics" => Task.FromResult(ReadVoiceVisionDiagnostics(cancellation)),
        "stop_vision_diagnostics" => StopVoiceVisionDiagnosticsAsync(cancellation),
        "calibrate_table" => CalibrateTableFromVoiceAsync(cancellation),
        "apply_table_calibration" => ApplyTableCalibrationFromVoiceAsync(callId, cancellation),
        "get_camera_state" => ReadVoiceCameraState(cancellation),
        "discover_cameras" => DiscoverCamerasFromVoiceAsync(cancellation),
        "connect_camera" => ConnectCameraFromVoiceAsync(callId, arguments, cancellation),
        "host_live_session" => HostLiveSessionFromVoiceAsync(callId, cancellation),
        "claim_camera_host" => ControlCameraHostFromVoiceAsync(callId, claim: true, cancellation),
        "release_camera_host" => ControlCameraHostFromVoiceAsync(callId, claim: false, cancellation),
        "start_capture" => ChangeVoiceCaptureAsync(callId, CaptureAction.Start, cancellation),
        "pause_capture" => ChangeVoiceCaptureAsync(callId, CaptureAction.Pause, cancellation),
        "resume_capture" => ChangeVoiceCaptureAsync(callId, CaptureAction.Resume, cancellation),
        "stop_capture" => ChangeVoiceCaptureAsync(callId, CaptureAction.Stop, cancellation),
        "choose_chromecast" => ChooseChromecastFromVoiceAsync(cancellation),
        "cast_live_table" => CastLiveTableFromVoiceAsync(cancellation),
        "stop_cast" => StopCastFromVoiceAsync(cancellation),
        "suggest_shots" => SuggestVoiceShotsAsync(callId, cancellation),
        "explain_shot" => ExplainVoiceShot(arguments, cancellation),
        "select_shot_by_number" => SelectVoiceShotByNumberAsync(callId, arguments, cancellation),
        "select_shot_by_id" => SelectVoiceShotByIdAsync(callId, arguments, cancellation),
        "clear_shots" => ClearVoiceShotsAsync(callId, cancellation),
        "get_match_state" => Task.FromResult(GameVoiceTools().Read(cancellation)),
        "review_settled_table_change" => ReviewSettledTableChangeFromVoiceAsync(arguments, cancellation),
        "start_match" => StartMatchFromVoiceAsync(callId, arguments, cancellation),
        "start_frame" => StartFrameFromVoiceAsync(callId, arguments, cancellation),
        "finish_match" => FinishMatchFromVoiceAsync(callId, arguments, cancellation),
        "declare_match_colours" => DeclareColoursFromVoiceAsync(callId, arguments, cancellation),
        "correct_match_state" => CorrectMatchFromVoiceAsync(callId, arguments, cancellation),
        "undo_match_decision" => UndoMatchFromVoiceAsync(callId, arguments, cancellation),
        "record_declared_shot" => RecordDeclaredShotFromVoiceAsync(callId, arguments, cancellation),
        "save_recent_clip" => SaveRecentClipFromVoiceAsync(callId, arguments, cancellation),
        "get_saved_clips" => GetSavedClipsFromVoiceAsync(cancellation),
        "play_saved_clip" => PlaySavedClipFromVoiceAsync(arguments, cancellation),
        "replay_recent" => ReplayRecentFromVoiceAsync(arguments, cancellation),
        "control_tv_replay" => ControlTvReplayFromVoiceAsync(callId, arguments, cancellation),
        "return_to_live" => ReturnToLiveFromVoiceAsync(cancellation),
        _ => Task.FromResult<object>(new { error = "That table action is not supported." })
    };

    Task<object> StartVoiceVisionDiagnosticsAsync(CancellationToken cancellation) => MainThread.InvokeOnMainThreadAsync<object>(() =>
    {
        cancellation.ThrowIfCancellationRequested();
        if (!CanAnalyzeTable || EditingTable)
            return new { error = "Vision diagnostics need the active live camera, host control, and a completed table edit." };
        bool started = table.StartVisionDiagnostics(out var message);
        return started ? (object)new { started = true, status = message, affectsTableState = false }
            : new { error = message };
    });

    object ReadVoiceVisionDiagnostics(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var snapshot = table.VisionDiagnostics;
        if (snapshot.LabelFrame is not null || snapshot.LabelCapturePending)
            return new { active = snapshot.Active, snapshot.SessionId, snapshot.StartedAt, snapshot.Status,
                snapshot.TotalSamples, labeling = true, detectorOutputHidden = true, affectsTableState = false };
        return new
        {
            active = snapshot.Active,
            sessionId = snapshot.SessionId,
            snapshot.StartedAt,
            snapshot.Status,
            snapshot.TotalSamples,
            samples = snapshot.Samples.TakeLast(3).ToArray(),
            affectsTableState = false,
            limitation = "These are per-frame detector diagnostics. They are not confirmed table state or player-declared match facts."
        };
    }

    async Task<object> StopVoiceVisionDiagnosticsAsync(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        await table.StopVisionDiagnosticsAsync().WaitAsync(cancellation).ConfigureAwait(false);
        var snapshot = table.VisionDiagnostics;
        return new { active = snapshot.Active, snapshot.Status, snapshot.TotalSamples, stopped = !snapshot.Active };
    }

    Task<object> CalibrateTableFromVoiceAsync(CancellationToken cancellation) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        cancellation.ThrowIfCancellationRequested();
        if (SessionMode == TableSessionMode.Dark)
            return (object)new { error = "Image-based calibration is unavailable during a dark session." };
        if (!CanAnalyzeTable || foreground is null)
            return (object)new { error = "Open the active live camera as host before calibrating the table." };
        if (EditingTable)
            return (object)new { error = "A table edit is already open. Finish or cancel it before starting calibration." };

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(foreground.Token, cancellation);
        try { await PrepareTableCalibrationAsync(linked.Token); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { return (object)new { error = "A fresh camera frame was unavailable. Keep the live view open and try calibration again." }; }
        linked.Token.ThrowIfCancellationRequested();
        if (edit.Mode != TableEditMode.Calibration)
            return (object)new { error = "Calibration could not start. Check that the live camera is still active." };

        return (object)new
        {
            prepared = true,
            automaticCornersFound = edit.Corners.Count == 4,
            proposedCornerCount = edit.Corners.Count,
            reviewRequired = true,
            nextStep = edit.Corners.Count == 4
                ? "Review the four proposed points on the frozen phone frame. Apply them only after the player explicitly approves."
                : "The automatic detector did not find four corners. Complete the four corner picks on the frozen phone frame before applying."
        };
    });

    Task<object> ApplyTableCalibrationFromVoiceAsync(string callId, CancellationToken cancellation) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        cancellation.ThrowIfCancellationRequested();
        if (edit.Mode != TableEditMode.Calibration || !EditingTable)
            return (object)new { error = "There is no current table corner review to apply. Ask Sky to start calibration first." };
        if (edit.Corners.Count != 4 || !CanApplyTableEdit)
            return (object)new { error = "Four current table corners are required. Finish or adjust the corner review on the phone first." };
        if (foreground is null)
            return (object)new { error = "The live camera session ended before the calibration could be applied." };

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(foreground.Token, cancellation);
        await ApplyTableEditAsync(CommandOrigin.Sky, VoiceOperationId(callId, "calibration-sweep"), linked.Token);
        linked.Token.ThrowIfCancellationRequested();
        var snapshot = table.State;
        var observation = app.State.Observation is { } current
            && current.AnalysisGeneration == snapshot.Generation && current.Source == snapshot.Source ? current : null;
        return (object)new
        {
            applied = snapshot.Calibration is not null,
            cornersApplied = snapshot.Calibration?.Corners.Length ?? 0,
            sweep = new
            {
                outcome = observation?.Outcome.ToString() ?? "unavailable",
                motion = observation?.Motion.ToString() ?? "unknown",
                quality = observation?.Quality.ToString() ?? "unknown",
                independentFrames = observation?.Verification?.IndependentFrames ?? 0,
                status = lastVerificationStatus ?? shotMessage
            },
            layout = snapshot.Table is { } layout ? new
            {
                ballCount = layout.Balls.Length,
                settled = layout.Settled,
                occluded = layout.Occluded,
                confirmed = layout.Confirmed
            } : null
        };
    });

    SkyVoiceGameTools GameVoiceTools() => new(() => app.State,
        (envelope, token) => app.DispatchAsync(envelope, token), VoiceOperationId,
        () => (PhysicsAvailable, PhysicsStatus), LiveOutcomeProposalForVoice, ReviewLiveOutcomeFromSkyAsync);

    Task<object> ReviewSettledTableChangeFromVoiceAsync(JsonElement args, CancellationToken cancellation)
    {
        if (!Guid.TryParse(StringArg(args, "candidate_id"), out var candidateId))
            return Task.FromResult<object>(new { succeeded = false, gameVersion = app.State.Game.Version,
                message = "Read the current match state and use its live candidate ID." });
        return GameVoiceTools().ReviewSettledTableChangeAsync(candidateId,
            StringArg(args, "action") ?? "", NullableIntArg(args, "reds"), NullableIntArg(args, "yellows"),
            NullableIntArg(args, "cue_balls"), NullableIntArg(args, "black_balls"), cancellation);
    }

    Task<object> StartMatchFromVoiceAsync(string callId, JsonElement args, CancellationToken cancellation)
        => GameVoiceTools().StartMatchAsync(callId, GameVersion(args), StringArg(args, "first_player") ?? "",
            StringArg(args, "second_player") ?? "", IntArg(args, "target"), cancellation);

    Task<object> StartFrameFromVoiceAsync(string callId, JsonElement args, CancellationToken cancellation)
        => GameVoiceTools().StartFrameAsync(callId, GameVersion(args), StringArg(args, "starting_player") ?? "", cancellation);

    Task<object> FinishMatchFromVoiceAsync(string callId, JsonElement args, CancellationToken cancellation)
        => GameVoiceTools().FinishMatchAsync(callId, GameVersion(args), cancellation);

    Task<object> DeclareColoursFromVoiceAsync(string callId, JsonElement args, CancellationToken cancellation)
        => GameVoiceTools().DeclareColoursAsync(callId, GameVersion(args), StringArg(args, "group") ?? "",
            StringArg(args, "player_name"), cancellation);

    Task<object> CorrectMatchFromVoiceAsync(string callId, JsonElement args, CancellationToken cancellation)
        => GameVoiceTools().CorrectAsync(callId, GameVersion(args), StringArg(args, "kind") ?? "",
            StringArg(args, "player_name"), StringArg(args, "group"), NullableBoolArg(args, "foul"),
            StringArg(args, "ball_id"), StringArg(args, "presence"), cancellation);

    Task<object> UndoMatchFromVoiceAsync(string callId, JsonElement args, CancellationToken cancellation)
        => GameVoiceTools().UndoAsync(callId, GameVersion(args), cancellation);

    Task<object> RecordDeclaredShotFromVoiceAsync(string callId, JsonElement args, CancellationToken cancellation)
    {
        if (!Enum.TryParse<DeclaredLegality>(StringArg(args, "legality"), true, out var legality))
            return Task.FromResult<object>(new { error = "The declared shot legality is missing or unsupported." });
        if (!TryBoolArg(args, "is_break", out var isBreak) || !TryBoolArg(args, "black_potted", out var blackPotted)
            || !TryBoolArg(args, "cue_ball_potted", out var cuePotted) || !TryBoolArg(args, "cue_ball_off_table", out var cueOff)
            || !TryBoolArg(args, "replace_latest", out var replaceLatest)
            || IntArg(args, "reds_potted") is < 0 or > 7 || IntArg(args, "yellows_potted") is < 0 or > 7)
            return Task.FromResult<object>(new { error = "State each required shot fact explicitly and keep potted counts from zero to seven." });
        if (!TryColour(StringArg(args, "first_contact"), includeUnknown: true, out var firstContact)
            || !TryColour(StringArg(args, "called_group"), includeUnknown: false, out var calledGroup))
            return Task.FromResult<object>(new { error = "Use red, yellow or black for first contact and red or yellow for a called group." });
        var facts = new DeclaredShot(isBreak, legality, IntArg(args, "reds_potted"), IntArg(args, "yellows_potted"),
            blackPotted, cuePotted, cueOff,
            NullableBoolArg(args, "legal_break"), firstContact, NullableBoolArg(args, "call_required"), calledGroup,
            NullableIntArg(args, "reds_before"), NullableIntArg(args, "yellows_before"));
        return GameVoiceTools().RecordShotAsync(callId, GameVersion(args), facts, replaceLatest, cancellation);
    }

    static long GameVersion(JsonElement args) => args.TryGetProperty("expected_game_version", out var value) && value.TryGetInt64(out var version) ? version : -1;
    static string? StringArg(JsonElement args, string name) => args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    static int IntArg(JsonElement args, string name) => args.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : -1;
    static int? NullableIntArg(JsonElement args, string name) => !args.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null ? null : value.TryGetInt32(out var number) ? number : null;
    static bool TryBoolArg(JsonElement args, string name, out bool result)
    {
        result = false;
        if (!args.TryGetProperty(name, out var value)) return false;
        if (value.ValueKind == JsonValueKind.True) { result = true; return true; }
        if (value.ValueKind == JsonValueKind.False) return true;
        return false;
    }
    static bool? NullableBoolArg(JsonElement args, string name) => !args.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null ? null
        : value.ValueKind == JsonValueKind.True ? true : value.ValueKind == JsonValueKind.False ? false : null;
    static bool TryColour(string? value, bool includeUnknown, out BallColour? colour)
    {
        colour = value?.Trim().ToLowerInvariant() switch
        {
            "red" or "reds" => BallColour.Red,
            "yellow" or "yellows" => BallColour.Yellow,
            "black" when includeUnknown => BallColour.Black,
            "unknown" when includeUnknown => null,
            "none" when !includeUnknown => null,
            null => null,
            _ => (BallColour?)BallColour.Unknown
        };
        return colour != BallColour.Unknown;
    }

    async Task<object> SaveRecentClipFromVoiceAsync(string callId, JsonElement args, CancellationToken cancellation)
    {
        int seconds = IntArg(args, "seconds");
        if (!ReplaySelection.Durations.Contains(seconds))
            return new { saved = false, error = "Choose 30, 60, 120, 300 or 600 seconds." };
        var state = await MainThread.InvokeOnMainThreadAsync(() => app.State);
        var camera = state.Camera;
        if (!active || foreground is null || !camera.CanControl || camera.Scope is not { } scope || string.IsNullOrWhiteSpace(client.HostToken))
            return new { saved = false, error = "Connect and become camera host before saving a clip." };
        var command = new SaveClip(seconds, new ClipSaveScope(scope.CameraIdentity, scope.SessionId, scope.HostGeneration, scope.CaptureRevision));
        var receipt = await app.DispatchAsync(new(VoiceOperationId(callId, "save-clip-" + seconds), CommandOrigin.Sky, state.Version, command), cancellation);
        if (receipt.Succeeded || receipt.Outcome == CommandOutcome.Uncertain)
            await LoadClipsAsync(cancellation);
        return new
        {
            saved = receipt.Succeeded,
            outcome = receipt.Outcome.ToString(),
            receipt.Duplicate,
            durationSeconds = receipt.SavedClip?.Duration,
            name = receipt.SavedClip?.Name,
            message = receipt.Message,
            clipsRefreshed = receipt.Succeeded || receipt.Outcome == CommandOutcome.Uncertain
        };
    }

    async Task<object> GetSavedClipsFromVoiceAsync(CancellationToken cancellation)
    {
        var (choices, error) = await RefreshVoiceClipsAsync(cancellation);
        return error is not null
            ? new { available = false, clips = Array.Empty<object>(), limitation = error }
            : new { available = true, clips = choices.Take(20).Select((clip, index) => new
                { number = index + 1, clip.Name, durationSeconds = clip.Duration, availableOnPhone = clip.Available }).ToArray(), limitation = choices.Length == 0 ? "No saved clips were returned by the camera." : null };
    }

    async Task<object> PlaySavedClipFromVoiceAsync(JsonElement args, CancellationToken cancellation)
    {
        int number = IntArg(args, "clip_number");
        string target = StringArg(args, "target")?.Trim().ToLowerInvariant() ?? "";
        if (number < 1 || number > 20 || target is not ("phone" or "tv"))
            return new { playing = false, error = "Choose a current clip number and target phone or tv." };
        var (choices, error) = await RefreshVoiceClipsAsync(cancellation);
        if (error is not null) return new { playing = false, error };
        if (number > choices.Length) return new { playing = false, error = "That clip number is no longer available. Ask Sky to list saved clips again." };
        var choice = choices[number - 1];
        if (target == "phone")
        {
            if (!choice.Available)
            {
                await DownloadClipAsync(choice, cancellation);
                if (!LocalClips.Any(clip => clip.Id == choice.Id))
                    return new { playing = false, error = ClipStatus };
            }
            await PlayClipAsync(choice.Id, cancellation);
            var phone = await MainThread.InvokeOnMainThreadAsync(() => (selected: PhoneSavedClipSelected, playing: PhoneIsPlayingSavedClip));
            return new { playing = phone.playing, prepared = phone.selected, target, name = choice.Name,
                message = phone.playing ? "Saved clip is playing on the phone, with room audio."
                    : phone.selected ? "The saved clip is prepared on the phone; playback has not yet been confirmed." : "Phone playback did not start." };
        }

        bool connected = await MainThread.InvokeOnMainThreadAsync(() => { ObserveCast(); return TvConnected; });
        if (!connected) return new { playing = false, target, error = "Choose a Chromecast before playing a saved clip on TV." };
        string? previous = await MainThread.InvokeOnMainThreadAsync(() => tvReplay.Request);
        await PlayClipOnTvAsync(choice, cancellation);
        string? request = await MainThread.InvokeOnMainThreadAsync(() => tvReplay.Request);
        if (request is null || request == previous) return new { playing = false, target, name = choice.Name, error = ClipStatus };
        return await WaitForTvReplayStartAsync(request, choice.Name, target, cancellation);
    }

    async Task<object> ReplayRecentFromVoiceAsync(JsonElement args, CancellationToken cancellation)
    {
        int seconds = IntArg(args, "seconds");
        if (!RecentReplay.Durations.Contains(seconds)) return new { playing = false, error = "Choose 15, 30 or 60 seconds." };
        bool ready = await MainThread.InvokeOnMainThreadAsync(() => { ObserveCast(); return CanRequestRecentReplay; });
        if (!ready) return new { playing = false, error = "A reachable active camera and connected Chromecast are required for recent replay." };
        string? previous = await MainThread.InvokeOnMainThreadAsync(() => tvReplay.Request);
        await RecentReplayAsync(seconds, cancellation);
        string? request = await MainThread.InvokeOnMainThreadAsync(() => tvReplay.Request);
        if (request is null || request == previous) return new { playing = false, error = RecentReplayStatus };
        return await WaitForTvReplayStartAsync(request, $"Recent replay · {seconds} seconds", "tv", cancellation);
    }

    async Task<object> WaitForTvReplayStartAsync(string request, string name, string target, CancellationToken cancellation)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            var result = await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ObserveCast();
                bool current = tvReplay.Request == request;
                bool playing = current && tvReplay.Confirmed && tvReplay.PlayerState == 2;
                return (current, playing, state: tvReplay.PlayerState, connected: TvConnected, status: TvStatus);
            });
            if (!result.connected) return new { playing = false, confirmed = false, target, name, error = "The Chromecast disconnected before playback was confirmed." };
            if (!result.current) return new { playing = false, confirmed = false, target, name, error = "A newer TV request replaced this replay." };
            if (result.playing) return new { playing = true, confirmed = true, target, name, status = result.status };
            if (result.status.Contains("could not", StringComparison.OrdinalIgnoreCase))
                return new { playing = false, confirmed = false, target, name, error = result.status };
            await Task.Delay(250, cancellation);
        }
        string status = await MainThread.InvokeOnMainThreadAsync(() => TvStatus);
        return new { playing = false, confirmed = false, requestSent = true, target, name, status, limitation = "The receiver has not confirmed playback yet." };
    }

    async Task<object> ControlTvReplayFromVoiceAsync(string callId, JsonElement args, CancellationToken cancellation)
    {
        string actionText = StringArg(args, "action")?.Trim().ToLowerInvariant() ?? "";
        if (!Enum.TryParse<TvReplayAction>(actionText, true, out var action))
            return new { confirmed = false, error = "Choose pause, resume or seek." };
        long seconds = args.TryGetProperty("seconds", out var position) && position.TryGetInt64(out var parsed) ? parsed : -1;
        var started = await MainThread.InvokeOnMainThreadAsync(() =>
        {
            cancellation.ThrowIfCancellationRequested();
            ObserveCast();
            if (!TvConnected || !tvReplay.Selected) return ((string?)null, (string?)"No current TV replay is selected.");
            if (action == TvReplayAction.Pause && !CanPauseTvReplay) return ((string?)null, (string?)"The current TV replay cannot be paused now.");
            if (action == TvReplayAction.Resume && !CanResumeTvReplay) return ((string?)null, (string?)"The current TV replay cannot be resumed now.");
            if (action == TvReplayAction.Seek && (!CanSeekTvReplay || seconds < 0 || seconds > TvReplayDuration))
                return ((string?)null, (string?)"Seek needs a supported current position in the TV replay.");
            string? request = TvReplayRequest;
            ControlTvReplay(request, action, action == TvReplayAction.Seek ? seconds : 0);
            if (tvReplay.Command is not { } command) return ((string?)null, (string?)"The TV did not accept a replay-control request.");
            return ((string?)command.Id, (string?)null);
        });
        if (started.Item2 is not null) return new { confirmed = false, error = started.Item2 };
        for (int attempt = 0; attempt < 48; attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            var result = await MainThread.InvokeOnMainThreadAsync(() =>
            {
                ObserveCast(); ExpireTvCommands();
                return tvReplay.Command?.Id == started.Item1
                    ? (outcome: tvReplay.CommandOutcome, status: TvCommandStatus)
                    : (outcome: TvCommandOutcome.Unknown, status: "A newer TV request replaced this control command.");
            });
            if (result.outcome == TvCommandOutcome.Confirmed) return new { confirmed = true, action = actionText, seconds = action == TvReplayAction.Seek ? seconds : (long?)null, status = result.status };
            if (result.outcome is TvCommandOutcome.Rejected or TvCommandOutcome.Unknown)
                return new { confirmed = false, action = actionText, status = result.status, error = result.status };
            await Task.Delay(250, cancellation);
        }
        string finalStatus = await MainThread.InvokeOnMainThreadAsync(() => TvCommandStatus);
        return new { confirmed = false, action = actionText, requestSent = true, status = finalStatus, limitation = "The TV has not confirmed this replay control." };
    }

    async Task<object> ReturnToLiveFromVoiceAsync(CancellationToken cancellation)
    {
        bool hadTv = await MainThread.InvokeOnMainThreadAsync(() => { ObserveCast(); return TvConnected; });
        await WatchLiveAsync(cancellation);
        bool phoneLive = await MainThread.InvokeOnMainThreadAsync(() => HasSource && selection.Kind == ViewingSource.Live);
        if (!phoneLive) return new { live = false, phone = false, tv = false, error = Message };
        if (!hadTv) return new { live = true, phone = true, tv = false, limitation = "The phone returned to live; no Chromecast is connected." };
        string? request = await MainThread.InvokeOnMainThreadAsync(() =>
        {
            cancellation.ThrowIfCancellationRequested();
            ObserveCast();
            if (!TvConnected) return null;
            PlayTv();
            return tvLive.Request;
        });
        if (request is null) return new { live = true, phone = true, tv = false, error = "The phone is live, but the Chromecast disconnected." };
        for (int attempt = 0; attempt < 60; attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            var status = await MainThread.InvokeOnMainThreadAsync(() =>
            { ObserveCast(); return (current: tvLive.Request == request, outcome: tvLive.Outcome, text: TvStatus); });
            if (status.current && status.outcome == TvCommandOutcome.Confirmed)
                return new { live = true, phone = true, tv = true, confirmed = true, status = status.text };
            if (status.outcome == TvCommandOutcome.Unknown) break;
            await Task.Delay(250, cancellation);
        }
        var final = await MainThread.InvokeOnMainThreadAsync(() => TvStatus);
        return new { live = true, phone = true, tv = false, confirmed = false, status = final, limitation = "The phone is live, but the TV has not confirmed live playback." };
    }

    async Task<(ShellClipChoice[] Choices, string? Error)> RefreshVoiceClipsAsync(CancellationToken cancellation)
    {
        var ready = await MainThread.InvokeOnMainThreadAsync(() => active && foreground is not null && CanLoadClips);
        if (!ready) return ([], "Connect a reachable camera before listing saved clips.");
        for (int attempt = 0; attempt < 40; attempt++)
        {
            bool loading = await MainThread.InvokeOnMainThreadAsync(() => LoadingClips);
            if (!loading) break;
            await Task.Delay(100, cancellation);
        }
        if (await MainThread.InvokeOnMainThreadAsync(() => LoadingClips)) return ([], "The saved-clip list is still refreshing. Ask again shortly.");
        await LoadClipsAsync(cancellation);
        for (int attempt = 0; attempt < 300; attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            var loading = await MainThread.InvokeOnMainThreadAsync(() => LoadingClips);
            if (!loading) break;
            await Task.Delay(100, cancellation);
        }
        return await MainThread.InvokeOnMainThreadAsync(() => LoadingClips
            ? ([], "The saved-clip list is still refreshing.")
            : clipCatalogError is { } error ? ([], error)
            : (RemoteClips.ToArray(), (string?)null));
    }

    Task<object> ReadVoiceCameraState(CancellationToken cancellation) => MainThread.InvokeOnMainThreadAsync(() =>
    {
        cancellation.ThrowIfCancellationRequested();
        ObserveCast();
        var camera = app.State.Camera;
        return (object)new
        {
            connected = camera.ConnectionId != Guid.Empty && camera.Reachability == CameraReachability.Reachable,
            reachability = camera.Reachability.ToString(),
            sessionMode = SessionMode.ToString().ToLowerInvariant(),
            ownership = camera.Ownership.ToString(),
            host = camera.HostOwner,
            canControl = camera.CanControl,
            capture = camera.Capture?.ToString() ?? "unknown",
            armed = camera.Armed,
            chromecastConnected = TvConnected,
            chromecastStatus = TvStatus,
            physicsAvailable = PhysicsAvailable,
            physicsStatus = PhysicsStatus
        };
    });

    async Task<object> DiscoverCamerasFromVoiceAsync(CancellationToken cancellation)
    {
        var cameras = await FindCamerasFromVoiceAsync(cancellation);
        return cameras.Count == 0
            ? new { found = false, cameras = Array.Empty<object>(), limitation = Message.Length > 0 ? Message : "No nearby camera was found." }
            : new { found = true, cameras = cameras.Select((camera, index) => new { camera_index = index + 1, camera.Name }).ToArray(), limitation = (string?)null };
    }

    async Task<object> ConnectCameraFromVoiceAsync(string callId, JsonElement arguments, CancellationToken cancellation)
    {
        string address;
        if (arguments.TryGetProperty("camera_index", out var indexValue))
        {
            if (!indexValue.TryGetInt32(out int index) || index < 1 || index > Cameras.Count)
                return new { error = "That discovered camera is no longer available. Ask Sky to find cameras again." };
            address = Cameras[index - 1].Address;
        }
        else address = SavedAddress;

        if (string.IsNullOrWhiteSpace(address)) return new { error = "No saved camera address is available. Ask Sky to find nearby cameras." };
        cancellation.ThrowIfCancellationRequested();
        var receipt = await ConnectCameraFromVoiceAsync(address, callId, cancellation);
        var camera = app.State.Camera;
        bool reachable = camera.ConnectionId != Guid.Empty && camera.Reachability == CameraReachability.Reachable;
        if (receipt is null) return new { connected = false, reachability = camera.Reachability.ToString(), error = Message.Length > 0 ? Message : "Camera connection did not finish." };
        return new
        {
            connected = reachable,
            requestAccepted = receipt.Succeeded,
            reachability = camera.Reachability.ToString(),
            ownership = camera.Ownership.ToString(),
            capture = camera.Capture?.ToString() ?? "unknown",
            message = receipt.Message
        };
    }

    async Task<object> HostLiveSessionFromVoiceAsync(string callId, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        if (!await MainThread.InvokeOnMainThreadAsync(() => active && foreground is not null))
            return new { ready = false, stage = "app", error = "The live table controls are not active." };

        var camera = await MainThread.InvokeOnMainThreadAsync(() => app.State.Camera);
        if (camera.ConnectionId == Guid.Empty || camera.Reachability != CameraReachability.Reachable)
        {
            string address = SavedAddress;
            if (string.IsNullOrWhiteSpace(address))
                return new { ready = false, stage = "connect", error = "No saved camera is available. Find nearby cameras and choose one first." };

            var connection = await ConnectCameraFromVoiceAsync(address, callId, cancellation);
            if (connection is null || !connection.Succeeded)
                return new { ready = false, stage = "connect", error = connection?.Message ?? "Camera connection did not finish." };

            camera = await MainThread.InvokeOnMainThreadAsync(() => app.State.Camera);
            if (camera.ConnectionId == Guid.Empty || camera.Reachability != CameraReachability.Reachable)
                return new { ready = false, stage = "connect", reachability = camera.Reachability.ToString(), error = "The saved camera did not become reachable." };
        }

        if (!camera.CanControl)
        {
            if (camera.Ownership != CameraOwnership.Available)
                return new { ready = false, stage = "host", ownership = camera.Ownership.ToString(), error = "Host control is not available on this camera." };

            await ControlCameraHostFromVoiceAsync(callId, claim: true, cancellation);
            camera = await MainThread.InvokeOnMainThreadAsync(() => app.State.Camera);
            if (!camera.CanControl)
                return new { ready = false, stage = "host", ownership = camera.Ownership.ToString(), error = "The camera did not grant this phone host control." };
        }

        if (camera.Capture != CaptureState.Active)
        {
            await ChangeVoiceCaptureAsync(callId, CaptureAction.Start, cancellation);
            camera = await MainThread.InvokeOnMainThreadAsync(() => app.State.Camera);
            if (camera.Capture != CaptureState.Active)
                return new { ready = false, stage = "capture", host = camera.CanControl, capture = camera.Capture?.ToString() ?? "unknown", error = "Host control is ready, but camera capture did not start." };
        }

        sessionMode = TableSessionMode.Live;
        Publish();
        return new { ready = true, connected = true, host = camera.CanControl, capture = camera.Capture?.ToString() ?? "unknown",
            sessionMode = SessionMode.ToString().ToLowerInvariant(), physicsAvailable = PhysicsAvailable, physicsStatus = PhysicsStatus,
            next = "The live session is ready to cast." };
    }

    Task<object> ControlCameraHostFromVoiceAsync(string callId, bool claim, CancellationToken cancellation) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        cancellation.ThrowIfCancellationRequested();
        if (!active || foreground is null) return (object)new { error = "The table controls are not active." };
        var camera = app.State.Camera;
        if (camera.Scope is not { } scope || camera.Reachability != CameraReachability.Reachable)
            return new { error = "Connect to a reachable camera before changing host control." };
        if (claim && camera.CanControl) return new { host = true, alreadyHost = true, capture = camera.Capture?.ToString() ?? "unknown" };
        if (claim && camera.Ownership != CameraOwnership.Available)
            return new { error = "Host control is not available on this camera. It may already have a host." };
        if (!claim && !camera.CanControl) return new { error = "This phone does not currently own camera host control." };

        var command = claim ? (CameraCommand)new ClaimCameraHost(scope, HostName) : new ReleaseCameraHost(scope);
        var receipt = await ExecuteApplicationCommandAsync(new(VoiceOperationId(callId, claim ? "claim-host" : "release-host"),
            CommandOrigin.Sky, app.State.Version, command), cancellation);
        var current = app.State.Camera;
        if (!receipt.Succeeded) return new { error = receipt.Message, ownership = current.Ownership.ToString() };
        return new { host = current.CanControl, ownership = current.Ownership.ToString(), capture = current.Capture?.ToString() ?? "unknown", message = receipt.Message };
    });

    Task<object> ChangeVoiceCaptureAsync(string callId, CaptureAction action, CancellationToken cancellation) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        cancellation.ThrowIfCancellationRequested();
        if (!active || foreground is null) return (object)new { error = "The table controls are not active." };
        var camera = app.State.Camera;
        if (camera.Scope is not { } scope || camera.Reachability != CameraReachability.Reachable)
            return new { error = "Connect to a reachable camera before changing capture." };
        if (!camera.CanControl) return new { error = "Become host before controlling camera capture." };
        var receipt = await ExecuteApplicationCommandAsync(new(VoiceOperationId(callId, "capture-" + action.ToString().ToLowerInvariant()),
            CommandOrigin.Sky, app.State.Version, new ChangeCapture(scope, action)), cancellation);
        var current = app.State.Camera;
        return receipt.Succeeded
            ? new { succeeded = true, capture = current.Capture?.ToString() ?? "unknown", message = receipt.Message }
            : new { succeeded = false, capture = current.Capture?.ToString() ?? "unknown", error = receipt.Message };
    });

    async Task<object> ChooseChromecastFromVoiceAsync(CancellationToken cancellation)
    {
        bool opened = await ChooseTvFromVoiceAsync(cancellation);
        return opened
            ? new { pickerOpened = true, next = "Ask the user to choose a Chromecast. Then cast the live table only after the connection is reported." }
            : new { pickerOpened = false, error = Message.Length > 0 ? Message : "The Chromecast picker could not be opened." };
    }

    async Task<object> CastLiveTableFromVoiceAsync(CancellationToken cancellation)
    {
        bool pickerOpened = false;
        string? preflight = await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            cancellation.ThrowIfCancellationRequested();
            if (!active || foreground is null) return "The table controls are not active.";
            ObserveCast();
            if (!CanCastLive) return "Connect a reachable camera and start capture before casting the live table.";
            if (!TvConnected)
            {
                pickerOpened = await ChooseTvCoreAsync(cancellation);
                return pickerOpened ? null : (Message.Length > 0 ? Message : "The Chromecast picker could not be opened.");
            }
            PlayTv();
            return null;
        });
        if (preflight is not null)
            return new { error = preflight };

        if (pickerOpened)
        {
            bool selected = false;
            for (int attempt = 0; attempt < 240; attempt++)
            {
                cancellation.ThrowIfCancellationRequested();
                selected = await MainThread.InvokeOnMainThreadAsync(() => { ObserveCast(); return TvConnected; });
                if (selected) break;
                await Task.Delay(250, cancellation);
            }
            if (!selected)
                return new { playing = false, pickerOpened = true, confirmed = false, next = "No Chromecast was selected. Choose one in the open Cast picker, then ask Sky to cast again." };

            string? playError = await MainThread.InvokeOnMainThreadAsync(() =>
            {
                cancellation.ThrowIfCancellationRequested();
                ObserveCast();
                if (!TvConnected) return "The Chromecast disconnected before playback started.";
                if (!CanCastLive) return "Camera capture stopped before the Cast session started.";
                PlayTv();
                return TvStatus.Contains("could not", StringComparison.OrdinalIgnoreCase) ? TvStatus : null;
            });
            if (playError is not null) return new { playing = false, pickerOpened = true, error = playError };
        }

        for (int attempt = 0; attempt < 40; attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            var status = await MainThread.InvokeOnMainThreadAsync(() => { ObserveCast(); return (TvConnected, TvStatus); });
            if (!status.TvConnected) return new { playing = false, error = "The Chromecast disconnected before playback was confirmed." };
            if (status.TvStatus.Contains("accepted the live playback request", StringComparison.OrdinalIgnoreCase)
                || status.TvStatus.StartsWith("TV playing", StringComparison.OrdinalIgnoreCase))
                return new { playing = true, confirmed = true, status = status.TvStatus };
            if (status.TvStatus.Contains("could not", StringComparison.OrdinalIgnoreCase))
                return new { playing = false, error = status.TvStatus };
            await Task.Delay(250, cancellation);
        }
        var finalStatus = await MainThread.InvokeOnMainThreadAsync(() => TvStatus);
        return new { playing = false, requestSent = true, confirmed = false, status = finalStatus, limitation = "The receiver has not confirmed playback yet." };
    }

    async Task<object> StopCastFromVoiceAsync(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        bool connected = await MainThread.InvokeOnMainThreadAsync(() => { ObserveCast(); return TvConnected; });
        if (!connected) return new { disconnected = true, alreadyDisconnected = true };
        await MainThread.InvokeOnMainThreadAsync(StopTv);
        for (int attempt = 0; attempt < 20; attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            bool stillConnected = await MainThread.InvokeOnMainThreadAsync(() => { ObserveCast(); return TvConnected; });
            if (!stillConnected) return new { disconnected = true, captureContinues = app.State.Camera.Capture == CaptureState.Active };
            await Task.Delay(250, cancellation);
        }
        return new { disconnected = false, requestSent = true, status = TvStatus, capture = app.State.Camera.Capture?.ToString() ?? "unknown" };
    }

    object ReadVoiceTableState(CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        var camera = app.State.Camera;
        var snapshot = table.State;
        var layout = snapshot.Table;
        if (SessionMode == TableSessionMode.Dark)
            return new { available = false, sessionMode = "dark", physicsAvailable = false,
                limitation = "This is a dark recording session. No image-based table analysis, motion tracking or shot physics is running. Match state comes from player conversation; recording and clips remain available." };
        if (!active || !CanAnalyzeTable || snapshot.Source != camera.Scope)
            return new { available = false, sessionMode = SessionMode.ToString().ToLowerInvariant(), physicsAvailable = false,
                limitation = "An active live table with host control is required. Guest controls cannot read or change the host table." };
        if (layout is null || snapshot.Calibration is null)
            return new { available = false, sessionMode = SessionMode.ToString().ToLowerInvariant(), physicsAvailable = false,
                physicsStatus = PhysicsStatus, calibrated = snapshot.Calibration is not null,
                limitation = "No current calibrated table layout is available. Calibrate, then request a fresh observation." };

        double age = (DateTimeOffset.UtcNow - layout.CapturedAt).TotalSeconds;
        var now = DateTimeOffset.UtcNow;
        var shotCandidates = ShotPlanner.Suggest(layout, now);
        var observation = snapshot.Observation;
        return new
        {
            available = true,
            sessionMode = SessionMode.ToString().ToLowerInvariant(),
            physicsAvailable = PhysicsAvailable,
            physicsStatus = PhysicsStatus,
            version = layout.Version,
            capturedAt = layout.CapturedAt,
            ageSeconds = Math.Round(age, 2),
            fresh = age is >= -2 and <= 6,
            group = layout.Group,
            settled = layout.Settled,
            occluded = layout.Occluded,
            confirmed = layout.Confirmed,
            outcome = observation?.Outcome.ToString() ?? "unknown",
            motion = observation?.Motion.ToString() ?? "unknown",
            quality = observation?.Quality.ToString() ?? "unknown",
            reason = observation?.Reason.ToString() ?? "unknown",
            shotAdviceAvailable = shotCandidates.Length > 0,
            shotAdviceLimitation = shotCandidates.Length == 0 ? NoShotCandidatesMessage(layout, now) : null,
            balls = layout.Balls.Select(ball => new { ball.Id, ball.Kind, x = ball.Position.X, y = ball.Position.Y, ball.Confidence }),
            warnings = snapshot.Vision?.Warnings ?? [],
            rules = PoolGeometry.RulesVersion,
            rulesReference = PoolGeometry.RulesUrl,
            limitation = !layout.Confirmed ? "This layout is not confirmed; it cannot support candidate advice." :
                !layout.Settled ? "The balls may still be moving; wait and request a fresh check." :
                layout.Occluded ? "The table is obstructed; clear the view and request a fresh check." :
                age is > 6 or < -2 ? "This layout is out of date; request a fresh check." : null
        };
    }

    Task<object> SuggestVoiceShotsAsync(string callId, CancellationToken cancellation) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        cancellation.ThrowIfCancellationRequested();
        if (SessionMode == TableSessionMode.Dark)
            return (object)new { error = "Shot physics and image-based table analysis are unavailable during a dark session. You can still save clips and manage the match from player reports." };
        if (!active || foreground is null || !CanShowChoices || EditingTable)
            return (object)new { error = "Open the active live table with host control before requesting shot choices." };
        if (candidateVerification is not null) return (object)new { error = "A table check is already running. Wait for it to finish before asking again." };

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(foreground.Token, cancellation);
        await RefreshShotChoicesAsync(linked.Token, VoiceOperationId(callId, "observe"), CommandOrigin.Sky);
        linked.Token.ThrowIfCancellationRequested();
        if (!CurrentShotCatalog || table.State.Table is not { } layout || app.State.Observation is not { } observation)
            return (object)new { error = lastVerificationStatus ?? "A fresh verified layout is not available. Check the live table and try again." };
        if (ShotChoices.Count == 0)
            return (object)new { error = lastVerificationStatus ?? "No supported shot was found for this layout and group." };

        voiceShotTarget = TvReplaySelected ? SceneTarget.Phone : SceneTarget.Both;
        var receipt = await ExecuteApplicationCommandAsync(new(VoiceOperationId(callId, "show"), CommandOrigin.Sky, app.State.Version,
            new SetShotDisplay(voiceShotTarget, ShotDisplayMode.Cycling)), linked.Token);
        if (!receipt.Succeeded) return (object)new { error = receipt.Message };
        return new
        {
            shown = true,
            target = voiceShotTarget.ToString(),
            tableVersion = layout.Version,
            observation = observation.Outcome.ToString(),
            ageSeconds = Math.Round((DateTimeOffset.UtcNow - layout.CapturedAt).TotalSeconds, 2),
            expiresAt = app.State.ShotChoices.ExpiresAt,
            choices = app.State.ShotChoices.Choices.Select((choice, index) => new { number = index + 1, choice.Id, choice.Type, pocket = choice.Pocket + 1 }),
            limitation = "Candidate difficulty ranking is not a pot probability. Cushion paths use ideal geometry; spin, pace, position and complete refereeing are not predicted."
        };
    });

    Task<object> SelectVoiceShotByNumberAsync(string callId, JsonElement arguments, CancellationToken cancellation) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        cancellation.ThrowIfCancellationRequested();
        if (!arguments.TryGetProperty("number", out var value) || !value.TryGetInt32(out int number))
            return (object)new { error = "Say a current candidate number from one to three." };
        if (!CanSelectVoiceShot(out var error)) return (object)new { error };
        var catalog = app.State.ShotChoices;
        if (number < 1 || number > catalog.Choices.Length) return (object)new { error = "That candidate number is no longer available. Ask Sky to show fresh choices." };
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(foreground!.Token, cancellation);
        var receipt = await ExecuteApplicationCommandAsync(new(VoiceOperationId(callId, "select"), CommandOrigin.Sky, app.State.Version,
            new ShowShot(voiceShotTarget, catalog.TableVersion, number)), linked.Token);
        if (!receipt.Succeeded) return (object)new { error = receipt.Message };
        var selected = catalog.Choices[number - 1];
        return new { selected = true, number, selected.Id, selected.Type, pocket = selected.Pocket + 1, tableVersion = catalog.TableVersion };
    });

    Task<object> SelectVoiceShotByIdAsync(string callId, JsonElement arguments, CancellationToken cancellation) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        cancellation.ThrowIfCancellationRequested();
        string? id = arguments.TryGetProperty("shot_id", out var value) ? value.GetString() : null;
        if (string.IsNullOrWhiteSpace(id)) return (object)new { error = "A current shot ID is required." };
        if (!CanSelectVoiceShot(out var error)) return (object)new { error };
        var catalog = app.State.ShotChoices;
        var selected = catalog.Choices.FirstOrDefault(choice => choice.Id == id);
        if (selected is null) return (object)new { error = "That candidate is stale or belongs to another layout. Ask Sky to show fresh choices." };
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(foreground!.Token, cancellation);
        var receipt = await ExecuteApplicationCommandAsync(new(VoiceOperationId(callId, "select"), CommandOrigin.Sky, app.State.Version,
            new PinShot(voiceShotTarget, catalog.TableVersion, selected.Id)), linked.Token);
        if (!receipt.Succeeded) return (object)new { error = receipt.Message };
        return new { selected = true, selected.Id, selected.Type, pocket = selected.Pocket + 1, tableVersion = catalog.TableVersion };
    });

    bool CanSelectVoiceShot(out string error)
    {
        if (!active || foreground is null || !CanShowChoices || EditingTable)
        { error = "Open the active live table with host control before selecting a shot."; return false; }
        if (!CurrentShotCatalog) { error = "Those choices are stale. Ask Sky to show fresh choices."; return false; }
        if (app.State.ShotChoices.Choices.IsDefaultOrEmpty) { error = "There are no current choices to select."; return false; }
        error = ""; return true;
    }

    Task<object> ExplainVoiceShot(JsonElement arguments, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        string? id = arguments.TryGetProperty("shot_id", out var value) ? value.GetString() : null;
        if (string.IsNullOrWhiteSpace(id) || !CurrentShotCatalog || table.State.Table is not { } layout)
            return Task.FromResult<object>(new { error = "That candidate is stale or unavailable. Ask Sky to show fresh choices." });
        var shot = table.State.Choices.FirstOrDefault(candidate => candidate.Id == id && candidate.TableVersion == layout.Version);
        if (shot is null) return Task.FromResult<object>(new { error = "That candidate belongs to an old layout or is unavailable." });
        return Task.FromResult<object>(new { shot.Id, shot.Type, pocket = shot.Pocket + 1, shot.CutDegrees, shot.Difficulty, shot.Explanation,
            limitation = "Difficulty is a ranking, not a probability. Spin, pace, cue-ball position and legality are not established." });
    }

    Task<object> ClearVoiceShotsAsync(string callId, CancellationToken cancellation) => MainThread.InvokeOnMainThreadAsync(async () =>
    {
        cancellation.ThrowIfCancellationRequested();
        if (!active || foreground is null || selection.Kind != ViewingSource.Live || !CanShowChoices || EditingTable)
            return (object)new { error = "Open the active live table before clearing shot paths." };
        var target = CurrentVoiceShotTarget();
        if (target is null) return (object)new { error = "No shot paths are currently shown." };
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(foreground.Token, cancellation);
        var receipt = await ExecuteApplicationCommandAsync(new(VoiceOperationId(callId, "clear"), CommandOrigin.Sky, app.State.Version,
            new ClearAnnotations(target.Value)), linked.Token);
        return receipt.Succeeded ? (object)new { cleared = true, target = target.Value.ToString() } : new { error = receipt.Message };
    });

    SceneTarget? CurrentVoiceShotTarget()
    {
        bool phone = app.State.Presentation.Phone.Mode is ShotDisplayMode.Cycling or ShotDisplayMode.Pinned;
        bool tv = app.State.Presentation.Tv.Mode is ShotDisplayMode.Cycling or ShotDisplayMode.Pinned;
        if (TvReplaySelected) tv = false;
        return (phone, tv) switch
        {
            (true, true) => SceneTarget.Both,
            (true, false) => SceneTarget.Phone,
            (false, true) => SceneTarget.Tv,
            _ => null
        };
    }

    Guid VoiceOperationId(string callId, string action)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("sky-voice\n" + voiceOperationScope.ToString("N") + "\n" + callId + "\n" + action));
        return new Guid(hash.AsSpan(0, 16));
    }
}
