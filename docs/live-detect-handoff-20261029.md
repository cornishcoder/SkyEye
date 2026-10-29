# Live Detect handoff — 29 October 2026

## Current state

- Objective: reliable live table inventory and locations; source-timed activity, movement, obstruction, settled pot/no-pot and rack events; event-linked clips. The goal remains active. Keep table-event evidence separate from canonical `SkyEye.GameState`; unresolved or obstructed observations stay unknown.
- Production code43 (`0.8.35`) is installed on Pixel 11 from source commit `08399b0`; it adds compact schema 7 obstruction audit telemetry. Agent UI checks passed; Release build had zero errors and 13 warnings. Rollback APK: `work/production/backups/20261029-120436/before.apk`; installed signed APK SHA-256: `A59A867529D0136A9FC8E90A1870CD7E48D48C9AD04F7BCB4BDD22F8DEB6E0A8`.
- Code42 no-pot acceptance remains inconclusive: the user confirmed the table stayed clear, but repeated false obstruction prevented settlement and no `no_pot_observed` or event clip followed. Full counts and the resolved all-run/Home-counter scope are retained in the linked acceptance record.
- Code43 stationary run `40f329c8-860e-412e-a8f2-ba71f8821125`: 146 frames, all 16 balls; 25 (17.12%) obstructed. All 25 triggers were the same 800×1600 analysis region (x=273, y=1352–1353, 120–121×111–112), with three ball centres and 0.5317–0.5420 body coverage. Size and body coverage passed; the five-centre ball-group exemption failed. A separate verifier PNG (not the triggering live frame), rectified to the audit region, shows two reds, one yellow and normal shadows with no object. Full evidence and compact statistics are in the acceptance record and `work/production/evidence/live-detect-code43-20261029/run-summary.json`.
- Capture is **stopped**: latest mounted-camera `/status` says `Stopped`, revision 73, battery 71%, 65 clips. Pixel 11 Home shows **Start live session**. Cast remains disconnected and was not touched.
- The earlier code42 stop required reclaiming host control after Home showed a host-lost warning; the recovery procedure is documented in `docs/device-control-guide.md`. Code43 was independently stopped and verified at revision 73.

## Findings to carry forward

1. **Correct and recheck the localized component.** Code43 evidence shows the same three-ball/shadow component triggering in 25/146 stationary frames; the rectified verifier crop shows no object. Code44 is implementing actual component-centre membership while keeping current size and 0.52 coverage gates. After build/install, repeat the bounded stationary run and verify this region becomes non-triggering while true object controls still trigger. Code44 is not yet accepted or installed.
2. **Retry no-pot after clear observations remain continuous.** Code42 did not validate the no-pot event logic physically. Keep no-pot, pot, cue-ball return/rack, event-to-footage correlation and full same-run acceptance open until source-stamped transitions and clip coverage are physically checked.
3. **Keep the verifier/live-frame distinction explicit.** The single saved verifier PNG is a separate clear frame; it is not the exact live frame that raised the obstruction warning. The close-up was independently rectified using the live audit bounds.

## Existing code pointers

- Obstruction generation: `src/SkyEye.Vision.Android/VisionAnalyzer.cs`, `LargeObstruction` around line 718.
- Audit model: `src/SkyEye.Core/Analysis/TableAnalysisSession.cs`, `ObstructionAuditTelemetry` and `ObstructionComponentAudit`.
- Live Detect log projection and append: `src/SkyEye.Companion.Hybrid/ShellLiveDetect.cs`, around line 317.
- Log schema, limits and trimming: `src/SkyEye.Companion.Hybrid/LiveDetectLogStore.cs`.
- Event reducer: `src/SkyEye.Companion.Hybrid/LiveDetectTableEventTracker.cs`.
- Pipeline and acceptance: `docs/live-detect-pipeline.md`, `docs/live-detect-plan.md`, `docs/device-acceptance.md`.

## Physical safeguards

Keep Pixel 10 capture stopped except during bounded live evidence collection. Do not interact with Cast/TV while the user watches. Preserve clips and app data. Do not start a match for perception checks, and do not let shot planning gate table readiness.
