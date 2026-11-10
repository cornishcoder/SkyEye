# SkyEye Detection, Tracking & Table-Motion Evidence Inventory

**Generated**: 2026-11-10  
**Scope**: Complete forensic audit of all tracked, untracked, and ignored visual and experimental evidence in the `c:\code\SkyEye` workspace (`tests/`, `docs/`, and `work/`).

---

## 1. Executive Summary & Audit Methodology

To establish why SkyEye's ball detection, colour classification, and settled-table verification remain unreliable under variable domestic lighting—and to stop repeating physical staging experiments on the pool table—every image, raw pixel buffer, video clip, diagnostic overlay, and JSON evaluation receipt in the workspace was recursively inventoried, hashed, clustered, and visually inspected:

1. **Cryptographic Deduplication (`SHA-256`)**: Every image and video file was hashed to separate genuinely distinct captures from identical files copied across multiple replay and experiment directories.
2. **Perceptual Scene Clustering (`64-bit dHash` & `aHash`)**: All unique 2D camera frames of the pool table (`740` unique `SHA-256` images across `1,528` files) were clustered by perceptual difference hash (`dHash` Hamming distance $\le 6$), revealing that all `1,528` raw table frame files originate from **36 distinct perceptual scene clusters**.
3. **Contact-Sheet Organisation & First-Hand Visual Inspection**: Six high-resolution annotated contact sheets (`72` panels total) were generated in [`docs/argon/contact-sheets/`](file:///c:/code/SkyEye/docs/argon/contact-sheets) and visually inspected alongside full-resolution source frames, rectified masks, and stage overlays.
4. **Ground-Truth & Provenance Separation**: Human-reviewed ground-truth label sets were strictly separated from model predictions, draft assistant labels, and unreviewed captures.

---

## 2. Complete Evidence Inventory

### 2.1 File Counts & Storage Breakdown

Excluding build/SDK caches (`bin`, `obj`, `android-sdk-11`, `flutter-sdk`, `railbird-pool-1.2.10`) and `3` third-party Python package test images inside `work/python-reference/`, the workspace contains **`4,970` project image files**, **`78` raw pixel buffers (`.bgr`, `.rgb`, `.argb`, `.ppm`)**, **`134` video recordings**, and **`1,527` JSON manifests/receipts/label files**:

| Evidence Category | Total Files | Unique `SHA-256` | Duplicate `SHA-256` Groups | Resolution(s) | Primary Locations |
| :--- | ---: | ---: | ---: | :--- | :--- |
| **Raw Table Camera Frames** (`.png`, `.jpg`) | 1,507 | **740** | 194 | `1920×1080` (739), `640×360` (1) | [`tests/fixtures/poolgame/`](file:///c:/code/SkyEye/tests/fixtures/poolgame), [`work/o08/`](file:///c:/code/SkyEye/work/o08), [`work/o09/`](file:///c:/code/SkyEye/work/o09), [`work/production/evidence/`](file:///c:/code/SkyEye/work/production/evidence) |
| **Synthetic Observation Buffers** (`.gray`) | 21 | **3** | 3 | `8×8` (`64 B` raw `uint8`) | [`tests/fixtures/observations/v1/`](file:///c:/code/SkyEye/tests/fixtures/observations/v1) |
| **Diagnostic Overlays & Contact Sheets** | 1,489 | **682** | 241 | `1920×1080`, `1000×2000`, `800×1600`, grids | [`docs/evidence/`](file:///c:/code/SkyEye/docs/evidence), [`work/production/evidence/`](file:///c:/code/SkyEye/work/production/evidence), [`work/o09/`](file:///c:/code/SkyEye/work/o09) |
| **Rectified Table Frames & Masks** | 1,022 | **358** | 212 | `1000×2000` (239), `800×1600` (162), windows | [`work/production/evidence/`](file:///c:/code/SkyEye/work/production/evidence), [`work/o08/`](file:///c:/code/SkyEye/work/o08) |
| **Companion UI Screenshots** | 852 | **846** | 6 | `1344×2992` (Pixel 11 Pro XL) | [`work/production/evidence/`](file:///c:/code/SkyEye/work/production/evidence), [`work/`](file:///c:/code/SkyEye/work) |
| **Ball/Pocket Crops & Patches** | 55 | **55** | 0 | `64×64` to `320×320` | [`docs/evidence/`](file:///c:/code/SkyEye/docs/evidence), [`work/o08/`](file:///c:/code/SkyEye/work/o08) |
| **Other Project Images** | 24 | **23** | 1 | Various | [`tests/fixtures/`](file:///c:/code/SkyEye/tests/fixtures), [`work/`](file:///c:/code/SkyEye/work) |
| **Total Standard Project Images** | **4,970** | **2,707** | **657** | — | Timestamp span: `2026-10-03` to `2026-11-02` |
| **Raw Headerless Pixel Dumps** (`.bgr`, `.rgb`, `.argb`, `.ppm`) | 78 | **78** | 0 | `1000×2000×3` (`42`), `400×800×3` (`28`), `800×1600×3` (`1`), `1920×1080×3` (`2`), `640×360×4` (`1`), `120×120` `.ppm` (`4`) | [`work/o08/native-pixels/`](file:///c:/code/SkyEye/work/o08/native-pixels), [`work/production/evidence/`](file:///c:/code/SkyEye/work/production/evidence) |
| **Video Recordings** (`.mp4`, `.ts`, `.h264`) | 134 | **125** | 9 | `1920×1080` @ `30 fps`, `1344×2992` screen captures, `640×360` @ `30 fps` TS | [`work/production/evidence/`](file:///c:/code/SkyEye/work/production/evidence), [`work/o08/`](file:///c:/code/SkyEye/work/o08), [`work/`](file:///c:/code/SkyEye/work) |

### 2.2 Inaccessible or Non-Standard Files Identified

Per investigation requirements, every file that standard image/video readers could not open directly was inspected at the byte level:

1. [`work/companion-candidates.png`](file:///c:/code/SkyEye/work/companion-candidates.png) (`8,961` bytes): Fails `PIL.Image.open` (`UnidentifiedImageError`). Byte inspection (`41 6e 64 72 6f 69 64 20 44 65 62 75 67...`) confirms this is **not an image**, but plain-text CLI output from `Android Debug Bridge version 1.0.41 (Version 36.0.0-13206524)` accidentally redirected to a `.png` filename.
2. [`tests/fixtures/observations/v1/*.gray`](file:///c:/code/SkyEye/tests/fixtures/observations/v1) (`21` files across the repo, `64` bytes each, `3` unique `SHA-256` hashes): Headerless `8×8` `uint8` grayscale arrays used by synthetic unit tests in [`VerificationChecks.cs`](file:///c:/code/SkyEye/tests/SkyEye.Checks/VerificationChecks.cs). They are decoded by reshaping raw `uint8[64]` to `(8, 8)`.
3. **Headerless Raw Byte Buffers (`.bgr`, `.rgb`, `.argb`)**:
   - `42` `.bgr` files of `6,000,000` bytes each = headerless `1000×2000×3` `uint8` rectified BGR dumps.
   - `1` `.bgr` file of `3,840,000` bytes = headerless `800×1600×3` `uint8` rectified BGR dump.
   - `28` `.bgr` files of `960,000` bytes each = headerless `400×800×3` `uint8` rectified BGR dumps in [`work/o08/native-pixels/`](file:///c:/code/SkyEye/work/o08/native-pixels).
   - `2` `.rgb` files of `6,220,800` bytes each = headerless `1920×1080×3` `uint8` source RGB dumps.
   - `1` `.argb` file of `921,600` bytes = headerless `640×360×4` `uint8` buffer.

---

## 3. Perceptual Scene Clusters of Raw Table Frames

Clustering all `740` unique `1920×1080` (and `640×360`) raw table frames by 64-bit `dHash` (Hamming distance $\le 6$) yields **36 distinct scene clusters**. All 36 clusters are visually displayed in:
- [`sheet_01_scene_clusters_part1.png`](file:///c:/code/SkyEye/docs/argon/contact-sheets/sheet_01_scene_clusters_part1.png) (Clusters `01`–`18`)
- [`sheet_02_scene_clusters_part2.png`](file:///c:/code/SkyEye/docs/argon/contact-sheets/sheet_02_scene_clusters_part2.png) (Clusters `19`–`36`)

| Cluster ID | Representative Source File | Unique `SHA-256` Frames | Total File Copies | Mean Luma Range (`0–255`) | Visual Scene & Lighting Description |
| :---: | :--- | ---: | ---: | :---: | :--- |
| **01** | [`tests/fixtures/poolgame/break-shot-red-potted-2026-10-26.png`](file:///c:/code/SkyEye/tests/fixtures/poolgame/break-shot-red-potted-2026-10-26.png) | 29 | 73 | `34.1–54.0` | Dim ambient room light; post-break loose cluster on right half of table, cue ball upper-left. |
| **02** | [`work/pixel-cushion-shadow.jpg`](file:///c:/code/SkyEye/work/pixel-cushion-shadow.jpg) | 2 | 4 | `129.9–130.5` | Bright daylight; 16 balls spread across table; strong top-rail shadow and bright cream top rail. |
| **03** | [`tests/fixtures/poolgame/pixel-packed-rack-2026-10-25.jpg`](file:///c:/code/SkyEye/tests/fixtures/poolgame/pixel-packed-rack-2026-10-25.jpg) | 30 | 48 | `87.0–134.9` | Standard 15-ball packed triangle rack + cue ball on head spot under bright/moderate light; chalk/clip on top rail. |
| **04** | [`tests/fixtures/poolgame/pixel-packed-rack-shadowed-dark.png`](file:///c:/code/SkyEye/tests/fixtures/poolgame/pixel-packed-rack-shadowed-dark.png) | 23 | 42 | `78.6–135.1` | Packed 15-ball rack + cue ball with directional shadow cast behind rack toward bottom-right corner. |
| **05** | [`work/t02-racked-live.jpg`](file:///c:/code/SkyEye/work/t02-racked-live.jpg) | 20 | 27 | `50.4–69.7` | Dim warm evening light; packed rack + cue ball and dim spread snapshots (`t02-spread-dim.jpg`). |
| **06** | [`tests/fixtures/poolgame/pixel-table-post-shot-spread-2026-10-27.png`](file:///c:/code/SkyEye/tests/fixtures/poolgame/pixel-table-post-shot-spread-2026-10-27.png) | **301** | **934** | `95.0–132.5` | Largest cluster: bright/normal-light 13-ball post-shot spread + pocket/cushion control captures (`844`, `455`, `423`, `1365`). |
| **07** | [`work/table-raw.jpg`](file:///c:/code/SkyEye/work/table-raw.jpg) | 6 | 9 | `49.7–126.8` | Scattered 16-ball layout and its synthetic darkened/obstruction test derivatives (`synthetic-dark.jpg`, etc.). |
| **08** | [`tests/fixtures/poolgame/synthetic-empty.jpg`](file:///c:/code/SkyEye/tests/fixtures/poolgame/synthetic-empty.jpg) | 1 | 1 | `90.0` | Synthetic flat teal-green `1920×1080` test image (`dHash = 0000000000000000`). |
| **09** | [`work/o08/frame978-development-v1/replay-input/978/978.png`](file:///c:/code/SkyEye/work/o08/frame978-development-v1/replay-input/978/978.png) | 96 | 129 | `67.3–80.0` | Dimmer/moderate-light pocket & cushion control layouts (`978`, `1691` opposite-cue series, `266` warm-dim rack). |
| **10–16** | [`work/h06b-production-tv.png`](file:///c:/code/SkyEye/work/h06b-production-tv.png) .. [`work/v01-tv-clear-frame.png`](file:///c:/code/SkyEye/work/v01-tv-clear-frame.png) | 14 | 14 | `35.6–129.5` | Early Oct (`2026-10-04..07`) TV overlay, shot-candidate line renders (`t02-tv-candidate-*`), and synthetic corner cover (`t08-covered.png`). |
| **17–18** | [`work/o08/capture-after-restart/o08-corpus/857791d45c6d4971b18f43b50960644c/3718.jpg`](file:///c:/code/SkyEye/work/o08/capture-after-restart/o08-corpus/857791d45c6d4971b18f43b50960644c/3718.jpg) | 18 | 18 | `128.4–130.6` | Bright daylight scattered-ball slow-roll sequences (`slow-roll-v9`, `slow-roll-v10`). |
| **19** | [`work/o09/transfer-new-controls-pixel-baseline-replay-20261102/inputs/562/562.png`](file:///c:/code/SkyEye/work/o09/transfer-new-controls-pixel-baseline-replay-20261102/inputs/562/562.png) | 7 | 13 | `85.5–86.7` | Opposite-side cue-absent control sequence (`562..660`): black at top cushion right of middle pocket, balls in all 6 pocket jaws. |
| **20** | [`work/o08/live-detect-black-pair-v1/physical-controls/mid-cushion-opposite-cue/87b15e584e33483eb2c29e8f299b481f/87b15e584e33483eb2c29e8f299b481f/1018.png`](file:///c:/code/SkyEye/work/o08/live-detect-black-pair-v1/physical-controls/mid-cushion-opposite-cue/87b15e584e33483eb2c29e8f299b481f/87b15e584e33483eb2c29e8f299b481f/1018.png) | 15 | 21 | `92.2–109.1` | Mid-cushion opposite-cue control (`959..1057`) and normal-validation `Layout02`/`Layout03` frames. |
| **21** | [`work/o09/development-v3/raw/normal-validation-layout-01/attempt-3eac0dd5b97f43b480ed06a954589f4e/3eac0dd5b97f43b480ed06a954589f4e/1587.png`](file:///c:/code/SkyEye/work/o09/development-v3/raw/normal-validation-layout-01/attempt-3eac0dd5b97f43b480ed06a954589f4e/3eac0dd5b97f43b480ed06a954589f4e/1587.png) | 4 | 6 | `111.2–112.6` | O09 Normal-Validation `Layout01` (`1557..1617`): 15-ball open spread under normal overhead pool lighting. |
| **22** | [`work/o09/development-v3/raw/normal-validation-layout-02/attempt-008a3905cd164a5db413886ab73a344d/008a3905cd164a5db413886ab73a344d/31.png`](file:///c:/code/SkyEye/work/o09/development-v3/raw/normal-validation-layout-02/attempt-008a3905cd164a5db413886ab73a344d/008a3905cd164a5db413886ab73a344d/31.png) | 5 | 7 | `94.2–108.4` | O09 Normal-Validation `Layout02` (`3..61`): 16-ball open spread under normal overhead pool lighting. |
| **23** | [`work/o09/development-v3/raw/normal-validation-layout-03/attempt-60411e14c26848739fa1d9a5c3619582/60411e14c26848739fa1d9a5c3619582/1006.png`](file:///c:/code/SkyEye/work/o09/development-v3/raw/normal-validation-layout-03/attempt-60411e14c26848739fa1d9a5c3619582/60411e14c26848739fa1d9a5c3619582/1006.png) | 7 | 7 | `91.0–92.1` | O09 Normal-Validation `Layout03` minus-cue & minus-black states (`916..1006`, `957`, `962`). |
| **24** | [`work/o09/development-v3/raw/normal-validation-layout-03/attempt-d72ad7021a294b308ff22ff5bbcff67b/d72ad7021a294b308ff22ff5bbcff67b/2219.png`](file:///c:/code/SkyEye/work/o09/development-v3/raw/normal-validation-layout-03/attempt-d72ad7021a294b308ff22ff5bbcff67b/d72ad7021a294b308ff22ff5bbcff67b/2219.png) | 6 | 6 | `85.0–85.2` | O09 Normal-Validation `Layout03` restored 16-ball state (`2219..2309`, `2263`): black + cue at upper-right pocket jaw. |
| **25–26** | [`work/production/evidence/code30-live-detect-frame-3496.png`](file:///c:/code/SkyEye/work/production/evidence/code30-live-detect-frame-3496.png) | 32 | 34 | `28.0–85.7` | Ultra-dark / lights-off rack captures (`code30` through `code32`) and gamma-boost diagnostic replays. |
| **27** | [`work/production/evidence/code65-source-frame-check-20261031/hand-positive-current/1.png`](file:///c:/code/SkyEye/work/production/evidence/code65-source-frame-check-20261031/hand-positive-current/1.png) | 3 | 7 | `90.5–93.6` | Human hand/forearm obstruction reaching over the right rail onto the 15-ball rack (`frame 1184`). |
| **28** | [`work/production/evidence/code66-lighting-holdout-20261031/input/all-01.png`](file:///c:/code/SkyEye/work/production/evidence/code66-lighting-holdout-20261031/input/all-01.png) | 24 | 72 | `130.2–132.1` | All-Lights holdout sequence: 15-ball rack + cue ball with all room + overhead lights on. |
| **29** | [`work/production/evidence/code66-lighting-holdout-20261031/input/daylight-01.png`](file:///c:/code/SkyEye/work/production/evidence/code66-lighting-holdout-20261031/input/daylight-01.png) | 38 | 79 | `97.2–134.2` | Daylight holdout sequence: 15-ball rack + cue ball with strong side window light. |
| **30** | [`work/production/evidence/code66-lighting-holdout-20261031/input/off-01.png`](file:///c:/code/SkyEye/work/production/evidence/code66-lighting-holdout-20261031/input/off-01.png) | 21 | 55 | `96.2–97.5` | Lights-Off holdout sequence: 15-ball rack + cue ball with overhead lights off. |
| **31–33** | [`work/production/evidence/o08-current-device-check-20261029/luma-gamma16/250.png`](file:///c:/code/SkyEye/work/production/evidence/o08-current-device-check-20261029/luma-gamma16/250.png) | 29 | 38 | `60.8–80.1` | Mid-light and manual-verifier rack sequences (`code13`, `code29`, `luma-gamma16`). |
| **34–36** | [`work/production/evidence/shot-samples-20261027/current-v21-five-clips-20261029/1cb2ac0be9ae47fd89dae45f1dd60238/frames-1400ms/frame-013.png`](file:///c:/code/SkyEye/work/production/evidence/shot-samples-20261027/current-v21-five-clips-20261029/1cb2ac0be9ae47fd89dae45f1dd60238/frames-1400ms/frame-013.png) | 4 | 14 | `112.5–117.3` | Live shot clip frames (`2026-10-27`), including player bridge hand and cue stick in `frame-013.png`. |

---

## 4. Meaningful Groupings & Visual Inspection Findings

All six forensic contact sheets in [`docs/argon/contact-sheets/`](file:///c:/code/SkyEye/docs/argon/contact-sheets) were visually inspected alongside full-resolution source images:

### 4.1 Lighting Strata & Camera Auto-Exposure / Auto-White-Balance Drift
Inspected via [`sheet_03_lighting_strata_and_ae_awb_drift.png`](file:///c:/code/SkyEye/docs/argon/contact-sheets/sheet_03_lighting_strata_and_ae_awb_drift.png):

| Lighting Condition | Representative Frame | Global Mean RGB | Central Cloth Patch RGB | Key Colorimetric & Visual Finding |
| :--- | :--- | :---: | :---: | :--- |
| **Daylight Bright** | [`daylight-01.png`](file:///c:/code/SkyEye/work/production/evidence/code66-lighting-holdout-20261031/input/daylight-01.png) | `(129.7, 124.1, 121.6)` | `(135.5, 147.1, 152.2)` | High luminance; cool blue-grey cloth (`B - R = +16.7`); intense window glare on left rail. |
| **All-Lights On** | [`all-01.png`](file:///c:/code/SkyEye/work/production/evidence/code66-lighting-holdout-20261031/input/all-01.png) | `(128.0, 130.9, 131.9)` | `(108.6, 128.6, 140.1)` | Strongest blue-cyan cloth chromaticity (`B - R = +31.5`). |
| **Normal Pool Room** | [`Layout01 667`](file:///c:/code/SkyEye/work/o09/development-v3/raw/normal-validation-layout-03/attempt-8f9d667a1e7342cfb4208c4eb3c0aef5/8f9d667a1e7342cfb4208c4eb3c0aef5/2083.png) | `(102.0, 98.6, 96.0)` | `(82.6, 92.5, 98.8)` | Moderate blue-grey cloth (`B - R = +16.2`). |
| **Lights-Off Ambient** | [`off-01.png`](file:///c:/code/SkyEye/work/production/evidence/code66-lighting-holdout-20261031/input/off-01.png) | `(108.9, 92.5, 81.8)` | `(68.2, 66.5, 65.8)` | **Blue dominance Invert!** Under warm ambient room light, the blue cloth has `R > G > B` (`B - R = -2.4`), collapsing HSV saturation to near zero. |
| **Mid-Light** | [`luma-gamma16/250.png`](file:///c:/code/SkyEye/work/production/evidence/o08-current-device-check-20261029/luma-gamma16/250.png) | `(62.5, 60.5, 57.4)` | `(61.9, 62.8, 59.4)` | Blue channel is lowest (`B = 59.4`); cloth appears warm greenish-grey. |
| **Warm-Dim Light** | [`warm-batch/266.png`](file:///c:/code/SkyEye/work/o09/development-v3/warm-batch-20261102/9a3c2da33ac44bb4a5b6458a3a44a2a7/266.png) | `(82.3, 69.0, 59.4)` | `(58.3, 55.0, 52.1)` | Strong warm cast (`R - B = +6.2` on blue cloth); both black and cue balls missed by baseline. |
| **Ultra-Dark Failure** | [`frame-3496.png`](file:///c:/code/SkyEye/work/production/evidence/code30-live-detect-frame-3496.png) | `(36.2, 30.2, 23.3)` | `(30.2, 28.2, 24.0)` | Cloth luminance (`~28`) falls below `DarkBodyValueCeiling = 40` and `ClothValueMin = 30`. |
| **AE Drift Pair A** | [`2831.png`](file:///c:/code/SkyEye/work/o09/transfer-new-controls-pixel-baseline-replay-20261102/inputs/2831/2831.png) vs [`542.png`](file:///c:/code/SkyEye/work/o09/transfer-new-controls-pixel-baseline-replay-20261102/inputs/542/542.png) | `(124.8, 126.0, 125.4)` vs `(107.8, 116.6, 120.4)` | `(112.9, 134.7, 145.0)` vs `(87.5, 120.6, 138.2)` | **Same physical room lighting**: removing the cue ball causes camera AE/AWB to shift cloth Red by **$-25.4$** and Green by **$-14.1$**! |
| **AWB Drift Pair B** | [`678.png`](file:///c:/code/SkyEye/work/o08/live-detect-black-pair-v1/corpus-v4/678/678.png) vs [`455.png`](file:///c:/code/SkyEye/work/o08/frame978-development-v1/physical-controls/cue-absent/21f8a006a157407da648ffebea8f4d96/455.png) | `(127.9, 123.0, 120.1)` vs `(119.1, 126.7, 129.1)` | `(119.8, 133.0, 139.2)` vs `(112.8, 140.0, 151.6)` | **Same physical room lighting**: removing the cue ball shifts cloth Red by **$-7.0$** and Blue by **$+12.4$**. |

### 4.2 Ball Arrangements, 4-State Controls & Obstructions
Inspected via [`sheet_04_ball_arrangements_and_controls.png`](file:///c:/code/SkyEye/docs/argon/contact-sheets/sheet_04_ball_arrangements_and_controls.png), [`sheet_05_failure_mechanisms_black_and_cue.png`](file:///c:/code/SkyEye/docs/argon/contact-sheets/sheet_05_failure_mechanisms_black_and_cue.png), [`sheet_06_failure_mechanisms_homography_inventory_verifier.png`](file:///c:/code/SkyEye/docs/argon/contact-sheets/sheet_06_failure_mechanisms_homography_inventory_verifier.png), and full-resolution [`raw-and-rectified-pocket-coverage.png`](file:///c:/code/SkyEye/docs/evidence/pocket-warp-coverage-audit-20261031/raw-and-rectified-pocket-coverage.png):

1. **Homography Boundary Clipping & 3D Parallax Projection**:
   - Full-resolution inspection of [`raw-and-rectified-pocket-coverage.png`](file:///c:/code/SkyEye/docs/evidence/pocket-warp-coverage-audit-20261031/raw-and-rectified-pocket-coverage.png) proves that the 4-corner calibration quadrilateral connects the inner cushion noses and **cuts across all six pocket mouths**. A ball resting on a pocket lip (e.g. the black ball in `frame 1365`, `frame 1691`, or `frame 1274`) lies up to 40–50% outside the calibration quadrilateral and is clipped against the `x=0`/`x=W`/`y=0`/`y=H` border when warped into `800×1600` or `1000×2000`.
   - Furthermore, because a pool ball is a 3D sphere of diameter $2R$ whose centre is at height $z = R$ above the cloth plane $z = 0$, the planar homography $H_{z=0}$ projects any ball near the rails radially outward by $14\text{--}27\,\text{px}$ (`0.65–1.25` ball radii). In `Frame 1049` (Tile 5 of Sheet 6), yellow ball `Y3` pressed against the bottom cushion is projected outward by `26.8 px`, exceeding the `0.010` (`8.0 px`) evaluation tolerance and registering as both a false negative (`missedIds: ["Y3"]`) and a false positive (`extraIds: ["yellow-2"]`).
2. **Black Ball Misses Near Pockets & Cushions (`Frames 1691, 2083, 2831, 288, 962, 1274, 1365`)**:
   - Zoomed crops in Sheet 6 (Tiles 1, 2, 4) show three simultaneous visual phenomena on the black 8-ball near rails/pockets: (i) the white `"8"` number spot and overhead LED specular highlight create a bright hole (`V > 180`) inside the dark body (`V <= 40`), shrinking the distance-transform peak radius in [`PackedBallClusterRecovery.cs`](file:///c:/code/SkyEye/src/SkyEye.Vision.Android/PackedBallClusterRecovery.cs#L244) (`7.64–7.80 px` vs `8.0 px` threshold in `Code70` frames `1365, 1425, 1645`); (ii) dark rail/pocket shadows create $>64$ connected components in scanline order, so [`FindPeakSeeds`](file:///c:/code/SkyEye/src/SkyEye.Vision.Android/PackedBallClusterRecovery.cs#L506) (`i <= 64` cap) drops the black ball in `Frame 1691` (components `133–135`, [`live-detect-o09-transfer-controls-black-support-trace-20261102.md`](file:///c:/code/SkyEye/docs/evidence/live-detect-o09-transfer-controls-black-support-trace-20261102.md)); and (iii) when the black ball is adjacent to a bright cue ball or rail glare, [`BallBoundaryRefiner.Refine`](file:///c:/code/SkyEye/src/SkyEye.Core/Analysis/BallBoundaryRefiner.cs#L467) rejects sectors with `exterior > 40` (`RejectExterior`) and sectors facing dark pocket shadows (`measuredEdge < 16`), dropping sector support to `15/32–20/32` below the `21/32` (`0.65`) gate.
3. **Cue Ball Misses Near Cushions (`Layout03 Frames 1274, 3360, 382`)**:
   - In `Layout03` (`Sheet 6 Tile 3`), the cue ball sits against the left short cushion (`y ≈ 31 px` near `y = 0` in the `800×1600` rectified frame) next to bright window glare on the wooden rail (`RGB > 200`). `RecoverNeutralCueBodies` ([`CueBallRecovery.cs`](file:///c:/code/SkyEye/src/SkyEye.Vision.Android/CueBallRecovery.cs#L60-L349)) rejects components within `1.7 * expectedRadius` (`36.7 px`) of the border (`rejected-image-edge`) or aborts if `candidates.Count > 4` (`line 290`), while `BallBoundaryRefiner` rejects the `y=0` cushion-facing sectors via `RejectExterior` (`exterior = 78–202 > 40`).
4. **Catastrophic Inventory-Cap Demotion (`Layout03 Frames 957 & 2263`)**:
   - In `Layout03` validation frames `957` (minus-cue) and `2263` (restored 16-ball), one non-yellow ball is misclassified as `yellow`, producing `8` yellow detections (`> 7` max). [`EnforceKnownTableBallInventory`](file:///c:/code/SkyEye/src/SkyEye.Vision.Android/VisionAnalyzer.cs#L1174-L1228) responds by **demoting all 8 yellow balls to `unknown`**, collapsing colour classification accuracy from `14/15` (`93.3%`) to `7/15` (`46.7%`)!

---

## 5. Historical Experiments Ledger & Audit of Repeated Physical Staging

A central finding of this forensic review is that **the user was repeatedly asked to stage near-identical physical ball arrangements on the pool table across multiple days without fixing the underlying architectural bottlenecks**:

| Date / Phase | Staged Physical Arrangement | Evidence Directories & Docs | Why It Was Repeated / What Happened |
| :--- | :--- | :--- | :--- |
| **2026-10-25 to 2026-10-29** (`O08 code13–code57`) | Packed 15-ball rack + cue ball under daylight, all-lights, mid-light, lights-off, and hand obstruction. | `work/o08/`, `work/production/evidence/o08-*`, [`docs/o08-observation-reliability-report.md`](file:///c:/code/SkyEye/docs/o08-observation-reliability-report.md) | Over 40 threshold tweaks (`gamma`, `ClothValueMin`, `DarkBodyValueCeiling`, rack shadow heuristics) were tuned on individual lighting captures without locking camera AE/AWB or normalising cloth chromaticity. |
| **2026-10-30 to 2026-10-31 AM** (`code64–code70`) | **Pocket-lip & cushion black+cue controls** (14 red/yellow balls in pocket jaws + mid-cushion, black+cue moved around pockets). | `work/production/evidence/code64-*` through `code70-*`, [`live-detect-control-ledger-20261031.md`](file:///c:/code/SkyEye/docs/evidence/live-detect-control-ledger-20261031.md) | Tuned `PocketExclusionRadius`, `AddSparseBlackBody` peak distance (`8.0 * scale`), and seed caps (`16`, `32`, `64`) on `frame 978` and `frame 1365`. |
| **2026-11-01 PM** (`Bounded Control Attempts 1–9`) | **Repeated 4-state black+cue physical controls** at lower-left (`844/678`), lower-right (`1049/461/477/375`), upper-right (`190/206/229/697`), mid-cushion (`2831/295/542/959`), and opposite-side cue (`1691/562`). | [`live-detect-bounded-control-attempts-20261101.md`](file:///c:/code/SkyEye/docs/evidence/live-detect-bounded-control-attempts-20261101.md), [`live-detect-targeted-test-ledger-20261101.md`](file:///c:/code/SkyEye/docs/evidence/live-detect-targeted-test-ledger-20261101.md) | **9 physical staging attempts in a single day**: Attempts 1–2 failed due to `SampleGap` (`866–966ms > 800ms`) caused by synchronous width-1000 fallback + PNG disk writes; Attempts 4, 7, 8 failed or drifted because removing the cue ball shifted unlocked camera AE/AWB by `7.5–18.7` gray levels; Attempt 9 (`1691`) failed because `FindPeakSeeds` capped connected components at `i <= 64`. |
| **2026-11-02** (`O09 YOLO11n Spike & Layout01–03`) | **Restaged 4-state black+cue controls** (`Layout01`, `Layout02`, `Layout03` in train and validation sets + `warm-batch 266` + physical retests `S01`). | [`docs/o09-learned-ball-detection-spike.md`](file:///c:/code/SkyEye/docs/o09-learned-ball-detection-spike.md), [`live-detect-o09-development-lighting-expansion-20261102.md`](file:///c:/code/SkyEye/docs/evidence/live-detect-o09-development-lighting-expansion-20261102.md) | Instead of fixing the deterministic bugs diagnosed in `1691`, `2831`, `1274`, and `957`, an under-trained 6–15 image YOLO11n spike (`max_det=100`, `664` false positives) was attempted, followed by restaging `Layout03` twice (`train` and `validation`), hitting the exact same `RejectExterior` sector gate and 8-yellow inventory demotion bug. |

---

## 6. Reproducible Offline Regression Corpus & Benchmark Metrics

The script [`aggregate_regression_corpus.py`](file:///c:/code/SkyEye/docs/argon/scripts/aggregate_regression_corpus.py) compiles all human-reviewed ground-truth corpora into [`aggregated_regression_benchmark.json`](file:///c:/code/SkyEye/docs/argon/data/aggregated_regression_benchmark.json). One ball diameter in `table-2x1` coordinates equals $43.0 / 800 = 0.05375$ table units ($43.0\,\text{px}$ at rectified width $800$).

### 6.1 Summary Across All Human-Reviewed Ground-Truth Corpora

| Reviewed Corpus | Frames (Complete) | Visible Balls (GT) | Spatial TP | False Pos (FP) | Missed (FN) | Precision | Recall | Colour Correct (End-to-End Acc) | $p_{50}$ Pos Error (Ball Diam) | $p_{95}$ Pos Error (Ball Diam / `px@800`) | Uncertain Flagged |
| :--- | :---: | ---: | ---: | ---: | ---: | :---: | :---: | :---: | :---: | :---: | :---: |
| **1. O01 Seed Regression** ([`o01-baseline.json`](file:///c:/code/SkyEye/docs/evidence/o01-baseline.json)) | `1 (1)` | 16 | 16 | 0 | 0 | `100.0%` | `100.0%` | `16/16 (100.0%)` | `0.044 d` | `0.077 d (3.30 px)` | `0/1` |
| **2. O08 Code65 Spread (`Frame 978`)** ([`baseline-0.010.json`](file:///c:/code/SkyEye/work/o08/frame978-development-v1/device-replays/f978current65v2-o08-export/baseline-development-tolerance-0.010.json)) | `1 (1)` | 15* | 15 | 0 | 0 | `100.0%` | `100.0%` | `15/15 (100.0%)` | `0.043 d` | `0.132 d (5.66 px)` | `1/1` |
| **3. O08 Pocket/Cushion v4 (`10 frames`)** ([`merged-v4-current-065`](file:///c:/code/SkyEye/work/o08/live-detect-black-pair-v1/physical-controls/exports/merged-v4-current-065/baseline-development-tolerance-0.010.json)) | `10 (7)` | 155 | 152 | 3 | 3 | `98.1%` | `98.1%` | `152/155 (98.1%)` | `0.027 d` | `0.151 d (6.49 px)` | `5/10` |
| **4. O09 Transfer Controls (`7 frames`)** ([`baseline-table2x1.json`](file:///c:/code/SkyEye/work/o09/transfer-new-controls-pixel-baseline-replay-20261102/baseline-development-tolerance-0.010-table2x1.json)) | `7 (4)` | 109 | 106 | 0 | 3 | `100.0%` | `97.2%` | `106/109 (97.2%)` | `0.028 d` | `0.132 d (5.68 px)` | `6/7` |
| **5. O09 Normal-Train `Layout02` (`1085`)** ([`layout02-baseline`](file:///c:/code/SkyEye/work/o09/layout02-baseline-v1/baseline-current-source-tolerance-0.010.json)) | `1 (1)` | 16 | 16 | 0 | 0 | `100.0%` | `100.0%` | `16/16 (100.0%)` | `0.028 d` | `0.101 d (4.34 px)` | `1/1` |
| **6. O09 Normal-Train `Layout03` (`4 frames`)** ([`layout03-baseline`](file:///c:/code/SkyEye/work/o09/development-v3/reviews/layout03-current-source-baseline-v1/baseline-current-source-tolerance-0.010.json)) | `4 (0)` | 62 | 56 | 0 | 6 | `100.0%` | `90.3%` | `56/62 (90.3%)` | `0.027 d` | `0.103 d (4.45 px)` | `4/4` |
| **7. O09 Normal-Val `Layout01/02` (`1587, 31`)** ([`reviewed-score.json`](file:///c:/code/SkyEye/work/o09/normal-validation-layout01-02-current-replay-20261102/reviewed-score.json)) | `2 (2)` | 31 | 31 | 0 | 0 | `100.0%` | `100.0%` | `31/31 (100.0%)` | `—` | `< 0.186 d (< 8.0 px)` | `1/2` |
| **8. O09 Normal-Val `Layout03` (`4 frames`)** ([`baseline-post-dup-v2`](file:///c:/code/SkyEye/work/o09/development-v3/reviews/layout03-post-fix-current-source-20261102/baseline-post-duplicate-v2-tolerance-0.010.json)) | `4 (0)` | 62 | 59 | 0 | 3 | `100.0%` | `95.2%` | **`43/62 (69.4%)`** | `0.025 d` | `0.063 d (2.70 px)` | `4/4` |
| **9. O09 Warm-Dim `Layout01` (`Frame 266`)** ([`warm266-score.json`](file:///c:/code/SkyEye/work/o09/development-v3/warm-batch-20261102/warm266-reviewed-spatial-score.json)) | `1 (0)` | 16 | 14 | 0 | 2 | `100.0%` | `87.5%` | `14/16 (87.5%)` | `—` | `< 0.186 d (< 8.0 px)` | `1/1` |
| **10. Post-Shot 13-Ball Spread** ([`live-detect-plan.md`](file:///c:/code/SkyEye/docs/live-detect-plan.md#L58)) | `1 (1)` | 13 | 13 | 0 | 0 | `100.0%` | `100.0%` | `13/13 (100.0%)` | `—` | `< 0.186 d` | `0/1` |
| **Combined Reviewed Corpus (`32 distinct frames`)** | **`32 (17)`** | **495** | **478** | **3** | **17** | **`99.4%`** | **`96.6%`** | **`462/495 (93.3%)`** | **`0.028 d`** | **`0.151 d (6.49 px)`** | **`23/32`** |

*\*Note on Missing / Partial Ground-Truth Labels*:
- In `Frame 978` (`Code65`) and `Frame 1365` (`Code70`), only `15` balls have reviewed centre labels; the `16th` ball (black ball deep in the pocket jaw) was left unlabelled or marked as an approximate interior pixel (`[102.0, 903.0]` in [`pocket-warp-coverage-audit-20261031/summary.json`](file:///c:/code/SkyEye/docs/evidence/pocket-warp-coverage-audit-20261031/summary.json#L33-L37)).
- In [`work/o09/development-v3/superseded-review-records/frame-1530-misclassified-review-not-ground-truth.json`](file:///c:/code/SkyEye/work/o09/development-v3/superseded-review-records/frame-1530-misclassified-review-not-ground-truth.json), `frame 1530` was explicitly quarantined as a misclassified review record and is **excluded** from ground truth.
- Real timed shot sequences with frame-by-frame trajectory and pot/miss ground truth remain at **`0 / 60`** (`heldOutRealShots: 0` in all manifests), even though `125` unique `.mp4`/`.ts` video files exist on disk without trajectory/event annotations.

### 6.2 Processing Latency & Verifier Stillness / `SampleGap` Metrics

From [`docs/evidence/live-detect-o09-warm266-reviewed-spatial-score-20261102.md`](file:///c:/code/SkyEye/docs/evidence/live-detect-o09-warm266-reviewed-spatial-score-20261102.md), [`work/o09/direct1000-existing-controls-20261102/summary.json`](file:///c:/code/SkyEye/work/o09/direct1000-existing-controls-20261102/summary.json), and [`live-detect-bounded-control-attempts-20261101.md`](file:///c:/code/SkyEye/docs/evidence/live-detect-bounded-control-attempts-20261101.md):
- **Width-800 Single-Pass Offline Latency** (`Frame 957`, `Frame 2083`): $p_{50} = 313\text{--}332\,\text{ms}$, $p_{95} = 339\text{--}368\,\text{ms}$.
- **Width-800 + Synchronous Width-1000 Fallback Latency** (`Warm Frame 266`, incomplete packed cluster `Count = 14`): $p_{50} = 656.5\,\text{ms}$, $p_{95} = 668.7\,\text{ms}$ (nearly double the 333ms budget!).
- **Physical Verifier `SampleGap` Failures**: In `Attempts 1 & 2` (`2026-11-01`), stationary tables failed verification with `Unsettled (SampleGap)` at `800.067 ms`, `866.855 ms`, and `966.756 ms` (exceeding `MaximumVerificationGapSeconds = 800 ms` in [`TableAnalysisVerification.cs`](file:///c:/code/SkyEye/src/SkyEye.Core/Analysis/TableAnalysisVerification.cs#L13)) because synchronous dual-width analysis plus synchronous PNG evidence encoding (`evidenceCapture`) blocked the verification loop.

---

## 7. Unresolved Questions & Protocol Before Any Future Physical Experiment

### 7.1 What Existing Evidence Already Resolves (No Physical Experiment Needed)
1. **Why lighting changes break detection even in the "same" room state**: Resolved by fixed-patch photometry showing unlocked camera AE/AWB drifting by up to `25.4` RGB units (`2831 vs 542`) and blue cloth losing its blue dominance (`R > G > B`) under warm/lights-off illumination (`off-01.png`, `266.png`).
2. **Why black and cue balls fail near cushions and pockets**: Resolved by exact stage traces (`1691`, `2083`, `2831`, `1274`, `3360`, `382`) and [`raw-and-rectified-pocket-coverage.png`](file:///c:/code/SkyEye/docs/evidence/pocket-warp-coverage-audit-20261031/raw-and-rectified-pocket-coverage.png).
3. **Why yellow colour accuracy drops to 0/7 on `Layout03` frames `957` and `2263`**: Resolved by tracing [`EnforceKnownTableBallInventory`](file:///c:/code/SkyEye/src/SkyEye.Vision.Android/VisionAnalyzer.cs#L1174-L1228).

### 7.2 Remaining Unknowns
1. **Exact Camera2 Exposure/AWB Lock Stability on the Mounted Pixel 10 Pro**: Existing evidence proves that `CaptureEngine.java` does not set `CONTROL_AE_LOCK` or `CONTROL_AWB_LOCK`. Whether locking AE/AWB after a 1-second convergence window (or fixing manual exposure/gains via `/settings`) completely eliminates inter-frame cloth drift on the physical Pixel 10 Pro requires a single bounded device check *only after* the software fixes land.
2. **End-to-End Multi-Shot Game-State Continuity on Labelled Video Clips**: While `125` unique video clips exist in `work/`, none of the real shot videos (`work/production/evidence/shot-samples-20261027/`, `work/o08/captures/slow-roll-*`) have reviewed before/after ball-inventory ground-truth labels in `manifest.json`. Annotating 10–20 existing `.mp4` shot recordings offline will allow full shot-to-settled-state regression testing without touching the physical pool table.
