# Live Detect O09 pivot readiness

**Targets:** `SPARSE-BLACK-CANDIDATE-ORDERING` (reused result) and `O09-TRAINING-DATA-READINESS` (read-only check)  
**Result:** sparse-black candidate **FAIL 0/3**; O09 benchmark **BLOCKED, 0 eligible training corpora**. No detector test, model training, export, build, or replay ran in this cycle.  
**Checkout:** `69194ffc159fb3a78d6e261b36fea427bb1e370a`

## Candidate ordering result: reused, not repeated

The continuation's next offline candidate-ordering test is already represented by `allseed-positive-174021` (candidate source commit `13fb7f9`). Full sparse-black seed enumeration followed by the existing radius/proximity ranking, with the expensive fit budget held at 12, selected a seed near the reviewed black location in each of frames 1691, 1726 and 1764. It still added no black: boundary support was 18/32, 19/32 and 18/32 against the unchanged 21/32 gate. The output remained 15 balls and uncertain in all three frames. This is **0/3 recovery**, not a detector pass. The retained report records 800px analysis times 1038.41, 855.37 and 872.02ms; three correlated frames are not a runtime percentile.

This completes the scan-order candidate evaluation. Do not rerun it or start another support-threshold sweep on the same frames. Together with the recorded 12-fit and 64-fit misses, it reaches the continuation prompt's three-candidate pivot point. No production threshold or ranking default changed.

## O09 input readiness

The current O09 capture directory contains one diagnostic batch and one camera recording session (`42c18bf3769e422e8386d7e09368f0ba`) with two JPEG frames. Each label JSON is marked reviewed and has one visible ball with X/Y radii, but the retained O09 notes say these dense-table frames label only one ball each and predate complete-frame attestation. They are not complete training examples. `work/o01` currently contains only benchmark artifacts; there is no O09/O01 manifest and split lock ready to export.

The existing exporter requires at least **3 independent Development recording sessions** for train/validation partitioning. It also requires reviewed `image-normalized` labels and independently reviewed X/Y radii for every visible training ball. The frozen O08 corpus-v4 is not a direct substitute: its 10 source frames are PNGs with `table-2x1` centre/absence labels, no numeric radii, and no numeric centre-uncertainty records. Converting those points or images silently would change the source/label contract; doing so requires a separately reviewed, provenance-preserving data preparation step. There are no trained model weights or selected inference runtime in this checkout, so there is no candidate comparison or Pixel benchmark to report.

| Readiness item | Current | Required before O09 benchmark |
| --- | ---: | --- |
| Complete reviewed O09 frames in current capture batch | 0/2 | Every visible ball labelled; complete-frame attestation |
| Independent Development sessions eligible for train/validation | 0/3 | At least 3 complete labelled sessions |
| O08 corpus-v4 visible labels with numeric radii | 0/155 | Independent X/Y radii and O09-compatible image-space labels, if reused |
| Frozen O09-compatible manifest/split lock | 0 | Freeze session-level assignments before model training |
| Model/runtime benchmark | Not run | Only after eligible data, candidate provenance and rights are established |

## Blocker and next step

The O09 pivot is authorized by the continuation prompt, but its required real reviewed training data is absent. The current room light was reported darker, so no new table capture was attempted. The next useful action is a batch of complete, independently reviewed table frames across at least three Development recording sessions, with source receipts, image-space centres and radii, and whole-session assignments frozen before training. Keep Holdout sessions separate and unopened. The broader O08 D1/D2/E denominators still apply; an O09 image-model result alone would not pass Live Detect readiness or event/clip acceptance.

The mounted-camera `/status` read during this check was `Stopped`, revision 64, timeline `c5d9a60d9fb44210888abaabc77545ed`, 39,676 completed frames and 139 clips. The Pixel 11 ADB connection was present. After a wake, its foreground remained `com.android.chrome` and the screen screenshot was black; the accessibility hierarchy exposed no companion Home labels. A request to launch `uk.co.skyeye.companion/.MainActivity` was rejected by the command runner as blocked by policy, so it was not retried through another channel. No capture started. Companion Home `Start live session` is **not verified** and needs a manual Home readback before the next device wait/build/replay boundary.
