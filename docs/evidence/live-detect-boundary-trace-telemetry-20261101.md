# Boundary trace telemetry contract and exact-profile replay — 1 November 2026

## Result

The diagnostics-only boundary trace is implemented and verified without changing boundary acceptance:

- BOUNDARY-TRACE-TELEMETRY-CONTRACT: synthetic contract PASS 3/3. The first fixture attempt did not create any sample-bound rejects and failed its intended assertion; the fixture was corrected, then the same focused target passed. No production threshold was changed.
- Exact saved-frame integration: PASS 1/1. The frame-1179 proposal-conditioned red rejection emitted per-fit sample, bounds and sector telemetry.
- BOUNDARY-TRACE-OUTPUT-PARITY: detector-output parity PASS 2/2 arms (baseline and proposal-assisted); strict full-audit equality FAIL because the current report includes eight additional colour-body separation-rejection rows per arm, added by earlier source commit 484f2c5. The detection outputs themselves are identical.

The trace explains this candidate's rejection. It had all 32/24 required background-ring samples and zero of 468 fit positions rejected by rounded sample bounds. The selected fit had 20/32 supported sectors, below the unchanged 24/32 gate. Sectors 0–19 were supported; 20–31 were weak non-exterior sectors; no sector was marked exterior-rejected. Thus this candidate is not explained by an insufficient background ring or sample-bound rejection. It remains rejected and is not a detector pass.

## Exact replay identity

The run used raw-proposal-replay mode on Code65 frame 1179 at width 800, with boundary audit enabled. The report states diagnosticOnly=true, canonicalStateChanged=false, configurationComparison=exact-match and configurationMatchesPrimary=true. The receipt recorded width-800 packed-colour extent 0.4; the run explicitly used 0.4. The unused width-1000 diagnostic extent was recorded as 0.65. The existing 0.75 support gate and other receipt settings were preserved.

Frame 1179 source PTS is 48928717966 microseconds. Pixel SHA-256 is 0ce1efbdfdb940d04c0021356a527ab5ce45312334aa8bf988eb84362fac7301; receipt SHA-256 is fca4a67f270caba01d04af620d40beb84eaae7ed69a10e772b86bbba5a8c00ca. The raw proposal input SHA-256 is e01ed0f56f97dac02aebccad438754432360b0f1da7bcab26fb0b401247a3290.

The traced row is local-model-raw proposal index 1, corresponding to proposal ID ball_2 at source pixel (125.2, 194.4). The user confirmed this one red centre after seeing the model proposal; it is proposal-conditioned evidence, not an independently placed label or complete frame inventory. In analyzer table coordinates the proposal was (754.9551, 74.3904); the selected fit was (751.1151, 84.1504), radius 16.2, support 20/32. These analyzer coordinates are not source pixels.

The diagnostics reported 468 positions considered, 468 within sample bounds, 318 below the existing 12-sector search-evidence minimum and 150 with evidence. The selected fit's support and per-sector arrays agree with the existing 20/32 decision. The instrumentation adds observation only; the synthetic contract compares Refine and RefineWithDiagnostics and confirms identical fit geometry and score.

## Replay and package artifacts

- Build command: dotnet build tests/SkyEye.Vision.Device/SkyEye.Vision.Device.csproj -c Release -m:1 --nologo --verbosity minimal. Result: success, 0 errors, 23 warnings, 59.05 seconds.
- Focused contract command: dotnet run --project tests/SkyEye.Checks/SkyEye.Checks.csproj -c Release -- --boundary-diagnostics. Result: PASS 3/3 cases after the noted fixture correction.
- Replay run directory: [btrace1179-182035](../../work/production/evidence/boundary-trace-telemetry-20261101/btrace1179-182035/).
- Device report: [exact-frame replay report](../../work/production/evidence/boundary-trace-telemetry-20261101/btrace1179-182035/o08-replay-proof-btrace1179-182035-rawproposals.json), SHA-256 18027e5e8c7d944a3611aa679a6438b4be026e64bb3d9e00b3b61f541248c9da.
- Replay manifest: [frame and configuration manifest](../../work/production/evidence/boundary-trace-telemetry-20261101/btrace1179-182035/manifest.json), SHA-256 cf7201b2c205792222569a0afdd3f4213b441f6ebcf57393a1882718ac29968c.
- Built isolated VisionChecks APK SHA-256: 3d4c1470f82b63aa8ebf3fe678758820a353783c8b22a0c8f44bfe39239fef09. The installed uk.co.skyeye.visionchecks version 0.1, code 1 APK was pulled read-only after installation; its hash exactly matched the built APK. Production companion and mounted-camera packages were not updated.
- The replay manifest records APK, pixel and receipt hashes but not the source Git commit. Build and replay used the worktree based on f6870c8d822f6621b334be13f593ab1640bbaa39 with the telemetry source and tests now committed as 6b1b4cb8d0fe0ac993b46b3c54f3bc6dc06cb5e8. No source files changed after build or replay, and the post-commit worktree is clean; the APK therefore matches the source files in that commit.

