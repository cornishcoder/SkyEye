# Live Detect handoff — 29 October 2026

## Current deployment and device state

Production code44 (`0.8.36`, source `5f3d372`) is installed on Pixel 11. Camera capture is independently confirmed **Stopped**, revision 79, battery 67%, 68 saved clips. Home shows **Start live session**; Cast is disconnected. Keep capture stopped for builds, replay and waits. The user requested continuing this same task on **GPT-6 Luna, extra-high reasoning**; the goal remains active.

## What now works physically

- Schema 7 logs a bounded obstruction-component audit. This isolated a repeated false warning from three nearby balls and their shadows. Code44 uses three actual component-member centres, retaining the existing size and 0.52 body-coverage gates. The real-hand replay remains obstructed/uncertain at both widths; the on-device rack/obstruction regression passed.
- The initial rack check returned 170/170 full, clear 16-ball reads. The user had re-racked while capture was off, so this does not recreate the prior three-ball spread exactly.
- Same-run play `b9f9163e-e782-4da3-b5c4-338e297d3e1f` matched the user's reports: no-pot break, red pot, only cue ball potted, cue returned, re-rack. Settled inventories: 16 → 15 → 14 → 15 → 16. Events included no-pot, red/cue settled decreases, return and new rack/settlement without restarting detection.
- No-pot clip `f748a31bbd2646658c0314dec221f2d7` (51.371s) and red-pot clip `8c42f73b5067427aaaae055bc3847172` (60.406s) were downloaded and SHA-256 checked. Their sampled footage covers line-up, shot and rest; reported source windows are covered. Contact sheets and MP4s are in `work/production/evidence/live-detect-code44-20261029/clips/`.

## Two clip defects and the prepared patch

The cue-pot clip was skipped with a misleading supported-range message although its activity was only about six seconds. Its exact estimated save PTS was not logged, so the original failure branch is not fully proven; the most plausible admission failure is post-roll source time. A later 31.204s clip `5abc075c390a46fcb8b41479de05a546` was incorrectly saved for cue replacement/rolling. It is not the cue-pot clip.

Commit `7be9d2d` prepares fixes: wait up to five seconds for a same-timeline observed frame PTS at least two seconds beyond settlement; log post-roll timeout separately; suppress clips throughout a ball-return activity. The existing serialized camera-save queue is retained. Root review fixed a parameter-shadowing compile issue and extended return suppression to both clip-selection branches. `SkyEye.AgentUi.Checks` passed after review; log: `work/production/code44-clip-fix-checks.log`.

**This patch has not had an Android build, version bump, installation or physical acceptance.** Next:

1. Review queued-stop behavior before deployment: the post-roll reader uses `table.State.Observation`; it may stop advancing when Live Detect is cancelled immediately after settlement. Add a focused check and preserve completion of an already queued clip when recording ends. Avoid an unbounded wait and avoid claiming post-roll from an estimated timestamp.
2. Build Production from current source, bump to code45 / `0.8.37`, install in place with the guarded installer while capture is stopped. Preserve clips/private storage.
3. Run a bounded cue-pot/return acceptance when the user is available: cue loss must save the correct clip after source post-roll, and cue replacement must produce no shot clip. Also check stopping immediately after a settled event and a temporary frame stall.
4. Stop through Home and independently verify `/status: Stopped` plus Home `Start live session` before offline inspection or waiting. Download/hash and visually review the saved clip.
5. Keep broader location/lighting reliability and accepted GameState integration open. Table events remain perception evidence; no shooter, legality or turn is inferred. Do not add a parallel canonical state store.

## Evidence and source pointers

- Complete play log reconstructed by deduplicating successive bounded snapshots in append order: `work/production/evidence/live-detect-code44-20261029/play-complete.jsonl` (763 frame rows, 37 table events). `play-summary.json` records outcomes. The phone log is bounded; do not assume its final tail retains the full run.
- Code43 false-warning audit and separate exact clear verifier frame: `work/production/evidence/live-detect-code43-20261029/`. The verifier PNG is not the exact flagged live image; the independently rectified close-up identifies the same region.
- Camera clip URL viewing access can be refreshed from `/status` and passed as `?access=` for a supported read. Never print the token. Downloading all three code44 clips worked and their hashes matched.
- `docs/device-acceptance.md`, `docs/live-detect-plan.md`, `docs/device-control-guide.md`, `docs/live-detect-pipeline.md`.
- `ShellLiveDetect.cs`, `LiveDetectShotClipWindow.cs`, `LiveDetectTableEventTracker.cs`, `LiveDetectLogStore.cs` in `src/SkyEye.Companion.Hybrid/`.
- ADB: `C:\Program Files (x86)\Android\android-sdk\platform-tools\adb.exe`; Pixel 11 serial `66181FDDV001HS`; camera status `http://192.168.1.124:8765/status`.
