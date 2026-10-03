# SkyEye implementation plan

Original plan prepared 3 October 2026 from the latest Project SkyEye Brief and inspection of `C:\code\MaeveWatch`. The sections below preserve the proposed implementation sequence. Current implementation and verification are recorded in `validation.md` and the repository README.

## Intended result

Two C# Android apps turn the mounted Pixel 10 Pro into a screen-off pool camera and the Pixel 11 Pro into the host controller. Other local-network users use the same companion in Guest mode. The host starts, pauses, resumes and stops sessions, selects replay duration and controls silent landscape playback on Chromecast Ultra. Everyone can view and download existing clips. Shared clips are portrait and include room audio. Later, the host runs Maeve's GPT-Live-1 conversation and pool-analysis tools.

Build in this order: prove screen-off operation; complete the viewing, casting and replay path; harden shared use and endurance; add coaching.

## Architecture and initial choices

| Component | Responsibility |
|---|---|
| SkyEye.Camera | Camera/microphone service, hardware encoding, replay history, local media server, durable clips, session state and host ownership |
| SkyEye.Companion — Host | Discovery, session controls, phone playback, Cast sender, clip downloads/export, table analysis and Maeve conversation |
| SkyEye.Companion — Guest | Discovery, playback and local clip library; deliberate host claim only when available |
| Shared C# contracts/core | State, commands/events, timestamps, clip metadata, ownership and replay selection logic |
| OpenAI | GPT-Live-1 voice and delegated model inference; the host executes local tools |

Start with native .NET Android on ARM64, matching Maeve's existing approach. Camera2, MediaCodec, AudioRecord and Android services provide the platform integrations; use Android Media3 for companion playback and the Cast sender SDK for Chromecast. Verify required C# bindings during the first prototype; a small Java adapter is acceptable where bindings are insufficient, following Maeve's WebRTC adapter pattern. Keep platform interfaces narrow so implementation choices can change without changing the product flow.

Evaluate HLS first because the brief accepts delay and Chromecast must fetch media directly from the camera. Do not make the companion a mandatory relay. Phone-video WebRTC is a later optimisation only if measurements justify a second video transport; GPT-Live voice WebRTC is a separate connection.

Use local network discovery with a manual address fallback. Commands use an authenticated host lease; guests receive read capabilities. Status/events can use a persistent connection while media and downloads use HTTP. Protocol messages include version, session ID, command ID and state revision. Retryable mutations must be idempotent.

## 1. Resolve screen-off feasibility before building the full UI

Create the smallest camera and companion prototypes, install on the actual phones and record OS/build versions. From a visible camera activity, grant permissions and arm a foreground service with the required camera/microphone types. The companion then controls the already armed service. Arming is not starting a recording session.

Test initial remote start, pause/resume and stop/start while the camera display remains off. Test after short idle and at least 30 minutes of idle. Compare suspending capture while keeping resources open against releasing and reopening camera/microphone within the same armed service. Determine the lowest-power option that actually works. Measure reachability, permission failures, frames/audio and resource cleanup.

Keep arming distinct from pool-session state. `Ready`, `Active`, `Paused` and `Stopped` describe the session; service readiness is reported separately. Stop ends capture and buffering but should retain armed remote readiness if the platform permits it. Show a clear unavailable state if the service is killed. Force-stop/reboot recovery is outside this first implementation.

**Exit condition:** repeated remote start/pause/resume/stop works without touching the mounted phone or illuminating its screen. If it does not, report the observed constraint and revise the lifecycle before committing to the rest of the implementation. Do not substitute a permanently lit screen silently.

