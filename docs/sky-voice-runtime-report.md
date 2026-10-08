# Sky voice runtime implementation and acceptance — 7 October 2026

## Delivered in source

The production Hybrid portrait host now uses the existing encrypted `SecretStore` credential and `SkyVoiceSession` WebRTC/sideband transport. The normal controls sheet exposes Connect, Mute/Unmute and End; Android microphone permission and the microphone foreground service are part of the connection flow. Native connection, VAD interruption, output-audio, transcript and tool-call updates feed the visible Sky state. Key edits are blocked during a session, removing the key ends the session first, and shell disposal requests bounded session shutdown. The voice session remains independent of camera capture and stays active across an activity pause while its foreground service runs.

New WebRTC sessions use Willow and Android communication audio mode, selecting the built-in speaker for hands-free table use when no external communication device is available. Connecting a headset releases the speaker route to Android, and ending Sky clears the app-selected route and restores the prior audio mode. Physical loudness on the Pixel still needs listening confirmation.

The Hybrid voice tool catalogue supports:

- current calibrated table state with age, confirmation, motion, quality, warnings and explicit limitations;
- starting table calibration by voice, with automatic corner proposals shown on a frozen frame for review;
- applying four current reviewed corners after explicit player approval, followed by the bounded initial multi-frame table check;
- fresh bounded verification before presenting shot choices;
- selection by current one-based number or stable candidate ID;
- explanation of one current candidate; and
- clearing current live phone/TV shot paths.

Shot observations and scene commands use `CommandOrigin.Sky`, the existing application coordinator and scene commands. Calibration reuses `TableAnalysisSession` and the existing reviewed `TableEditDraft`; its initial bounded observation uses `CommandOrigin.Sky`. Observation and display/selection/clear commands receive deterministic, voice-session-scoped operation IDs derived from the provider's tool-call ID and action. One serialized executor rejects duplicate call IDs; cancellation suppresses stale output and stops a pending effect on user speech, transport loss or end. The event adapter accepts the current Realtime VAD `input_audio_buffer.speech_started` and WebRTC `output_audio_buffer.started`/`stopped` events, including the existing Live sideband envelope. See the [official server-event reference](https://developers.openai.com/api/reference/resources/realtime/server-events).

## Voice-triggered table calibration — 8 October 2026

`calibrate_table` starts the existing frozen-frame corner workflow and reports whether automatic detection proposed all four points. The proposal remains editable on the phone. `apply_table_calibration` is available only for a current four-corner preview and runs only after explicit player approval; applying it starts `VerifyForAdvice` across multiple fresh frames and returns the observed ready, unsettled, unclear or unavailable status with its evidence count. A stale camera/calibration generation cannot apply. The signed Release built and installed in place on Pixel 11 Pro XL, preserving the existing app data. The install was launched back to the home screen; no live calibration or spoken approval/sweep has yet been exercised, and the camera was not in active capture at install time.

When a TV replay is active, voice shot choices target the phone so a spoken request does not replace the replay. Clear and select remain scoped to active live viewing and the current choice catalogue; a stale number or ID is refused.

## Native Agent stream bridge — A02.02

The Hybrid host now projects completed user transcripts, assistant transcript deltas, native function calls, their serialized results and response completion into the existing `AgentContext`/`UIAgent` conversation. Typed blocks cover the current table, shot advice/explanation/selection and clear tools. Tool arguments and results share the native call ID; the bridge renders and records those events but never executes them. `SkyVoiceSession` remains the sole executor and retains deterministic command IDs, duplicate-call rejection and bounded shutdown.

