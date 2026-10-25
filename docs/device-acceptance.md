# Physical-device acceptance

Record phone models, OS/builds, app revision, network and capture settings for each run. Keep automated/emulator evidence separate from these results.

For the current Production companion's home controls, live-session flow, package identity, and read-only detector diagnostics, see [Pixel companion control guide](device-control-guide.md).

## Detector replay evidence (not physical acceptance)

On 25 October 2026, the dedicated `uk.co.skyeye.visionchecks` Release package replayed the manually reviewed [bright packed-rack frame](../tests/fixtures/poolgame/pixel-packed-rack-2026-10-25.jpg) and [labels/receipt](../tests/fixtures/poolgame/pixel-packed-rack-2026-10-25.json) on a Pixel 11 Pro XL (Android 17, build `CD1A.261005.003`). This was an offline OpenCV replay of the saved 1920×1080 frame, not a live-camera run. The replay itself did not modify Production; a later guarded Production update is recorded below.

At the production 800px analysis width, the first pass found 15 balls and detected an incomplete compact cluster. It retried the same source frame once at 1000px, then returned 16 balls with the reviewed 1 cue, 7 red, 7 yellow and 1 black. Maximum centre error was 0.006857 in normalized image coordinates; the result was not uncertain or obstructed. Shot advice remained ineligible for the touching rack. Analyzer time was 1199ms for the primary pass, 1319ms for the retry, and 2518ms total; this excludes JPEG decode and camera-to-screen delay. The retry used 1.28 million primary analysis pixels plus 2.00 million fallback pixels, or 2.56 times the pixel area of one 800px pass. A direct 1000px run took 1288ms and did not retry.

The replay sampled process working-set change every 10ms: the 800px-plus-retry run peaked 124,735,488 bytes (119.0 MiB) above its pre-run baseline; direct 1000px peaked 81,199,104 bytes (77.4 MiB). This coarse process-wide measurement includes runtime and native allocations, can miss short peaks, and is not a precise per-pass memory attribution.

Clear-frame positives, empty-table, synthetic obstruction and dim touching-rack controls passed across all four tested settings. Empty and obstruction controls did not trigger a retry. The dim rack triggered one retry at 800px but stayed uncertain and obstructed, with advice ineligible. This is single-frame tuning evidence, not held-out accuracy or live physical acceptance.

## Production package delivery and current live state

At 15:45:58 local device time, the guarded Release installer updated Production in place. The installed artifact is `uk.co.skyeye.companion`, version code 2 / version `0.2`, SHA-256 `2D210F3FC5CCDE946E9557A5315792A1B37A9AB57D743F3135D7E1C76DD68918`, signer certificate SHA-256 `54a26d27fa0a0bfe68f0c96f9640229e4d9a9b2fa0ad1861bc9ce874ce65a7c1`. The installed APK hash matched the signed Release artifact. The installer verified the same UID (`10644`) and private-storage inodes (`ceDataInode=639806`, `deDataInode=617901`) before and after the in-place update. It retained the prior APK at `work/production/backups/20261025-154547/before.apk`; no uninstall, data clear or camera-package update was used.

At the first post-update inspection, the phone was at Android's lock screen and CameraService reported no active camera client. The new app activity was behind the lock screen. No fresh Production live-session or detector evidence has been recorded yet; the phone must be unlocked before the accessible home action can be checked and the session restored. Match declarations were not changed. Chromecast and Sky were left untouched.

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
