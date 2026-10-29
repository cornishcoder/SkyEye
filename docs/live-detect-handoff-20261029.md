# Live Detect handoff — 29 October 2026

## Current state

- Objective: reliable live table inventory and locations; source-timed activity, movement, obstruction, settled pot/no-pot and rack events; event-linked clips. The goal remains active. Keep table-event evidence separate from canonical `SkyEye.GameState`; unresolved or obstructed observations stay unknown.
- Production code42 (`0.8.34`) is installed on Pixel 11. Source branch is `master`; the last relevant commits are `7e425fd`, `84a5b45`, and `cce942a`.
- The user-authorized code42 no-pot check did **not** complete. It established 7 reds, 7 yellows, black and cue, then logged movement during the user's no-pot shot. The user confirmed the table remained clear. Live Detect reported a large obstruction on 618 of 950 retained frame rows, with only 332 clear and isolated samples. It never settled, so it produced no `no_pot_observed` and no event clip. Treat this as a detector/observation-quality failure, not a reducer verdict.
- The pulled log is `work/production/evidence/live-detect-code42-20261029/pixel11-code42-miss-final.jsonl`; full facts are in `docs/device-acceptance.md` and `docs/live-detect-plan.md`.
- Capture is **stopped**: mounted-camera `/status` says `Stopped`, revision 69, battery 74%, 65 clips. Pixel 11 Home shows **Start live session**. Cast remains disconnected and was not touched.
- Stopping required reclaiming host control. Home first displayed a host-lost warning; Settings showed **Host available · Active**. Tapping **Become host** changed it to **You control this camera · Active**. Returning Home, revealing the panel with an upward swipe on the video, and tapping **End recording session** stopped the camera. This recovery sequence is now in `docs/device-control-guide.md`.

## Findings to carry forward

1. **False obstruction blocks the no-pot event.** The code42 run's baseline and ball counts stayed constant; after the shot there was one clear movement observation, followed by repeated obstruction. No stable clear sequence followed. Do not loosen obstruction or settlement gates just to make this run yield a miss.
2. **Log a compact obstruction audit.** `VisionResult.ObstructionAudit` already contains component bounds/area, component counts, whether it triggers, ball centres in bounds, and ball-body coverage. `ShellLiveDetect` currently logs only the warning text. Add a bounded compact summary of the largest triggering components to a new optional log field/schema; retain old-schema reading and the existing 4 MiB/2,000-row limits. Avoid writing all 64 component records for every frame.
3. **Reproduce with still table frames.** After implementing the audit, run a bounded, non-Cast stationary Live Detect sample. Stop capture via Home at the safe boundary and independently verify `/status: Stopped` before replay or builds. Compare audit bounds and trigger counts across clear/obstructed frame transitions. Use exact source frames if a retained clip can be retrieved; do not claim a fix from counts alone.
4. **Investigate decoder retries separately.** The run included `changed-during-copy`, `no-playing-texture`, and `unmapped-source-clock`, then decoder recoveries across media generations 4 and 5. Decide whether this affected the warning only after checking exact source IDs/timestamps and the obstruction audit.
5. **Retest no-pot after continuous clear observations work.** Then exercise pot, cue-ball return and rack detection in one run, checking source PTS, event history and saved clip coverage. Current code42 did not validate no-pot event logic physically; code39 offline reducer replay is not a substitute.
6. **Check the log count readout.** Home showed 1,665 frames/17 table events; the final pulled file has 950 frame rows/16 table events for the run. Establish whether this is cumulative versus retained reporting, or a missing-log issue, before treating the Home counters as an audit receipt.

## Existing code pointers

- Obstruction generation: `src/SkyEye.Vision.Android/VisionAnalyzer.cs`, `LargeObstruction` around line 718.
- Audit model: `src/SkyEye.Core/Analysis/TableAnalysisSession.cs`, `ObstructionAuditTelemetry` and `ObstructionComponentAudit`.
- Live Detect log projection and append: `src/SkyEye.Companion.Hybrid/ShellLiveDetect.cs`, around line 317.
- Log schema, limits and trimming: `src/SkyEye.Companion.Hybrid/LiveDetectLogStore.cs`.
- Event reducer: `src/SkyEye.Companion.Hybrid/LiveDetectTableEventTracker.cs`.
- Pipeline and acceptance: `docs/live-detect-pipeline.md`, `docs/live-detect-plan.md`, `docs/device-acceptance.md`.

## Physical safeguards

Keep Pixel 10 capture stopped except during bounded live evidence collection. Do not interact with Cast/TV while the user watches. Preserve clips and app data. Do not start a match for perception checks, and do not let shot planning gate table readiness.
