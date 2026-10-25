# Physical-device acceptance

Record phone models, OS/builds, app revision, network and capture settings for each run. Keep automated/emulator evidence separate from these results.

For the current Production companion's home controls, live-session flow, package identity, and read-only detector diagnostics, see [Pixel companion control guide](device-control-guide.md).

## Detector replay evidence (not physical acceptance)

On 25 October 2026, the dedicated `uk.co.skyeye.visionchecks` Release package replayed the manually reviewed [bright packed-rack frame](../tests/fixtures/poolgame/pixel-packed-rack-2026-10-25.jpg) and [labels/receipt](../tests/fixtures/poolgame/pixel-packed-rack-2026-10-25.json) on a Pixel 11 Pro XL (Android 17, build `CD1A.261005.003`). This was an offline OpenCV replay of the saved 1920×1080 frame, not a live-camera run. The replay itself did not modify Production; a later guarded Production update is recorded below.

At the production 800px analysis width, the first pass found 15 balls and detected an incomplete compact cluster. It retried the same source frame once at 1000px, then returned 16 balls with the reviewed 1 cue, 7 red, 7 yellow and 1 black. Maximum centre error was 0.006857 in normalized image coordinates; the result was not uncertain or obstructed. Shot advice remained ineligible for the touching rack. Analyzer time was 1199ms for the primary pass, 1319ms for the retry, and 2518ms total; this excludes JPEG decode and camera-to-screen delay. The retry used 1.28 million primary analysis pixels plus 2.00 million fallback pixels, or 2.56 times the pixel area of one 800px pass. A direct 1000px run took 1288ms and did not retry.

The replay sampled process working-set change every 10ms: the 800px-plus-retry run peaked 124,735,488 bytes (119.0 MiB) above its pre-run baseline; direct 1000px peaked 81,199,104 bytes (77.4 MiB). This coarse process-wide measurement includes runtime and native allocations, can miss short peaks, and is not a precise per-pass memory attribution.

Clear-frame positives, empty-table, synthetic obstruction and dim touching-rack controls passed across all four tested settings. Empty and obstruction controls did not trigger a retry. The dim rack triggered one retry at 800px but stayed uncertain and obstructed, with advice ineligible. This is single-frame tuning evidence, not held-out accuracy or live physical acceptance.

## Production package delivery and earlier live state (through v0.4)

At 15:45:58 local device time, the guarded Release installer delivered an intermediate Production build in place. Later that day, Production was updated in place to the then-current `uk.co.skyeye.companion` version code 4 / version `0.4`, signed Release artifact SHA-256 `7DBBA8A502865938C8673A1DAA6F744BDC345E91C58F49610D4B5C96BF55E614`. The package, signing identity, UID and private app storage were retained; no uninstall, data clear or camera-package update was used.

After the initial update, the first inspection found the phone at Android's lock screen. The user later unlocked it, and the accessible `Start live session` home action opened the active mounted-camera view. The package and camera identity remained unchanged. No Cast/TV action, match declaration, or Sky action was made.

At 16:10:23 UTC, the read-only Production dashboard sampled a fresh 1920×1080 frame captured at 16:10:21.538 UTC (about 992 ms old). In a 24-sample window, 23 samples classified all 16 visible balls with no uncertainty or obstruction; one remained at 15 and uncertain after the packed-cluster fallback. The visible arrangement was a separate cue ball and 15 touching object balls (7 red, 7 yellow, 1 black). These are diagnostic samples; they do not update the tracked table or prove settlement.

At 16:18 UTC, `Show candidate shots` on the phone invoked the existing bounded `VerifyForAdvice` path (five independent frames over about two seconds; maximum verification duration four seconds), with the target set to Phone only. The sequence alternated between 15 uncertain and 16-ball readings, with one clear 16-ball middle sample. Its final sample was uncertain and produced an 8.54 px detected-centre displacement; the image-template motion estimate was 0.36 px. Verification returned `Moving` / `Unsettled`, and the tracked table was refreshed to 16:18:41.812 UTC but remained unsettled, occluded and unconfirmed. A later read-only snapshot at 16:21:56 UTC reported a fresh 16-ball detector frame (1.22 s age, clear and unobstructed) while the observation and tracked table still pointed to the failed 16:18 verification. The evidence is consistent with unstable geometry around the touching rack, but does not establish whether that centre shift was detector jitter or physical motion; no gate was relaxed.

The Match screen still offered `Start match`, so no active match/frame existed to receive a canonical GameState table baseline. `gameTableCurrent` therefore remained false and the activity lifecycle remained unavailable pending a trusted settled match baseline. No match was started and no score or match facts were changed. The bounded-verification and later diagnostic snapshots are retained locally at `work/production/evidence/production-packed-rack-verification-20261025T161849Z.json` and `work/production/evidence/production-packed-rack-post-verification-20261025T162156Z.json`.

