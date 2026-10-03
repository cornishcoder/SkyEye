# Physical-device acceptance

Record phone models, OS/builds, app revision, network and capture settings for each run. Keep automated/emulator evidence separate from these results.

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
