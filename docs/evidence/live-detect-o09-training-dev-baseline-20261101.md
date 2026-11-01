# O09 scratch training baseline — 1 November 2026

## Result

**`O09-TRAINING-DEV-BASELINE` — training/evaluation pipeline completed; detector result FAIL 0/4 complete layouts.** A 20-epoch YOLO11n model initialized from its architecture config (no pre-trained checkpoint) ran on six Development images and was scored on four Development validation images from one held-out setup group. Validation mAP50 was **0.000**; class-aware matching at confidence ≥0.001 and IoU ≥0.50 found **0/62** labelled boxes, with **148** false positives and **62** false negatives. None of the four layouts was complete. This is a Development failure for a random-initialized model, not an O08 detector-inventory score or Holdout result.

## Frozen input and model identity

- O09 derivative corpus: `live-detect-20261101-black-cue-development-v4-o09-reviewed-radii-v1`.
- Manifest SHA-256: `146497273B267D6B9E9B230B233B97E89455C299399E90B02D37972168981C23`.
- Export report SHA-256: `8DAF9A9D9098DEC7DFE52D087EBB0FE024DF8C41C499817A9B0C7E3CB13E562C`.
- Train: **6 images / 93 visible boxes / 2 setup groups**. Class boxes: red 42, yellow 42, cue 5, black 4, unknown 0.
- Validation: **4 images / 62 visible boxes / 1 setup group**. Class boxes: red 28, yellow 28, cue 3, black 3, unknown 0.
- No Holdout samples were opened or copied into the trainer. The four validation frames are correlated within one lower-right setup group; they are not four independent layout groups.
- Trainer: Python 3.13.13, Ultralytics 8.4.194, PyTorch 2.15.0 CPU, Intel i7-11800H CPU; CUDA unavailable.
- Architecture config: installed package `cfg/models/11/yolo11.yaml`, copied byte-for-byte and named for the `n` scale. SHA-256 `43D8A7C86ACC77282DDF4966D6526E091E5B064C140ED4A6E4C0B68ECBC3784C`. The configured architecture has 2,624,080 parameters before replacing its 80-class output; trained five-class model has 2,590,815 parameters.
- Initialization: random from YAML; **no pretrained model weights**. Best checkpoint SHA-256 `F5B250A2D0B8ECE9CC7BBDE487162DFA5185C7253EF5A9C96A8D83A66375AAB9`, size 5,445,338 bytes. It remains under ignored `work/o09/training-baseline-v1/` and is not part of SkyEye product code.
- Parameters: 20 epochs maximum, patience 8, image size 640, batch 2, deterministic seed `20261101`, CPU, AdamW, learning rate 0.001, no augmentation. All 20 epochs completed.

Ultralytics' validation output was precision 0, recall 0, mAP50 0, mAP50–95 0. The separate source-pixel audit applied class-aware greedy matching at IoU 0.50 to each of the four validation frames:

| Frame | Labelled balls | Predictions at confidence ≥0.001 | TP | FP | FN | Complete layout |
| ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 461 | 15 | 37 | 0 | 37 | 15 | No |
| 375 | 16 | 37 | 0 | 37 | 16 | No |
| 477 | 15 | 37 | 0 | 37 | 15 | No |
| 1049 | 16 | 37 | 0 | 37 | 16 | No |
| **Total** | **62** | **148** | **0** | **148** | **62** | **0/4** |

No matched detections means no centre-error statistic is available. The black-absent frame 461 produced zero black proposals, but the model missed every visible ball on that frame; this single Development observation does not establish safe absence behavior. No `unknown` examples were available in train or validation, so the model has not learned an abstention class.

## Orchestration record and decision

The first model-name lookup for bare `yolo11n.yaml` failed before model construction because the installed package bundles the base `yolo11.yaml`, not a separate nano-named file. A byte-identical local copy with `yolo11n.yaml` naming resolved the intended `n` scale; configuration-only initialization passed, without a checkpoint. Attempt 1 then stopped before epoch 1 because Ultralytics resolved the export's relative `path: .` from the repository working directory and looked for `C:\code\SkyEye\images\val`. It processed **0 samples** and did not change the frozen export. The retained run record is `work/o09/training-baseline-v1/training-attempt-01-dataset-path-failure.json`. The justified retry created a training-only YAML with the export's absolute path, leaving the original dataset YAML and labels untouched. Attempt 2 then scanned 6/6 train and 4/4 validation images and completed all 20 epochs. No model weights were downloaded. Ultralytics did fetch its Arial display font into the user cache; package version was not updated.

The outcome shows that configuration-only random initialization is not useful on six training images. Do not tune thresholds against this same validation group or call this model a candidate winner. The next bounded experiment is `O09-TRAINING-DEV-TRANSFER-BASELINE`: use an explicitly hash-bound pre-trained initialization only after checking its exact asset provenance and license, keep the identical Development split, and report the result separately. Ultralytics package/model assets are AGPL-3.0 unless separately covered by an applicable Enterprise license; this work remains local and is not integrated or distributed. See [official Ultralytics licensing](https://github.com/ultralytics/ultralytics).

No Pixel inference, ONNX/TFLite export, memory/thermal/battery test, Holdout score, detector-inventory result, or physical D1/D2/E acceptance follows. The desktop CPU run and its timing are not a Pixel benchmark.
