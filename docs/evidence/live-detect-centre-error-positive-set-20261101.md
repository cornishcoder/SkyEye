# Reviewed centre error on the cue-positive Development set

**Target:** `REVIEWED-CENTRE-ERROR-POSITIVE-SET`

**Status:** scoring complete; descriptive measurement only, with no numeric acceptance tolerance.
**Scoring checkout:** `0b1b86a39cff7581570e89084c131ffe34c4b736` (`2026-11-01T18:44:37Z`), before this documentation change.

## Result

The saved detector output has a one-to-one, same-colour correspondence with all **15 reviewed non-black points in each of three frames**. The pooled sample is 45 ball/frame pairs from one correlated Development setup group:

| Frame | Pairs | Mean error (px) | Median (px) | P95, nearest-rank (px) | Maximum (px) |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 1691 | 15 | 1.865 | 1.743 | 3.921 | 3.921 |
| 1726 | 15 | 1.919 | 1.632 | 5.502 | 5.502 |
| 1764 | 15 | 2.012 | 1.197 | 5.863 | 5.863 |
| **Pooled** | **45** | **1.932** | **1.462** | **5.218** | **5.863** |

P95 is the nearest-rank percentile, `sorted[ceil(0.95*n)]` with one-based rank; for each 15-pair frame it equals that frame's maximum, and for the pooled 45 pairs it is rank 43. Errors are Euclidean distances in source-image pixels after mapping the width-800 detector centres back through the receipt calibration. The calibration corner round-trip maximum was `2.45e-13` px.

These are reviewed-image centre estimates, not physical cloth-plane accuracy. The user reviewed the raw-only markers and replied “Looks correct”; no numeric tolerance was set. Thus 45/45 describes correspondence coverage, not a threshold pass. The black is absent from detector output in all three frames: the detector still returns 15/16, so the full-inventory result remains **0/3 positive cases passing**. Do not use this score as D1 acceptance or as evidence that the black miss is fixed.

## Inputs and provenance

- Reviewed labels: `work/o08/live-detect-black-pair-v1/physical-controls/cue-present-opposite-cue-restaged-20261101/review-v1/user-reviewed-centres-20261101.json` (SHA-256 `9b96729fbb4466dd832a49b3fde6e35643249d1b2d8b8a10042e87ac114bc1`). The review says detector output was not displayed, source pixels were unchanged, all visible markers were user-confirmed, and numeric tolerance was null. The 15 scored labels per frame are seven red, seven yellow and cue; the reviewed black point is deliberately excluded from centre matching because there is no black prediction.
- Source images are 1920×1080 PNGs from timeline `c5d9a60d9fb44210888abaabc77545ed`. The saved receipts identify those exact pixel hashes, source PTS, calibration `00a2e8e6d0eb4c90bec72c9bfea9c28f` and identical calibration corners:

  | Frame | Source PTS (µs) | PNG SHA-256 | Receipt SHA-256 |
  | ---: | ---: | --- | --- |
  | 1691 | 30592009633 | `fc1ad7873fa6e35d539b6b28f5fafd1fa7840691221db6cbe7408b29e1e58e3d` | `4b814a03414a002afdda1f742e61bad425d4bfa7b85e1981a07f483569f3914b` |
  | 1726 | 30593186600 | `bba5df8d3198218cd06af903e1b1071a999931e6e900463c000e0c788406544d` | `eabddf9c011a30933447ad12b4bda6abdd494667c81334e165437aa41c28222f` |
  | 1764 | 30594464455 | `a2b0bf0c0a51564b4c8a8e12f50a946bc7671f6b56118bda1404a7bc8085b19e` | `3b30ccadac32316a151886a2075a41f4e690d09289119d6981c4f0e6916b6432` |

- Detector report: `work/o08/live-detect-black-pair-v1/physical-controls/stage-replays/oppositecue-20261101/runs/mccuepos-1715-171924/o08-replay-proof-mccuepos-1715-171924-blacktrace.json`, SHA-256 `5badbae7857125252d4f58d4414dbcd2baad9abe29904e97b37133c45fc68484`. Manifest: the adjacent `manifest.json`, SHA-256 `896e0a56b5188ffc5bb8a73361598eefb4bcb4a2617777206049c1708f5b43f8`.
- Replay run `mccuepos-1715-171924` was created at `2026-11-01T17:19:56Z` using package `uk.co.skyeye.visionchecks`, APK SHA-256 `9b9e1658b781a8773bf3fc3e70a54f889e7132ad5fb600224c6963b6cb754be3`. Its mode remains `full-audit`; it has not been relabelled as an O08 screening or probe-only run. Captured and replay width-800 analyzer identities and settings match, and the report says the algorithm was not changed. The manifest does not record the APK's source Git commit. The analysis/scoring checkout is recorded separately above; no source commit is inferred for that APK.
- The label-review record says generation 14 while the bound capture receipts say generation 23. Timeline and exact PNG hashes agree across the records. This generation discrepancy is retained as unresolved metadata; exact source hashes, not the generation field, bind the reviewed centres to these scored pixels.
- The analyzer identity is `body-priority-packed-colour-peaks-v25-neutralceiling94-edge23-pocket48-sparse20-review75-redyellow-black-radius13-support21`; settings are width 800, radius 18–35, separation 45, pocket exclusion 48, Hough support 0.75, sparse-colour support 0.625, packed-colour extent 0.65, sparse-black extent 1.3, sparse-black support 0.65625, radius-first ranking enabled, dark-body ceiling 40 and image-edge margin 23. Full values remain in the replay JSON and receipts.
- Saved centre audits and overlays are in `work/production/evidence/centre-error-positive-set-20261101/{1691,1726,1764}/`. The audit helper is `scripts/Compare-ManualVisionCentres.ps1`, SHA-256 `bd605ba8ea8773cf2510f9fbe2b25e7e2ff871f7e579a3eb4c53ba00390b1d15`, last changed at commit `000b1ee3300c3f302ac3a083387289371ded93b0`.

The crosshair overlays were inspected after scoring; the same-colour marker associations appear visually consistent, including the rail-adjacent balls. Each audit also binds the report to the exact source pixel hash and records its calibration mapping round-trip. This audit did not replay CV or alter the detector output.

The acquisition manifest recorded camera `/status` as `Stopped` before and after the replay run at revision 63. That is historical acquisition metadata, not a status read performed during this scoring turn; this turn made no device request and does not claim a fresh device state.

## Failed orchestration attempt and rerun policy

The first wrapper attempt returned exit 1 after the frame 1691 audit had already been written because it checked a stale PowerShell `$LASTEXITCODE`. The generated 1691 JSON and overlay were validated and retained. The subsequent invocation processed only frames 1726 and 1764; frame 1691 was not replayed or rescored. This is recorded as an orchestration failure, not a detector or metric failure. No separate persistent wrapper log was retained.

Do not rerun this score unchanged. Re-score only if the reviewed label record, source image/receipt, detector output, transform or scoring method changes. Current aggregate remains software suites 3/3 carried forward, focused motion 20/20 carried forward, production inventory 0/3 and physical D1/D2/E 0/3 proven.

## Next evidence gate

`BOUNDARY-MATCHED-NEGATIVE-CONTROL` remains unavailable: the nearby reviewed frame 978 has a red at the target point and is a positive, not an empty-location negative. Acquire a reviewed, adequately lit empty control at the same camera/calibration/setup point before any boundary acceptance comparison. Do not substitute the currently darker unmatched captures.
