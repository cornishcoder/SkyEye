# Live Detect O09 pivot readiness

> **Superseded for data preparation on 1 November 2026:** the user approved all 155 displayed source-image radius guides. `O09-RADIUS-LABEL-ROUNDTRIP` then produced and verified a separate Development train/validation export (10 samples, 155 boxes, 3 setup groups). The earlier “0 eligible training corpora” finding below records the state before that review and export; it is no longer the current data blocker. Model training/comparison, Holdout and Pixel benchmarking remain outstanding. See [current round-trip evidence](live-detect-o09-radius-label-roundtrip-20261101.md).

**Targets:** `SPARSE-BLACK-CANDIDATE-ORDERING` (reused result) and `O09-TRAINING-DATA-READINESS` (read-only check)  
**Result:** sparse-black candidate **FAIL 0/3**; O09 benchmark **BLOCKED, 0 eligible training corpora**. No detector test, model training, export, build, or replay ran in this cycle.  
**Checkout:** `69194ffc159fb3a78d6e261b36fea427bb1e370a`

## Candidate ordering result: reused, not repeated

The continuation's next offline candidate-ordering test is already represented by `allseed-positive-174021` (candidate source commit `13fb7f9`). Full sparse-black seed enumeration followed by the existing radius/proximity ranking, with the expensive fit budget held at 12, selected a seed near the reviewed black location in each of frames 1691, 1726 and 1764. It still added no black: boundary support was 18/32, 19/32 and 18/32 against the unchanged 21/32 gate. The output remained 15 balls and uncertain in all three frames. This is **0/3 recovery**, not a detector pass. The retained report records 800px analysis times 1038.41, 855.37 and 872.02ms; three correlated frames are not a runtime percentile.

This completes the scan-order candidate evaluation. Do not rerun it or start another support-threshold sweep on the same frames. Together with the recorded 12-fit and 64-fit misses, it reaches the continuation prompt's three-candidate pivot point. No production threshold or ranking default changed.

## O09 input readiness

The current O09 capture directory contains one diagnostic batch and one camera recording session (`42c18bf3769e422e8386d7e09368f0ba`) with two JPEG frames. Each label JSON is marked reviewed and has one visible ball with X/Y radii, but the retained O09 notes say these dense-table frames label only one ball each and predate complete-frame attestation. They are not complete training examples. `work/o01` currently contains only benchmark artifacts; there is no O09/O01 manifest and split lock ready to export.

The existing exporter requires at least **3 independent Development recording sessions** for train/validation partitioning. It also requires reviewed `image-normalized` labels and independently reviewed X/Y radii for every visible training ball. The frozen O08 corpus-v4 is not a direct substitute: its 10 source frames are PNGs with `table-2x1` centre/absence labels, no numeric radii, and no numeric centre-uncertainty records. Converting those points or images silently would change the source/label contract; doing so requires a separately reviewed, provenance-preserving data preparation step. There are no trained model weights or selected inference runtime in this checkout, so there is no candidate comparison or Pixel benchmark to report.

| Readiness item at this report's original check | Then | Required before O09 benchmark |
| --- | ---: | --- |
| Complete reviewed O09 frames in current capture batch | 0/2 | Every visible ball labelled; complete-frame attestation |
| Independent Development sessions eligible for train/validation | 0/3 | At least 3 complete labelled sessions |
| O08 corpus-v4 visible labels with numeric radii | 0/155 | Independent X/Y radii and O09-compatible image-space labels, if reused |
| Frozen O09-compatible manifest/split lock | 0 | Freeze session-level assignments before model training |
| Model/runtime benchmark | Not run | Only after eligible data, candidate provenance and rights are established |

## Blocker and next step

The O09 pivot is authorized by the continuation prompt, but its required real reviewed training data is absent. The current room light was reported darker, so no new table capture was attempted. The next useful action is a batch of complete, independently reviewed table frames across at least three Development recording sessions, with source receipts, image-space centres and radii, and whole-session assignments frozen before training. Keep Holdout sessions separate and unopened. The broader O08 D1/D2/E denominators still apply; an O09 image-model result alone would not pass Live Detect readiness or event/clip acceptance.

At the original check, mounted-camera `/status` was read as `Stopped`, revision 64, timeline `c5d9a60d9fb44210888abaabc77545ed`, 39,676 completed frames and 139 clips. Pixel 11 ADB was present; the companion Home readback was not verified. This is historical device state and must not be treated as a current observation. No capture started during that check.
