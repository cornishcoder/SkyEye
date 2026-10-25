# Pixel companion control guide

Use this guide to operate the Hybrid Production companion on the Pixel 11 and inspect the mounted camera without guessing at screen coordinates or changing match state. Prefer the current accessibility label and a fresh screenshot/UI tree; layout bounds move as the video and Settings panel scroll.

## Identify the installed app

- Production is `uk.co.skyeye.companion`. On 2026-10-25 it was updated in place on the Pixel 11 Pro XL; its package, signing identity, UID and private app storage were retained. Its displayed version remains `0.2` / code `2`, so that version label alone does not identify the installed source revision.
- `uk.co.skyeye.companion.lab` is a separate Hybrid Lab app with its own private storage. The separate `uk.co.skyeye.companion.commands.lab` package is not Production either. Do not use either Lab package to judge the Production session.
- During the 2026-10-25 run, Hybrid Lab failed while validating its own game journal. No Lab data was cleared. Do not clear or uninstall it to diagnose Production.

## Main screen and session controls

The video surface advertises **“Table video. Tap to reveal controls or swipe up to open them.”** The six home actions use these accessibility labels in [`Routes.razor`](../src/SkyEye.Companion.Hybrid/Components/Routes.razor):

| Label | Action | State check |
| --- | --- | --- |
| `Settings` | Opens camera, analysis, TV and voice controls. | This is not the session-start action. |
| `Talk to Sky` | Starts a live voice conversation. | The label changes to `End Sky conversation` while connected/ending. Leave it alone if Sky is already connected. |
| `Start live session` | Starts the live camera session. | Once active, the same grid shows `End recording session`. Never press that tile while the session should stay running. |
| `Start dark session` | Starts a recording session without image analysis/physics. | Do not use for a live table check. |
| `Open clips` | Opens saved clips. | Read-only inspection is preferable during a live check. |
| `Open match controls` | Opens match controls. | Do not use it to check detector output. |

`Start live session` is the normal setup route. Source code in [`ShellCameraControls.cs`](../src/SkyEye.Companion.Hybrid/ShellCameraControls.cs) sequences connection to the saved camera when needed, claims host control only when available, starts capture if needed, then opens the live phone view. It verifies that the camera is active and the live source opened. A previous 2026-10-25 run used the direct home-grid action successfully; the screen reported **“Live session · Camera active”** and **“Physics available.”** On each run, inspect the current tile and live state: press `Start live session` once only when the tile shows that label and the camera is confirmed inactive. If it shows `End recording session`, leave it alone. The implementation rejects a second start while a session is active.

Those labels show session readiness, not detector accuracy or a fresh settled table. Use the diagnostic snapshot below to check the frame and current detection.

## Navigation and menus

- When a screen panel is open, use its visible in-app left chevron, accessibility label **`Back to home`**, to return to the home screen. Android Back collapsed the Settings panel during the 2026-10-25 run; it did not reliably navigate to Home.
- Settings content is a scrolling panel over the video. Read the current screen after each navigation action. Do not reuse saved pixel coordinates after a scroll, overlay change, or window resize.
- If a tap opens an unexpected menu or screen, stop after that one mismatch. Inspect the current visible screen and accessibility labels; if the target is ambiguous, ask the user for one clearly named on-device action. Do not try a series of guessed coordinates.
- In Settings, **`Open table` / `I'm reds` / `I'm yellows` / `I'm on the black`** is the **My group** selector in the Table awareness section, not the home `Open match controls` action and not camera setup. It changes the shot-advice target group. Leave its current `Open table` value unchanged for a detector-only inspection. In the 2026-10-25 run this native option list opened accidentally; no option was selected.

## Read-only live diagnostics

The Production Hybrid shell exposes a read-only live vision dashboard when it is running. Its URL is shown under **Settings → Table analysis and shot display → Live vision dashboard**. The displayed browser dashboard and these GET endpoints inspect current state without sending a match command:

