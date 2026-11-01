# Opposite-side cue presence and absence screen

**Target:** `OPPOSITE-CUE-MATCHED-ABSENCE`

**Status:** saved evidence reused; the cross-run matched-light gate is not satisfied. No CV replay was run for this audit.
**Scoring checkout:** `50d0ac07bfa47b0efa206daedd3545b05385cc05` (`2026-11-01`), before this documentation change.

## Groupwise result

| User-reviewed setup | Detector-requested frames | Observed cue output | Black output | Other output / state |
| --- | --- | ---: | ---: | --- |
| Cue absent, black present | 562, 598, 631, 660 | 0/4 proposals | 4/4 detected | 7 red + 7 yellow in each; exact reduced inventory 4/4; all uncertain |
| Cue present, black present | 1691, 1726, 1764 | 3/3 detected | 0/3 detected | 7 red + 7 yellow + cue in each; full inventory 0/3; all uncertain |

This is a descriptive screen, not a matched paired score or D1 acceptance. The cue-absent result has no false cue proposal in its four detector-requested frames; the restored cue-present result has cue output in all three requested frames. The absent group’s reduced counts remain uncertain, and the present group still misses the black. No numeric cue-centre tolerance was agreed.

## Why the groups are not a valid matched comparison

The user's staged control changed only the cue by instruction, and both groups are Development data. Receipts preserve the same calibration ID (`00a2e8e6d0eb4c90bec72c9bfea9c28f`) and identical ordered corners. The capture timeline and receipt generation differ across the stopped-camera restarts: absent frames use timeline `972b743103a54820afdc4484ffaf7042`, generation 19; restored-present frames use timeline `c5d9a60d9fb44210888abaabc77545ed`, generation 23. This split is retained in provenance; it does not by itself establish a camera-pose change.

More importantly, the existing fixed-patch photometric summary reports a maximum within-group requested-frame luminance delta of 0.5708 in the restored-present group, but a maximum absolute cross-run patch luminance delta of **20.4516** against cue-absent frame 562. It records `matchedImageConditionEstablishedAcrossRuns: false`. The cue-absent frames are also internally stable by patch (per-region ranges 1.0000–1.4344). These image-space changes cannot distinguish physical illumination from camera exposure or white-balance processing. Therefore neither the cue outcomes nor the black difference (4/4 versus 0/3) can be attributed to cue presence. Do not tune from this cross-run contrast.

## Source and replay records

- Cue-absent stage summary: `work/o08/live-detect-black-pair-v1/physical-controls/stage-replays/oppositecue-20261101/opposite-cue-absent-stage-replay-summary-current-source-20261101.json`, SHA-256 `0015dd0495655875cfbc612480659b48cd40af9a5ee05a2553bc2756ed8acb43`. Its user-reviewed centres file is `opposite-cue-absent-user-reviewed-centres-20261101.json` in the same directory; the review says “looks good,” detector output was not displayed, source pixels were unchanged and no numeric tolerance was set.
- Cue-absent report `mcsopcueabs-1652-165436`, SHA-256 `86f1e5f19536c1397b95797fd2d0fd0fc7527d83fb482707c4ad14af205e773e`; adjacent manifest SHA-256 `4b4c41cd0e5a56c37009f7c1e54239b21d54642d6a72ee544b17bdd9766f4c56`.
- Cue-present/restored summary: `work/o08/live-detect-black-pair-v1/physical-controls/stage-replays/oppositecue-20261101/opposite-cue-present-restaged-stage-replay-summary-current-source-20261101.json`, SHA-256 `5ba91e4caeb1caecdd0b41e810668a66dfedd3cd51c3ee6d2552ef0878b5af25`. Its user-reviewed centres record is `work/o08/live-detect-black-pair-v1/physical-controls/cue-present-opposite-cue-restaged-20261101/review-v1/user-reviewed-centres-20261101.json`, SHA-256 `9b96729fbb4466dd832a49b3fde6e35643249d1b2d8b8a10042e87ac114bc1`; the user replied “Looks correct,” with detector output not displayed and no numeric tolerance.
- Cue-present report `mccuepos-1715-171924`, SHA-256 `5badbae7857125252d4f58d4414dbcd2baad9abe29904e97b37133c45fc68484`; adjacent manifest SHA-256 `896e0a56b5188ffc5bb8a73361598eefb4bcb4a2617777206049c1708f5b43f8`.
- The four-patch photometric summary is `work/o08/live-detect-black-pair-v1/physical-controls/cue-present-opposite-cue-restaged-20261101/photometric-roi-check-restaged-cue-present-20261101.json`, SHA-256 `649a429c80356e9a270a6d5f44a396ae05a339e7ae7fa60d9047359cfa8406ab`.
- Both replay manifests name the same isolated `uk.co.skyeye.visionchecks` APK hash `9b9e1658b781a8773bf3fc3e70a54f889e7132ad5fb600224c6963b6cb754be3`. Both are `full-audit`; each reports captured/replay width-800 settings match and no algorithm change. The manifests do not bind the APK to a Git commit. The cue-absent summary’s source checkout and VisionAnalyzer commit identify summary-time source, not the APK build, and are not substituted for that missing APK source SHA.
- Existing detailed capture, review and status notes: [cue-absent stage replay](opposite-cue-absent-stage-replay-20261101.md) and [restored cue-present stage replay](opposite-cue-positive-restaged-stage-replay-20261101.md). Their camera `Stopped` readbacks are historical to those captures/reviews; no current device status was requested or verified for this offline audit.

No capture, device action, detector replay, production code or threshold change occurred in this cycle. Reuse these results unchanged. A future paired comparison needs a new reviewed cue-present/absent group with fixed camera pose and lighting, then an image-patch match check before opening the paired result. Keep both conditions on Development and follow the [capture batching protocol](../live-detect-workflow-efficiency-20261101.md); do not relabel this set as Holdout.
