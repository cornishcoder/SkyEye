# Pixel companion control guide

Use this guide to operate the Hybrid Production companion on the Pixel 11 and inspect the mounted camera without guessing at screen coordinates or changing match state. Prefer the current accessibility label and a fresh screenshot/UI tree; layout bounds move as the video and Settings panel scroll.

## Identify the installed app

- Production is `uk.co.skyeye.companion`. On 2026-10-25 it was updated in place on the Pixel 11 Pro XL to version `0.6` / code `6`. Its package, signing identity, UID, private app storage and eight existing clips were retained. The previous signed APK is at `work/production/backups/20261025-193333/before.apk`; the camera package was not updated. See [the v0.6 device check](device-acceptance.md#production-v06-delivery-and-bounded-verification-attempt-25-october-2026).
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

### Keep live capture short

Live capture uses battery on the mounted Pixel 10 camera even if the Pixel 11 companion is backgrounded or closed. Keep the session active only while an authorized task is actively acquiring live evidence. At the first safe boundary after a bounded capture, before a long build or offline replay, while waiting, or when finishing, use the home tile **`End recording session`**. It sends the existing camera `Stop` action and ends phone playback/analysis; backgrounding or closing the companion does not stop camera recording. This preserves saved clips. The native video surface can auto-expand and cover Home controls after a live session starts, so screenshot coordinates and accessibility bounds from an earlier screen can become stale. If the stop tile is covered, collapse the video, immediately reread the current labels and bounds, then tap the current **`End recording session`** action. If it is still intercepted, ask the user to press that named action. Do not try a sequence of old coordinates. Verify `GET http://<camera-address>:8765/status` reports `state: "Stopped"`; `armed: true` or a retained host does not mean capture is still running. Return the companion to Home and confirm **`Start live session`** is shown. If the stop is not confirmed, inspect current status and recover through the existing controls before leaving the device.

Restart capture only when the authorized work again needs live frames, using the normal Home route. Perception stillness checks do not require a match; do not start a match just to run them. Do not interact with Cast or the TV while the user is watching.

Those labels show session readiness, not detector accuracy or a fresh settled table. Use the diagnostic snapshot below to check the frame and current detection.

The live video header's small **`CAM N%`** gauge reports the mounted camera's battery when a fresh camera status is available. Unknown status appears as **`CAM —`** and is exposed to accessibility as an unknown level; it is never shown as 0%. The camera API does not expose charging state. At the latest stop check (25 October, revision 37), the camera reported 100% battery, eight clips, zero viewers and `Stopped`; the companion Home showed `Start live session`. `armed: true` did not mean it was recording.

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

`Observe table once` submits a single `ObserveTable` request through the coordinator ([`ShellTable.cs`](../src/SkyEye.Companion.Hybrid/ShellTable.cs)); it can update the app's local table observation but one frame cannot establish stillness or confirmation. `Show candidate shots` uses the separate bounded `VerifyForAdvice` path (up to six independent frames in four seconds), then computes choices only if the verification passes. The dashboard sampler is diagnostic-only: its samples do not update the local table tracker or canonical GameState. The dashboard GETs remain read-only. Do not press `Recalibrate table`, `Correct layout`, or the My group selector during an inventory check.

## TV and Sky

- The Cast icon is separate from the six home actions. It opens Android's Cast picker; choose the receiver named **Chromecast**, not an audio-only speaker. A connected route and active playback are separate: the Settings **TV and Chromecast** section has **`Choose TV`** and **`Play live on TV`**. Verify the image on the physical TV. After the Production update/restart, Cast showed disconnected and had to be reselected; the prior TV session did not survive the app restart.
- Sky is live voice, not a text composer. Use **`Talk to Sky`** only when its current accessible label shows it is disconnected; when it shows **`End Sky conversation`**, do not toggle it. A real spoken request must come from the user; do not claim a tool call or spoken answer that did not occur.

## Packed-rack finding from 2026-10-25

The mounted frame showed a separate cue ball and a complete triangle of 15 touching object balls. In an earlier pre-update inspection at 11:59:24 UTC, the fresh dashboard frame was captured at 11:59:22 UTC (about 827 ms old) but found only two circle candidates (`0 red, 1 yellow, 1 cue, 0 black`), uncertain and obstructed. A 24-sample batch from 11:54:24–11:54:53 UTC likewise remained uncertain and obstructed. Those were real-time diagnostics, not stale camera frames.

After the guarded Production update and live-session restore, the 24 diagnostic samples from 16:09:48–16:10:21 UTC were substantially better: 23 classified all 16 visible balls (1 cue, 7 red, 7 yellow, 1 black) with no uncertainty or obstruction. One sample remained at 15 and uncertain after fallback. The exact frame captured at 16:10:21.538 UTC was fresh at the 16:10:23.144 UTC snapshot. This improved detector output still did not settle or confirm the table because dashboard samples do not enter the observation tracker.

An in-app `Show candidate shots` action at 16:18 UTC ran bounded verification on five independent frames spanning about 2.03 seconds. Its first detected view had 15 balls and was uncertain; a middle view had 16 clear balls; the final view had 16 but was uncertain and the verifier reported `Moving`. The final estimate was 8.54 px of detected-centre displacement while the image-template motion estimate was 0.36 px. The verifier safely returned an unsettled, occluded, unconfirmed table. This points to unstable candidate geometry around the touching rack as a plausible explanation, but cannot distinguish detector-centre jitter from physical motion. At 16:21:56 UTC, a fresh diagnostic frame again had 16 clear, unobstructed balls while the tracked table still referenced the failed 16:18 verification. Raw snapshots are retained locally under `work/production/evidence/`.

This is not a missed dashboard-to-tracker handoff: the dashboard is a separate diagnostic sampler. The explicit bounded verification did refresh the local tracked table, but its evidence failed readiness. `gameTableCurrent` stayed false because the Match screen had no active match/frame to receive a GameState baseline; no match was started. The raw verification response is retained locally at `work/production/evidence/production-packed-rack-verification-20261025T161849Z.json`.

For first-rack acceptance, inspect the touching rack as presented. Do not ask the user to spread the balls to make detection pass or promote a partial count to a settled layout. Require all 15 object balls plus the cue, clear current observations and bounded stillness verification before treating the rack as usable. A held-out labelled sequence is still required; see the lossless replay evidence below.

### Lossless stationary recapture and dual-width replay

At 16:56:45–16:56:48 UTC, a bounded verifier capture saved six 1920×1080 PNGs from the exact decoded bitmap supplied to analysis. The user confirmed that the table and touching rack had not moved. Receipt hashes matched all six local files under `work/production/evidence/o08-corpus-lossless-3610f159c74446588df1d84cd08c179b`; the corrected 800/1000 replay proof is `work/production/evidence/o08-replay-proof-lossless-corrected-3610f159c74446588df1d84cd08c179b.json`.

Replay at 800 reproduced every captured vision frame's circle centres and verification motion measurements exactly. Its five post-bootstrap motion samples were all `Still`; detected-centre displacement stayed at or below 1.72px while template displacement stayed at or below 0.25px. However, the detector reported 15 circles on three analysed frames, and the final 16-circle frame remained uncertain. The verifier returned no consensus because the final-quality/count check failed.

Replay at 1000 reported `Moving` for the first three analysed intervals, with detected-centre displacement of 10.40px, 6.37px and 6.37px while template displacement stayed below 0.19px. Its last two intervals were `Still` at 4.47px and 4.03px. Same-colour circle positions were generally close across widths (maximum nearest-pair difference 0.0097 table units), but matched ID suffixes across widths varied from 40% to 100%, consistent with assignment jitter among touching balls. Together with the user's stationary-table confirmation and sub-pixel template motion, this points to width-sensitive detector-centre/association jitter as the explanation for the earlier `Moving` result.

This is one short development sequence, not a blind holdout or independent per-ball labelling. It does not establish full-inventory accuracy or pass the stillness/readiness gate. The exact replay images are retained, but the connected-component mask and a held-out real-rack set remain unavailable. After the replay, camera status was `Stopped`, battery 100%, armed true (revision 25); the companion was at Home showing `Start live session`. No match was started, and Cast/TV were left untouched.

After the coordinate and stillness fixes, an offline replay of the same six PNGs classified all five post-bootstrap intervals as `Still` at both 800px and 1000px, even when supported detector centres disagreed by up to 10.40px. The final 15/uncertain detection still failed the layout consensus gate. Full 800px analysis now resolves the fallback-circle scale mismatch, but that result does not change the bounded verifier's acceptance. The follow-up evidence and negative controls are recorded in [device acceptance](device-acceptance.md#replay-after-coordinate-and-stillness-fixes-25-october).
