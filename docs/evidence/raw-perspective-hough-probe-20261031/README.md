# Raw-perspective Hough proposal screen — 31 October 2026

This is a first offline proposal-stage screen for the raw-frame question. It runs OpenCV `HOUGH_GRADIENT` on a cloth mask in (a) decoded camera perspective and (b) the VisionAnalyzer-style rectified image. Raw proposals are mapped through the saved calibration before comparison with labels. It does not invoke or alter SkyEye's production detector.

## Method

The tool `scripts/Compare-RawPerspectiveHoughProposals.py` accepts exact image, frame-receipt and label triplets, verifies both source-image hashes, reconstructs the same 1000×2000 warp, and records an input manifest. The cloth mask approximates the existing `VisionAnalyzer.ClothMask` thresholds and morphology. Six rectified pocket disks are projected into raw perspective as ellipses. The raw Hough radius range is estimated from the inverse-homography Jacobian over a table grid, with a 15% margin; raw minimum separation and Hough vote threshold are scaled by the median projected ball radius. Both proposal sets are scored in rectified pixels with a greedy one-to-one geometry match within 27px, one nominal ball radius. Matching does not use colour.

The repeated exact-frame inputs were:

- `daylight`: frame 1365, 15 draft centres; the black centre is unresolved.
- `dim-pocket-control`: frame 978, 15 draft centres, black absent.
- `packed-rack`: frame 1, 16 draft centres.

All three label files are provisional visual estimates. Frames 1365 and 978 are from separate lighting timelines. Thresholds were swept on these same frames, so the sweep is diagnostic rather than held-out validation.

## Results

At a loose reference vote threshold of 9, recall and unmatched proposal counts were:

| Frame | Raw matched / labels | Raw proposals unmatched | Rectified matched / labels | Rectified proposals unmatched |
| --- | ---: | ---: | ---: | ---: |
| Daylight pocket, 1365 | 12 / 15 | 149 | 14 / 15 | 147 |
| Dim pocket control, 978 | 10 / 15 | 256 | 9 / 15 | 207 |
| Packed rack, 1 | 9 / 16 | 121 | 8 / 16 | 63 |

At reference threshold 15, raw results were 5/15, 4/15 and 2/16 matched, with 40, 90 and 27 unmatched proposals. Rectified results were 2/15, 5/15 and 2/16 matched, with 46, 59 and 8 unmatched proposals. The raw proposal path does not give a consistent recall improvement; the loose threshold produces a very large unmatched set in both views.

The overlays show many proposals on rails, pocket boundaries and cloth texture. Their high counts are a sign that raw-space Hough proposals need strong follow-up validation; they are not balls. The results do not prove that a full raw-frame detector cannot help. This prototype omits production colour-body and packed-cluster proposals, colour classification, duplicate handling, boundary refinement, pocket/edge policy and uncertainty gates. Its mask is an approximation, and its radius/separation scaling is global rather than position-specific.

## Artifacts and reproduction

`summary.json` contains source and label hashes, calibration-derived radius estimates, every candidate and every threshold result. `candidates.csv` contains a flat proposal table. The six per-view PNGs overlay draft labels in cyan and Hough proposals in magenta. Re-run the three cases using the `--case NAME IMAGE RECEIPT LABELS` arguments recorded in the summary's `inputManifest`.

This screen does not support a production change. The next useful comparison is a proposal-only raw/rectified A/B with the same existing rectified validation and classification gates, including the colour-body proposal path, then session-separated holdouts with reviewed labels.
