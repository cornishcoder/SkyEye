# SkyEye glow-up implementation plan

4 October 2026 · Revised for the installed .NET 11 RC1 SDK and actual Blazor AI components. Planning only; no application changes or workloads installed by this revision.

Based on the complete three-turn conversation **SkyEye Agentic UI Benefits** and the current `C:\code\SkyEye` checkout at `f6ea29c`. The working tree was clean when reviewed.

## Product outcome

SkyEye opens into a full-screen portrait view of the table. Sky is the voice partner and primary navigation system. Touch, voice, vision and TV use the same application commands and state. The resting screen contains only useful game context and a small Sky indicator; controls and explanations appear when needed.

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

## What exists and what needs building

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

Keep platform-independent state and rules in `SkyEye.Core`. Put orchestration, persistence and Android adapters in companion services, extracting them from `MainActivity`. Add a Razor UI library and an Android-only MAUI Hybrid host. Validate the host with a separate development package ID before replacing the existing companion. The production migration retains `uk.co.skyeye.companion`, signing identity, private storage and launch/service contracts. Leave camera capture working through its existing protocol until the observation/replay extensions are independently verified.

**One source of truth:** the coordinator owns canonical `SkyEyeState`. `UIAgent<SkyEyeViewState>` holds a sanitized observable projection, not a second independently mutable game state. State mappers accept validated application snapshots/proposals; model-produced patches cannot directly set a winner, group or calibration. Tool/UI requests dispatch typed commands and the resulting coordinator state updates both the Blazor projection and native scene. Preview state remains separate until accepted against the original version.

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

1. Introduce Session → Frames → Turns → Shots; start with player-declared events and a reviewed, versioned implementation of the currently referenced rules profile. Verify its exact break, assignment, foul and black-ball rules before encoding them. Add other profiles only with their own rule fixtures.
2. Implement start/end session, start/reset frame, breaker/current-player selection, assign/correct groups, declare/correct foul, record/correct winner and undo. Resetting a frame must not erase earlier frame wins.
3. Derive score and legal-target state from committed events. Render a restrained current-player/group/score HUD only when a match exists. Add match controls/history to the sheet and equivalent voice tools.
4. Persist events and rebuild after process death. Corrections append amendments referencing the original event; replaying the ledger repairs dependent turns, scores and statistics. Duplicate winner events cannot count twice.

**Gate:** Play three declared frames between two players, correct a group and winner, undo a foul and restart the app. Score, turn, legal targets and history all agree. Changing the current player invalidates incompatible displayed candidates immediately.

This makes scoring and game-aware advice useful before automatic refereeing is ready. Unknown game state remains unknown; Sky requests the missing declaration rather than guessing from colour counts.

### 5. Turn TV into a directed presentation surface

1. Generalize the candidate renderer into a shared table scene: paths, selected-shot emphasis, ball/pocket highlights, scoreboard and small transient annotations. Render that scene into TV media; phone buttons/sheets/API setup must never be captured as TV content.
2. Support `ShowLive`, `ShowShotChoices`, `ShowShot`, `ClearOverlays` and `ShowScoreboard`, targeting phone, TV or both. Preserve the existing 2-second cycling and 10-second automatic refresh when cycling is selected. Pinning a candidate stops rotation; layout/turn/version changes clear it.
3. Extend player/Cast adapters for buffered MP4 replay, position, pause/resume and seeking. Load replay with the appropriate media type, not the current live-HLS definition. Initially use camera-hosted saved clips; keep live capture running and return to live on command.
4. Add replay-window selection relative to camera/shot timestamps, rather than the delayed phone image. “Show that shot again” resolves to the last completed shot when available; before shot tracking exists, expose a clearly named recent replay. Support temporary replay assets with bounded retention without cluttering the user's saved-clip library.
5. Prototype playback-rate support on the actual Chromecast. If its default receiver cannot deliver reliable quarter speed, prepare a slow-motion asset locally and serve that. Add freeze and calibrated pocket zoom through the compositor, with independent TV framing. Extend encoding to paused/replay sources rather than requiring `player.isPlaying()` on a live URL.

**Gate:** On the physical TV: show legal choices, select one, clear, show score, show replay, pause, seek back two seconds, play at supported slow speeds and return to live. Confirm receiver acknowledgements and inspect TV pixels; sender output alone is insufficient. Account for HLS delay and attach annotations to the correct source timeline.

The existing annotated stream depends on the visible companion and falls back to raw camera when backgrounded. Preserve and clearly communicate this during the first redesign. Removing that limitation requires a separately tested service-owned decoder/compositor/encoder; moving the existing view to a service is insufficient. A custom Cast receiver is an alternative only if the native relay cannot meet measured presentation requirements.

### 6. Build shot observations and proposed game events

1. Capture a labelled sequence corpus under the user's standard lighting: pots/misses, cue-ball loss/replacement, black pots, break/rerack, hand/cue obstruction, shadows, near-pocket balls, close collisions and camera interruption. Keep held-out sequences separate from tuning fixtures.
2. Measure a lightweight motion detector and temporal tracking over decoded frames, retaining native media timestamps. Run expensive colour/circle analysis on stable observations. The current one-snapshot-per-completed-segment endpoint and 10-second candidate refresh cannot be the shot-event source.
3. Implement `AwaitingShot → Moving → Settling → Complete/Uncertain`. Associate balls across observations; close same-colour crossings may remain ambiguous. Produce before/after evidence and candidate pot, scratch and rack events with source/time/confidence.
4. Present inferred changes as proposals until repeated evidence and reviewed rule preconditions are satisfied. Occlusion, a missing frame or a disappearing ball alone is not a pot. A returned full rack proposes a new frame; it does not silently reset a match.