Each native response ID is attached to its active Agent turn. Because input transcription completion may arrive before or after response events, the bridge buffers early text/tool/result events and drains them when the user transcript opens its Agent turn ([Realtime event reference](https://developers.openai.com/api/reference/resources/realtime/server-events)). Speech interruption and session end cancel that turn and remove its response mappings, so late transcripts/results are ignored. Tool responses can create a follow-up native response without ending the same UI turn. The conversation panel now renders streamed text and pending/completed tool blocks.

The transcript caption retains up to twelve recent user/Sky lines in memory, caps each line, reconciles response-before-transcript ordering, and prefers the spoken audio transcript when both audio and text deltas arrive. It remains visible after Sky ends and across reconnects while the app process is alive; transcript text is not written by this view to persistent storage. Controlled checks cover these cases. The updated production app is installed on Pixel 11 Pro XL, but multi-turn rendering has not yet been verified in a live conversation.

The Agent UI checks exercise the actual `AgentContext`, generated typed tool blocks, argument/result correlation, duplicate call/result suppression, response-before-transcript buffering, a tool follow-up response and interruption with stale-event rejection. A prior short Pixel 11 Live session completed and the user heard Sky answer; requesting “show my shots” returned an unavailable result because there was no connected live camera. The previous installed app showed only its latest transcript segment. The new bounded conversation view and actual “show my shots”/“number two”/“clear that” spoken parity remain physical acceptance gates.

## Camera, host and Chromecast voice controls — A04.02 slice

Sky now has tools to read camera/Cast status, discover nearby cameras, connect the saved camera or a numbered discovery result, claim/release the current camera host lease, start/pause/resume/stop capture, open the Android Cast picker, cast the active live table, and stop Cast. The explicit `host_live_session` action sequences saved-camera connection, available host claim and capture start, with a truthful failure stage if one step is refused. Camera changes use `CommandOrigin.Sky`, the application coordinator, current camera scope and deterministic call IDs. Discovery and connection retain Android's nearby-device permission. The address is never returned in voice results.

Capture control requires a reachable camera and the current host lease. The model instructions require an explicit user request before changing capture. Live Cast requires active capture; when no receiver is selected, the voice tool opens Android's native picker and waits up to 60 seconds for the user to choose before sending playback. Receiver status is checked before reporting success. Stopping Cast leaves capture running. The tool-result UI uses typed blocks for camera state, discovery, connection, host, capture and Cast operations.

## Canonical match state and declared controls — A03.02

`get_match_state` reads the current `AppCoordinator` snapshot backed by `SkyEye.GameState`: match/frame IDs, players, score, groups and declaration provenance, current turn/provenance, foul/winner/rules status, ball colour/presence/position, table uncertainty/freshness, and the current undo/correction references. Mutations require the exact `gameVersion` from that read and use the existing serialized coordinator and durable game journal. The coordinator enforces local/host authority and stale-version rejection.

Sky can start a named match/frame, finish a match, declare open/red/yellow groups, correct group/turn/foul/frame-winner/ball-presence facts, undo only the latest eligible decision, and record or replace a player's declared shot through the existing `DeclareShotOutcome` rules command. Player names must resolve exactly and ball-presence corrections use current ball IDs. The model instructions require a clear user declaration; images never become a pot, foul, turn or winner declaration. Unresolved rules remain pending. Controlled checks cover match/frame creation, complementary groups, turn correction, undo, declared-shot recording/replacement and stale versions.

## Saved clip and replay controls — A04.01

The production Hybrid coordinator now uses the existing durable `ClipSaveExecutor` and `CameraClipSaveEffect`. Sky can save supported camera durations (30, 60, 120, 300 or 600 seconds) only with the current host scope; the effect refreshes the camera session/host scope before sending, and an unknown result is never retried as a new save. An acknowledged clip preserves its recorded annotation journal when available.

Sky can refresh the numbered saved-clip list, play a current clip on the phone or connected TV, prepare the existing temporary 15/30/60-second recent replay on TV, control TV replay pause/resume/seek, and return phone and TV to live. The phone uses the existing corrected local playback path and downloads a clip first when needed. TV controls and replay report success only from current receiver metrics; a missing receiver, stale clip, preparation error or unconfirmed command is reported as such. Recent replay remains temporary and recording continues.

## Updated source verification and limits — 7 October 2026

`SkyEye.AgentUi.Checks` passes with new adapter checks for canonical match updates and typed voice blocks. The production Hybrid Release Android build succeeds with zero errors and generated OpenCV/native-packaging warnings. The updated Release was installed in place on Pixel 11 Pro XL on 7 October; its refreshed home, launch and launcher icon were visually checked. This smoke check does not exercise the new voice tools: live speech parsing, listener/audio parity, camera save, phone/TV replay, interruption and receiver-confirmation behavior remain acceptance gates. The earlier user-confirmed A04.02 camera/host/Cast run applies to its original installed build and does not validate these new tools.

The O01 offline seed baseline was rerun from its frozen manifest/lock. It reproduces 16/16 spatial matches and colours, zero misses/extras and p95 centre error 0.00666 on the one tuned real still. It still has zero real timed shots and zero held-out shots, and scores archived detections rather than rerunning native CV. O01 accuracy and O08 physical image acceptance remain open.

## Verification and limits

Before the A03.02/A04.01 source additions above, the A02/A04.02 Release build was installed through the guarded in-place update; package/signature, UID and private-storage preservation were verified. The user reports the camera connection, hosted session, Chromecast selection and live cast all worked very well. The newer Release was subsequently installed in place with the same identity and private storage retained; camera/session/Cast, match, clip and voice behavior from that newer build still need hands-on acceptance.

The reported run covers the requested connect/host/capture/Cast path on the earlier build. It did not separately establish that stopping Cast leaves capture active, nor permission denial, interruption, reconnect, spoken shot parity or lifecycle behavior. The transcript view, match tools and save/replay tools still need direct Pixel acceptance after the newer install. No saved key was read during source/build work. O08 held-out accuracy, unsafe windows and physical image acceptance remain separate open gates; successful camera connectivity/Cast does not establish image quality.