- `GET /api/snapshot` returns the latest sampled frame, frame age and sequence, detector counts/warnings, calibration ID, tracked table and whether the canonical game table is current.
- `GET /api/frame?sessionId=<id>&sequence=<n>` returns the exact JPEG associated with that snapshot. Use the same session ID and sequence; if it responds `409` because a newer frame arrived, fetch a new snapshot and inspect that one.
- `GET /api/live-match` returns a read-only current match/game summary.

For a live analysis, check `frame.capturedAt`, `frame.frameAgeMs`, `detection.uncertain`, `detection.obstructed`, `detection.balls`, the observation timestamp/outcome, `trackedTable.settled`/`occluded`/`confirmed`, and `gameTableCurrent`. A calibration ID only shows that calibration exists; it does not make an old table observation current. `Physics available` is also not proof of a correct inventory.

The live dashboard has no general Production command endpoint. Do not use POST routes such as `/api/live-match/review` or any `/api/scenario/*` route for diagnosis; review routes can change canonical state, while Scenario Lab routes edit a disposable match. The Production package and dashboard were verified on this device; do not assume a separately installed Lab package has the same runtime or journal state.

`Observe table once` is an in-app application command that submits an `ObserveTable` request through the coordinator ([`ShellTable.cs`](../src/SkyEye.Companion.Hybrid/ShellTable.cs)); it can update the app's current observation. It is not required to read the dashboard's current detector output. Do not press `Recalibrate table`, `Correct layout`, or the My group selector during a read-only inventory check.

## TV and Sky

- The Cast icon is separate from the six home actions. It opens Android's Cast picker; choose the receiver named **Chromecast**, not an audio-only speaker. A connected route and active playback are separate: the Settings **TV and Chromecast** section has **`Choose TV`** and **`Play live on TV`**. Verify the image on the physical TV. After the Production update/restart, Cast showed disconnected and had to be reselected; the prior TV session did not survive the app restart.
- Sky is live voice, not a text composer. Use **`Talk to Sky`** only when its current accessible label shows it is disconnected; when it shows **`End Sky conversation`**, do not toggle it. A real spoken request must come from the user; do not claim a tool call or spoken answer that did not occur.

## Packed-rack finding from 2026-10-25

The mounted frame was fresh and showed a complete 15-ball rack with all object balls touching, plus a separate cue ball. At 11:59:24 UTC the Production dashboard reported a live frame captured at 11:59:22 UTC (`frameAgeMs` about 827), a calibration ID, and only **0 red, 1 yellow, 1 cue, 0 black** (`circleCount: 2`). Detection was uncertain and obstructed; `gameTableCurrent` was false. Its tracked table was still from 11:47:30 UTC, unsettled, occluded and unconfirmed. This is a fresh camera frame with a failed packed-rack inventory and stale settled-table state, not a stale camera feed.

A separate 24-sample dashboard batch from 11:54:24–11:54:53 UTC remained uncertain and obstructed throughout: 19 samples had two circle candidates, four had three, and one had four. None approached the visible rack inventory.

The packed rack is the leading explanation for both detector symptoms. [`VisionAnalyzer.cs`](../src/SkyEye.Vision.Android/VisionAnalyzer.cs) builds circle candidates from Hough detection and connected colour bodies; its source comments say merged colour groups are excluded and merged/touching bodies retain unresolved geometry. Its obstruction check flags a connected non-cloth region larger than its width, height and area thresholds. A touching rack can therefore be merged into too few circles and plausibly trigger the “large object” warning. The API does not expose the exact connected-component mask, so that obstruction cause remains a source-backed inference rather than a measured component label.

For future first-rack acceptance, inspect the touching rack as presented. Do not ask the user to spread the balls to make the detector pass, and do not promote a partial count to a settled layout. Require all 15 object balls plus the cue to be detected with current, clear evidence before treating the rack as usable.

After the guarded Production update at 15:45:58 local device time, the phone was at Android's lock screen and CameraService reported no active camera client. The Production activity is behind the keyguard. Unlock the phone before inspecting the live screen or restoring the live session; no detector evidence has been collected after the update yet.
