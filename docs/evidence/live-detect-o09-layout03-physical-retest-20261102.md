# Layout03 bounded physical verifier retest — 2 November 2026

**Test:** `O09-NORMAL-TRAIN-LAYOUT03-BOUNDED-PHYSICAL-VERIFIER-RETEST`  
**Result:** FAIL for stillness; the run ended `Unsettled (TimedOut)`. The installed package predates the clipped-template fix, so this does not evaluate the current source candidate.

## Run and provenance

The mounted camera was Active on Pixel 11 companion Home before the bounded verifier action. The saved operation was `56a3d87f-2c2b-4077-abf5-bc79a062f66d`, captured at 14:29:32–14:29:35 UTC from the mounted-camera timeline `ac521c6a5444431ebe81ec97e2ed3fe5`, with calibration `c634fbb6da0341c1994b10b862f2de87`. All seven lossless 1920×1080 PNGs match their receipt SHA-256 values (7/7). The token-free manifest is `work/o09/device-state/s01-physical-retest-active-20261102/physical-retest-manifest.json` (SHA-256 `984a3c8d88c85c2529d488cb68b6d8c4a1ebdf9ff74bdb87c35f227b52f3aaa9`); the original PNGs and receipts are preserved beside it.

The checked-out HEAD was commit `2e40e2b` (`fix: support clipped verifier templates at table edges`, committed 14:10 UTC). The working tree also had uncommitted diagnostic-only edits in `ObservationPolicy.cs` and `CueBallRecovery.cs`; the installed package predates both the HEAD fix and those edits. Pixel 11 had `uk.co.skyeye.companion` version `0.8.62`, version code 70, last updated at 12:55 UTC. Its pulled installed APK SHA-256 is `9901000426CD00FAACCB6B48E75849C12A0336E316B5DD9C02F7076863F71551`. Since that package predates the fix commit, the run is a pre-fix installation check rather than physical acceptance of commit `2e40e2b`.

## Measurements

| Frame IDs | Detection | Result |
| --- | --- | --- |
| 2383, 2413, 2443, 2473 | Requested; 4/4 hash-valid source/receipt pairs | Each detected 7 red + 7 yellow (14 circles), no cue or black; all four outputs were uncertain. |
| 2398, 2428, 2458 | Motion-only samples | No detector inventory was requested. |

All seven motion measurements were `Unknown` with `InsufficientEvidence`; none was `Still` or `Moving`. The anchor (2383) had valid coverage. The following six measurements had invalid coverage, so the required still span never formed and the verifier timed out. This is not evidence that the table moved. The separate complete-inventory gate also failed on every analyzed sample.

## Device stop and interpretation

At 14:32:46 UTC, camera `/status` was `Stopped`, revision 29, battery 100%, 24,915 completed frames, 139 clips and zero viewers. The same Pixel 11 Home tree showed `Start live session`; the after-stop screenshot is `work/o09/device-state/s01-physical-retest-active-20261102/screen-after-stop.png` (SHA-256 `6A688208FF15872D44F8EAB9758BAE958DB6FB29F806BA6BFA47CB1F269A001D`). No clip was added, Live Detect is off, and no Cast action occurred.

Physical stillness passes remain **0/5 attempts** including this pre-fix run; the clipped-template source candidate has **0 current-build physical attempts**. One source-matched build/install and bounded retest is the next necessary cycle. If it passes, it establishes only the tested Layout03 motion-still result; if it fails, return to exact-frame diagnosis. A motion-still result would not close detector inventory, D1, D2's broader held-out coverage, or event/clip gate E.

**Next named test:** `O09-NORMAL-TRAIN-LAYOUT03-EDGE-TEMPLATE-PHYSICAL-VERIFIER-RETEST`.
