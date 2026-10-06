# SkyEye glow-up implementation plan

4 October 2026 · Updated 6 October after delivering the Match & Table State core and declared phone match management in checked chunks.

The original planning baseline was the three-turn conversation **SkyEye Agentic UI Benefits** and the `C:\code\SkyEye` checkout at `f6ea29c`. Current delivery status and evidence are recorded below and in the progress log.

## Current delivery status — Match & Table State

**R01 — Establish the Match & Table State Core** is implemented and verified within its first vertical slice, in the **Match & Table State** epic. R01.01-R01.07 landed in small checked commits. The [user brief](r01-match-table-state-brief.md) is canonical; the [delivery plan](r01-match-table-state-plan.md) and [completion report](r01-match-table-state-report.md) record chunks, APIs, tests, actual device evidence and deferrals.

The pure C# `SkyEye.GameState` library owns canonical match/frame/table state, ordered meaningful events, deterministic reduction, provenance and corrections. Both companion hosts persist events and expose the canonical game snapshot through the shared coordinator. Replay, coaching, scoring, rules, shot reconstruction and future 3D simulation consume this model. R01 is not replay functionality and does not implement complete rules, new detectors or GPT integration.

Delivered: **R01**. Next: **remaining T13/T14 acceptance → T06 → T07**, with G/O/rules/coaching work consuming the state model. T13/T14 runtime work is implemented with open acceptance gates; R01 does not close them. See [progress](glow-up-progress.md), [T13 report](t13-performance-report.md) and [T14 report](t14-report.md). Earlier execution-order notes below are historical.

The user then prioritises work needing little table/TV testing. The [manual match slice](manual-match-report.md) delivers the declared setup/score/turn/group/winner/correction/undo/recovery/history portions of G01-G05 and frame summaries from C03 on the phone. Its [bounded plan](manual-match-plan.md) and phone-only Lab proof preserve the earlier media gates. Complete rules, automatic refereeing, voice and TV score presentation remain open. For continued low-table work, extend declared history/controls and reviewed rule fixtures; physical O-series tracking and T-series acceptance require separate device/table evidence.

The supplied brief describes moving R01 out of Replay; this checkout instead assigns R01 to rules outcomes in a flat backlog. Preserve that existing mission as **R04 — Apply accepted shot outcomes to turn/groups/entitlements**. R02/R03 and all T-series replay/media IDs remain unchanged. R04 depends on R01 plus accepted observation evidence and the reviewed rules profile.

## Product outcome

SkyEye opens into a full-screen portrait view of the table. Sky is the voice partner and primary navigation system. Touch, voice, vision and TV use the same application commands and state. The resting screen contains only useful game context and a small Sky indicator; controls and explanations appear when needed.

6 October addition: automatically corrected table geometry is the default for all footage: live phone/TV, recent replay, saved-clip playback, downloaded clips and shared/exported files. Corner pockets are detected automatically, including from historical footage; no manual corner picking is a prerequisite. Saved/exported clips should also preserve the table annotations SkyEye actually displayed at the corresponding recording time. The recorded source remains available internally for recovery and future rendering. These requirements are planned below; T03-T05 currently play original camera framing and do not record overlays into MP4s.

The companion becomes a referee-assisted scorekeeper, shot adviser and replay director across a session between two players. Its game decisions distinguish observations, inferences and player declarations, and can be corrected. The product should never silently turn an uncertain detection into a score or foul.

The revised plan adopts **Microsoft.AspNetCore.Components.AI in a .NET 11 Android MAUI Blazor Hybrid companion**, using `UIAgent<SkyEyeViewState>`, `AgentBoundary`, custom block renderers and proposed-state previews. These are actual package components, rather than an architectural imitation. Microsoft's [Agentic UI article](https://devblogs.microsoft.com/dotnet/build-agentic-ui-blazor/) identifies the package as experimental. Its web sample is not evidence of Android Hybrid compatibility: the first milestone must prove package restore, on-device rendering and coexistence with SkyEye's native media.

Retain native camera capture, ExoPlayer, WebRTC, OpenCV and Cast adapters. The mounted camera remains on its verified .NET 10 build; only the companion UI host moves to .NET 11. Use Razor for contextual controls, sheets and agent blocks. Native rendering remains responsible for the video/table scene and TV encoding. Reuse the existing voice connection through a narrow adapter; installing the Blazor package does not replace voice transport or camera processing.

## Prerequisites: checked and still required

Read-only checks on 4 October found:

| Prerequisite | Status | Action before implementation |
|---|---|---|
| .NET 11 SDK | **Installed:** `11.0.100-rc.1.26425.128`; selected automatically in the repo | Pin this exact SDK for the Hybrid host; do not allow an unplanned preview upgrade |
| Existing .NET 10 SDK | **Installed:** `10.0.400` | Pin the existing camera/check build separately; it currently has no `global.json` |
| Android/MAUI .NET 11 workloads | **Not proven ready:** SDK 11 lists Android/iOS/Mac/Windows registrations from preview 6, but installed Android packs inspected are 35/36 and MAUI SDK packs are 7/9/10; `maui-android` is not listed as installed | Install/repair a compatible pinned .NET 11 Android MAUI workload set, then verify actual packs and a device build |
| MAUI Blazor templates | **Present:** `maui-blazor` and `maui-blazor-web` | A template listing is not a successful Android restore/build; prove the minimal Android-only host |
| Android SDK / Java | **Present:** Android platforms 35/36, build tools 36.0.0 and JDK 21.0.8 | Validate against the chosen workload's requirements; add only missing platform/build tools and check licences |
| Blazor AI package | Documented in the article/sample; not restored or tested for Android here | Restore and pin `Microsoft.AspNetCore.Components.AI`; sample candidate is `0.1.0-preview.1.26459.102`, subject to restore/compatibility checks |
| MAUI WebView and AI dependencies | Not yet resolved for the new host | Pin compatible `Microsoft.AspNetCore.Components.WebView.Maui`, MAUI and `Microsoft.Extensions.AI` dependencies together, with a lock file |
| Android System WebView on Pixel 11 | Not checked in this revision | Check active provider and supported features when the phone reconnects |
| Pixel 10 / Pixel 11 / Chromecast | No ADB devices connected at this check | Reconnect wireless debugging for device gates; pair again only if authorization requires it. Use the same LAN for real Cast tests |

The normal Android-only MAUI workload installation is `dotnet workload install maui-android`, executed under the selected .NET 11 SDK. Because this machine's preview registrations and packs disagree, first select a coherent workload set and record its version. A broad workload update could change unrelated platforms; do not treat it as routine project setup. Workload installation may require Windows elevation, which is distinct from project-file changes.

