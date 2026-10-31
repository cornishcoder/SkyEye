# Raw versus rectified boundary-support comparison — 31 October 2026

This offline diagnostic asks a narrow question: for the exact candidate fits already saved from the warped detector, does the existing generic boundary-support predicate gain support when it samples decoded, unwarped camera pixels?

## Result

The comparison covers 23 saved candidate pairs (20 positive pairs across 6 daylight frames, and 3 negative pairs across 2 frames), with a regular and an arc fit per pair. Recomputed support indices on the rectified raster exactly match all 46 saved fits, and all raw background rings have enough in-bounds samples.

| Case / fit | Fits | Raw gained sectors | Equal | Raw lost sectors | Median raw − rectified | Fits at 18+ sectors, rectified → raw | Fits at 21+ sectors, rectified → raw |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Positive regular | 20 | 0 | 3 | 17 | −2 | 12 → 6 | 3 → 0 |
| Positive arc | 20 | 0 | 8 | 12 | −1 | 0 → 0 | 0 → 0 |
| Negative regular | 3 | 0 | 1 | 2 | −1 | 2 → 2 | 2 → 1 |
| Negative arc | 3 | 0 | 1 | 2 | −1 | 0 → 0 | 0 → 0 |

For these fixed geometries, raw pixel sampling never adds a supported visible sector; support is usually lower. This does **not** prove that a separately generated and refitted raw-space detector would perform worse. It is a reason not to implement a full raw-space boundary pipeline on the assumption that avoiding the warp automatically restores the missing balls.

## Method

`Compare-RawPerspectiveBoundarySupport.py` loads the saved sector geometry and source-frame receipts, verifies image SHA-256 values, reconstructs the calibration homography, and samples each same sector point from (a) the saved warped 1000×2000 raster and (b) the decoded source image using the inverse homography. It mirrors the generic `BallBoundaryRefiner.Inspect` support decision: at least 24 background samples, reject a sector when mean per-channel exterior contrast is greater than 40, and count support when mean per-channel edge contrast is at least 16. For arc fits, support is compared only over the saved visible sectors.

Reproduce from the repository root:

```powershell
python scripts/Compare-RawPerspectiveBoundarySupport.py
```

Input evidence: `work/production/evidence/code70-daylight-pocket-capture-20261031/pocket-arc-holdout/regular-sector-trace/source-pixel-support/sector-pixel-evidence.json` (SHA-256 `a5b22ce4ff2080f86308aff1080c5d2479f45e5a470f15b9faece5b98c83aba3`). The script writes the complete per-sector JSON and a compact CSV beside this README.

## Limits and interpretation

- Candidate centres and fit geometry were found on the rectified path. The experiment does not generate or refit raw candidates.
- The black-positive/empty-pocket sets are unmatched in lighting and session. Repeated frames are not independent trials.
- Pocket reference points are approximate interiors, not measured ball centres; this cannot score localization error or detector accuracy.
- The comparison omits raw candidate generation, classification, duplicate handling, obstruction policy, final inventory, table events, and event clips.
- The input is decoded camera-video pixels, not sensor RAW.

The result narrows the raw-pixel hypothesis to a proposal-only test: generate candidates for the missing black or cue directly from the source image, map them through calibration into canonical table coordinates, then apply the existing validation and uncertainty gates. Score on reviewed, session-separated positives and matched same-light absent-ball/pocket controls. Do not change production until that branch demonstrates a repeatable gain without extra false positives.
