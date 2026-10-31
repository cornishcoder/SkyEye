# Pocket warp coverage review — 31 October 2026

This diagnostic compares the exact decoded daylight frame 1365, where the black was reported at the lower-left pocket, with the earlier black-absent frame 978. It draws each frame's saved calibration quadrilateral on the raw source and shows the corresponding `VisionAnalyzer.Warp` output. The overlay marks source point `(102,903)` and its mapped position only on the positive frame.

The marked point is an approximate user-reviewed **black-ball interior point**, not a ball centre. The script verifies source hashes against both receipts and reconstructs the source-corner to 1000×2000 portrait warp used by `VisionAnalyzer.Warp` before rotating the result for landscape display.

## What this can tell us

The overlay shows where the saved table crop sits relative to the pocket and where the interior reference lands in the rectified raster. The two receipts carry identical calibration-corner coordinates, so the displayed mapping is geometrically comparable. The empty-mouth control is from a separate, dimmer timeline; it is not a matched-light or same-session trial.

No ball-centre, silhouette, or clipping-area measurement is made. Without a reviewed centre and boundary, an interior point cannot establish what fraction of a ball lies outside the calibrated quadrilateral. This is a geometry visualization, not detection-accuracy evidence and does not prove that rectification causes the miss.

## Reproduce

Run from the repository root:

```powershell
python scripts/Render-PocketWarpCoverageReview.py
```

Outputs are `raw-and-rectified-pocket-coverage.png` and `summary.json` in this directory.