A later bounded verifier capture saved six exact 1920×1080 decoded-bitmap frames as PNGs over 2.84 seconds. Receipt SHA-256 hashes matched every local PNG. The images and receipts are in `work/production/evidence/o08-corpus-lossless-3610f159c74446588df1d84cd08c179b`; the dual-width proof is `work/production/evidence/o08-replay-proof-lossless-corrected-3610f159c74446588df1d84cd08c179b.json`.

At 800px, replay reproduced the captured verification detections and measurements exactly for all four frames where Production ran vision (15, 15, 15 and 16 circles; zero matched-centre delta). All five post-bootstrap motion samples classified `Still`, with at most 1.72px detected-centre displacement and 0.25px image-template displacement. The verification still had no consensus: three detector frames were uncertain at 15 circles, and the final 16-circle frame was also uncertain, so the final-quality/count gate failed.

At 1000px, the same untouched-rack frames produced `Moving` on the first three analysed intervals, with 10.40px, 6.37px and 6.37px detected-centre displacement while image-template displacement stayed below 0.19px. The last two intervals classified `Still` at 4.47px and 4.03px. This supports width-sensitive detector-centre/association jitter around the touching same-colour rack as the likely source of the earlier false movement report; the physical rack was declared untouched. This is one short development sequence, not independently labelled or held-out acceptance evidence, and it does not pass the settled-layout gate.

After capture and offline replay, camera status was `Stopped`, battery 100%, armed true (revision 25), and the companion was back on Home showing `Start live session`. The post-stop phone screen is retained at `work/production/evidence/phone-v04-post-stop-latest.png`. No match was started and Cast/TV remained untouched.

### Replay after coordinate and stillness fixes (25 October)

The same six lossless stationary PNGs were replayed after correcting fallback-circle coordinate scaling and the verifier's precedence between complete local templates and detector-centre jitter. All five post-bootstrap motion samples now report `Still` at both 800px and 1000px. At 1000px the detector-centre estimate still reaches 10.40px while all 16 local templates remain supported and move by at most 0.19px; this disagreement no longer becomes a false `Moving` result.

The layout remains unready: the final verification detection is uncertain, so both widths still return no consensus with `final-quality-or-count`. Full 800px analysis now reaches 16-ball consensus across the same six frames after fallback geometry is converted back to the configured 800px grid. That full-analysis result is diagnostic evidence only; the bounded verifier still refuses the rack. No readiness, motion or uncertainty thresholds changed.

The on-device packed-rack regression and clear-positive, empty-table, true-obstruction and dim-rack controls passed across all four tested settings, including exact 16-ball recovery on the 800px fallback path. The replay proofs are `work/production/evidence/o08-replay-proof-bugs-fixed-3610f159c74446588df1d84cd08c179b.json` and `work/production/evidence/o08-packed-rack-controls-bugs-fixed-20261025.json`. These remain development/fixture checks on one user-confirmed stationary sequence, not blind holdout acceptance. Camera capture was not started; Cast/TV and match state were not changed.

### Production update and live check (25 October 2026, v0.5)

The guarded installer delivered Production `uk.co.skyeye.companion` version `0.5` / code `5` to the Pixel 11 Pro XL in place. The signed APK SHA-256 is `AF5890FC40E589D74FAC4DB0940E7DB755F7A88201DC9A14E6C9F8038CFECE34`; the prior APK is retained at `work/production/backups/20261025-183656/before.apk`. The installer verified that the package/signing identity, UID `10644`, private storage and existing clips were retained. `firstInstallTime` remained 2026-10-03 12:35:39. The camera package was not updated. The build included fixes `be0bfb3`, `7d6067c` and `8917461`; the CAM battery gauge and bounded lossless verifier-frame capture/receipt diagnostics are also present.

During the brief live check, the camera was active at 100% battery with eight clips. The rack had not been intentionally disturbed. A bounded verifier capture saved six exact 1920×1080 PNG frames and receipts over 2.925 seconds, with source frame IDs `2644, 2662, 2678, 2694, 2714, 2731`; every local PNG hash matched its receipt. The files are under `work/production/evidence/o08-production-live-f1cffaca1f194b36bf11520bad9416b0/`. Production reported `Saved 6 of 6 verifier frames. Table result: FrameTooOld.` This capture path synchronously writes lossless PNGs from inside the verifier worker, so the result does not establish that the ordinary no-export verifier exceeded its freshness limit.

An attempted no-export `Observe table once` returned `A fresh table frame is unavailable. Request again when useful.` This single-frame observation did not yield a no-export bounded-verifier motion or layout result. Normal bounded-verifier latency therefore remains unmeasured; the capture-enabled `FrameTooOld` must remain separate from that unknown. The Production dashboard `/api/snapshot` and `/api/live-match` requests timed out from the host, so no dashboard conclusions are drawn from those endpoints.

