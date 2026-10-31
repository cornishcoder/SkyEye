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

Sector zero points right; indices increase clockwise in image coordinates. The reported support sets were reproduced from the warped BGR samples with the generic `BallBoundaryRefiner.Inspect` support rule. The exporter stops with an error if either model's recomputed reported indices differ from the saved report. Arc support is compared after restricting raw supported sectors to that report's visible-sector indices. A sector's floating sample coordinates are rounded to pixel coordinates with the same nearest-even rule used by `Math.Round`.

The reports' evaluation references conflict in the source frame and remain provisional: the regular report references source pixel `(1282,536)` / warped `(494.9,1540.2)`, while the pocket-arc report references `(102,903)` / `(57.1,36.6)`. Both are copied for human review and neither was used to select pairs, ranks, fits, sectors or contact-sheet cases. This export makes no accuracy claim.

This is source-pixel evidence for the already selected fits, not a candidate precision result. Regular and arc visibility/support contracts differ; label proximity must remain evaluation-only.