**Gate:** Run annotated held-out video through the pipeline and report shot-boundary accuracy, false pots, missed pots, scratch proposals and uncertain-event rate. On-device testing measures latency, CPU, memory and battery. No automatic score or group change follows a one-frame disappearance.

### 7. Connect observations to rules and corrections

1. Evaluate accepted shot events using the chosen rules profile: open table, group assignment, legal targets, turn continuation/change, foul entitlement and frame result.
2. Start automation with reliably supported settled-table outcomes. Keep first contact, cushion requirements, simultaneous pots and other contact-dependent calls pending/manual until a higher-rate pipeline proves the evidence needed for each rule.
3. Add voice/touch correction interactions: “I'm yellows,” “that wasn't a foul,” “the yellow is still there,” “Mike won that frame,” and undo. Rebuild affected state and immediately update both surfaces and Sky's context.
4. Offer restrained proactive notifications after settlement or rerack, with a quiet mode. Avoid talking over shots and avoid repeated requests about the same pending decision.

**Gate:** Scenario fixtures cover disputed detection, ambiguous open-table pot, scratch, legal/illegal black, correction after later turns and camera-timeline discontinuity. Rules decisions cite the committed evidence/profile, and corrections repair the full downstream projection.

Counting remaining reds/yellows helps validate a frame; it cannot identify the player at the table, prove first contact or settle every foul. Missing balls must not automatically enable a black shot.

### 8. Add contextual coaching and session analytics

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

The previous eight milestones were not sufficiently small as individual commits. Use the following **48 bounded work units**. A row normally ends in a checked commit; split it again if a compatibility problem expands its scope. Preserve the old companion until the replacement passes its migration checks. Commit production changes only after relevant checks pass; device-gated work must record a blocked/unverified check if devices are unavailable, rather than claiming completion.

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
| T01 | Generalize shared native scene beyond candidate paths | Phone/encoded TV show same scene versions; sheets excluded |
| T02 | Target-specific live/choices/pin/clear/score commands | Phone and TV can differ intentionally; 2s/10s cycling retained |
| T03 | Buffered MP4 load and replay position callbacks | Real Cast plays a camera clip; current capture continues |
| T04 | Pause/resume/seek/return-live commands | Phone and physical TV acknowledge requested operations |
| T05 | Recent replay window and bounded temporary asset lifetime | Camera timeline selects correct window; library not cluttered |
| T06 | Playback-rate support/fallback slow-motion proof | Physical TV plays measured 0.25x/0.5x when offered |
| T07 | Freeze/zoom and non-live compositor support | Frozen replay and pocket zoom work without live-player requirement |
| O01 | Labelled sequence corpus and held-out split | Manifest records cases, ground truth and split provenance |
| O02 | Timestamped decoded-frame feed plus lightweight motion benchmark | Bounded queue; no use of 10s snapshot cadence as shot evidence |
| O03 | Shot boundary state machine | Held-out boundaries and interruptions scored |
| O04 | Temporal association and pot/scratch/rack proposals | One-frame loss/obstruction cannot become a committed pot |
| O05 | Proposed-event UI and accept/reject/correction tools | Accepted event references evidence; rejected proposal does not recur unchanged |
| R01 | Apply accepted shot outcomes to turn/groups/entitlements | Rules fixtures cover open table, continuation and foul cases |
| R02 | Black/frame outcome checks and history corrections | Ambiguous evidence stays pending; corrections repair later state |
| R03 | Quiet-mode proactive settlement/rerack hints | No repeated prompts or speech over a shot |
| C01 | Structured blocked-route reason and on-table explanation | Highlighted obstruction is geometrically verified |
| C02 | Replay coaching with evidence/context bounds | No invented contact or post-impact cue finish |
| C03 | Statistics/session summary derived from corrected ledger | Amendments change aggregates; incomplete coverage shown |
| X01 | Network/host/process/TV recovery acceptance | State and media recover without duplicated effects |
| X02 | Long-session thermal/battery/latency and accessibility acceptance | Recorded device evidence and explicit remaining limits |
| X03 | Final migration report, signed APK, source snapshot and rollback instructions | Artifact provenance matches tested commit |

**First action when implementation is authorized:** P01, then P02–P05. The first user-visible application slice is S02. Release A is not dependent on automatic refereeing, and Release B is not dependent on contact-level vision. Background TV composition, a custom Cast receiver, wake-word listening and advanced cue-ball physics remain separate gated extensions rather than hidden prerequisites.

## Execution order update — voice deferred

On 4 October, the user chooses to defer Sky voice/API-key work until they revisit key setup. Leave A02-A05 deferred; do not use the saved personal credential for voice testing meanwhile. The non-voice work can proceed independently.

Recommended next: T01 shared native phone/TV scene, then T02's touch/live/candidate controls in the portrait UI (retain two-second cycling and ten-second refresh). Score presentation waits for G01-G05's ledger. Follow with T03-T04 playback of existing saved clips on TV, pause/seek and return live. G01-G05 session/player scoring, restart recovery and undo can also proceed without an API key. Existing native More controls remains available until each replacement touch flow is verified. Keep the small checked-commit and physical-device gates for these reordered chunks.

The user now prioritises H06b ahead of saved-clip TV playback. Locate actual corner pockets automatically, including tonight's dim/racked frame. Pocket-centre framing is a distinct display transform from cloth calibration used for ball analysis; do not silently overwrite the latter. Keep a small outer margin so complete pocket mouths remain visible while their centres define a perfect rectangle. Verify phone texture and encoded TV geometry with the same mapping, and clear/re-detect on camera change. Automatic detection must not require manual corner selection; uncertain or obstructed results keep the whole-camera view and retry. A single tuning frame does not establish general pocket accuracy.
