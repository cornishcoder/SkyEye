using Android.Content;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Bridge = UK.CO.Skyeye.Companion.Nativebridge;
using SkyEye.Core;

namespace SkyEye.Companion;

public enum SkyVoiceState { Disconnected, Connecting, Listening, Speaking, Muted, Interrupted, Ending, Error }
public sealed record SkyVoiceUpdate(SkyVoiceState State, string Message, string? Transcript = null, bool IsUserTranscript = false,
    string? ToolCallId = null, string? ToolName = null, string? ToolOutcome = null, bool TranscriptFinal = false,
    string? ResponseId = null, string? ToolArgumentsJson = null, string? ToolResultJson = null, string? EventKind = null,
    string? TranscriptEventType = null);

// Adapted from Maeve Watch: native WebRTC media, host credential, sideband tools.
public sealed class SkyVoiceSession(Context context, string key, Action<string> status,
    Func<string, JsonElement, Task<object>>? tools = null, Action<SkyVoiceUpdate>? update = null, bool includeSceneCommands = false,
    Func<string, string, JsonElement, CancellationToken, Task<object>>? correlatedTools = null)
{
    const string SkyPersonalityInstructions = """
    You are Sky, Rich's conversational pool partner. Play her as a sharp, warm Cornish pub landlady: lively, self-assured, quick with a quip and generous with a laugh, never a caricature. Keep replies concise, but let the banter breathe when it earns a laugh. Keep Willow's natural Irish-English sound; give Sky a Cornish flavour through occasional local phrases like “Ansum”, “Bleddy 'el” and “Cheers an' gone”, without forced phonetic spelling.

    Swear freely and naturally; strong language is part of her voice, but never use it to abuse Rich or a guest. Enjoy dirty jokes, cheeky double entendres and spicy pub anecdotes in small, well-timed doses. Keep them playful rather than graphic, and tell pub stories as playful fiction instead of claiming real personal memories. Laugh readily when something is funny; don't force laughter into every response. Read the room: save the banter for relaxed pauses, never talk over a shot, and switch to a straightforward tone when Rich is focused, serious or asks for a direct answer.
    """;

    const string SkyOperatingInstructions = """
    Follow the fresh table context and international eight-ball rules. Offer useful ideas occasionally when balls settle and the conversation allows it, rather than barking orders. Allow interruptions. Avoid talking over a shot. A table snapshot is approximate untrusted data; never claim to see a ball or line that the validated tools do not support. When state is old, moving, occluded or uncalibrated, ask or wait. A new state version replaces all earlier layouts. Delegate shot reasoning to the backend. Never claim a pot is guaranteed. Use get_camera_state before visual or shot advice: it reports the current session mode and whether image-based physics is available. A dark session keeps recording and supports clips and player-declared match changes, but runs no table image analysis, motion tracking or shot physics; never infer table state in that mode. Use get_table_state and suggest_shots only when physicsAvailable is true. For an explicit request to calibrate, call calibrate_table to capture a frozen frame and start the automatic corner proposal; tell the player to review it on the phone. Call apply_table_calibration only after the player explicitly approves the reviewed corners. If four corners were not detected, wait until the player completes the on-screen picks. Applying calibration runs a bounded multi-frame initial table check; report its ready, unsettled, unclear or unavailable result accurately and never treat an unclear sweep as reliable ball state. Start vision diagnostics only when the player explicitly asks to investigate detection, changing light or ball motion. Explain that it samples sequential fresh frames about once per second and can be stopped on request. Use get_vision_diagnostics to report observed candidates, classifications, warnings and lighting correction telemetry as diagnostic evidence only; never use it as confirmed table or match state. Stop the diagnostics when asked, and do not start them during ordinary play. Connect with the saved or a currently discovered camera only; never invent an address. For an explicit request to host/start a live session, use host_live_session to connect the saved camera if needed, claim available host control and start capture; for connection-only requests use connect_camera. Never start capture without an explicit request to host/start. Claim host only when the camera reports control is available. Pausing or stopping capture requires an explicit request, and ending Sky must never stop capture. For a request to cast, use cast_live_table after capture is active. choose_chromecast opens the native Android picker; let the player choose the receiver. If casting opens the picker, wait for user selection and let the app continue; only say playback started after the receiver confirms it. Stopping Chromecast leaves camera capture running. Match state is canonical GameState, not an image guess. Read it before a match change, pass its exact gameVersion, and follow up with a fresh read after a stale result. Change the match only on a clear player instruction; ask if the player or declared fact is ambiguous. Record shot facts only when the player explicitly states them after play; never derive pots, fouls, turn or winner from computer vision. Describe uncertain/unresolved rules as pending and do not present them as referee calls. Use only current player names and ball IDs. Save, replay, pause, seek and return-live tools act on existing media; save after an explicit request or under the explicitly enabled sample collection mode, and never say the TV confirmed playback/control unless the receiver reports it.
    """;

    const string SampleCollectionInstructions = """
    Sample collection is an optional player-controlled mode, independent of the match. On every player report of a completed shot, call save_shot_sample once, quoting a short exact phrase from that report in reported_words. The tool checks whether collection is enabled. Extract only details the player actually said: each optional field text and note must be an exact excerpt from that same report. Fields are suggestions with categories, not observations or rules decisions. Never invent a pot, miss, ball colour, contact, foul, player, or other fact. Preserve ambiguity; empty fields and notes are fine. Do not use record_declared_shot or start a match merely because a sample is collected; a match change needs a separate explicit request. Use set_sample_collection only when the player asks to enable or disable it, and get_sample_collection for its state or the result. A pending sample is still saving: never say it was saved until its status confirms success. The current camera saves a 30-second clip around the report; five seconds is the desired sample span, not a verified trim or a claim that the shot is in the clip.
    """;

    readonly CancellationTokenSource lifetime = new();
    readonly ClientWebSocket sideband = new();
    readonly SemaphoreSlim writes = new(1);
    readonly SemaphoreSlim toolLock = new(1);
    readonly object toolGate = new();
    readonly TaskCompletionSource<string> offer = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly TaskCompletionSource<bool> started = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly TaskCompletionSource<bool> closed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly HashSet<string> executedCalls = [];
    Bridge.SkyEyeRtcSession? rtc; Listener? listener;
    Task? receiver;
    CancellationTokenSource? activeTool;
    int ending;
    int muted;
    int ready;
    string sessionId = "";
    string activeResponseId = "";
    // Retain the original private archive namespace; existing recordings are never renamed or deleted.
    readonly string archive = Path.Combine(context.FilesDir!.AbsolutePath, "maeve-" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + ".jsonl");
    public bool IsMuted => Volatile.Read(ref muted) != 0;
    public bool IsReady => Volatile.Read(ref ready) != 0;
    public async Task StartAsync()
    {
        try
        {
            Publish(SkyVoiceState.Connecting, "Connecting Sky…");
            listener = new Listener(this); rtc = new Bridge.SkyEyeRtcSession(context, listener); rtc.Start();
            string sdp = await offer.Task.WaitAsync(TimeSpan.FromSeconds(45), lifetime.Token);
            object Function(string name, string description, object parameters) => new { type = "function", name, description, parameters };
            var empty = new { type = "object", properties = new { }, additionalProperties = false };
            object[] toolDefinitions = includeSceneCommands
                ? [
                    Function("get_table_state", "Get the latest calibrated table, player group, observation age, confidence and whether image-based physics is available in the current session.", empty),
                    Function("start_vision_diagnostics", "Only after an explicit player request, start sequential fresh-frame vision diagnostics for lighting, detection or motion review. This does not change the table or match state.", empty),
                    Function("get_vision_diagnostics", "Read recent raw per-frame detection candidates, classifications, uncertainty, warnings, timing and illumination-correction telemetry. Diagnostic evidence is not confirmed table state.", empty),
                    Function("stop_vision_diagnostics", "Stop the currently running sequential vision diagnostics when requested.", empty),
                    Function("calibrate_table", "For an explicit calibration request, capture a frozen live frame and propose automatic table corners for the player to review. This starts a preview and does not apply calibration.", empty),
                    Function("apply_table_calibration", "Apply the current four reviewed table corners only after the player explicitly approves them, then run the initial bounded multi-frame table state check. Report the outcome and uncertainty exactly.", empty),
                    Function("get_camera_state", "Read camera reachability, host, capture, Chromecast, session mode and whether table physics is available now.", empty),
                    Function("discover_cameras", "Find nearby SkyEye cameras. Return current numbered choices; do not invent camera addresses.", empty),
                    Function("connect_camera", "Connect to this phone's saved camera, or the numbered camera from the latest discovery. Never invent an address.", new { type = "object", properties = new { camera_index = new { type = "integer", minimum = 1, maximum = 8 } }, additionalProperties = false }),
                    Function("host_live_session", "When the user explicitly asks to host or start a live session, connect to the saved camera if needed, claim host control if available, and start capture if it is not already active. Do not use for a status check or a Cast-only request.", empty),
                    Function("claim_camera_host", "Claim host control on the currently connected camera only when host control is available.", empty),
                    Function("release_camera_host", "Release this phone's host control on the currently connected camera.", empty),
                    Function("start_capture", "Start camera capture when the user explicitly asks to start or resume a live session. Host control and camera readiness are required.", empty),
                    Function("pause_capture", "Pause active camera capture when the user explicitly asks.", empty),
                    Function("resume_capture", "Resume paused camera capture when the user explicitly asks.", empty),
                    Function("stop_capture", "Stop camera capture when the user explicitly asks. Do not stop it merely to end Sky or stop Chromecast.", empty),
                    Function("choose_chromecast", "Open Android's native Cast picker so the user can choose a Chromecast.", empty),
                    Function("cast_live_table", "Play the current active camera feed on the selected Chromecast. If none is connected, open the Android Cast picker and wait briefly for the user to choose one, then continue. Wait for receiver confirmation before claiming playback.", empty),
                    Function("stop_cast", "End the current Chromecast session without stopping camera capture.", empty),
                    Function("suggest_shots", "When physicsAvailable is true, request a fresh still-table verification and show up to three validated candidates. If physics is unavailable, the table is moving or unclear, explain the limitation instead of guessing.", empty),
                    Function("explain_shot", "Explain one current candidate using its stable ID.", new { type = "object", properties = new { shot_id = new { type = "string" } }, required = new[] { "shot_id" }, additionalProperties = false }),
                    Function("select_shot_by_number", "Select one of the currently displayed candidates by its spoken one-based number. Reject stale numbers.", new { type = "object", properties = new { number = new { type = "integer", minimum = 1, maximum = 3 } }, required = new[] { "number" }, additionalProperties = false }),
                    Function("select_shot_by_id", "Select one currently available candidate by its stable ID. Reject stale IDs.", new { type = "object", properties = new { shot_id = new { type = "string" } }, required = new[] { "shot_id" }, additionalProperties = false }),
                    Function("clear_shots", "Clear the currently shown shot paths from the live phone and TV views.", empty),
                    Function("get_match_state", "Read the current declared match, player groups, score, turn, table facts, freshness and available correction references.", empty),
                    Function("start_match", "Start a player-requested match with two distinct names and a frame target. Read current state first and supply its game version.", new { type = "object", properties = new { first_player = new { type = "string" }, second_player = new { type = "string" }, target = new { type = "integer", minimum = 1, maximum = 100 }, expected_game_version = new { type = "integer", minimum = 0 } }, required = new[] { "first_player", "second_player", "target", "expected_game_version" }, additionalProperties = false }),
                    Function("start_frame", "Start a requested frame and name its starter exactly as shown by get_match_state. Supply the current game version.", new { type = "object", properties = new { starting_player = new { type = "string" }, expected_game_version = new { type = "integer", minimum = 0 } }, required = new[] { "starting_player", "expected_game_version" }, additionalProperties = false }),
                    Function("finish_match", "Finish the current match only when the player explicitly asks. Supply the current game version.", new { type = "object", properties = new { expected_game_version = new { type = "integer", minimum = 0 } }, required = new[] { "expected_game_version" }, additionalProperties = false }),
                    Function("declare_match_colours", "Record the player's explicit open-table or red/yellow group declaration. For red or yellow, name the player. Supply the current game version.", new { type = "object", properties = new { group = new { type = "string", @enum = new[] { "red", "yellow", "open" } }, player_name = new { type = "string" }, expected_game_version = new { type = "integer", minimum = 0 } }, required = new[] { "group", "expected_game_version" }, additionalProperties = false }),
                    Function("correct_match_state", "Apply an explicit player-requested correction: group, turn, foul, frame winner or a current ball's presence. Use only current names/IDs from get_match_state and its game version.", new { type = "object", properties = new { kind = new { type = "string", @enum = new[] { "group", "turn", "foul", "winner", "ball_presence" } }, player_name = new { type = "string" }, group = new { type = "string", @enum = new[] { "red", "yellow", "open" } }, foul = new { type = "boolean" }, ball_id = new { type = "string" }, presence = new { type = "string", @enum = new[] { "OnTable", "Missing", "Potted" } }, expected_game_version = new { type = "integer", minimum = 0 } }, required = new[] { "kind", "expected_game_version" }, additionalProperties = false }),
                    Function("undo_match_decision", "Undo only the latest eligible player-confirmed match decision shown by get_match_state. Supply the current game version.", new { type = "object", properties = new { expected_game_version = new { type = "integer", minimum = 0 } }, required = new[] { "expected_game_version" }, additionalProperties = false }),
                    Function("record_declared_shot", "Record or replace a player's fully stated shot facts after the balls stop. Never infer pots, contact, legality or fouls from images; leave optional facts unknown. Supply the current game version.", new { type = "object", properties = new { is_break = new { type = "boolean" }, legality = new { type = "string", @enum = new[] { "Unknown", "Legal", "StandardFoul", "LossOfTurn", "LossOfFrame" } }, reds_potted = new { type = "integer", minimum = 0, maximum = 7 }, yellows_potted = new { type = "integer", minimum = 0, maximum = 7 }, black_potted = new { type = "boolean" }, cue_ball_potted = new { type = "boolean" }, cue_ball_off_table = new { type = "boolean" }, legal_break = new { type = new[] { "boolean", "null" } }, first_contact = new { type = "string", @enum = new[] { "unknown", "red", "yellow", "black" } }, call_required = new { type = new[] { "boolean", "null" } }, called_group = new { type = "string", @enum = new[] { "none", "red", "yellow" } }, reds_before = new { type = new[] { "integer", "null" }, minimum = 0, maximum = 7 }, yellows_before = new { type = new[] { "integer", "null" }, minimum = 0, maximum = 7 }, replace_latest = new { type = "boolean" }, expected_game_version = new { type = "integer", minimum = 0 } }, required = new[] { "is_break", "legality", "reds_potted", "yellows_potted", "black_potted", "cue_ball_potted", "cue_ball_off_table", "replace_latest", "expected_game_version" }, additionalProperties = false }),
                    Function("save_recent_clip", "Save an explicitly requested bounded duration from the camera recording. Requires the current camera host. Use supported durations 30, 60, 120, 300 or 600 seconds.", new { type = "object", properties = new { seconds = new { type = "integer", @enum = new[] { 30, 60, 120, 300, 600 } } }, required = new[] { "seconds" }, additionalProperties = false }),
                    Function("set_sample_collection", "Enable or disable automatic clip samples for player shot reports, only on explicit request. Does not start recording or a match.", new { type = "object", properties = new { enabled = new { type = "boolean" } }, required = new[] { "enabled" }, additionalProperties = false }),
                    Function("get_sample_collection", "Read whether sample collection is enabled, saving, saved or failed and its recording limitations.", empty),
                    Function("save_shot_sample", "For each player report of a completed shot, submit exact words from that report. Only active collection saves. Fields and notes contain exact excerpts, with optional suggested categories; they never change match state.", new { type = "object", properties = new { reported_words = new { type = "string" }, fields = new { type = "array", items = new { type = "object", properties = new { kind = new { type = "string", @enum = new[] { "outcome", "ball", "shot", "position", "other" } }, text = new { type = "string" } }, required = new[] { "kind", "text" }, additionalProperties = false } }, notes = new { type = "array", items = new { type = "string" } } }, required = new[] { "reported_words" }, additionalProperties = false }),
                    Function("get_saved_clips", "Refresh and list the camera's current saved clips with recent numbered choices.", empty),
                    Function("play_saved_clip", "Play a current numbered saved clip on phone or the connected TV. Call get_saved_clips first; reject stale numbers.", new { type = "object", properties = new { clip_number = new { type = "integer", minimum = 1, maximum = 20 }, target = new { type = "string", @enum = new[] { "phone", "tv" } } }, required = new[] { "clip_number", "target" }, additionalProperties = false }),
                    Function("replay_recent", "Prepare recent camera footage on the connected TV, without adding it to saved clips. Recording continues. Use 15, 30 or 60 seconds.", new { type = "object", properties = new { seconds = new { type = "integer", @enum = new[] { 15, 30, 60 } } }, required = new[] { "seconds" }, additionalProperties = false }),
                    Function("control_tv_replay", "Pause, resume or seek the currently selected TV replay. Report success only after the receiver confirms the command.", new { type = "object", properties = new { action = new { type = "string", @enum = new[] { "pause", "resume", "seek" } }, seconds = new { type = "integer", minimum = 0 } }, required = new[] { "action" }, additionalProperties = false }),
                    Function("return_to_live", "Return the TV and phone to the active live camera view without stopping capture.", empty)
                  ]
                : [
                    Function("get_table_state", "Get the latest calibrated table, player group, state age and confidence.", empty),
                    Function("suggest_shots", "Rank up to three validated direct, bank or kick pots for the fresh, settled, confirmed table and selected group. Read every cue and object path segment; scores are difficulty rankings, not pot probabilities.", empty),
                    Function("explain_shot", "Explain one candidate from suggest_shots using its stable ID.", new { type = "object", properties = new { shot_id = new { type = "string" } }, required = new[] { "shot_id" }, additionalProperties = false })
                  ];
            var definition = new
            {
                session = new
                {
                    model = "gpt-live-1",
                    instructions = SkyPersonalityInstructions + "\n\n" + SkyOperatingInstructions + (includeSceneCommands ? "\n\n" + SampleCollectionInstructions : ""),
                    audio = new { output = new { voice = "willow" } },
                    delegation = new
                    {
                        type = "responses",
                        responses = new
                        {
                            model = "gpt-5.6-luna",
                            instructions = "Use get_table_state and suggest_shots to ground pool advice. Explain the geometry concisely and treat confidence and timestamps as limits. Use the rule reference returned by the tools. Distinguish an open table from an assigned group. Do not invent spin, foul calls, clearance or a legal black shot. For match state, read get_match_state first and preserve its exact version for every change. Only record player-declared outcomes; image evidence is never a declaration. Tool errors are uncertainty, not permission to guess." + (includeSceneCommands ? "\n\n" + SampleCollectionInstructions : ""),
                            tools = toolDefinitions, tool_choice = "auto"
                        }
                    }
                }, transport = new { type = "webrtc", sdp }
            };
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(65) };
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/live/sessions") { Content = JsonContent.Create(definition) };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            using var response = await http.SendAsync(request, lifetime.Token);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("OpenAI rejected voice setup (" + (int)response.StatusCode + "). Check your API key and access.");
            using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync(lifetime.Token));
            sessionId = result.RootElement.GetProperty("session").GetProperty("id").GetString()!;
            string answer = result.RootElement.GetProperty("transport").GetProperty("sdp").GetString()!;
            sideband.Options.SetRequestHeader("Authorization", "Bearer " + key);
            rtc.SetRemoteAnswer(answer);
            await sideband.ConnectAsync(new Uri("wss://api.openai.com/v1/live/sessions/" + Uri.EscapeDataString(sessionId) + "/attach"), lifetime.Token);
            receiver = ReceiveAsync();
            await started.Task.WaitAsync(TimeSpan.FromSeconds(30), lifetime.Token);
            Volatile.Write(ref ready, 1);
            Publish(SkyVoiceState.Listening, "Sky is listening. Table coaching becomes available after calibration.");
        }
        catch { await CloseAsync(); throw; }
    }
    async Task ReceiveAsync()
    {
        byte[] bytes = new byte[16384];
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                using var message = new MemoryStream(); WebSocketReceiveResult part;
                do
                {
                    part = await sideband.ReceiveAsync(bytes, lifetime.Token);
                    if (part.MessageType == WebSocketMessageType.Close) return;
                    message.Write(bytes, 0, part.Count); if (message.Length > 2 * 1024 * 1024) throw new IOException("Voice event exceeds limit.");
                } while (!part.EndOfMessage);
                await HandleAsync(Encoding.UTF8.GetString(message.ToArray()));
            }
        }
        catch (Exception e) when (e is WebSocketException or System.OperationCanceledException or IOException)
        { if (ending == 0) { CancelActiveTool(); Volatile.Write(ref ready, 0); PublishVoiceEvent("session_interrupted", activeResponseId); Publish(SkyVoiceState.Interrupted, "Sky connection interrupted. Camera recording continues."); } }
    }
    async Task HandleAsync(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json); var root = doc.RootElement; string? type = root.GetProperty("type").GetString();
            JsonElement voiceEvent = root;
            string eventType = type ?? "";
            if (type == "response.event" && root.TryGetProperty("event", out var nested)
                && nested.TryGetProperty("type", out var nestedType))
            { voiceEvent = nested; eventType = nestedType.GetString() ?? ""; }
            string? responseId = ResponseId(voiceEvent) ?? activeResponseId;
            if (eventType.EndsWith("speech_started", StringComparison.Ordinal))
            {
                CancelActiveTool();
                PublishVoiceEvent("user_interrupted", responseId);
            }
            else if (eventType is "response.cancelled" or "response.canceled")
            {
                CancelActiveTool();
                PublishVoiceEvent("response_cancelled", responseId);
            }
            if (type == "session.started") { started.TrySetResult(true); Publish(SkyVoiceState.Listening, "Sky is listening."); }
            if (IsTranscriptEvent(eventType) || type == "session.closed")
                File.AppendAllText(archive, JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, @event = root }, JsonDefaults.Options) + "\n");
            if (type == "session.closed") { Volatile.Write(ref ready, 0); CancelActiveTool(); PublishVoiceEvent("session_interrupted", activeResponseId); closed.TrySetResult(true); Publish(SkyVoiceState.Disconnected, "Sky session ended."); return; }
            if (IsTranscriptEvent(eventType))
            {
                bool userTranscript = IsInputTranscript(eventType);
                bool transcriptFinal = IsFinalInputTranscript(eventType);
                string field = transcriptFinal ? "transcript" : "delta";
                string transcript = voiceEvent.TryGetProperty(field, out var text) ? text.GetString() ?? "" : "";
                if (transcript.Length > 0) PublishTranscript(transcript, userTranscript, transcriptFinal, responseId, eventType);
            }
            if (eventType == "response.created")
            {
                activeResponseId = ResponseId(voiceEvent) ?? activeResponseId;
                PublishVoiceEvent("response_started", activeResponseId);
            }
            if (eventType.EndsWith("output_audio_buffer.started", StringComparison.Ordinal)
                || eventType.EndsWith("output_audio.started", StringComparison.Ordinal))
                Publish(SkyVoiceState.Speaking, "Sky is speaking.");
            else if (eventType.EndsWith("output_audio_buffer.stopped", StringComparison.Ordinal)
                || eventType.EndsWith("output_audio_buffer.done", StringComparison.Ordinal)
                || eventType.EndsWith("output_audio.done", StringComparison.Ordinal))
                Publish(IsMuted ? SkyVoiceState.Muted : SkyVoiceState.Listening, IsMuted ? "Sky microphone muted." : "Sky is listening.");
            if (eventType == "response.output_item.done" && voiceEvent.TryGetProperty("item", out var item)
                && item.TryGetProperty("type", out var itemType) && itemType.GetString() == "function_call")
            {
                var captured = item.Clone(); _ = ExecuteToolAsync(captured, responseId);
            }
            if (eventType == "response.done")
            {
                if (HasFunctionCallOutput(voiceEvent)) PublishVoiceEvent("response_finished", responseId);
                else if (ResponseCompleted(voiceEvent)) PublishVoiceEvent("response_completed", responseId);
                else PublishVoiceEvent("response_cancelled", responseId);
                if (string.Equals(activeResponseId, responseId, StringComparison.Ordinal)) activeResponseId = "";
            }
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException) { }
    }
    static bool IsInputTranscript(string type) => type is "session.input_transcript.delta" or "session.input_transcript.completed"
        or "conversation.item.input_audio_transcription.delta" or "conversation.item.input_audio_transcription.completed"
        or "input_audio_transcription.delta" or "input_audio_transcription.completed";
    static bool IsFinalInputTranscript(string type) => type is "session.input_transcript.completed"
        or "conversation.item.input_audio_transcription.completed" or "input_audio_transcription.completed";
    static bool IsTranscriptEvent(string type) => IsInputTranscript(type)
        || type is "session.output_transcript.delta" or "response.output_audio_transcript.delta" or "response.output_text.delta";
    static string? ResponseId(JsonElement value)
    {
        if (value.TryGetProperty("response_id", out var id) && id.ValueKind == JsonValueKind.String) return id.GetString();
        if (value.TryGetProperty("response", out var response) && response.TryGetProperty("id", out id) && id.ValueKind == JsonValueKind.String) return id.GetString();
        return null;
    }
    static bool HasFunctionCallOutput(JsonElement value)
    {
        if (!value.TryGetProperty("response", out var response) || !response.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array) return false;
        return output.EnumerateArray().Any(item => item.TryGetProperty("type", out var itemType) && itemType.GetString() == "function_call");
    }
    static bool ResponseCompleted(JsonElement value)
        => value.TryGetProperty("response", out var response) && response.TryGetProperty("status", out var status)
            && status.GetString() == "completed";
    async Task ExecuteToolAsync(JsonElement item, string? responseId)
    {
        try
        {
            await toolLock.WaitAsync(lifetime.Token);
            try
            {
                string id = item.GetProperty("call_id").GetString()!;
                if (!executedCalls.Add(id)) return;
                string name = item.GetProperty("name").GetString()!;
                string argumentsJson = item.GetProperty("arguments").GetString() ?? "{}";
                PublishTool(id, name, "requested", responseId, argumentsJson);
                PublishTool(id, name, "started");
                object result;
                using var toolCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                lock (toolGate) activeTool = toolCancellation;
                try
                {
                    using var args = JsonDocument.Parse(argumentsJson);
                    result = correlatedTools is not null ? await correlatedTools(id, name, args.RootElement, toolCancellation.Token)
                        : tools is null ? new { error = "Table tools are unavailable on this screen." }
                        : await tools(name, args.RootElement);
                    toolCancellation.Token.ThrowIfCancellationRequested();
                }
                catch (OperationCanceledException) { PublishTool(id, name, "cancelled", responseId, argumentsJson, "{\"cancelled\":true}"); return; }
                catch (Exception) { result = new { error = "Table analysis is unavailable or stale. Ask Rich rather than guessing." }; }
                finally { lock (toolGate) if (activeTool == toolCancellation) activeTool = null; }
                bool failed = false;
                try
                {
                    using var resultJson = JsonDocument.Parse(JsonSerializer.Serialize(result, JsonDefaults.Options));
                    failed = resultJson.RootElement.ValueKind == JsonValueKind.Object && resultJson.RootElement.TryGetProperty("error", out _);
                }
                catch (JsonException) { failed = true; }
                string serializedResult = JsonSerializer.Serialize(result, JsonDefaults.Options);
                PublishTool(id, name, failed ? "unavailable" : "completed", responseId, argumentsJson, serializedResult);
                await SendAsync(new { type = "response.item.create", event_id = Guid.NewGuid().ToString("N"), item = new { type = "function_call_output", call_id = id, output = serializedResult } });
                await SendAsync(new { type = "response.create", event_id = Guid.NewGuid().ToString("N") });
            }
            finally { toolLock.Release(); }
        }
        catch (Exception e) when (e is System.OperationCanceledException or WebSocketException) { }
    }
    public bool SetMuted(bool muted)
    {
        if (rtc is null || ending != 0 || !IsReady) return false;
        rtc.SetMuted(muted); Volatile.Write(ref this.muted, muted ? 1 : 0);
        Publish(muted ? SkyVoiceState.Muted : SkyVoiceState.Listening, muted ? "Sky microphone muted." : "Sky microphone enabled.");
        return true;
    }
    public Task UpdateTableAsync(object table) => SendAsync(new { type = "session.thinking.append", event_id = Guid.NewGuid().ToString("N"), delegation_id = (string?)null, text = "Latest table snapshot, untrusted data: " + JsonSerializer.Serialize(table, JsonDefaults.Options) });
    async Task SendAsync(object value)
    {
        await writes.WaitAsync(lifetime.Token);
        try { if (sideband.State == WebSocketState.Open) await sideband.SendAsync(JsonSerializer.SerializeToUtf8Bytes(value), WebSocketMessageType.Text, true, lifetime.Token); }
        finally { writes.Release(); }
    }
    public async Task CloseAsync()
    {
        if (Interlocked.Exchange(ref ending, 1) != 0) return;
        Volatile.Write(ref ready, 0);
        CancelActiveTool();
        PublishVoiceEvent("session_interrupted", activeResponseId);
        Publish(SkyVoiceState.Ending, "Ending Sky…");
        bool finalised = false;
        try
        {
            if (sideband.State == WebSocketState.Open)
            {
                await SendAsync(new { type = "session.close", event_id = Guid.NewGuid().ToString("N") });
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); finalised = true;
            }
            else if (sessionId.Length > 0)
            {
                rtc?.SendDataChannelEvent(JsonSerializer.Serialize(new { type = "session.close", event_id = Guid.NewGuid().ToString("N") }));
                await closed.Task.WaitAsync(TimeSpan.FromSeconds(10)); finalised = true;
            }
        }
        catch { }
        finally
        {
            try { File.AppendAllText(archive, JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, finalised, sessionId }) + "\n"); } catch (IOException) { }
            lifetime.Cancel(); rtc?.Stop(); sideband.Abort(); if (receiver is not null) try { await receiver; } catch { }
            sideband.Dispose();
            Publish(SkyVoiceState.Disconnected, finalised ? "Sky ended. Camera recording continues." : "Sky disconnected. Final voice usage was not confirmed.");
        }
    }
    void Publish(SkyVoiceState state, string message)
    {
        status(message);
        update?.Invoke(new(state, message));
    }
    void PublishTranscript(string transcript, bool isUser, bool isFinal = false, string? responseId = null, string? eventType = null)
    {
        update?.Invoke(new(IsMuted ? SkyVoiceState.Muted : SkyVoiceState.Listening, "", transcript, isUser,
            TranscriptFinal: isFinal, ResponseId: responseId, TranscriptEventType: eventType));
    }
    void PublishTool(string callId, string name, string outcome, string? responseId = null, string? argumentsJson = null, string? resultJson = null)
    {
        update?.Invoke(new(IsMuted ? SkyVoiceState.Muted : SkyVoiceState.Listening,
            outcome is "requested" or "started" ? "Sky is checking the table…" : outcome == "completed" ? "Sky completed a table action." : "Sky could not complete that table action.",
            ToolCallId: callId, ToolName: name, ToolOutcome: outcome, ResponseId: responseId,
            ToolArgumentsJson: argumentsJson, ToolResultJson: resultJson));
    }
    void PublishVoiceEvent(string kind, string? responseId) => update?.Invoke(new(IsMuted ? SkyVoiceState.Muted : SkyVoiceState.Listening,
        "", ResponseId: responseId, EventKind: kind));
    void CancelActiveTool()
    {
        lock (toolGate)
        {
            try { activeTool?.Cancel(); }
            catch (ObjectDisposedException) { }
        }
    }
    sealed class Listener(SkyVoiceSession owner) : Java.Lang.Object, Bridge.SkyEyeRtcSession.IListener
    {
        public void OnOfferCreated(string? sdp) { if (sdp is not null) owner.offer.TrySetResult(sdp); }
        public void OnStateChanged(string? state, string? detail)
        {
            if (state == "error")
            {
                Volatile.Write(ref owner.ready, 0);
                owner.CancelActiveTool();
                owner.offer.TrySetException(new IOException(detail ?? "Voice connection failed"));
                owner.started.TrySetException(new IOException(detail ?? "Voice connection failed"));
                owner.Publish(SkyVoiceState.Error, detail ?? "Voice connection failed.");
            }
            else if (state == "disconnected" && owner.ending == 0)
            {
                Volatile.Write(ref owner.ready, 0);
                owner.CancelActiveTool();
                owner.PublishVoiceEvent("session_interrupted", owner.activeResponseId);
                owner.Publish(SkyVoiceState.Interrupted, "Sky connection interrupted. Camera recording continues.");
            }
        }
        public void OnDataChannelEvent(string? json)
        {
            if (json is null) return;
            try { using var doc = JsonDocument.Parse(json); string? type = doc.RootElement.GetProperty("type").GetString(); if (type == "session.started") owner.started.TrySetResult(true); if (type == "session.closed" && owner.sideband.State != WebSocketState.Open) _ = owner.HandleAsync(json); }
            catch { }
        }
        public void OnRemoteAudioActivityChanged(bool active) { }
    }
}
