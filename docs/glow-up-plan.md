# SkyEye glow-up implementation plan

## Standing execution guidance

**Before every tool action submitted to automatic approval review, describe what you are about to do in commentary.** Give the checker clear, truthful context: the purpose, exact action, target device/app/files and scope of the user's authorization. Identify whether it is a read, build, UI interaction, guarded in-place update or application API request. For device updates, name the package and the existing signer/UID/private-storage safeguards; for tests, distinguish controlled fixtures from real table/camera state and say what the result can establish.

Keep each description concise and matched to the actual command. Do not rely on a vague “continuing checks” or an unexplained batch of device operations. Then perform the scoped action. Clear context helps the checker assess authorized work; it does not guarantee approval. If rejected, report the exact blocked action and the supplied reason, and accurately distinguish work that ran from work that did not. This is a standing user instruction for future glow-up work.

## Current delivery priority — Vision reliability under changing light, 8 October 2026

**P0: restore dependable table observation before expanding automatic shot advice.** The user reports that vision is “borderline unusable” in the pool room, with multiple failure types among missed balls, detections in shadows and wrong colours. This is a user-reported severity, not a measured accuracy rate. First address the current-frame cue ball dropout through [fresh cue ball recovery](cue-ball-detection-recovery-plan.md): preserve supported geometry through colour correction, add a bounded neutral-body search and stage telemetry, then measure the result before extending temporal recovery. Continue the O08.01/O01 labelled, session-separated baseline and bounded [O09 learned ball detection spike](o09-learned-ball-detection-spike.md) against the same frozen data. O09 compares the current pipeline, a small learned detector and learned proposals with existing OpenCV geometry refinement. Finish O08 safety, held-out and release gates with the selected detector before treating automatic advice as dependable; voice parity waits behind that gate.

The 8 October build contains opt-in sequential diagnostics, provisional local illumination normalization/circular hue clustering and a bounded on-device ball labeller that feeds the existing O01 corpus. No reviewed closed-blind sunlight sample or held-out lighting result has been collected, so the vision changes remain candidate branches. Do not replace the production detector unless the comparison wins on untouched real sessions, passes O08 safety/resource gates and has clear model/code distribution rights. Keep the existing C# OpenCV geometry and O01 workbench as the baseline; O09 is a bounded investigation, not production migration.

