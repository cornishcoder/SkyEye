# Source pixel support mapping

This diagnostic maps the regular boundary and pocket-aware arc fit sectors for the 23 exact matched pairs already listed in `regular-sector-trace/{positive,negative}-comparison.csv` (20 positive, 3 empty-mouth negative). It reads saved reports, source PNGs and calibration receipts only.

Requires Python packages `opencv-python`, `numpy`, and `Pillow`. Run from the repository root with:

```powershell
python scripts/Export-PocketArcSectorPixelEvidence.py
```

The exporter verifies source PNG SHA-256, report/receipt frame hashes and timestamps, ranked seed identity, and the 1000 x 2000 calibrated perspective warp. It emits:

- `sector-pixel-evidence.csv`: one row for each of 32 sectors for each of two fits in every pair (1,472 rows), including seed/fit geometry, inner/outer and fine sample coordinates, warped BGR values, background BGR, edge contrasts, visibility and support.
- `sector-pixel-evidence.json`: the same evidence grouped by pair, plus source validation and evaluation-reference provenance.
- `sector-pixel-contact-sheet.png`: four geometry-selected crops covering a high-drift positive pair, a positive duplicate neighbourhood, a high-drift negative pair, and an exact duplicate negative hypothesis pair.
- `target-near-frame1365-rank19-review.png` and `.json`: a separate raw/warped overlay for the explicitly requested target-near example. Its approximate pocket point was used only to choose this review illustration and is marked non-ground-truth.

Sector zero points right; indices increase clockwise in image coordinates. The reported support sets were reproduced from the warped BGR samples with the generic `BallBoundaryRefiner.Inspect` support rule. The exporter stops with an error if either model's recomputed reported indices differ from the saved report. Arc support is compared after restricting raw supported sectors to that report's visible-sector indices. A sector's floating sample coordinates are rounded to pixel coordinates with the same nearest-even rule used by `Math.Round`.

The regular O08 report's source reference `(1282,536)` / warped `(494.9,1540.2)` lands on empty cloth in the exact source frames, not on the black ball. It is invalid for black localization, and every label-distance or accuracy claim using it is void. The pocket-arc reference `(102,903)` / warped `(57.1,36.6)` lies in the lower-left pocket ball cluster but is only an approximate interior point, not a centre label; it cannot support a centre-error or accuracy claim. Both references are retained for review only. They were not used to select the 23 matched pairs, ranked fits or sector data. This export makes no accuracy claim.

The separate rank-19 review was selected at the user's request using the approximate pocket point solely to inspect one target-near full-sweep candidate. Re-render it with `python scripts/Render-PocketArcTargetNearEvaluationExample.py`. The arc fit passes its diagnostic checks with 12 supported-visible sectors; the regular fit has 18 sectors. Their displayed separations from the approximate point (9.6 px and 50.1 px) are descriptive point distances only, never localization error or accuracy.

This is source-pixel evidence for the already selected fits, not a candidate precision result. Regular and arc visibility/support contracts differ; label proximity must remain evaluation-only.
