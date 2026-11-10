# SkyEye Forensic CV Investigation — Argon Handoff Notes

**Last Updated**: 2026-11-10T22:41:00Z  
**Folder**: [`docs/argon/`](file:///c:/code/SkyEye/docs/argon)  
**Status**: First-pass diagnostic audit complete (`100%` of evidence inventory, perceptual clustering, contact-sheet visual inspection, reproducible offline evaluation, and full end-to-end code trace completed).  
**Production Code Status**: Untouched (diagnostic pass only, as instructed).  
**Software Regression Suite**: `dotnet run --project tests/SkyEye.Checks/SkyEye.Checks.csproj -c Release --no-build` — **`72/72` test groups PASS** (exit code `0`).

---

## 1. Completed Work & Argon Artefact Locations

### 1.1 Automated Recursive Evidence Inventory (`DONE`)
- **Script**: [`docs/argon/scripts/audit_evidence.py`](file:///c:/code/SkyEye/docs/argon/scripts/audit_evidence.py)
- **Outputs**:
  - [`docs/argon/data/evidence_inventory_summary.json`](file:///c:/code/SkyEye/docs/argon/data/evidence_inventory_summary.json)
  - [`docs/argon/data/inaccessible_and_raw_report.json`](file:///c:/code/SkyEye/docs/argon/data/inaccessible_and_raw_report.json)
- **Headline Inventory Counts**:
  - **Total Image Files Scanned**: `4,973` across `tests/`, `docs/`, and `work/` (`4,970` project images after excluding `3` third-party library test images in `work/python-reference/`).
  - **Unique Image `SHA-256` Hashes**: `2,710` (`2,707` project-only), with `657` project exact-duplicate `SHA-256` groups where identical frames/overlays were copied across multiple replay directories.
  - **Breakdown by Image Category (All Files / Unique `SHA-256`)**:
    - `raw_table_frame`: `1,528` files / `743` unique `SHA-256` (`740` standard 2D camera frames + `3` unique `8×8` `.gray` synthetic test buffers across `21` files).
    - `diagnostic_overlay_or_contact_sheet`: `1,489` files / `682` unique `SHA-256`.
    - `rectified_or_mask`: `1,022` files / `358` unique `SHA-256` (`162` at `800×1600`, `239` at `1000×2000`).
    - `ui_screenshot`: `852` files / `846` unique `SHA-256` (`1344×2992` Pixel 11 Pro XL companion screenshots).
    - `crop_or_patch`: `55` files / `55` unique `SHA-256`.
    - `other_image`: `24` project files / `23` unique `SHA-256`.
  - **Perceptual Scene Deduplication (`64-bit dHash`, Hamming distance $\le 6$)**:
    - All `740` unique 2D raw table frames collapse into **36 perceptual scene clusters**, proving that thousands of repository files originate from ~36 distinct physical table arrangements/lighting setups.
  - **Video Recordings**:
    - `134` video files (`112` `.mp4`, `21` `.ts`, `1` `.h264`) totaling `~1.54 GB`, representing **`125` unique `SHA-256` video recordings**, all probed via OpenCV (`width`, `height`, `fps`, `frame_count`).
  - **Inaccessible / Non-Standard Files Explained**:
    - [`work/companion-candidates.png`](file:///c:/code/SkyEye/work/companion-candidates.png): `8,961` bytes; not a PNG image — contains ASCII text starting with `"Android Debug Bridge version 1.0.41..."` (an `adb` CLI help output redirected to `.png`).
    - `21` `.gray` files (`64` bytes each = `8×8` raw `uint8` synthetic grayscale contract buffers in `tests/fixtures/observations/v1/` and two `work/o01-checkout-*` copies).
    - `71` `.bgr` raw headerless 3-channel byte dumps (`42` at `6,000,000` bytes = `1000×2000×3`; `1` at `3,840,000` bytes = `800×1600×3`; `28` at `960,000` bytes = `400×800×3`).
    - `2` `.rgb` raw byte dumps (`6,220,800` bytes = `1920×1080×3`), `1` `.argb` raw dump (`921,600` bytes = `640×360×4`), and `4` `.ppm` images (`43,215` bytes = `120×120` RGB patches).

### 1.2 Contact Sheets Generated & Visually Inspected (`DONE`)
- **Scripts**: [`docs/argon/scripts/inspect_and_generate_sheets.py`](file:///c:/code/SkyEye/docs/argon/scripts/inspect_and_generate_sheets.py) & [`docs/argon/scripts/update_sheet6.py`](file:///c:/code/SkyEye/docs/argon/scripts/update_sheet6.py)
- **Sheets (All 6 visually inspected via `read_file`)**:
  - [`docs/argon/contact-sheets/sheet_01_scene_clusters_part1.png`](file:///c:/code/SkyEye/docs/argon/contact-sheets/sheet_01_scene_clusters_part1.png)
  - [`docs/argon/contact-sheets/sheet_02_scene_clusters_part2.png`](file:///c:/code/SkyEye/docs/argon/contact-sheets/sheet_02_scene_clusters_part2.png)
  - [`docs/argon/contact-sheets/sheet_03_lighting_strata_and_ae_awb_drift.png`](file:///c:/code/SkyEye/docs/argon/contact-sheets/sheet_03_lighting_strata_and_ae_awb_drift.png)
  - [`docs/argon/contact-sheets/sheet_04_ball_arrangements_and_controls.png`](file:///c:/code/SkyEye/docs/argon/contact-sheets/sheet_04_ball_arrangements_and_controls.png)
  - [`docs/argon/contact-sheets/sheet_05_failure_mechanisms_black_and_cue.png`](file:///c:/code/SkyEye/docs/argon/contact-sheets/sheet_05_failure_mechanisms_black_and_cue.png)
  - [`docs/argon/contact-sheets/sheet_06_failure_mechanisms_homography_inventory_verifier.png`](file:///c:/code/SkyEye/docs/argon/contact-sheets/sheet_06_failure_mechanisms_homography_inventory_verifier.png)

### 1.3 Reproducible Offline Regression Evaluation (`DONE`)
- **Script**: [`docs/argon/scripts/aggregate_regression_corpus.py`](file:///c:/code/SkyEye/docs/argon/scripts/aggregate_regression_corpus.py)
- **Output**: [`docs/argon/data/aggregated_regression_benchmark.json`](file:///c:/code/SkyEye/docs/argon/data/aggregated_regression_benchmark.json)

---

## 2. Complete End-to-End Code Pipeline Trace (`DONE`)

Every file in the production detection, classification, boundary-refinement, verification, motion, and settled-state pipeline was read and traced:

1. **Overhead Camera Capture (`Pixel 10 Pro`)**:
   - [`src/SkyEye.Camera/CaptureEngine.java`](file:///c:/code/SkyEye/src/SkyEye.Camera/CaptureEngine.java#L51-L54): Uses `CameraDevice.TEMPLATE_RECORD` with `CONTROL_AF_MODE_CONTINUOUS_VIDEO` and `CONTROL_AE_TARGET_FPS_RANGE(30, 30)` at `1920×1080` H.264 (`6 Mbps`, `1 s` I-frame interval) over SRT (`port 5000`).
   - **Critical Finding**: Neither Auto-Exposure (`CONTROL_AE_LOCK`) nor Auto-White-Balance (`CONTROL_AWB_LOCK`) is locked or exposed via `/status` or `/settings`, nor is `CONTROL_AF_MODE` fixed after initial focus. Removing or moving a bright white cue ball or an arm entering the frame causes measurable camera AE/AWB drift even when room lighting is completely unchanged (`-25.4 R, -14.1 G` between `Frame 2831` and `Frame 542`; `-7.0 R, +12.4 B` between `Frame 678` and `Frame 455`).
2. **Companion Decoding & Frame Pairing (`Pixel 11 Pro XL`)**:
   - [`src/SkyEye.Companion/PlayerTableFrameSource.cs`](file:///c:/code/SkyEye/src/SkyEye.Companion/PlayerTableFrameSource.cs#L15-L407) & [`src/SkyEye.Companion/DecodedFrameLedger.java`](file:///c:/code/SkyEye/src/SkyEye.Companion/DecodedFrameLedger.java#L1-L35): Captures latest decoded ExoPlayer frame (`SurfaceView` `PixelCopy` or `TextureView.GetBitmap`), pairing it with `DecodedFrameLedger` (`renderIndex`, `presentationTimeUs`, `realtimeMs`). Rejects stale (`> 750 ms`) or drifted (`> 250 ms`) frames.
3. **Pre-Detection Planar Homography Rectification & Pocket Masking**:
   - [`src/SkyEye.Vision.Android/VisionAnalyzer.cs`](file:///c:/code/SkyEye/src/SkyEye.Vision.Android/VisionAnalyzer.cs#L259) & [`src/SkyEye.Vision.Android/VisionAnalyzer.cs`](file:///c:/code/SkyEye/src/SkyEye.Vision.Android/VisionAnalyzer.cs#L634-L646): Warps perspective `1920×1080` image into a flat `800×1600` (or `1000×2000` fallback) 1:2 table image via `Cv2.WarpPerspective` using the 4 corner calibration points before any ball detection.
   - **Critical Finding**: Because the 4 calibration points sit on the inner cushion noses, the homography quadrilateral cuts straight through all six pocket jaws ([`raw-and-rectified-pocket-coverage.png`](file:///c:/code/SkyEye/docs/evidence/pocket-warp-coverage-audit-20261031/raw-and-rectified-pocket-coverage.png)), clipping pocket-lip balls at `[0..W, 0..H]`. Moreover, because a pool ball is a 3D sphere of height $2R$ resting on the cloth plane $z=0$ while the camera views from finite height $H_{\text{cam}}$ at oblique angles near the rails, planar homography projects balls near cushions/pockets radially outward toward the rails by `14–27 px` (`0.65–1.25` ball radii, e.g. `26.8 px` on `Y3` in `Frame 1049`).
4. **Candidate Proposal & Recovery Cascade**:
   - **Stage A — Cloth Segmentation + Hough Circles**: [`VisionAnalyzer.cs`](file:///c:/code/SkyEye/src/SkyEye.Vision.Android/VisionAnalyzer.cs#L648-L820) builds HSV cloth mask (`H=82..132`, `S>=30`, `V>=30`), inverts to non-cloth mask, applies morphological close/open, and runs `Cv2.HoughCircles`. Under warm/dim/lights-off light (`off-01.png`, `250.png`, `266.png`), the blue cloth has `R >= B` (`S` collapses to near zero), breaking static HSV cloth segmentation.
   - **Stage B — Packed Cluster Recovery & Width-1000 Fallback**: [`src/SkyEye.Vision.Android/PackedBallClusterRecovery.cs`](file:///c:/code/SkyEye/src/SkyEye.Vision.Android/PackedBallClusterRecovery.cs#L18-L620) & [`VisionAnalyzer.cs`](file:///c:/code/SkyEye/src/SkyEye.Vision.Android/VisionAnalyzer.cs#L208-L244). When a packed cluster is incomplete (`Count >= 5 && Count < 15`), `VisionAnalyzer` runs a **second full analysis pass at width 1000 synchronously**, doubling frame analysis latency from `~313 ms` to `~656 ms`.
   - **Stage C — Sparse Black Body Recovery**: [`PackedBallClusterRecovery.cs`](file:///c:/code/SkyEye/src/SkyEye.Vision.Android/PackedBallClusterRecovery.cs#L188-L353) (`AddSparseBlackBody`) and [`src/SkyEye.Vision.Android/DarkBodyRecovery.cs`](file:///c:/code/SkyEye/src/SkyEye.Vision.Android/DarkBodyRecovery.cs#L17-L220).
     - **Critical Bug 1**: [`FindPeakSeeds`](file:///c:/code/SkyEye/src/SkyEye.Vision.Android/PackedBallClusterRecovery.cs#L506) iterates connected components with a hard scanline loop cap `for (var i = 1; i < componentCount && i <= 64; i++)`. When dark rail/pocket noise in the upper table creates $>64$ components, valid black ball components lower on the table (e.g. `Frame 1691`, components `133–135`) are silently skipped before scoring!
     - **Critical Bug 2**: `DarkBodyValueCeiling = 40` (`V <= 40`) is punctured by overhead specular highlights and the white `8`-patch on the black ball, shrinking distance-transform peaks below `minPeakDistance = 8.0 * scale` (`6.4 px` at `800 px`, `7.64–7.80 px < 8.0 px` in `Code70` frames `1365, 1425, 1645`).
   - **Stage D — Cue Ball Recovery**: [`src/SkyEye.Vision.Android/CueBallRecovery.cs`](file:///c:/code/SkyEye/src/SkyEye.Vision.Android/CueBallRecovery.cs#L60-L349) (`RecoverNeutralCueBodies`).
     - **Critical Bug 3**: Rejects any neutral component within `1.7 * expectedRadius` (`36.7 px` at `800 px`) of the border (`rejected-image-edge`), rejects components touching rail glare (`aspectRatio` outside `0.75..1.33`), and **aborts entirely if `candidates.Count > 4`** ([`line 290`](file:///c:/code/SkyEye/src/SkyEye.Vision.Android/CueBallRecovery.cs#L290)).
5. **Boundary Refinement (`BallBoundaryRefiner.cs`)**:
   - [`src/SkyEye.Core/Analysis/BallBoundaryRefiner.cs`](file:///c:/code/SkyEye/src/SkyEye.Core/Analysis/BallBoundaryRefiner.cs#L365-L620): Samples 32 angular rays, computes `background` median at `1.7 * expectedRadius`, and requires `support >= 0.65` (`21/32` sectors).
   - **Critical Bug 4**: For non-red/yellow balls (`black`, `cue`, `unknown`), any sector where `exterior > 40` is rejected as `RejectExterior` ([`line 467`](file:///c:/code/SkyEye/src/SkyEye.Core/Analysis/BallBoundaryRefiner.cs#L467)). When a black ball sits near a bright cue ball or rail glare (`Frames 1691, 2083, 2831`), or a cue ball sits near the `y=0` top cushion (`Layout03` frames `1274, 3360, 382`), valid sectors are rejected as `RejectExterior` or weak-edge (`measuredEdge < 16` against dark pocket/cushion shadow), dropping support to `15/32–20/32` and killing the detection!
6. **White Balance, Colour Classification & Inventory Capping**:
   - [`VisionAnalyzer.cs`](file:///c:/code/SkyEye/src/SkyEye.Vision.Android/VisionAnalyzer.cs#L369-L410) (`NormalizeRectifiedImageFromSingleCueBall`): Only normalizes RGB using the cue ball when `cueIndices.Length == 1`. If the cue ball is missed or misclassified as yellow, normalization is skipped (`normalized = false`).
   - [`VisionAnalyzer.cs`](file:///c:/code/SkyEye/src/SkyEye.Vision.Android/VisionAnalyzer.cs#L1174-L1228) (`EnforceKnownTableBallInventory`): **Critical Bug 5** — When `yellowIndices.Count > 7` (e.g. `7` true yellows + `1` cue ball misclassified as yellow in `Layout03` frames `957` and `2263`), instead of demoting/reclassifying only the single surplus candidate, it **demotes all 8 yellows to `unknown`** (`line 1190`), collapsing colour accuracy from `14/15` to `7/15`!
7. **Verification, Motion & Settled-State Tracking**:
   - [`src/SkyEye.Core/Analysis/TableAnalysisVerification.cs`](file:///c:/code/SkyEye/src/SkyEye.Core/Analysis/TableAnalysisVerification.cs#L13-L210): Sequential verification requires `RequiredCleanFrames = 5` consecutive clean frames with `MaximumVerificationGapSeconds = 0.8s` (`800 ms`) between successive source frames.
   - **Critical Bug 6**: Because `VerifyAsync` runs full `VisionAnalyzer.Analyze` synchronously in the loop (`~313 ms` at width 800, or `~656 ms` when width-1000 fallback triggers) AND invokes `evidenceCapture` (synchronous PNG encoding + file I/O) inside the loop ([`lines 156-163`](file:///c:/code/SkyEye/src/SkyEye.Core/Analysis/TableAnalysisVerification.cs#L156-L163)), inter-sample elapsed time hits `800.067 ms–966.756 ms`, triggering false `Unsettled (SampleGap)` failures on a completely motionless table!

---

## 3. Recommended Implementation Sequence for Next Session

When moving from this diagnostic pass to code changes, execute in this order and verify offline using [`docs/argon/scripts/aggregate_regression_corpus.py`](file:///c:/code/SkyEye/docs/argon/scripts/aggregate_regression_corpus.py) and `SkyEye.Checks`:

1. **Step 1 — Fix Targeted Inventory-Cap Demotion (`VisionAnalyzer.cs:1174-1228`)**:
   - Rank surplus colour group candidates by chromatic margin / cue-whiteness proximity; reclassify or demote only the `Count - 7` surplus candidate(s) rather than wiping all 8 detections to `unknown`.
2. **Step 2 — Uncap Scanline Seeds, Fill Dark Specular Holes & Make Boundary Refinement Occlusion-Aware (`PackedBallClusterRecovery.cs`, `CueBallRecovery.cs`, `BallBoundaryRefiner.cs`)**:
   - Remove `&& i <= 64` in `FindPeakSeeds` and `candidates.Count > 4` abort in `RecoverNeutralCueBodies`; fill small internal specular/`"8"`-spot holes inside compact dark contours before distance transform; treat border/pocket/neighbour-occluded sectors as neutral rather than `RejectExterior` in `BallBoundaryRefiner.Refine` (`>= 0.65` over unoccluded cloth-facing sectors, minimum `14/32` valid sectors).
3. **Step 3 — Per-Frame Interior Cloth Chromaticity Normalisation (`VisionAnalyzer.cs`, `BallClassifier.cs`)**:
   - Normalize frame RGB/Lab against the live interior cloth distribution on every frame (even when the cue ball is absent) and segment foreground by relative Mahalanobis distance from the cloth distribution.
4. **Step 4 — Padded Homography Canvas (`+48 px`), 3D Sphere Parallax Correction & Local Cluster ROI Fallback (`VisionAnalyzer.cs`, `CaptureEngine.java`)**:
   - Add a `48 px` padding margin during `WarpPerspective`, apply radial 3D sphere height parallax correction $\Delta \mathbf{p} = -(\mathbf{p} - \mathbf{p}_{\text{nadir}}) \cdot (R / H_{\text{cam}})$, replace full-frame width-1000 fallback with a local cluster ROI crop, and lock Camera2 AE/AWB (`CONTROL_AE_LOCK`, `CONTROL_AWB_LOCK`) after initial convergence.
