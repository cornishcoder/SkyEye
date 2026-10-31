# Raw versus rectified boundary-signal probe — 31 October 2026

This is an exploratory image-signal comparison, not a ball detector, detector-accuracy score, or Live Detect acceptance result.

## Result

On one hash-verified 1920×1080 rack frame, the median radial luma-gradient magnitude at the labelled ball boundaries was **15.273 per rectified pixel in the decoded source** and **15.270 in the VisionAnalyzer-style 1000×2000 rectification**. The median raw/rectified ratio was **0.997**; its sector-level 25th/75th percentiles were **0.968 / 1.033**. Of 512 expected sectors, 396 were sampled and 116 were excluded because the approximate labelled geometry placed another ball over that part of the boundary.

This one rack frame shows no aggregate boundary-gradient advantage for the unwarped source at the measured locations. It does not answer whether raw-space proposals work better at pocket lips or cushions; those failure locations still need reviewed per-ball centres and matched empty-pocket controls.

## Method

`Compare-RawWarpBallEdgeSupport.py` verifies the source PNG hash against both the frame receipt and manual-label file. It reconstructs the same source-corner to 1000×2000 portrait homography used by `VisionAnalyzer.Warp`, then rotates that raster back to landscape for the visual overlay.

For each approximate labelled centre, the script projects the centre into the rectified raster and samples 32 expected ball-boundary sectors at radius 27px. It excludes sectors whose expected contour falls within 28.5px of another approximate labelled centre. For the remaining sectors, it searches a shared ±4px radial band and compares the absolute Sobel luma derivative along the expected boundary normal. Raw derivatives are mapped into rectified-pixel units through the local inverse-homography Jacobian. The overlay marks available sectors green and geometrically excluded sectors red.

Run from the repository root:

```powershell
python scripts/Compare-RawWarpBallEdgeSupport.py
```

The script writes `summary.json`, `sectors.csv`, and `overlay.png` alongside this README by default. Input paths and output directory can be overridden with `--evidence`, `--output`, and `--width`.

## Limits

- There is one rack frame and one lighting condition.
- The centres are approximate manual draft labels with stated uncertainty; they are not ground truth.
- Expected circle radii and the ball-to-ball occlusion mask inherit the approximate labels and a nominal 27px rectified radius.
- The measure describes local luma-gradient signal. It does not measure candidate recall, classification, duplicate rejection, accepted table inventory, event correctness, or processing cost.
- It does not distinguish useful edge detail from noise or H.264 artifacts.

The production pipeline and detector settings are unchanged. Extend this comparison to reviewed pocket/cushion positives and matched empty-pocket controls before using it to choose a detector architecture.