Use separate, explicit working directories/SDK pins for .NET 10 camera and .NET 11 Hybrid builds; SDK selection follows the command's working directory, not simply its `--project` argument. Update the build/install scripts to invoke them separately and sequentially. Shared Core can retain a compatible .NET 10 target initially. Upgrade an Android binding project only when reference compatibility requires it, behind its own native-load gate.

No new Azure subscription, Microsoft Foundry deployment, Aspire CLI or AG-UI server is required for the chosen in-process design. Those are dependencies of the [article's complete web sample](https://github.com/danroth27/AgenticUI), which uses an Interactive Server web app; they are not requirements of every application using the Blazor AI package. Add an AG-UI transport only if a later server/client split has a concrete need. The existing host-owned voice credential remains encrypted and outside Razor/JavaScript state.

Sources: [MAUI BlazorWebView hosting](https://learn.microsoft.com/hi-in/dotnet/maui/user-interface/controls/blazorwebview?view=net-maui-10.0), [Android MAUI installation](https://learn.microsoft.com/en-us/dotnet/maui/get-started/installation?tabs=visual-studio-code), [workload installation](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-workload-install), [Blazor AI shared/custom state](https://learn.microsoft.com/en-us/aspnet/core/release-notes/aspnetcore-11#experimental-blazor-ai-components-for-agentic-user-interfaces). The MAUI hosting guide describes the hosting model; it does not establish compatibility with this experimental .NET 11 package.

## Original baseline — what existed and needed building

This table records the 4 October planning baseline. Current delivery status is in [progress](glow-up-progress.md); the R01 priority above supersedes the original game-state ownership and execution order.

| Area | Current implementation | Required change |
|---|---|---|
| Phone | One scrolling `MainActivity`, inline controls and separate table snapshot panel | .NET 11 MAUI Blazor Hybrid host, full-screen native table surface, contextual Razor controls and one swipe-up sheet |
| Voice | Maeve session; three read-only pool tools | Sky branding, structured voice state, complete application command tools |
| State | Activity fields, camera status, `TableTracker`, manually selected group | Application store, match/frame state, presentation state and command dispatch |
| Vision | Calibrated snapshot analysis, settlement/obstruction checks | Before/motion/after shot observations, temporal tracking and event proposals |
| Advice | Direct/bank/kick geometry filtered by selected group | Targets derived from current player's rules state; contextual selection |
| Phone/TV overlays | Candidate cycling every 2 seconds, analysis every 10 seconds | Shared scene including scoreboard, selected shot, highlights and replay context |
| TV | Live HLS loading; foreground companion encodes annotated live video | Buffered replay, seek/pause/speed, independent presentation mode, freeze/zoom |
| Match history | No match ledger or frame scores | Durable sessions, frame results, corrections, undo and derived statistics |

The current camera snapshot endpoint extracts one frame from a completed recording segment. It is not a high-rate observation stream. The phone player exposes play/stop/mute/rotation, and Cast loads everything as live HLS. The annotation encoder currently rejects non-live sources. These are explicit implementation dependencies, rather than functionality already supplied by the SDK wrappers.

## Architecture

```text
Touch controls ─────┐
Sky tool calls ─────┼──> AppCoordinator / typed commands
Vision proposals ──┘             │
                          Validation + host authority
                                 │
                    Durable game events + effect results
                                 │
                         SkyEyeState / projections
                          │        │        │
                       Phone      TV     Sky context
```

Keep canonical match/frame/table state and its deterministic event reducer in the new platform-independent `SkyEye.GameState`. `SkyEye.Core` retains application commands, analysis and consumer projections; rules consume the state/event model rather than owning a competing ledger. Put orchestration, persistence and Android adapters in companion services, extracting them from `MainActivity`. Add a Razor UI library and an Android-only MAUI Hybrid host. Validate the host with a separate development package ID before replacing the existing companion. The production migration retains `uk.co.skyeye.companion`, signing identity, private storage and launch/service contracts. Leave camera capture working through its existing protocol until the observation/replay extensions are independently verified.

**One source of truth:** the coordinator owns the application snapshot `SkyEyeState`, which exposes the authoritative game snapshot reduced by `SkyEye.GameState`. `UIAgent<SkyEyeViewState>` holds a sanitized observable projection, not a second independently mutable game state. State mappers accept validated application snapshots/proposals; model-produced patches cannot directly set a winner, group or calibration. Game observations and corrections enter through typed domain inputs and append events. Tool/UI requests dispatch typed commands and the resulting coordinator state updates both the Blazor projection and native scene. Preview state remains separate until accepted against the original version.

**One command executor:** a local `IChatClient`/event adapter bridges actual voice/tool/activity events into the Blazor block model. Prove unsolicited continuous-voice events and cancellation early; do not assume a request/response chat API automatically fits the existing voice session. The voice transport and Blazor tool pipeline must not both execute the same call. Tool IDs and command IDs correlate one execution with its rendered pending/result blocks. App-originated progress can use custom content without requiring remote AG-UI endpoints.

**One media scene:** use a native media host/MAUI handler and a Razor controls layer. First prove WebView transparency, touch routing, native Cast pickers, accessibility and lifecycle on Pixel 11. If stacking a full-screen WebView over the native view is unreliable, keep it in contextual regions/sheets while the native surface remains full-screen. Do not replace ExoPlayer with browser video just to simplify layout. Neither DOM pixels nor private control sheets are the source of the TV stream.

Suggested state slices:

- **Connection/Capture:** camera identity, reachability, host ownership, capture status, timeline and replay availability. Starting a frame and starting recording are separate operations.
- **Session:** stable player identities, rules profile/version, frame ledger, score and session status.
- **Frame/Turn:** breaker, current player, open/assigned groups, legal targets, foul entitlement, pending decisions and result.
- **Table/Shot:** immutable timestamped observations, calibration version, confidence, motion, previous settled layout and current shot phase.
- **Analysis:** candidates, selected candidate ID, table/rules version, expiry and verified explanation data.
- **Presentation:** separate phone and TV modes, live/replay source, overlays, selection, zoom, replay position/speed and requested versus observed playback status.
- **Sky/Activity:** disconnected/connecting/ready/listening/speaking/working/muted/error plus short app-generated task progress.

Commands carry an operation ID, origin, expected state version and relevant frame/timeline identity. Serialize mutations. Reject stale selections and duplicate side effects. Camera and receiver effects produce pending/succeeded/failed results; the state cannot claim a clip saved or TV playing until the appropriate acknowledgement arrives.

Store durable game events and operation outcomes, with schema versions and atomic writes/checkpoints. Keep video frames and high-rate detection history bounded and separate. Recovery rebuilds scores and history, then rechecks camera, host and live table; it does not restore yesterday's detections as current facts. For the first version the owning companion holds the match journal. Host changes must explicitly restore/import that journal or start a new session; ownership of the camera alone must not imply possession of another phone's game history.

## Implementation chunks and acceptance gates

The sections below describe milestones. The commit-sized execution backlog at the end is the implementation unit: each row has one bounded result and a specific check. Run core checks for state/rules changes and build Android sequentially. Install after each complete user-visible slice; retain a usable build throughout.

### 0. Prove the actual Blazor AI Android host

1. Pin the two SDK/build lanes, reconcile .NET 11 Android MAUI packs, restore pinned UI packages and document the exact toolchain.
2. Build an Android-only minimal MAUI Blazor host with a fake `IChatClient`. Render `UIAgent<SkyEyeViewState>`, a real typed tool block and one proposed-state accept/reject interaction without a network/model dependency.
3. Load the existing native media/vision/voice libraries. Embed the player under/alongside contextual Razor controls and verify layout/touches, Cast picker and lifecycle.
4. Test a real trimmed Release device build, runtime/native compatibility and asset loading. .NET 11 changes the standard Android runtime to CoreCLR; old Mono/AOT build assumptions must be checked, not carried over blindly. Keep experimental NativeAOT out of the first migration unless separately justified.

**Gate:** The real package works on Pixel 11, shows a streamed tool result/state change, cancels cleanly and does not disrupt native playback. Confirm APK signing, Java callbacks, OpenCV/WebRTC loading and 16 KB native/package alignment. No camera reinstall. If the gate fails, record the specific compatibility failure and revise the host choice before replacing production UI; do not silently ship an imitation of the requested package.

### 1. Establish the shared command/state layer

1. Add typed state, command/result envelopes, a serialized dispatcher and state subscriptions.
2. Extract camera/session/clip effects from `MainActivity` behind adapters; route existing touch actions through them.
3. Move table analysis/candidate lifecycle into a coordinator, preserving freshness, cancellation, bounded work and host restrictions.

**Gate:** Existing connect, host, capture, save/download, candidate cycling and Cast flows behave as before. Duplicate save commands do not create multiple clips. A stale analysis response cannot overwrite a newer calibration or camera connection. No credential or host token enters renderable state.

### 2. Deliver the portrait visual redesign

1. Replace the scrolling home with the verified MAUI Hybrid shell containing an edge-to-edge full-screen native video container, portrait by default. Respect cutouts, navigation gestures and accessible touch targets.
2. Implement contextual controls and the swipe-up sheet as Razor components. Add tap-to-reveal controls with a short inactivity fade, a small Sky affordance and temporary messages. Keep controls visible during editing, errors requiring action and accessibility interaction. Use low-level Blazor AI boundaries/renderers within this custom layout; do not make `ChatPage` the resting home screen.
3. Move calibration onto the same surface in an explicit frozen-image editing mode. Preview proposed corners/ball corrections with accept/reject/adjust before changing canonical state.
4. Add calibrated table framing: first rotate/fit without hiding pockets, then prototype GPU perspective rectification so the table fills the portrait view. Reuse one coordinate transform for video, paths, ball hits and calibration; preserve a whole-camera fallback. Phone framing and TV framing are independent.

**Gate:** Pixel 11 shows the whole playable table without scrolling; opening/closing the sheet does not reload playback. Corners, balls and trajectories remain aligned through portrait/landscape, resize and calibration changes. Editing pauses control fading. A guest can watch without host-only controls appearing.

### 3. Make Sky control the application

1. Rename user-facing Maeve text, assistant identity, status and components to Sky. Preserve encrypted credentials, preferences, calibrations, clips and existing archives.
2. Give touch and voice the same command catalogue: connect/discover, host claim/release, recording start/pause/resume/stop, save a bounded duration, download/open/share clips, analyse/calibrate/correct, choose group, show choices/select/clear, and Cast connect/play/stop.
3. Feed structured operation results and observable voice state into the real Blazor AI block/projection adapter and the Sky indicator. Map actual integration events where supported; do not infer a listening state just from an animation timer. Add a minimal typed-input fallback inside the sheet, sharing command execution with voice.
4. Maintain references to the currently displayed candidate set, ball, clip and surface. “Number two” resolves against that set/version; “that one” asks briefly if ambiguous. An explicit shots request refreshes analysis when required by the strict fresh-state gate.

**Gate:** “Sky, show me my shots,” “number two,” “clear that,” and “save the last 45 seconds” cause the same state/effects as touch. Failures are visible and spoken truthfully. Guests cannot obtain host actions through voice. Native device/permission/share pickers remain available where Android requires them.

Use the existing user-started continuous conversation initially. Saying “Sky” within that conversation is not a new always-on wake-word capability. A dedicated wake-word/background-listening feature is a separately scoped enhancement.

### 4. Add durable two-player match management

Depends on R01's Match & Table State core. G01-G05 extend its commands, journal and projections for complete match management; they must not introduce a separate game ledger. R01 proves only the minimal two-player frame/shot/correction slice, without complete rules or score UI.

1. Introduce Session → Frames → Turns → Shots; start with player-declared events and a reviewed, versioned implementation of the currently referenced rules profile. Verify its exact break, assignment, foul and black-ball rules before encoding them. Add other profiles only with their own rule fixtures.
2. Implement start/end session, start/reset frame, breaker/current-player selection, assign/correct groups, declare/correct foul, record/correct winner and undo. Resetting a frame must not erase earlier frame wins.
3. Derive score and legal-target state from committed events. Render a restrained current-player/group/score HUD only when a match exists. Add match controls/history to the sheet and equivalent voice tools.
4. Persist events and rebuild after process death. Corrections append amendments referencing the original event; replaying the ledger repairs dependent turns, scores and statistics. Duplicate winner events cannot count twice.

**Gate:** Play three declared frames between two players, correct a group and winner, undo a foul and restart the app. Score, turn, legal targets and history all agree. Changing the current player invalidates incompatible displayed candidates immediately.

This makes scoring and game-aware advice useful before automatic refereeing is ready. Unknown game state remains unknown; Sky requests the missing declaration rather than guessing from colour counts.

### 5. Turn TV into a directed presentation surface

1. Generalize the candidate renderer into a shared table scene: paths, selected-shot emphasis, ball/pocket highlights, scoreboard and small transient annotations. Render that scene into TV media; phone buttons/sheets/API setup must never be captured as TV content.
2. Support `ShowLive`, `ShowShotChoices`, `ShowShot`, `ClearOverlays` and `ShowScoreboard`, targeting phone, TV or both. Retain two-second display cycling over already computed choices where useful; T13 supersedes the ten-second automatic analysis refresh with explicit observation requests. Pinning a candidate stops rotation; layout/turn/version changes clear it.
3. Extend player/Cast adapters for buffered MP4 replay, position, pause/resume and seeking. Load replay with the appropriate media type, not the current live-HLS definition. Initially use camera-hosted saved clips; keep live capture running and return to live on command.
4. Add replay-window selection relative to camera/shot timestamps, rather than the delayed phone image. “Show that shot again” resolves to the last completed shot when available; before shot tracking exists, expose a clearly named recent replay. Support temporary replay assets with bounded retention without cluttering the user's saved-clip library.
5. Prioritise automatic correction of recorded footage before slow motion: T08-T10 establish historical corner geometry, a compositor that accepts recorded/paused sources, and corrected derivatives used consistently by phone, TV, download and share. Preserve the smooth corrected-live quality, actual table proportions and complete pocket mouths. Historical clips use their own recorded geometry, never the current camera's corners. Old clips without geometry metadata are analysed automatically from their own frames.
6. T11-T12 persist a recording-time annotation journal and render it into saved/exported clips. Record scene changes, candidate cycling/pinning, highlights, captions, clears and scores when those features exist; retain geometry/coordinate-space versions and source timeline/PTS. An annotation belongs to the source frame it describes, not its arrival time or delayed TV playback position. Replay seeks and slow motion select the historical scene for the corresponding source time. Only table annotations enter exports; controls, credentials and assistant transcripts remain outside the scene.
7. T14 proves background delivery and bounded long-session behavior. Then prototype playback-rate support on the actual Chromecast (T06). If its default receiver cannot deliver reliable quarter speed, prepare a slow-motion asset locally and serve that. Add freeze and calibrated pocket zoom through the shared compositor (T07), with independent TV framing. Slow motion transforms video and annotation timing together, with an explicit supported audio policy.

**Gate:** On the physical TV: show legal choices, select one, clear, show score, show replay, pause, seek back two seconds, play at supported slow speeds and return to live. Confirm receiver acknowledgements and inspect TV pixels; sender output alone is insufficient. Account for HLS delay and attach annotations to the correct source timeline.

T14 now implements a service-owned GPU decoder/compositor/encoder and source-clock journal independent of activity visibility and phone playback. Corrected moving TV, background/lock and separate local-phone playback are recorded in the [T14 report](t14-report.md). Automatic network recovery, unplugged endurance, physical latency/audio sync and moving annotation correlation remain open acceptance gates. Plain TV releases the compositor. A custom Cast receiver remains an alternative only if the native relay cannot meet measured presentation requirements.

### 6. Build shot observations and proposed game events

O01-O05 supply richer timestamped evidence to R01's typed input seams. The state library contains no CV implementation or timers. T13/T14's bounded decoded-frame architecture is the integration basis; preserve requested-only expensive perception while later missions add measured motion/shot tracking.

1. Capture a labelled sequence corpus under the user's standard lighting: pots/misses, cue-ball loss/replacement, black pots, break/rerack, hand/cue obstruction, shadows, near-pocket balls, close collisions and camera interruption. Keep held-out sequences separate from tuning fixtures.
2. Measure a lightweight motion detector and temporal tracking over decoded frames, retaining native media timestamps. Run expensive colour/circle analysis on stable observations. The current one-snapshot-per-completed-segment endpoint and 10-second candidate refresh cannot be the shot-event source.
3. Implement `AwaitingShot → Moving → Settling → Complete/Uncertain`. Associate balls across observations; close same-colour crossings may remain ambiguous. Produce before/after evidence and candidate pot, scratch and rack events with source/time/confidence.
4. Present inferred changes as proposals until repeated evidence and reviewed rule preconditions are satisfied. Occlusion, a missing frame or a disappearing ball alone is not a pot. A returned full rack proposes a new frame; it does not silently reset a match.

**Gate:** Run annotated held-out video through the pipeline and report shot-boundary accuracy, false pots, missed pots, scratch proposals and uncertain-event rate. On-device testing measures latency, CPU, memory and battery. No automatic score or group change follows a one-frame disappearance.

### 7. Connect observations to rules and corrections

Rules R04/R02 and quiet-mode R03 consume the Match & Table State events and canonical state. The domain correction seam is established in R01; conversational/touch workflows and complete rule decisions follow here.

1. Evaluate accepted shot events using the chosen rules profile: open table, group assignment, legal targets, turn continuation/change, foul entitlement and frame result.
2. Start automation with reliably supported settled-table outcomes. Keep first contact, cushion requirements, simultaneous pots and other contact-dependent calls pending/manual until a higher-rate pipeline proves the evidence needed for each rule.
3. Add voice/touch correction interactions: “I'm yellows,” “that wasn't a foul,” “the yellow is still there,” “Mike won that frame,” and undo. Rebuild affected state and immediately update both surfaces and Sky's context.
4. Offer restrained proactive notifications after settlement or rerack, with a quiet mode. Avoid talking over shots and avoid repeated requests about the same pending decision.

**Gate:** Scenario fixtures cover disputed detection, ambiguous open-table pot, scratch, legal/illegal black, correction after later turns and camera-timeline discontinuity. Rules decisions cite the committed evidence/profile, and corrections repair the full downstream projection.

Counting remaining reds/yellows helps validate a frame; it cannot identify the player at the table, prove first contact or settle every foul. Missing balls must not automatically enable a black shot.

### 8. Add contextual coaching and session analytics

C01-C03, replay reconstruction and future 3D/actual-versus-predicted analysis consume R01's canonical state and event/evidence history. Presentation replay T03-T12 retains its existing media scope and IDs; it does not own the game model.

1. Implement the first coaching slice: tap/reference a red, ask why it cannot be potted, run geometry and highlight the verified blocker/path on the table and optionally TV. Expose structured rejected-route reasons where today's planner only returns a boolean.
2. Extend shot/replay explanations using actual observation evidence. Describe candidate difficulty as geometric ranking, not a calibrated pot probability. “Safest” needs an explicit supported criterion, not a renamed score.
3. Derive statistics from accepted/corrected events: frame wins, observed pots/misses/fouls, runs and break results. Show coverage when events were not recorded or remained uncertain. Add a session summary and optional longer-term player history.
4. Treat post-impact cue-ball finish, spin/pace simulation and measured expected-versus-actual contact coaching as further research features. The existing ghost ball is an aiming contact position, not a predicted cue-ball resting position.

**Gate:** An explanation highlights a real verified obstacle; an uncertain shot produces an appropriately limited answer. Correcting a historical event updates its statistics and session summary. No unsupported percentage or positional prediction is displayed.

## Operation policy and UX details

Ordinary explicitly requested actions should execute immediately: show/clear choices, save a clip, seek replay, change surface or set a player/group. Use clarification for ambiguous references and preview/confirmation for uncertain inferred events or consequential replacement/deletion. Do not add repeated approval prompts to normal play. Calibration changes are previewed against an explicit version; obsolete proposals cannot apply after the mount/layout changes.

Sky gets a compact snapshot of authoritative state, freshness, pending proposals and what is actually displayed. Tool results identify success/failure and source versions. Progress comes from application steps such as “checking table” and “preparing replay,” not invented reasoning or fabricated vision confidence. Manual controls remain available for offline voice, noisy rooms, corrections and accessibility.

## Release strategy

- **Compatibility milestone:** chunk 0. Installed/locked toolchain, actual Blazor AI rendering and native media in an Android Hybrid host.
- **Release A — visual and voice glow-up:** chunks 1–3 after compatibility passes. Full-screen portrait Hybrid UI, sheets, Sky identity, reliable command-driven actions, existing castable candidates.
- **Release B — match and TV director:** chunks 4–5. Durable manually managed games, current-player legal shots, scores and voice-controlled TV/replay.
- **Release C — assisted referee and coach:** chunks 6–8. Validated shot observations, corrections, supported rules automation and honest session statistics.

Before each release, run the existing core/native checks plus the relevant new state/media cases, build a signed trimmed Release APK with the chosen runtime's supported settings, and verify on Pixel 11 with the mounted Pixel 10 and Chromecast when available. Preserve app data and camera recording during companion updates. Long-session acceptance must include camera/network loss, host expiry, process death, TV reconnect, background/return, replay exhaustion and thermal/battery measurements.

## Commit-sized execution backlog

The previous eight milestones were not sufficiently small as individual commits. Use the following bounded work units. A row normally ends in a checked commit; split it again if a compatibility problem expands its scope. Preserve the old companion until the replacement passes its migration checks. Commit production changes only after relevant checks pass; device-gated work must record a blocked/unverified check if devices are unavailable, rather than claiming completion.

### Match & Table State epic

| ID | Bounded result | Verification before proceeding |
|---|---|---|
| R01 | Establish the Match & Table State Core — implemented and verified 6 October | Pure C# state/evidence/events/reducer, provenance/corrections, durable journal, minimal shared-coordinator integration and readable yellow-pot/correction/rebuild checks. Pixel 11 verifies real uncertain decoded evidence and a separate declared scenario; no autonomous pot detector is claimed. See [completion report](r01-match-table-state-report.md), [brief](r01-match-table-state-brief.md) and [chunks](r01-match-table-state-plan.md) |

### Existing delivery backlog

| ID | Bounded result | Verification before proceeding |
|---|---|---|
| P01 | Pin SDK 10 camera and SDK 11 Hybrid lanes; update sequential build entry points | Each invocation reports its intended SDK; original core checks still pass |
| P02 | Reconcile Android MAUI workload set and lock package/toolchain versions | Minimal Android Hybrid host restores/builds with recorded pack versions |
| P03 | Real AI package renders fake streaming content, tool result and typed state | Pixel renders incremental updates and a pending-to-complete tool block |
| P04 | Proposed-state preview with accept/reject against a version | Reject preserves state; obsolete accept fails |
| P05 | Native video plus Hybrid layer, native libraries and Release runtime | Playback/touches/picker work; trimmed APK starts and loads native libraries |
| S01 | Immutable state/command/result types and subscriptions | Pure checks prove ordering and stale-version rejection |
| S02 | ShowShotChoices end-to-end through coordinator, existing UI first | Touch drives the same validated phone/TV candidates |
| S03 | SaveClip effect, bounded duration and duplicate operation handling | Duplicate request creates one clip; failed save remains failed |
| S04 | Connect/host/capture effects with existing authority | Guest rejected; reconnect/host expiry reflected correctly |
| S05 | Remaining clip and analysis effects outside Activity | Download/correction/group flows preserve cancellation and freshness |
| H01 | Portrait Hybrid shell using the proven native handler | Whole table visible without scrolling |
| H02 | Gesture/tap controls, fade rules and inset/accessibility handling | Controls remain reachable and do not fade during editing |
| H03 | Connection/session/Cast sheet sections | Open/close does not recreate or reload media |
| H04 | Clips and key setup through platform adapters | Offline play/share work; key never appears in DOM/state snapshots |
| H05 | In-surface frozen calibration and correction workflow | Preview/reject/adjust works; applying invalidates old advice |
| H06 | GPU table-framing spike and shared coordinate mapping | Pockets and paths align; fallback remains usable |
| H06b | Automatic pocket detection and rectangular phone/TV view | Four detected corner-pocket centres map to exact rectangle corners; detection retries without manual corner picking; uncertain views fall back whole-camera |
| H07 | Production package/signing/storage migration | In-place update retains clips, key, calibration and launch/service behavior |
| A01 | Rename Maeve to Sky in product and assistant identity | UI/spoken identity agrees; legacy data remains readable |
| A02 | Bridge real voice events into IChatClient/custom AI blocks | Continuous events, turn cancellation and interruption do not double-execute tools |
| A03 | Read state and execute choices/select/clear via shared tools | Voice/touch produce identical state; stale number rejected |
| A04 | Voice camera/session/clip commands and typed-input fallback | Save 45 seconds and capture controls acknowledge real results |
| A05 | Sky state/progress indicator and contextual references | Listening/speaking/muted/error reflect actual events; ambiguous references clarified |
| G01 | Session/players/frame ledger plus versioned rule-profile baseline | New frame does not reset session score; reviewed rule fixtures pass |
| G02 | Turn, groups and legal-target projection | Player change clears incompatible paths and filters candidates |
| G03 | Frame result/score commands with duplicate protection | Repeated winner call counts one win |
| G04 | Durable event journal and restart recovery | Recovered score agrees; live facts are revalidated |
| G05 | Amendment/undo commands plus score HUD and Sky tools | Correct winner/group/foul repairs dependent projections |
| G01-G05 / C03 manual slice | Implemented 6 October: phone match setup/score, declared groups/turns/results, earlier-frame correction, supported undo, recovery and previous-match summaries | Pure/core fixtures and actual Pixel 11 Lab touch/restart/archive scenario pass; no camera observation/TV test. [Report](manual-match-report.md). Complete rules, voice, TV HUD and shot statistics remain separate |
| T01 | Generalize shared native scene beyond candidate paths | Phone/encoded TV show same scene versions; sheets excluded |
| T02 | Target-specific live/choices/pin/clear/score commands | Phone and TV can differ intentionally; display cycling retained, timer-driven analysis superseded by T13 |
| T03 | Buffered MP4 load and replay position callbacks — implemented 4 Oct | Chromecast plays a saved camera clip with position callbacks; phone stays live and current capture continues. Evidence: saved-clip-tv.json |
| T04 | Pause/resume/seek/return-live commands — implemented 4 Oct | Receiver-backed confirmations and physical TV pause/seek/resume/return-live verified; final production build also preserves playback when seeking. Evidence: tv-replay-controls.json |
| T05 | Recent replay window and bounded temporary asset lifetime — implemented 6 Oct | Camera-clock 15/30/60-second windows, separate two-asset/128 MB store, ten-minute expiry; physical TV playback and pause/seek/resume confirmed, saved count remains eight. Evidence: recent-tv-replay.json |
| T06 | Playback-rate support/fallback slow-motion proof | Physical TV plays measured 0.25x/0.5x when offered |
| T07 | Replay freeze/zoom, using T09-T12 compositor | Frozen corrected replay and pocket zoom keep historical annotations aligned; no live-player requirement |
| T08 | Historical automatic corner detection and geometry provenance — implemented 6 Oct | Source-scoped geometry journals, recording origins and automatic recorded-frame detection; moved/dim/covered/recovery fixture and real camera replay pass on Pixel 11. Evidence: recorded-geometry.json |
| T09 | Shared compositor for live/recorded/paused sources — implemented 6 Oct | GPU render puts historical perspective correction into portrait/landscape MP4 pixels, using the live pocket transform; independent pixel/audio checks and physical phone/TV pause/seek/resume pass. Evidence: recorded-compositor.json |
| T10 | Corrected replay, library, download/share/export derivatives (implemented 6 Oct) | Recent and saved clips use automatic correction everywhere by default; final MP4 pixels are corrected, audio/sync retained, publication verified and source footage preserved |
| T11 | Durable annotation journal tied to recording time (implemented 6 Oct) | Scene/cycle/pin/clear/score changes survive restart and select the correct historical frame; unrelated/stale scenes are rejected; no current annotation is invented for old footage |
| T12 | Bake historical annotations into saved/shared clips (implemented 6 Oct) | MP4 contains recorded table overlays without needing SkyEye to play it; phone/TV/export agree after seek, clear and geometry changes; originals and sidecars retained |
| T13 | Make SkyEye Sing: live-first performance and Sky orchestration — runtime implemented, acceptance open | Actual-device baseline before changes; idle full-table/candidate analyses = 0; one bounded fresh observation per request; shared Sky/touch commands and independent presentation; measured camera-to-phone latency target below 500 ms; no continuous annotation encoding for plain live surfaces. See [mission brief](t13-live-first-brief.md), [implementation chunks](t13-live-first-plan.md) and [report](t13-performance-report.md) |
| T14 | Background corrected delivery and media resource acceptance (formerly T13) — runtime implemented, acceptance open | TV remains corrected when companion backgrounds/locks; scene journal and file exports recover from interruption; bounded queues/cache, measured quality, sync, latency and thermal behavior. See [report and remaining gates](t14-report.md) |
| O01 | Labelled sequence corpus and held-out split | Manifest records cases, ground truth and split provenance |
| O02 | Timestamped decoded-frame feed plus lightweight motion benchmark | Bounded queue; no use of 10s snapshot cadence as shot evidence |
| O03 | Shot boundary state machine | Held-out boundaries and interruptions scored |
| O04 | Temporal association and pot/scratch/rack proposals | One-frame loss/obstruction cannot become a committed pot |
| O05 | Proposed-event UI and accept/reject/correction tools | Accepted event references evidence; rejected proposal does not recur unchanged |
| R04 | Apply accepted shot outcomes to turn/groups/entitlements (previous R01; rules consumer of R01) | Rules fixtures cover open table, continuation and foul cases |
| R02 | Black/frame outcome checks and history corrections | Ambiguous evidence stays pending; corrections repair later state |
| R03 | Quiet-mode proactive settlement/rerack hints | No repeated prompts or speech over a shot |
| C01 | Structured blocked-route reason and on-table explanation | Highlighted obstruction is geometrically verified |
| C02 | Replay coaching with evidence/context bounds | No invented contact or post-impact cue finish |
| C03 | Statistics/session summary derived from corrected ledger | Amendments change aggregates; incomplete coverage shown |
| X01 | Network/host/process/TV recovery acceptance | State and media recover without duplicated effects |
| X02 | Long-session thermal/battery/latency and accessibility acceptance | Recorded device evidence and explicit remaining limits |
| X03 | Final migration report, signed APK, source snapshot and rollback instructions | Artifact provenance matches tested commit |

**Current next work:** remaining T13/T14 acceptance, then T06/T07. R01.01-R01.07 and the original P01-P05/S02 starting sequence are delivered. G/O/R04/R02/R03/C work extends or consumes the new canonical state. Release A is not dependent on automatic refereeing, and Release B is not dependent on contact-level vision. Wake-word listening and advanced cue-ball physics remain separately scoped.

## Historical execution order update — voice deferred

On 4 October, the user chooses to defer Sky voice/API-key work until they revisit key setup. Leave A02-A05 deferred; do not use the saved personal credential for voice testing meanwhile. The non-voice work can proceed independently.

Recommended next: T01 shared native phone/TV scene, then T02's touch/live/candidate controls in the portrait UI (retain two-second display cycling; T13 replaces timer-driven analysis with requested observations). Score presentation waits for G01-G05's ledger. Follow with T03-T04 playback of existing saved clips on TV, pause/seek and return live. G01-G05 session/player scoring, restart recovery and undo can also proceed without an API key. Existing native More controls remains available until each replacement touch flow is verified. Keep the small checked-commit and physical-device gates for these reordered chunks.

The user now prioritises H06b ahead of saved-clip TV playback. Locate actual corner pockets automatically, including tonight's dim/racked frame. Pocket-centre framing is a distinct display transform from cloth calibration used for ball analysis; do not silently overwrite the latter. Keep a small outer margin so complete pocket mouths remain visible while their centres define a perfect rectangle. Verify phone texture and encoded TV geometry with the same mapping, and clear/re-detect on camera change. Automatic detection must not require manual corner selection; uncertain or obstructed results keep the whole-camera view and retry. A single tuning frame does not establish general pocket accuracy.

## Execution order update — all footage corrected and annotations recorded, 6 October

This update supersedes the T05 completion note's recommendation to start T06 next. New order: **T08 → T09 → T10 → T11 → T12 → T13 → T14 → T06 → T07**. First deliver corrected replay and exports, then recorded annotations, then live-first performance and orchestration, background acceptance, and slow motion/freeze/zoom. Keep the existing IDs for completed chunks and separate commits/device gates for each new chunk. Voice/API-key work A02-A05 stays deferred: geometry and journalling can be verified using existing candidate overlays and declared scene fixtures. Actual Sky-generated annotations join the same scene/journal contract when voice tools are implemented.

### T08 — prove recorded geometry before changing media

Persist recording timeline, source start/end, image dimensions/orientation, automatic pocket centres, confidence and transform version. Geometry belongs to a source-time interval, not a mutable global calibration. On camera/mount change, close that interval and re-detect; never stretch old footage with new corners. Keep display pocket geometry separate from cloth/ball-analysis calibration. For existing saved clips, inspect frames from that clip, with retries through obscured intervals and a stable accepted transform; never require manual calibration. Gate: recorded fixtures with moved mounts, dim lighting and covered pockets produce the right historical mapping or explicitly uncertain fallback. Metadata survives restart and clip-window selection preserves the applicable intervals.

### T09 — prove a reusable recorded-media compositor

Accept a recorded decoder frame and its source PTS independently of a visible live player. Reuse the pocket-centre homography and scene coordinate mapping for GPU video and overlay rendering, with one correction pass. Preserve physical table proportions, all pocket mouths and the current smooth 1080p live-feed standard; output orientation can suit phone, TV and files without changing geometry. Gate: a short fixture renders matching rectified pixels on phone, decoded TV output and a saved MP4, including pause and seek. Detecting uncertain corners automatically retries; show the whole-camera fallback honestly until reliable geometry is available rather than fabricate alignment or ask for manual corners.

### T10 — route every playback/export path through corrected media

Generate and verify corrected derivatives for recent replay and saved clips, including older clips. Phone playback, TV, downloaded library files and shared MP4s use that result by default. Transform actual file pixels: a player-only warp does not satisfy corrected download/share. Preserve original recording/audio and use verified atomic publication, cancellation/failure cleanup and bounded temporary/cache lifetime. Associate derivatives with source checksum, geometry intervals and render version so changed geometry invalidates the right output and already-corrected files cannot be warped twice. Show preparing/unavailable status when required, and never report a corrected export before it exists. Gate: real recent and saved footage has aligned corners, retained pockets, clear edges and synchronized room audio on phone/TV and when played as a standalone shared file; capture continues and saved originals remain intact.

### T11 — record annotations against the footage they describe

Journal actual published table-scene events with source timeline/PTS, table/layout and geometry versions, coordinate space, target and bounded render content. Record show/change/clear/expiry events and candidate rotation/pinning so an exported clip preserves the scene displayed at each source time. Relate delayed decoded frames to the camera timeline explicitly; wall-clock tool arrival or TV position cannot place an annotation. Historical replay evaluates scene validity at historical source time, not today's live TTL. Keep annotation data alongside a permanent saved clip before its rolling journal window expires; trim recent replay metadata with its temporary asset. Gate: known moving-frame fixtures demonstrate alignment across delayed delivery, two-second cycling, clear/expiry, seek, mount changes and process recovery. Unrelated timelines and invalidated layouts cannot acquire annotations. A clip recorded without annotations remains without them; current suggestions are never retroactively invented.

### T12 — save annotations in the MP4 itself

Render corrected video plus its historical scene journal into the saved/share derivative. Table paths, highlights, supported captions and scores are included; phone controls, setup sheets, secrets and general conversation are excluded. Preserve original footage and the journal for reliable re-rendering, but the default shared file needs no app or sidecar to display its annotations. If no annotations were recorded, export a corrected plain clip. Gate: the same annotated interval matches on phone, TV and standalone MP4, survives pause/seek, and clears markings at the correct frame. Interrupted/duplicate export cannot publish a partial clip or clutter the saved library.

### T14 — prove that correction survives leaving the app (formerly T13)

Measure and choose the service-owned or camera-side processing path that can sustain automatic correction when the companion is backgrounded/locked. Exercise live TV, journalling and media preparation through background/return, network/receiver loss and interrupted exports. Persist only valid annotation spans; absence of an active scene must not create stale overlays. Gate: an observed long-session phone/Chromecast run proves consistent geometry, bounded memory/storage/queues, acceptable temperature/latency and audio sync. Current foreground-only fallback remains a stated limitation until this gate passes. This acceptance is required before describing all footage as universally corrected.

Implementation update, 6 October: an independent foreground media service now owns the GPU live-TV pipeline and source-clock journal. Moving corrected watermarked TV was user-confirmed; service frames continued through home/background, lock and separate saved local phone playback. Camera geometry now stays independent of phone clip dimensions and survives return to live. Permanent export retry retains verified originals and frozen journals within bounded recovery storage. Shorter segments reduced user-observed TV latency from about 11 seconds to 3.5–4 seconds; the user confirmed the final normal release is smooth. It is installed and corrected TV restored. Full T14 acceptance remains open: the network test required a Pixel 11 restart, and unplugged endurance, physical audio synchronization and background annotation correlation need further evidence. See [T14 report](t14-report.md).

T06/T07 then reuse corrected video and historical annotations. Seeking and slow motion map to the original source timeline, keeping drawings attached to the correct frames; freeze/pocket zoom transforms the same composed scene. Rate support and room-audio behavior still need their own physical receiver gate.

### T10 implementation evidence - 6 October

Production Hybrid saved clips, recent TV replay, local playback, downloads and sharing now use verified corrected derivatives, with separate phone/TV orientations and original audio/footage retained. Preparation is cancellable, paired publication is atomic, and temporary replay files are bounded and expire. See `evidence/corrected-clip-delivery.json`. The production APK is installed on Pixel 11; camera recording continued and phone/TV returned to live.

The delivery pipeline is implemented, with a remaining visual acceptance gap: automatic historical corner detection accepted only 12.0 of 31.4 seconds in the saved test clip and 2.0 of 16.8 seconds in the latest dim recent replay. Other intervals preserve the whole-camera view. Do not claim universal correction until detection coverage and T14 background acceptance pass. Legacy native More-controls media workflows remain outside this migrated production flow. T11 annotation journalling is next, then T12 baking historical annotations into standalone shared files.

### T11 implementation evidence - 6 October

Production Hybrid now journals actual phone draws and TV-composed frames against the embedded camera recording clock. History retains bounded render content, separate targets, source-image coordinates, layout and geometry provenance, observed clears/expiry and gaps. The journal survives restart; clip history is retained on verified save acknowledgements and before rolling eviction, and accompanies corrected saved/recent bundles. Legacy footage does not inherit current annotations. Evidence: `evidence/recorded-annotations.json`, `evidence/annotation-clock-checks.json` and `evidence/annotation-live-checks.json`.

Core fixtures pass delayed frames, cycle/pin/clear/expiry, mount/layout fencing and retention/recovery; native timestamp and real-video scene/reload proofs pass on Pixel 11. The production update preserves storage and camera recording, and live TV was restored after transient buffering. Real-video geometry stayed on the same mount; moved-mount rejection is fixture evidence. T12 is next: use this history to bake annotations into MP4 pixels. T14 background acceptance and T10 detection coverage remain explicit limits.

### Watermark delivery - 6 October

The supplied `watermark.png` now brands companion live phone/TV output and prepared saved/recent portrait and landscape MP4s at the top-left. It remains upright after perspective correction, preserves its circular shape and transparency, and is included once in shared file pixels. Asset/style fingerprinting rebuilds older derivatives from retained originals. Legacy More-controls clip routes now also use corrected watermarked derivatives. Evidence: `evidence/watermark-delivery.json` and its decoded-pixel and existing-clip proofs. The user confirmed the round white logo on live TV.

T14 must extend this to independent background/live-TV composition when the companion leaves its live view; direct camera URLs and private originals currently remain unbranded. T12 follows T11 to bake historical drawings into those same watermarked exports. Historical corner-detection coverage remains a separate acceptance gap.

### T12 implementation evidence - 6 October

Corrected prepared MP4s now include historical published scenes directly in their video pixels. Portrait exports follow recorded phone scenes; landscape follows recorded TV scenes. Paths and markers use the same historical homography as the video; labels and score text stay upright, and the watermark is applied last. A frozen source-scoped annotation journal is included in the atomic media bundle, and its fingerprint invalidates outdated derivatives. Empty history produces plain corrected watermarked footage. No current suggestions are added to old clips.

Evidence: `evidence/recorded-annotation-compositor.json` and linked fixture/actual-replay decoded-pixel and TV-control proofs. Both orientations of a real recent replay pass independent checks for captured candidate/cycle/pin content and historical clears, with original room audio retained. The user confirmed annotations and watermark in the standalone MP4 on Chromecast; pause, seek to zero and resume were receiver-confirmed. Recording continued and permanent camera clip count stayed eight.

T13 is next: measured live-first performance, on-demand perception and shared Sky/touch orchestration. T14 then handles independent composition during background/lock/local-phone playback, interrupted-export recovery and sustained resource/quality acceptance. Historical pocket coverage remains a separate visual gap. Voice A02-A05 remains deferred; the verified scene content is declared lab advice published through the real scene renderer.

### Historical priority update - new T13, 6 October

The user adopts **T13 - Make SkyEye Sing: Live-First Performance & Sky Orchestration** as the next mission. Its [full brief](t13-live-first-brief.md) is canonical; [the implementation plan](t13-live-first-plan.md) divides it into independently verifiable commits. The previous T13 is now **T14** and retains its background, recovery and long-session acceptance scope. Existing evidence reports that mention the old T13 retain their historical wording; those references now map to T14.

New order after completed T12: **T13 → T14 → T06 → T07**. Prioritize measured live responsiveness and deliberate intelligence before more perception accuracy, physics, game intelligence or UI features. Normal live viewing must not trigger full table/candidate analysis on polling or a refresh timer. Sky-compatible commands and touch actions use one typed command surface; actual voice transport/API-key integration remains A02-A05, so a key is not a prerequisite for the command/performance mission.

Default phone and TV presentation is plain live footage, with independently requested existing annotations. Existing watermark/correction capabilities must remain available; their current forced companion re-encoding and periodic pocket work are measured explicitly, not excluded from the idle-cost baseline. T13 must establish a coherent media/presentation policy that preserves these capabilities and meets the no-continuous-annotation-encoding gate when both surfaces require plain live. Do not silently disable branding or correction to manufacture a latency result. Automatic corners remain automatic; manual picking is not introduced as a performance shortcut.

Physical Pixel 10 camera to Pixel 11 display latency requires before/after evidence. Below 500 ms is the initial target, below 250 ms is a stretch goal; neither is achieved merely by a build or decoder timestamp estimate. Requested observations report source timestamp/age and analysis p50/p95. A measured transport decision precedes replacement of working HLS infrastructure. Background/lock and representative endurance acceptance are explicitly T14.

## Declared rules delivery — 6 October 2026

R04/R02's player-declared slice is delivered alongside the O-series roadmap priority. The pinned International 2c consumer records turn/group/target, placement and supported black/frame decisions with clause references; unknowns stay pending. Latest-shot replacement/undo and journal reconstruction are checked. See [report](declared-rules-report.md), [plan](declared-rules-plan.md) and [phone proof](evidence/declared-rules-phone.json).

This does not complete contact-level referee automation or change the temporal-tracking priority. O05 must feed reviewed/accepted evidence through the same state/event seam. Normal production is installed; the Lab declaration/correction/restart test needed no camera/table/TV or voice/key testing.

## O01 tooling delivered; real-shot evidence remains open — 6 October 2026

O01's offline foundations are delivered: immutable corpus/labels contracts, deterministic indexed-frame receipt replay, session-grouped split freeze, stored-detector scoring with explicit missing-output coverage, seed fixtures and annotation guidance. See the [delivery report](o01-workbench-report.md), [corpus guide](observation-corpus-guide.md) and [verification](evidence/o01-verification.json). Pure/Core/GameState checks pass and the new Release workbench builds without warnings/errors; frozen fixtures survive a fresh Windows checkout.

**O01 is partial:** the seed contains one tuned real still and one synthetic contract sequence, with zero real timed shots and zero held out. The existing OpenCV still output is historical regression evidence. Collect/review the real 60/20 session-grouped corpus, freeze before tuning and measure the required normal/failure cases before closing O01. No tracking or automatic event accuracy is established.

**Next implementation slice: O02 atomic pixels/source-PTS receipt proof**, followed by bounded active/latest frame ownership and ROI/flow benchmarks. Controlled frame-ID tests can proceed despite dim table lighting; live physical pairing/resource gates and real tracking accuracy remain required. O03-O05 retain the current order. T13/T14 acceptance, declared R04/R02 limits, voice deferral and downstream V/PH/O07 scope are unchanged. This status supplements the preserved concurrent temporal-tracking/HUD roadmap drafts.