The earlier exact-profile report was compared offline; no second replay was run for parity. Both arms are exact-profile matches and have identical uncertainty, layout uncertainty, obstruction state, ball counts, classifications, circle centres/radii and boundary-audit output. Candidate-audit row counts differ: baseline 143 then 151; proposal-assisted 158 then 166. The eight added rows per arm are the current source's colour-body candidates rejected by separation, already introduced at 484f2c5. They do not change any emitted circle or decision at the detector-output level. Preserve the strict audit-row mismatch as a failed equality check.

## Same-frame seed-source reuse

Target COLOUR-SEED-VS-PROPOSAL-FRAME1179 reuses the exact report above; no replay was run. The configuration and both analyzer arms are the same as the receipt-bound raw-proposal run. These rows are intermediate candidate stages, not independent detector trials.

- The packed-red index 3 peak at analyzer coordinates (755, 71) fitted to (753.4, 70.2), with 19/32 support. It was accepted as an intermediate colour peak, then the same body at sparse-colour-add index 2 was rejected: 19/32 is below MinimumSparseColourBoundarySupport 0.625 (20/32). It was not emitted as a final ball; both report arms still output six reds.
- The local-model-raw index 1 seed is nearby at (754.96, 74.39). Its fit moved to (751.12, 84.15) and reached 20/32, but the generic 0.75 gate requires 24/32, so it was rejected.
- The nearest Hough proposal is index 40 at (585.65, 407.55), 373.71 analyzer pixels from the reviewed-point model seed; it was rejected as cloth. The Hough path did not propose this red location.

This source audit narrows the diagnosis: colour-peak seeding reaches the region but misses its final sparse-add support gate by one sector; the generic model proposal has one more sector but faces the higher 24/32 gate; Hough has no nearby seed. It does not compare the same seed under both refiner modes, establish independent centre accuracy, or clear any gate. Preserve thresholds and require the matched empty control before a colour-aware refinement experiment.

## Matched negative and device state

Frame 978 is not an empty control for the frame-1179 red point: its complete reviewed labels place red-01 at (124, 193), adjacent to frame 1179's confirmed red at (125.2, 194.4). It is therefore another positive at the same location. Both source records use timeline `2ea48e0c35cd474eb627acf970a7eec5`, generation 3 and calibration `6d304dd864f84c8486ee30844fe685b1`; frame 978 is at PTS 48,922,017,388 us and frame 1179 at 48,928,717,966 us (6.700578 s later). The reviewed-label JSON SHA-256 values are `4f8a2c1139b6e007e4a4789b1e6a9592b4d880d042772a709b36b4ff210b2fc1` (978) and `3dfd5b4a579af11ade699fd28aa59bcdff82b70ca9ff399658ce74cf58278635` (1179). Source pixel hashes are `b368348c63d924f5c7834e8e849b7287225ab525b470bdc3b85f7a8278cbbe70` and `0ce1efbdfdb940d04c0021356a527ab5ce45312334aa8bf988eb84362fac7301`; receipt hashes are `653e1ea4bc6c927ba592ad796a6c084e49103c27003d13e5d23d0c98a3144034` and `fca4a67f270caba01d04af620d40beb84eaae7ed69a10e772b86bbba5a8c00ca`, respectively. No same-location, same-light, reviewed empty-pocket or rail negative was identified in the retained Code65 labels. The user reported that current lighting is darker. Do not substitute a different timeline or an unmatched lighting frame and call it matched.

The mounted camera read Stopped at 18:20:29.6616121Z before replay and 18:22:43.5822980Z after replay, revision 64, timeline c5d9a60d9fb44210888abaabc77545ed, 39676 completed frames and 139 clips. The Pixel 11 companion remained at MainActivity/Home; a fresh accessibility-tree read showed Camera ready and enabled Start live session after replay. No live capture or new clip was started.

## Metrics and next target

The aggregate scorecard is unchanged: software suites 3/3 PASS (carried forward; not rerun in this cycle), focused motion-feed 20/20 PASS (carried forward), production inventory 0/3 PASS, physical D1/D2/E 0/3 proven. These denominators are separate from the synthetic telemetry contract 3/3, exact-frame trace 1/1 and detector-output parity 2/2 arms.

Next target: BOUNDARY-MATCHED-NEGATIVE-CONTROL. It is not runnable from the current reviewed same-location labels: frame 978 is positive at that point, and no matched empty control is available. Resume it only with an adequately lit, same-camera and same-setup reviewed empty pocket/cushion control. Keep the 24/32 boundary gate fixed.