The incremental [fresh cue recovery candidate](cue-ball-detection-recovery-plan.md#delivered-candidate-and-evidence) is now native-checked and installed in SkyEye Lab. It preserves current geometry through colour correction and validates cue identities against raw body support. Controlled replay recovers three previously missing cue detections among six existing fixtures and the cue in both recent source images, without reducing labelled non-cue matches. Some candidates/colours remain unresolved; the next gate is a live stationary-table and lighting run. Further temporal recovery follows that outcome, while O09 still needs complete, reviewed recording sessions.

The subsequent v2 live regression supersedes that cue-only acceptance. v3 now prioritizes physical painted/dark body proposals, rejects partial shadow outlines using raw boundary evidence, and passes exact whole-layout matching on ten captured failures. A final three-minute version 5 readback records 132 clear 16-ball analyses and 234 successful HTTP reads, including a recovered sampling interruption. Require whole-table precision/recall and false-ready checks in future candidate acceptance, including long stationary runs and changing illumination. Keep O09 training and wider lighting Holdout gates open; burst consensus must not conceal underlying proposal failures.

**Current Lab implementation: G06, the dashboard Match and Tracking Scenario Lab.** The canonical match view, separate resettable workspace and typed controls are installed. Scenario events wait for an explicit choice, and completed scenario shots can be evaluated by the existing International Eightball rules engine. G06.03a copies the canonical match into an isolated scenario journal and proposes changed layouts after three stable diagnostic samples. G06.03b proposes a fresh camera baseline when saved positions are stale or the camera sees fewer same-colour balls than the copied inventory. The user can accept absent balls as Missing or explicitly confirm the presence of one absent red/yellow as Potted; no shooter, pocket, shot result, score or turn is inferred. Full source-time tracking and Sky voice decisions remain open; O08's broader changing-light and held-out acceptance remains open. [G06 plan](g06-match-scenario-dashboard-plan.md).

**O04.01b offline evaluator and O04.01c offline sequence viewer implemented; an O04-ready diagnostic import path is in place; live tracking remains planned.** The workbench accepts a frozen detector output and explicit inventory, position-association and movement thresholds. It reports session/tag metrics, abstains when excess candidates cannot be resolved, creates a bounded standalone viewer, and imports paired on-device detections plus calibrated, identity-labelled diagnostic shot sequences. Run it on reviewed O01 Development shots to tune; the checked-in seed has none, so there are no real association or movement results yet. Keep the ground-truth movement thresholds fixed across candidate runs. Evaluate on untouched O01/O08/O09 lighting holdouts before claiming a gain. Enable the bounded live diagnostic feed only after O02.05 timing, cadence, resource and lifecycle gates. Integrate motion/settling through O03 and reviewed reconciliation through O05. The full data contract, steps and acceptance targets are in [temporal tracking design](temporal-tracking-design.md#o0401--inventory-aware-association-from-last-seen-positions) and [the corpus guide](observation-corpus-guide.md#tune-association-and-movement-offline-o04). No runtime tracking or detector accuracy is claimed.

## Previous delivery priority — Companion UI refresh and Sky, 7 October 2026

**The A04.02 camera-connect/host/live-Cast path passed the user's Pixel 11 check. A03.02 match reads/declarations/corrections and A04.01 save/replay tools are now implemented in the Hybrid source and Release-build verified.** The tools reuse canonical `SkyEye.GameState`, durable clip saving and the existing phone/TV replay paths. See the bounded [Sky voice plan](sky-voice-priority-plan.md) and [runtime report](sky-voice-runtime-report.md).

**The companion home/settings/clips refresh, dark-session policy and supplied launcher icon are implemented.** The updated production Release was installed in place on Pixel 11 Pro XL on 7 October: signer, UID and private storage were retained, and the camera package was untouched. Device review confirms the six icon-only home actions fit in the bottom dock without scrolling and the launcher mask displays the supplied icon cleanly. Live/dark recording, Sky physics reporting and the Clips/Match flows still need hands-on acceptance when the table is available; see the [companion UI plan](companion-ui-refresh-plan.md).

The user reports that the earlier camera/session/Cast flow worked very well; that result does not cover the newer match/save/replay tools. Stop-Cast behavior, voice shot-advice parity, the updated multi-turn transcript view, new tool interruption/lifecycle cases, billing finalization and O08 held-out/unsafe/physical image gates remain open.

4 October 2026 · Updated 7 October to prioritise Sky voice after user-reported API-key setup and add corrected-history analytics, smart replay, practice and Shot Lab missions. The 8 October vision reliability priority above supersedes that execution order. R01, declared match/rules and O01/O02 foundations retain their recorded delivery status.

The original planning baseline was the three-turn conversation **SkyEye Agentic UI Benefits** and the `C:\code\SkyEye` checkout at `f6ea29c`. Current delivery status and evidence are recorded below and in the progress log.

## Current delivery status — Match & Table State

**R01 — Establish the Match & Table State Core** is implemented and verified within its first vertical slice, in the **Match & Table State** epic. R01.01-R01.07 landed in small checked commits. The [user brief](r01-match-table-state-brief.md) is canonical; the [delivery plan](r01-match-table-state-plan.md) and [completion report](r01-match-table-state-report.md) record chunks, APIs, tests, actual device evidence and deferrals.

The pure C# `SkyEye.GameState` library owns canonical match/frame/table state, ordered meaningful events, deterministic reduction, provenance and corrections. Both companion hosts persist events and expose the canonical game snapshot through the shared coordinator. Replay, coaching, scoring, rules, shot reconstruction and future 3D simulation consume this model. R01 is not replay functionality and does not implement complete rules, new detectors or GPT integration.

Delivered: **R01** and the declared match/rules slices. A02.01/A02.02 and the user-confirmed A04.02 camera-connect/host/live-Cast path retain their recorded status. **After the O08/O09 detector reliability gate, resume A03.01 + A05.01 spoken shot-advice parity**, then continue **O02.05 alongside O01 real timed-shot/holdout work → O03 → O04 → O05**. O01/O02 foundations already have delivery evidence. The [history/analytics/replay/practice plan](analytics-practice-replay-plan.md) ranks AN01 → C03/SR01/AN06 as the first declared-data consumers, without waiting for perfect tracking or physics. Keep remaining T13/T14 acceptance open and address source-clock, decoded-frame, interruption and resource prerequisites before dependent runtime claims. See [temporal tracking design](temporal-tracking-design.md), [progress](glow-up-progress.md), [T13 report](t13-performance-report.md) and [T14 report](t14-report.md). Earlier execution-order notes are historical.

The earlier low-table/TV priority delivered the [manual match slice](manual-match-report.md): declared setup/score/turn/group/winner/correction/undo/recovery/history portions of G01-G05 and frame summaries from C03 on the phone. Its [bounded plan](manual-match-plan.md) and phone-only Lab proof preserve the earlier media gates. Sky voice delivery is active; the O-series remains the observation foundation with separate physical tracking evidence. Complete rules, automatic refereeing, remaining voice acceptance and TV score presentation remain open.

The supplied brief describes moving R01 out of Replay; this checkout instead assigns R01 to rules outcomes in a flat backlog. Preserve that existing mission as **R04 — Apply accepted shot outcomes to turn/groups/entitlements**. R02/R03 and all T-series replay/media IDs remain unchanged. R04 depends on R01 plus accepted observation evidence and the reviewed rules profile.

## Product outcome

SkyEye opens into a full-screen portrait view of the table. Sky is the voice partner and primary navigation system. Touch, voice, vision and TV use the same application commands and state. The resting screen contains only useful game context and a small Sky indicator; controls and explanations appear when needed.

6 October addition: automatically corrected table geometry is the default for all footage: live phone/TV, recent replay, saved-clip playback, downloaded clips and shared/exported files. Corner pockets are detected automatically, including from historical footage; no manual corner picking is a prerequisite. Saved/exported clips should also preserve the table annotations SkyEye actually displayed at the corresponding recording time. The recorded source remains available internally for recovery and future rendering. These requirements are planned below; T03-T05 currently play original camera framing and do not record overlays into MP4s.

The companion becomes a referee-assisted scorekeeper, shot adviser and replay director across a session between two players. Its game decisions distinguish observations, inferences and player declarations, and can be corrected. The product should never silently turn an uncertain detection into a score or foul.

The shot guidance should feel like a polished pool-game or augmented-reality coaching HUD over live footage. **The predicted cue-ball route after contact and its likely finish become as important as the object-ball potting line.** V01-V04 separate current-geometry presentation, a labelled simple cue-ball approximation, validated physics outcomes and observed-versus-predicted coaching. The [advanced shot HUD design](advanced-shot-hud-plan.md) defines their dependencies, semantic contract, independent phone/TV density, replay compatibility and smallest useful slice. These missions neither block basic Match & Table State nor replace the O08/O09 detector evidence gate before dependable automatic and spoken shot advice.

7 October product direction: **SkyEye should remember corrected player performance over time, find meaningful moments automatically, and turn real matches into better practice.** Accepted/corrected GameState history feeds a durable queryable Shot Record projection, statistics, search, semantic replay, drill attempts and later personalized coaching/Shot Lab. Analytics and replay are consumers of one authoritative history, not competing state stores. Current declared results and geometry support early value; missing shot intent, trajectories and physics remain unknown. See [bounded missions, evidence tiers and priorities](analytics-practice-replay-plan.md).

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

### 3. Make Sky control the application — voice plan resumes after the detector gate

The 7 October key-setup report superseded the earlier voice deferral. A02, A03.01/A05.01, A03.02 and A04.01 now have source implementations in the Hybrid host; the bounded voice report records controlled evidence and the still-open Pixel/Chromecast checks. The user has already confirmed the earlier A04.02 connect/host/live-Cast path. Current game tools read and update canonical GameState using explicit player declarations, host authority and game-version checks; media tools reuse the durable save executor and existing phone/TV playback flows. Voice controls and declared-match tools can proceed independently; dependable spoken shot-advice parity waits for the O08/O09 detector gate.

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

**Advanced shot HUD extension:** V01 extends T01/T02/T13's typed annotations and T14's renderer, with translucent aim corridors, target/pocket and ghost/contact markers, distinct object-ball and incoming cue paths, and restrained TV labels. V02 adds a separately labelled geometric post-contact cue direction; travel distance/finish regions are omitted until defensible. V03 consumes PH01's validated physics capabilities through the same scene. V04 consumes trustworthy O04/O05 trajectories and a frozen pre-shot prediction; PH02/PH03 supply later fitted reconstruction and counterfactuals. Reuse T11/T12 historical journalling/export and T07 replay transforms; do not build another overlay, replay or physics system. See [mission boundaries and acceptance](advanced-shot-hud-plan.md).

### 6. Build shot observations and proposed game events

O01-O05 retain their observation dependency order after the next Sky voice slice. O01 tooling and O02.01-O02.04 foundations are delivered within recorded limits; resume O02.05 real-source acceptance alongside O01 real corpus/holdouts, then O03-O05. The [temporal tracking design](temporal-tracking-design.md) is the detailed review, mission mapping, source/licence register, domain-integration policy and measurable first-slice acceptance. The state library contains no CV implementation or timers. T13/T14's bounded decoded-frame architecture is the integration basis; preserve requested/phase-triggered expensive perception while adding measured lightweight tracking in an explicitly enabled observation session.

1. **O01:** collect real labelled sequences covering pots/misses, slow/hard shots, cushions, multiple moving balls, scratch/replacement, black pots, near-pocket balls, crossings, close collisions, hand/cue occlusion, shadows/reflections, temporary hiding, break/rerack and camera interruption. Include the user's pool-room case: sunlight through closed blinds casts a changing striped pattern over part of an otherwise stationary table. Freeze a session-grouped held-out split before tuning and benchmark the existing detector.
2. **O02:** pair decoded pixels with actual source PTS, then benchmark ROI measurements, Lucas-Kanade flow and source-time position/velocity prediction. One active frame plus one replaceable latest frame; no stale backlog. Preserve high-resolution master footage and compare 30/60 Hz tracking before considering higher capture rates.
3. **O03:** implement `AwaitingShot → Moving → Settling → Complete/Uncertain` using source time. Retain the existing C# Hough/colour detector for accurate settled before/after anchors and bounded recovery, never as a video-rate full-frame loop.
4. **O04:** separate detection IDs, persistent tracks and nullable durable ball identity. Reconcile trajectories with the post-shot layout; propose collision/first-contact, cushion, pocket/pot, cue scratch and rack evidence using calibrated geometry and velocity changes. Same-colour crossings and unresolved contact order stay uncertain.
5. **O05:** stage outcome-producing proposals for accept/reject/correction before feeding a committing evidence chain through `SubmitGameInput`. Reuse `SkyEye.GameState` and its durable journal, with narrowly versioned evidence/contact seams where required. Player corrections outrank machine inference. A returned rack cannot silently reset a match.

**First vertical slice:** settled seed → cue/object-ball pot or miss tracking → settled reconciliation → pending review → accepted canonical event → correction/reload. Proposed gates in the design include ≥60 real sequences/≥20 held out, source/pixel pairing, bounded frame leases, 30 Hz worker p95 ≤33.3 ms, held-out boundary F1 ≥0.90, visible centre error p95 ≤0.5 ball diameter, pot precision ≥0.95/recall ≥0.90 and zero falsely accepted pots from loss/occlusion/interruption. Report denominators, uncertain coverage and real-device resource/regression evidence; these targets are not achieved results.

**Downstream:** O06 adds simple timestamped Pixel 10 microphone transients as visual corroboration, after clock alignment and audio-feed availability are proven. PH01-PH03 define pure C# physics, best-fit reconstruction, labelled Observed/Inferred/Simulated replay and later what-if coaching. GameState never depends on Physics/OpenCV; observed reality outranks simulation. O07 compares OpenCV, neural and hybrid detectors only after labelled ground truth exists. No source is copied from reference projects without exact licence/notice verification.

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

The [analytics/practice/smart-replay plan](analytics-practice-replay-plan.md) extends this section with AN01-AN06, SR01-SR04, PR01-PR04, C04 and PH04; C03 remains the basic statistics mission and V04 remains actual-versus-predicted presentation. AN01 projects corrected events into optional, versioned Shot Records. C03/SR01/AN06 first use declared/accepted facts; O03-O05 add automatic shot coverage and trustworthy trajectories. AN02 formalises current geometric difficulty without a pot-probability claim. SR02 starts with existing-media playlists; SR03 renders reels later; SR04 reuses T11/T12 historical annotations. Practice grows from one recognized drill and declared attempt to measured scoring/history and evidence-based C04 plans. PH04 loads real pre-shot state into the PH01-PH03 simulator/counterfactual seam. No second ledger, recorder, overlay or simulator is introduced.

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
| G06 | **P0 active Lab slice — Dashboard Match and Tracking Scenario Lab** | Show the canonical live match read-only beside an isolated scenario journal. Typed events wait for accept/reject choices; settled scenario shots use canonical GameState and the existing International Eightball rules engine. G06.03a adds opt-in import of the live journal and three-frame stable camera-layout proposals. G06.03b adds a reviewable scenario-only camera baseline when positions are stale or uncertain and detected colour counts do not exceed the copied inventory. Next: O03/O04 source-time tracks, pocket evidence and O05 reviewed reconciliation; then connect Sky's voice choices to the same review seam. [Plan](g06-match-scenario-dashboard-plan.md) |

### Mounted-Camera Observation Reliability epic

| Mission | Priority / status | Deliverable and acceptance |
| --- | --- | --- |
| O08 — Reliable mounted-camera table observation | **P0 release blocker; runtime foundations implemented, physical acceptance open** | One action obtains a current verified layout or a specific reason; O08.01–O08.09 cover baseline/corpus, bounded source-time verification, motion/quality separation, appearance stability, consensus, shared UX, mounted health/resources, blind evaluation and guarded delivery. The selected detector must pass held-out and safety gates. [Full plan](o08-mounted-table-reliability-plan.md). Real timed-shot tracking/refereeing gates remain separate |
| O09 — Learned Ball Detection Spike | **P0 investigation; live diagnostic dashboard, hand-labelling/import/export/scoring tools implemented; model work awaits reviewed sessions** | Serve paired live camera frames, ball/candidate labels, illumination diagnostics and canonical game state from the Pixel to a laptop over local Wi-Fi. Compare the production baseline, a small learned detector and learned proposals with OpenCV refinement on the same session-separated real frames, including the closed-blind sunlight pattern; measure per-stratum accuracy and Pixel resources. No production replacement without a held-out win, O08 acceptance and verified rights. [Spike plan](o09-learned-ball-detection-spike.md) |

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
| A02 | **Next — P0:** bridge existing real voice transport into Hybrid IChatClient/custom AI blocks | Protected configured key, actual connection/audio/mute/end/lifecycle; continuous events/cancellation/interruption do not double-execute tools. [Chunks](sky-voice-priority-plan.md) |
| A03 | **P0:** state, requested observation, choices/select/clear and supported match/correction tools | Voice/touch produce identical canonical state; stale number rejected; unknowns and authority respected |
| A04 | **P0:** voice camera/session/clip/replay commands and typed-input fallback | Save 45 seconds deduplicates; capture/replay/target-surface controls acknowledge real receipts; native pickers retained |
| A05 | **P0:** real Sky state/progress/context, minimum state alongside A03 | Connecting/listening/speaking/muted/error reflect actual events; ambiguous references clarified; remaining polish follows A04 |
| CU01 | **Source delivered; Pixel smoke checked 7 October:** simplify the home into Sky, live/dark session, Clips, Match and Settings; keep dark recording conversational while reporting physics unavailable | Pixel verifies navigation and stop behavior; a dark recording continues, clips save, player-declared match updates reach canonical GameState, and Sky never claims image-derived physics or rulings |
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
| V01 | Planned — Game-Style Shot HUD using current candidate geometry | Extend shared typed annotations/scene and T13/T14 presentation; corridor, target/pocket, ghost/contact, distinct incoming cue/object paths and richer TV styling. First slice V01.01 in [HUD plan](advanced-shot-hud-plan.md); no physics or O-series prerequisite |
| V02 | Planned — Simple Cue-Ball Projection, after V01 | Labelled geometric post-contact stun/tangent estimate with assumptions and uncertainty; distinguish incoming/projected paths. No pace/spin claim or invented finish distance; approximate zone only with defensible travel inputs |
| V03 | Deferred — Physics-Backed Cue-Ball Outcome, after V02 and validated PH01 capabilities | Consume SkyEye.Physics paths/pace/spin/cushion outcomes and probabilistic finish regions in the same semantic scene; capability gates and table calibration govern what can be shown, no duplicate engine |
| V04 | Deferred — Predicted vs Actual, after V02 or V03 plus trustworthy O04/O05 | Preserve pre-shot prediction/model/assumptions, compare source-aligned observed contact/cushion/path/finish with uncertainty; C02 explanations, PH02 fitted reconstruction and PH03 what-if reuse this view |
| O01 | P1 after voice: real labelled corpus/holdouts; offline tooling partial delivery | Real timed-shot/holdout accuracy still open; immutable source/PTS/calibration manifest and session-grouped split; [targets and design](temporal-tracking-design.md) |
| O02 | P1 after voice: O02.05 real-source acceptance; pairing/feed/ROI foundations delivered | One active + one latest pending frame; actual mounted 1080p cadence/latency/resources and recovery still open; controlled 640×360 proof does not close them |
| O03 | Source-time shot boundary state machine with settled detector anchors | Held-out boundary accuracy/timing; gaps/occlusion remain uncertain; full detector runs at anchors/recovery, not video rate |
| O04 | Persistent temporal association plus collision/cushion/pocket/pot/scratch/rack evidence | Separate track/domain identities, geometry + velocity support, clear post-shot reconciliation; ambiguous crossing/order stays unresolved |
| O05 | Proposed-event review/correction and canonical GameState integration | Stage outcomes before committing; accepted event references evidence; reject/deduplicate/correct/reload proven; contact seam extension stays non-adjudicating |
| O06 | Optional timestamped audio-transient corroboration, after visual slice | Pixel 10 source-clock mapping and feed availability verified; visual confidence supported, no audio-only decision |
| O07 | Deferred detector/accelerator comparison using O01 ground truth | Existing OpenCV vs small neural vs hybrid measured for accuracy, temporal stability, latency, CPU/GPU/NPU, thermals and battery; no premature rewrite |
| R04 | Apply accepted shot outcomes to turn/groups/entitlements (previous R01; rules consumer of R01) | Rules fixtures cover open table, continuation and foul cases |
| R02 | Black/frame outcome checks and history corrections | Ambiguous evidence stays pending; corrections repair later state |
| R03 | Quiet-mode proactive settlement/rerack hints | No repeated prompts or speech over a shot |
| C01 | Structured blocked-route reason and on-table explanation | Highlighted obstruction is geometrically verified |
| C02 | Replay coaching with evidence/context bounds | No invented contact or post-impact cue finish |
| C03 | P1 — extend delivered frame summaries to basic corrected session/shot statistics after AN01 | Shots/pots/known misses/percent/runs/time/difficulty/fouls/scratches/results only with eligible evidence; available/partial/not measurable and correction/restart parity. [Scope](analytics-practice-replay-plan.md) |
| PH01 | Deferred pure C# event-driven physics vertical slice | After O04/O05: deterministic SI-unit rolling/sliding and bounded collision/cushion models; measured English-pool parameters; no GameState dependency on Physics |
| PH02 | Deferred best-fit shot reconstruction consuming accepted evidence | Pre/trajectories/contact times/post fit with residuals/uncertainty and versioned models; Observed/Inferred/Simulated distinguished |
| PH03 | Deferred reconstructed replay/coaching and counterfactuals | Clean/3D TV replay and actual-vs-predicted comparison; softer/stun/screw/side reruns from pre-shot state only after fidelity validation |
| X01 | Network/host/process/TV recovery acceptance | State and media recover without duplicated effects |
| X02 | Long-session thermal/battery/latency and accessibility acceptance | Recorded device evidence and explicit remaining limits |
| X03 | Final migration report, signed APK, source snapshot and rollback instructions | Artifact provenance matches tested commit |

### Analytics — planned consumers of corrected history

All new rows remain planned. Detailed first chunks, evidence tiers and gates are in the [history plan](analytics-practice-replay-plan.md); C03 above retains basic statistics ownership.

| ID | Bounded result / priority | Dependencies and verification |
|---|---|---|
| AN01 | P1 — durable queryable Shot Record projection; declared/accepted fields first | R01/G corrected journal; O03-O05 for automatic enrichment. Nullable intent/result/layout/media/path fields; correction/undo/attribution and restart rebuild parity |
| AN02 | P2 — versioned geometric Shot Difficulty | Existing ShotPlanner/ShotMath geometry; AN01 for history. Deterministic supported factors/scale, frozen model/assumptions; no personal pot probability |
| AN03 | P2 — two-player/session/head-to-head comparison | AN01/C03; AN02 for difficulty; explicit local identity reconciliation for historical players. Scope/coverage/sample-size parity and correction repair |
| AN04 | P2/P3 — trends, category success and later personal calibration | AN01-AN03/C03 and sufficient evidence. Supported distance/cut/pocket/type/opponent categories; position requires intended + measured finish; personal probabilities require held-out calibration |
| AN05 | P4 — local skill rating/household or club standings | AN03 stable identity and completed corrected results. Versioned Elo-like first slice, chronological correction rebuild; no social/global platform |
| AN06 | P1 — bounded corrected CSV/JSON export | AN01/C03; optional geometry/temporal fields only when supported. Schema/revision/units/provenance/unknowns match queries; no raw detector-structure dump |

### Smart Replay — reuse existing source-time media

| ID | Bounded result / priority | Dependencies and verification |
|---|---|---|
| SR01 | P1 — typed searchable/filterable shot history for touch and Sky | AN01; AN02 difficulty and AN03 historical opponent filters when available. Paged deterministic known-fact queries, unsupported filters explicit; correction changes membership |
| SR02 | P2 — pot/miss/scratch and supported category playlists | SR01/AN01 source windows + T03-T05/T08-T12. Play retained footage at correct historical PTS; missing/expired media visible; best shots use an explicit supported criterion |
| SR03 | P3 — condensed highlight MP4 from a selected playlist | SR02 + T09-T12 verified compositor/export. Remove dead time, preserve source map/audio/scene; bounded cancellable atomic publication, revision-aware derivatives |
| SR04 | P2 — selected-shot annotated export | AN01/SR01 + T08-T12 historical journals. Recorded score/target/paths/result or labelled retrospective analysis; no current scene on old footage; V/PH capability gates retained |

### Practice / Drills — definitions, recognized setup and accepted attempts

| ID | Bounded result / priority | Dependencies and verification |
|---|---|---|
| PR01 | P2 — one executable straight-pot drill definition, then X/spot/position/bank/cue control | Shared table coordinates/commands; versioned layout/target/success/attempt-count/scoring and tolerances; only measurable criteria |
| PR02 | P2 — recognized layout/setup guidance | PR01 + corrected calibrated settled recognition/shared scene; A03/A05 for speech. Table-coordinate tolerance, stable ball reference, uncertainty review and stale-layout invalidation |
| PR03 | P2 — declared attempt tracking, then supported automatic scoring | PR01/PR02/AN01 and canonical practice-context seam; O03-O05 for auto outcomes. Known success/unknown, finish/time/difficulty/consistency only with evidence; deduplication/correction/restart |
| PR04 | P2 — drill history/progress and comparable trends | PR03 + AN01/C03; AN03 identity for historical progress. Same drill version/tolerances, eligible attempts and corrected scoring; no merging incomparable definitions |
| C04 | P3 — Sky-generated practice plan from measured weaknesses | Mature C03/AN04 + usable PR01-PR04 + A02-A05. Bounded timed plan with valid drill IDs, sample-grounded reason; no diagnosis from transcript memory |

### Shot Lab / Physics — reuse existing simulation and comparison missions

| ID | Bounded result / priority | Dependencies and verification |
|---|---|---|
| PH04 | P3 — load a real historical shot into Shot Lab and experiment | AN01 pre-shot layout + validated PH01 + PH03 rerun seam; PH02 for fitted parameters. Direction/power and supported spin controls, measured/assumed/simulated labels; real record never overwritten |
| V04 / C02 / PH02-PH03 | Retain existing actual-versus-predicted presentation, explanations and fitting/counterfactual ownership | O04/O05 accepted trajectories + frozen V02/V03 prediction or PH04 simulation. Contact/path/cushion/finish/pace claims require aligned evidence and measured resolution; no duplicate mission/engine |

**Current next work — 7 October:** Install and check the updated Sky **A03.02/A04.01** flows when convenient, alongside the still-open A03.01/A05.01 spoken parity and transcript view. Then finish A05.02 and resume O02.05 with O01 real corpus/holdout work → O03 → O04 → O05; retain/fix relevant T13/T14 prerequisites. The offline O01 seed baseline reproduces its tuned still but has zero real timed shots and zero held out. First history value is AN01 → C03/SR01/AN06 from declared/accepted facts; richer replay/comparison and practice follow their evidence gates. R04/R02 declared consumers are delivered within limits; automatic consumers still need O05. T06/T07 remain presentation extras. V01/V02 can use current geometry, while V03/PH01-PH04 and V04 retain physics/observed-evidence dependencies. Release A does not depend on automatic refereeing; basic statistics do not depend on perfect physics. Wake-word listening is separately scoped. Historical order updates below do not override this priority; see [voice chunks](sky-voice-priority-plan.md) and [ranked history slices](analytics-practice-replay-plan.md).

## Historical execution order update — voice deferred

On 4 October, the user chose to defer Sky voice/API-key work until revisiting key setup. A02-A05 and personal-credential testing were deferred then. **Superseded 7 October:** the user reports setup complete and asks for Sky voice next; use the current priority and bounded voice plan above. This documentation update does not test the credential.

Recommended next: T01 shared native phone/TV scene, then T02's touch/live/candidate controls in the portrait UI (retain two-second display cycling; T13 replaces timer-driven analysis with requested observations). Score presentation waits for G01-G05's ledger. Follow with T03-T04 playback of existing saved clips on TV, pause/seek and return live. G01-G05 session/player scoring, restart recovery and undo can also proceed without an API key. Existing native More controls remains available until each replacement touch flow is verified. Keep the small checked-commit and physical-device gates for these reordered chunks.

The user now prioritises H06b ahead of saved-clip TV playback. Locate actual corner pockets automatically, including tonight's dim/racked frame. Pocket-centre framing is a distinct display transform from cloth calibration used for ball analysis; do not silently overwrite the latter. Keep a small outer margin so complete pocket mouths remain visible while their centres define a perfect rectangle. Verify phone texture and encoded TV geometry with the same mapping, and clear/re-detect on camera change. Automatic detection must not require manual corner selection; uncertain or obstructed results keep the whole-camera view and retry. A single tuning frame does not establish general pocket accuracy.

## Historical execution order update — all footage corrected and annotations recorded, 6 October

This historical update superseded the T05 completion note's recommendation to start T06 next. Its order was **T08 → T09 → T10 → T11 → T12 → T13 → T14 → T06 → T07**: corrected replay/exports, recorded annotations, live-first performance/orchestration, background acceptance, then slow motion/freeze/zoom. Keep the existing IDs and separate commits/device gates. Voice/API-key work A02-A05 was deferred then; the 7 October voice-first priority above supersedes that deferral. Geometry/journalling use the existing candidate overlays and declared scene fixtures; actual Sky-generated annotations join the same scene/journal contract when voice tools are implemented.

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

## O02 pairing delivered; enabled feed/ROI measurements next — 6 October 2026

O02.01-O02.03 are delivered: exact native surface/release timestamp matching, one owned bitmap/receipt, scoped decoder-generation fencing, source-clock conversion and C# identity handoff. [Delivery report](o02-frame-pairing-report.md), [remaining chunks and reproduction](o02-frame-pairing-plan.md), [physical proof](evidence/o02-frame-pairing.json). Controlled Pixel tests prove 125 MP4/restart/live-TS pixel-ID/time matches and ten C# raw-bitmap/source-time matches, with cancellation and exact TS epoch checks. Changed/empty/unsupported copies are refused. The normal signed Release is updated in place with UID/private storage retained.

**Next implementation: O02.04**, the explicitly enabled one-active/one-latest frame feed and native ROI/flow measurements. Then O02.05 actual mounted 1080p, 15/30/60 Hz cadence/latency/resource and interruption acceptance. The controlled 640×360 fixture does not close those gates. O01 real-shot collection/held-out detector accuracy remains open; O03-O05 follow trustworthy feed and corpus evidence. Existing T13/T14 TV/annotation/endurance acceptance and the concurrent V/PH/O07 drafts remain preserved. No production tracking loop or automatic referee outcomes are introduced by the pairing slice.

## O02.04 feed/ROI foundations delivered; real-source performance next — 7 October 2026

The [bounded motion feed slice](o02-motion-feed-report.md) implements explicit one-active/one-latest ownership, metadata-only duplicate candidate polling, source/calibration/decoder fencing, source-time gap reset and native fixed-region feature flow. Core ownership/failure/overload checks and controlled Pixel ROI/feed proofs pass. Normal production is updated in place with signer/UID/storage retained; startup starts no motion collection and no public tracking control is added. Companion diagnostic enablement is available only through an opt-in development endpoint.

**O02.04 is an experimental foundation, not full tracking acceptance.** Real companion startup/gating remains untested after automatic approval review rejected the combined Lab command with only “blocked by policy”. The shared player/C# source, worker and queue are physically checked in the isolated test package. Fixed regions do not follow ball identity, and no GameState outcomes are submitted. Full bitmap readback remains; native cropping bounds conversion/flow. Final short synthetic-source rates are 13.1/18.5/25.3 Hz at 15/30/60 Hz requests, and sixteen repeated ROIs at 30 Hz process 20.9 Hz with worker p95 39.3 ms. These do not meet the sustained performance gate.

**Next observation slice after Sky voice: O02.05 actual 1080p readback/ROI, lifecycle, cadence/latency/resources and real-source validation**, alongside O01 real corpus/holdout collection. O03 boundaries and O04 ROI recentering/durable association follow that evidence; O05 review remains mandatory before canonical effects. Existing T13/T14 TV/annotation/endurance gates, declared rules limits and the concurrent V/PH/O07 drafts remain preserved. Current overall priority and new consumer missions are recorded above and in the [voice](sky-voice-priority-plan.md) and [history plans](analytics-practice-replay-plan.md).

## V01/V02 delivery and current live acceptance — 7 October 2026

The user brings V01/V02 forward while the table is dim. **V01 HUD and V02 assumed stun-direction implementation are now delivered in the normal companion**, in small verified commits. The [delivery report](v01-v02-hud-report.md) records semantic scene schema 2, corridors/contact/rail markers, separate path roles, independent phone/TV presets and source-timed journal/export compatibility. The dashed cue arrow is direction only; pace/spin/friction/finish predictions remain unsupported. V03/V04 retain their physics/observed-evidence dependencies.

Core/Agent UI suites and sequential signed builds pass. Pixel native portrait/landscape presets, clear/expiry/revision/schema gates and two historical MP4 exports pass. The user completes the controlled prerecorded Cast check; [receiver proof](evidence/v01-hud-cast.json) and [normal installation/provenance](evidence/v01-hud-delivery.json) distinguish it from real-table acceptance.

| Mission | Current status | Remaining acceptance |
| --- | --- | --- |
| V01 | Implemented and installed; live direct HUD/presets/clear/expiry pass | Physical bank/kick arrangements, individual target-selector touch coverage, real movement/source/layout invalidation and broader T13/T14 gates |
| V02 | Implemented and installed; live supported direct-cut direction display passes | Physical bank/kick coverage; actual pace/spin/stun/finish is not measured and remains outside this geometric model |

The user now reports **good table lighting**. Continue the bounded live V01/V02 acceptance next. That report is test readiness, not a passed alignment gate. Remaining T13/T14 acceptance, O01 timed corpus/holdouts and O02.05 real-source costs remain open; this HUD delivery does not establish tracking/refereeing accuracy. Preserve the separately planned Sky voice and declared-history consumers after this requested delivery work.

## V01/V02 live follow-up and TV policy — 7 October 2026

The user now requires **the shiny live-TV view only**. Corrected/branded composition is always requested; the original-TV toggle is removed and shared Touch/Sky commands cannot disable TV correction. Phone framing remains independent and persisted. The guarded normal update is installed, and the user confirms both watermark and corrected TV perspective are back. Connection currently restores compatible saved calibration; it does not run fresh corner detection. Explicit calibration reads/detects/reviews new corners; unchanged stream metadata does not verify an unmoved mount.

Live requested observations 2.8 seconds apart produce three candidates. A display-loop race clearing fresh catalogues before show is fixed, and table status/repeat timing is now visible beside the shot controls. Ball counts vary between samples; this is not detector accuracy acceptance. Full live HUD alignment/preset/clear-expiry acceptance remains open. The diagnostic launch/API gate is rejected as “blocked by policy”; normal Release and live Cast are restored, and owned ADB forwards are removed. [Updated report](v01-v02-hud-report.md) and [live follow-up evidence](evidence/v01-hud-live-followup.json) preserve these limits alongside the passed native/recorded checks.

## Latest V01/V02 live acceptance — 7 October 2026

Real direct-shot presentation now passes on Pixel 11 and live Chromecast. All three presets are verified independently on phone/TV through shared typed commands; actual taps verify all three Both presets without another perception run. Independent clear and fifteen-second expiry remove advice while source time advances, retaining TV correction and branding. The user confirms **“Aligned and readable”**. [Live evidence](evidence/v01-hud-live-acceptance.json), [updated report](v01-v02-hud-report.md).

Transient paired-frame acquisition now retries only an unavailable exact native snapshot; pixel/PTS, cancellation, scope and age fences remain. Observation failures surface a reason. TV rendering now republishes valid advice when its own decoder generation changes. A controlled TV-only reader restart proves recovery on a newly encoded frame with the same unexpired candidate and unchanged phone generation. This does not pass sustained network recovery/endurance. Normal Release is restored with matching installed APK hash, retained signer/UID/private storage, live phone/Cast and diagnostics absent; owned forwards are removed. These successful checks supersede the earlier rejected diagnostics attempt for the checks actually exercised.

Keep the remaining gates explicit: physical bank/kick arrangements and individual Phone/TV target-selector touch coverage; real movement/source/layout invalidation correlation; T13/T14 recovery/endurance/room audio/resources; O01 real corpus/holdouts and O02.05 real-source tracking performance. Intermittent count/colour detection still resets settlement even when physical balls are still; do not describe that as observed motion. The current live session allows a fifteen-second observation gap, while the tracker default and review freshness use six seconds; O08 will reconcile policy/UI documentation and preserve conservative source-time evidence. Connection reuses compatible stored calibration, with fresh corners still an explicit action. At that point O08 reliability preceded A02.01 voice-tool dispatch; later updates below supersede that order. V03/V04 retain their physics/observed-trajectory prerequisites.

## O08 implementation status — 8 October 2026

The [runtime/evidence report](o08-observation-reliability-report.md) now records explicit bounded verification, independent conservative motion/quality, current-support consensus, source-clock/publication fences and the one-action controls. Controlled checks and an actual Pixel tap pass within their scope. **Reliability acceptance and normal O08 delivery remain open**: development availability, blind-session/unsafe/geometry and corrected-TV/resource gates are not closed. Failed variants are retained and the original normal APK is restored after diagnostics. This status supersedes the earlier planned-only runtime descriptions, while preserving their proposed acceptance gates.

The 8 October provisional local-illumination/circular-hue candidate and opt-in sequential diagnostics have not been evaluated on labelled pool-room sunlight frames or a held-out session. O09 captures the proposed A/B/C comparison and model-provenance gates; O08 continues to own reliable current evidence and release acceptance. No detector accuracy improvement is claimed.