Offline replay of the six captured images at 800px and 1000px still found 14–15 circles and no black candidate. The opt-in proposal trace is `work/production/evidence/o08-replay-proof-live-v05-candidate-audit.json`. In every frame at both widths, the dark mask produced 20–29 peak seeds across the warped image but zero inside the selected packed-rack search window; no black boundary fit or classification was attempted. This locates the observed failure before candidate fitting, at dark-seed generation/window entry, but does not establish which mask or coordinate condition excludes the black-ball seed. The existing 45px duplicate bound is unchanged, and no count, uncertainty, motion, fit or readiness gate was relaxed. Shot eligibility still rejects the touching rack because candidate spacing overlaps.

The camera was stopped at status revision 31 on the same timeline, with battery 100%, eight clips, and zero live viewers. Home showed `Start live session`; the final screen is `work/production/evidence/skyeye-home-stopped.png`. No match was started or declared, and no Cast, TV or Sky control was used. The final `/status` response reported `Stopped`; the Cast tile read `Disconnected` during the check.

Remaining work, ranked by this evidence:

1. Diagnose why the dark-mask trace contributes no seed inside the rack window, using labelled black-ball pixels and exact per-frame mask/coordinate overlays. Keep the fit and readiness requirements unchanged.
2. Obtain a successful ordinary no-export bounded-verifier result. The one no-export single-frame request found no fresh table frame, while the capture-enabled `FrameTooOld` is confounded by synchronous PNG export.
3. Resolve packed-rack shot-spacing ineligibility with evidence-backed geometry; do not widen planner tolerances to admit overlapping centres.
4. Repeat on independent held-out physical racks, including unsafe negatives and changed lighting/mount conditions, before calling O08 acceptance complete.

## Lifecycle gate

Open/arm the camera while visible; turn its display off. From the companion, start, pause, resume and stop three times without touching the mounted phone. Verify actual frames and audio, not just state text. Repeat after 30 minutes of armed idle. Check stopped/paused history does not grow. Confirm the notification disappears and camera/microphone resources release when the service ends.

If idle readiness stops responding or screen-off restart fails, capture logs and state. Do not quietly keep the display illuminated to pass this gate.

## Viewing and Chromecast

With the development computer disconnected, view the camera on the actual LAN. Rotate phone presentation and orientation; check all six pockets and ball clarity during movement. Connect Chromecast Ultra, verify landscape full-table playback, and turn TV volume up to prove the feed is silent. Stop/reconnect casting independently of recording.

Use a visible clock or flashing event in the camera view to measure phone and TV delays independently. Record startup time, repeated samples, stalls and recovery; do not infer delay from HLS segment length alone.

## Replay and transfer

Exercise 30s/1m/2m/5m/10m with sufficient history. Test a longer selection immediately after start and after pause/resume; actual duration must be truthful. Make two saves quickly and during downloads. Check room audio synchronization across every joined boundary.

Interrupt a download, reconnect and retry. Verify the original camera clip remains. Disconnect camera and play/share the local portrait file in at least two other Android apps. Inspect coded width/height and rotation with FFprobe; do not accept a landscape-only pixel file disguised solely by metadata.

## Ownership and failures

Use simultaneous companion host claims: exactly one succeeds. Guests cannot mutate session state or request saves. Confirm a dropped host can reconnect within grace; after expiry a deliberate new claim works and the former token is rejected. Close the former host's Maeve session during handover.

Test Wi-Fi loss, companion backgrounding, revoked permissions, camera service/process loss, low storage and low battery. Recording can continue through viewer/controller disconnects. Frozen images must not appear current. Saved files must remain accessible after re-arming.

## Endurance

Compare armed idle, capture only, phone viewing, phone plus TV, multiple viewers and saves/downloads. Log battery, heat, frame consistency and storage. First run a shorter stability test, then continue unplugged to the operational cutoff. Report active and idle runtime separately.

## Maeve and perception

Verify native microphone/speaker operation, echo cancellation, mute, interruptions and final closure on both the physical host and the real API. Confirm tools return local results and voice failure leaves capture/viewers intact. Lock/background the host and verify microphone foreground-service behavior and ownership renewal.

Calibrate against the actual mounted view. Compare detected colours/positions against manually labelled frames with moving balls, hands/cues, glare and balls touching cushions/each other. Changed, old, uncertain and obstructed layouts must invalidate confident advice. Check manually corrected positions and chosen group against the rendered schematic and candidate lines. Ask Maeve naturally about a pot, alternative or safety; judge timing and repetition in play.

Clear-frame colour/count regressions and ideal direct/bank/kick geometry are checked separately. Broader recognition accuracy needs held-out real layouts, including touching balls, changed mounts and pocket-adjacent black/cue balls. The planner handles group eligibility and at most one cushion rebound per ball; it does not provide complete physics or referee decisions. See `poolgame-port.md` for current evidence.

For the automatic candidate overlay, check two-second cycling on the main video, ten-second analysis attempts, visible uncertainty/recovery and toggle off/on. Confirm receiver playback switches between annotated and original streams. The initial phone/encoded-feed/Chromecast receiver checks are in `candidate-overlay.md`; TV pixel inspection, added latency and overlay endurance remain separate acceptance checks.
