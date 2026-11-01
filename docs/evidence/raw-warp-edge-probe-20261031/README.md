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

The script writes `summary.json`, `sectors.csv`, and `overlay.png` alongside this README by default. The original rack-frame inputs and output directory remain the defaults. For another exact frame, pass its image, receipt, and manual-label JSON together, and select a separate output folder:

```powershell
python scripts/Compare-RawWarpBallEdgeSupport.py `
  --image work/production/evidence/example/frame.png `
  --receipt work/production/evidence/example/frame.json `
  --labels work/production/evidence/example/manual-labels.json `
  --output docs/evidence/raw-warp-edge-probe-example
```

The three explicit input paths are all required together. The script verifies the image SHA-256 against both the receipt and label file, and checks the labelled dimensions against the source image. Explicit-input summaries also include an `inputManifest` with each input path and SHA-256. `--evidence` continues to select the original default filenames; `--width` and `--radius` control the rectified raster and nominal ball radius.

## Pocket and cushion samples

The same probe was run on two additional exact frames with separate draft centre labels:

| Frame | Layout | Timeline | Visible / excluded sectors | Raw median | Rectified median | Median ratio (raw / rectified) | Ratio P25 / P75 |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: |
| 1365 | Daylight; black, cue and red grouped by lower-left pocket | `76ec0f8c622045378494b0c250198ce6` | 476 / 4 | 17.166 | 17.310 | 0.999 | 0.965 / 1.036 |
| 978 | Dimmer, black absent; balls near pocket mouths and cushions | `2ea48e0c35cd474eb627acf970a7eec5` | 474 / 6 | 14.190 | 13.886 | 1.000 | 0.965 / 1.044 |

Frame 1365's black centre could not be defensibly placed because its body merges with the pocket/jaw and cue ball, so the probe has no black-boundary measurement. Both label sets were assistant drafts when this probe ran. On 1 November, the user reviewed the full frame 978 overlay and confirmed all 15 displayed centres; see the [frame 978 review record](../pocket-cushion-centre-labels-20261031/negative/user-reviewed-centres-frame-978.json). Frame 1365 remains unreviewed. The frame 978 confirmation checks assistant-estimated marker locations and does not specify a numeric tolerance or change this probe's historical calculations. Frame 978 is a separate, dimmer timeline, so these samples are not a matched-light positive/negative pair. Within each frame the raw and rectified boundary-gradient distributions are close; neither shows a useful aggregate gradient advantage for unwarped pixels at the labelled sectors.

The explicit-run summaries, per-sector CSVs and overlays are in `pocket-positive-1365/` and `pocket-empty-978/`. Input and label hashes are embedded in each `summary.json`. Reproduce them with the exact frame, receipt and draft-label files recorded in the summary's `inputManifest`.

## Limits

- There is one rack frame and one lighting condition.
- The centres are approximate manual estimates with stated uncertainty; the frame 978 overlay has subsequent user confirmation, while frame 1365 remains a draft. No numeric centre-error tolerance was agreed.
- Expected circle radii and the ball-to-ball occlusion mask inherit the approximate labels and a nominal 27px rectified radius.
- The measure describes local luma-gradient signal. It does not measure candidate recall, classification, duplicate rejection, accepted table inventory, event correctness, or processing cost.
- It does not distinguish useful edge detail from noise or H.264 artifacts.

The production pipeline and detector settings are unchanged. These samples improve coverage of pocket/cushion locations, but the single frame 978 review, unmatched conditions and a local image-gradient metric do not establish detector accuracy or justify a detector architecture decision. A defensible black-present centre and matched controls are still needed before scoring detector proposals or deciding whether a raw-space branch helps.