Android documents continued background camera access through a camera foreground service, but restricts creation of camera/microphone services from the background. That supports testing an already armed service; it does not prove idle resume on these devices. [Service types](https://developer.android.com/develop/background-work/services/fgs/service-types), [background-start restrictions](https://developer.android.com/develop/background-work/services/fgs/restrictions-bg-start).

## 2. Establish capture, orientation and phone viewing

Start with 1080p/30 fps, hardware H.264 and synchronised AAC room audio. Tune focus/exposure for the mounted table after observing real moving-ball footage. Retain the complete playable area and all six pockets. Compare 60 fps later against battery, heat and motion clarity.

Maintain one source coordinate system and a saved orientation transform. Proposed media design: rotate pixels into landscape before one video encoder, publish video-only HLS for live viewing/casting, and preserve the matching video plus audio for replay clips. The companion can rotate presentation for upright portrait viewing. Produce physical portrait pixels for shared MP4s on the companion during download/export, rather than trusting every sharing app to honour rotation metadata.

This avoids an always-running second encoder while keeping casting silent by construction. Validate the single-encode design, export cost and portrait resolution in the prototype. If quality is insufficient, benchmark a different capture/encode profile before choosing a second stream.

Use short, independently decodable segments with deliberate keyframe boundaries. Begin with approximately two-second segments, then tune from measured delay. Publish files and playlists atomically. Keep the short live playlist independent of the longer replay index.

**Exit condition:** the phone switches portrait/landscape without restarting capture, all pockets stay visible, room audio/video are synchronised in a saved sample, and capture continues with no viewers.

## 3. Prove Chromecast Ultra in the first end-to-end slice

Add Cast discovery and a sender using a standard receiver where it satisfies the prototype. Chromecast fetches the camera's LAN HLS URL. The encoded landscape orientation must already be correct; rotating the companion display alone cannot rotate TV playback. Supply a video-only feed so TV volume changes cannot introduce room-audio feedback.

Check media types, H.264 profile, playlist/segment access, CORS, receiver-compatible scoped URLs and same-network reachability. Do not rely on app-only request headers that the receiver cannot supply. Keep host-control credentials separate from receiver read access. Introduce a custom receiver only if the standard receiver fails a required behaviour.

Measure phone and TV delay separately with a visible timed event. Compare playback startup, stalls and reconnection. Changing phone orientation must not affect TV orientation. Stopping casting must not stop capture.

**Exit condition:** screen-off camera → phone viewer and Chromecast Ultra, with a complete landscape table and silent TV. Google lists HLS support and H.264 up to 1080p/60 fps for Ultra; compatibility still requires the actual pipeline test. [Cast media support](https://developers.google.com/cast/docs/media).

## 4. Complete replay saving, download and sharing

Retain up to ten minutes of encoded history while Active, subject to a storage reserve. Default each save to 30 seconds; expose 30 seconds, 1, 2, 5 and 10 minutes in the companion. Selected duration affects the save interval, not whether history accumulates. At an illustrative 8 Mbit/s, ten minutes of video is about 600 MB before audio/overhead; determine the actual profile by measurement.

For the initial UI, label the action “Save last …” and anchor its end to camera capture time when the command is accepted. Make clear that delayed playback is behind live capture. Carry source timestamps and timeline IDs throughout the protocol so a later displayed-moment save can be added accurately. Do not subtract an assumed fixed playback delay.

Pin selected segments against deletion, finish the current segment safely, assemble a durable camera clip and publish it only after successful completion. Report actual duration if insufficient history exists. Keyframe-aligned cuts may include a small lead-in; measure and expose actual boundaries. Pause introduces a timeline discontinuity: preserve existing clips/history, but do not present footage across a gap as continuous. Start a new contiguous interval on resume.

Assign stable clip IDs and persist metadata in a local SQLite catalogue. Downloads support byte-range resume, size/hash verification and atomic completion. Maintain clear states: saved on camera, downloading, preparing portrait and available locally. Prepare portrait MP4s with room audio on the companion, then allow offline playback and Android sharing. Retain the camera original after download.

**Exit condition:** the first complete slice saves 30 seconds and downloads a portrait clip that plays with audio after disconnecting the camera. Then validate every duration, short history, pause gaps, concurrent saves, rolling deletion, low storage and interrupted downloads.

## 5. Enforce one host and reliable guest use

The camera atomically grants the first deliberate host claim. Guest mode never claims automatically. Enforce host-only session/settings/Cast-related authority at camera endpoints, rather than only hiding buttons. Guests can browse/download existing clips; assume new retrospective save requests are host-only initially because guest creation rights remain unresolved in the brief.

Proposed lease policy: heartbeat every five seconds with a 30-second reconnect grace, configurable for device testing. Expired leases invalidate old control tokens; a deliberate new claim can then succeed. Provide explicit release and handover. Capture continues if the host disconnects, subject to battery/storage safeguards; losing a controller is distinct from stopping the session.

Cast commands are issued by the host companion. Include ownership information in camera state so a newly claimed host knows whether a Cast session exists and can explicitly reconnect or replace it. A previous host must disable its controls and close its Maeve conversation on ownership loss. AI credentials and private conversation never transfer to the next host.

**Exit condition:** simultaneous claims yield exactly one host, guest mutations are rejected, stale commands fail after handover, reconnect works within grace, and host plus multiple guests can view/download without interrupting recording.

## 6. Measure endurance and harden the recording MVP

Run comparable profiles: armed idle, capture/buffer only, one phone, phone plus TV, multiple viewers, and capture during save/download/export. Record device/software versions, elapsed time, battery percentage, thermal events, frame drops, storage and network use, startup time, median/tail playback delay and stalls. Measure active and idle runtime separately. Begin with a shorter stability run, then run unplugged to a configured battery cutoff. Do not estimate battery life from a brief desktop test.

Handle Wi-Fi loss/reconnect, companion backgrounding, camera process loss, permission revocation, low battery/storage and thermal throttling. Finalise available segments and preserve saved files. Never display a frozen picture as if it were current live video. Bound concurrent clip creation/download work so it cannot starve capture. Keep interruption and resource cleanup visible in diagnostics.

**MVP acceptance:** operate from the companion with the mounted phone's screen off; repeatedly control sessions; view in both phone orientations; cast silently in landscape; save all five replay durations; download/share portrait clips with audio; support host and guests; report measured limitations and runtime.

## 7. Adapt Maeve Watch's existing GPT-Live integration

Inspection confirms the current reference is .NET 10 Android. `Maeve.Phone/GatewayService.cs` reads a host-local encrypted credential, creates `gpt-live-1` sessions through `POST /v1/live/sessions`, and attaches the transcript/tool sideband. `Maeve.Watch/MaeveRtcSession.java` provides native microphone/speaker WebRTC with hardware echo cancellation/noise suppression where supported. `Shared/LiveSessionDefinition.cs` configures the persona and Responses delegation. The phone's memory code includes SQLite transcripts and function-result routing.

Adapt these pieces into the SkyEye host companion, bringing both session creation and voice media onto that phone. Remove the Wear Data Layer handshake. Give SkyEye separate app IDs, credential-storage aliases and local data; the watch's matching package/signature arrangement is specific to Wear OS. Enter/import the existing personal API key through host setup, without copying private key files into source or the camera app.

Start with small adaptations within SkyEye rather than restructuring Maeve Watch or introducing a cross-project library. The personal prototype can follow the existing host-phone credential model; distributing an app with a developer-owned key would require a different credential boundary. No separate laptop/hosted service is a dependency of this plan.

Keep coaching optional and host-only. Separate camera recording from the host's conversational microphone; live room playback is muted by default when coaching to reduce echo. Closing coaching must not stop camera capture. Reuse transcript/tool patterns selectively; shared-sheet memory is not an MVP dependency.

Improve shutdown during adaptation: the reference sends `session.close` then cancels reception; SkyEye should keep receiving until `session.closed` or a bounded timeout, persist final usage/transcripts, and mark incomplete finalisation explicitly. Generalise the memory-specific tool dispatcher for pool tools. Quiet table updates can use `session.thinking.append`; do not announce every refresh. [Official GPT-Live controls](https://developers.openai.com/api/docs/guides/voice-server-controls).

**Exit condition:** a host voice conversation starts, accepts interruptions/mute, executes a fake local tool and returns its result, then closes cleanly. Recording and guest playback continue through voice failure. This reference inspection does not establish SkyEye device behaviour.

## 8. Add calibrated table awareness and conversational coaching

Use a host-only snapshot endpoint to obtain recent frames independently of delayed HLS playback. Calibrate the table corners, pockets and playable surface; map detections into table coordinates. Begin with manually corrected static layouts, then add local ball detection/classification and settled-table detection. Every table snapshot carries capture time, version, confidence, motion/occlusion status and calibration version.

Expose host tools such as `get_table_state`, `suggest_shots` and `explain_shot`. Geometry computes candidate pot lines, obstructions and simple safety/position options. Use a versioned authoritative international eight-ball rules source for legal candidate filtering. Let Rich correct player groups, ball identity and game state. Ask for clarification when confidence is inadequate.

Supply Maeve concise fresh state when balls settle, invalidate suggestions as soon as movement occurs, cancel old analysis and reject stale tool results. Coach naturally: respond to questions, offer occasional useful opportunities, allow interruptions and avoid repeated commentary or lengthy advice during a shot. The voice model explains candidates grounded in validated perception/geometry; do not assume GPT-Live consumes the live video or reliably infers physics by itself.

**Exit condition:** on recorded and live layouts, detections and suggested lines can be inspected against the table; stale/occluded states suppress confident advice; corrections work; conversation remains natural while recording/casting run. Automatic scoring, comprehensive refereeing and advanced spin prediction remain later work.

## Proposed project structure

```text
C:\code\SkyEye\
  SkyEye.sln
  src\SkyEye.Camera\
  src\SkyEye.Companion\
  src\SkyEye.Core\
  tests\SkyEye.Checks\
  docs\implementation-plan.md
  docs\device-acceptance.md
  docs\benchmarks.md
  scripts\Install-SkyEye.ps1
```

Keep meaningful automated checks around state transitions, ownership races, idempotent commands, replay boundaries/discontinuities, clip pinning, transfer integrity and stale table/tool results. Camera lifecycle, Cast compatibility, audio/echo, thermal behaviour and endurance require physical-device evidence. Each milestone should leave installable apps, a repeatable acceptance procedure and results with observed versus untested behaviour distinguished.

## Decisions that can wait until their milestone

- Guest requests for new saves: initial assumption is host-only creation; guests browse/download.
- Host lease timing: proposed 5-second heartbeat/30-second grace; tune during shared-use testing.
- Clip retention/deletion: cap rolling history, preserve saved originals, report storage pressure; define saved-clip cleanup before endurance release.
- Whole-session recording, automatic reboot recovery, a separate guest APK, internet access and coaching overlays: deferred unless explicitly added to scope.
- Latency and battery targets: establish measured baselines before setting optimisation targets.

The first implementation package should cover milestones 1–4 as a narrow end-to-end prototype. Expand concurrency and hardening after that succeeds; add Maeve only once the recording path is dependable.
