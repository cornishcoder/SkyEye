# O09 pretrained transfer baseline — 1 November 2026

## Result

**`O09-TRAINING-DEV-TRANSFER-BASELINE` — training/evaluation pipeline completed; detector result FAIL 0/4 complete layouts.** Fine-tuning an official COCO-pretrained YOLO11n checkpoint on the frozen six-image Development train split improved mAP50 from the scratch run's 0.000 to **0.0698**, but the source-image audit matched only **16/62** labelled boxes. All 16 matches were yellow. It returned **384 false positives**, **46 misses**, **3 black proposals** in the black-absent frame, and **0/4 complete layouts**. This is a weak Development result, not an O08 detector-inventory score, Holdout result, or Pixel candidate.

## Frozen data and model provenance

- Corpus: `live-detect-20261101-black-cue-development-v4-o09-reviewed-radii-v1`.
- Manifest SHA-256: `146497273B267D6B9E9B230B233B97E89455C299399E90B02D37972168981C23`.
- Export report SHA-256: `8DAF9A9D9098DEC7DFE52D087EBB0FE024DF8C41C499817A9B0C7E3CB13E562C`.
- Train: **6 frames / 93 visible boxes / 2 setup groups**. Development validation: **4 frames / 62 boxes / 1 setup group**. Holdout was unopened. The validation frames are correlated within one lower-right setup group.
- Initialization: official `yolo11n.pt` from the Ultralytics Assets `v8.3.0` release, SHA-256 `0EBBC80D4A7680D14987A577CD21342B65ECFD94632BD9A8DA63AE6417644EE1` (5,613,764 bytes). The checkpoint metadata identifies Ultralytics 8.2.100, 2024-09-25, and COCO's 80 classes including sports ball. The installed Ultralytics 8.4.194 loaded it and transferred 493/499 items into the five-class output head. The official YOLO11 documentation lists `yolo11n.pt`; Ultralytics licensing is AGPL-3.0 unless an applicable Enterprise license covers the use ([model documentation](https://docs.ultralytics.com/models/yolo11), [licensing](https://github.com/ultralytics/ultralytics), [v8.3.0 release](https://github.com/ultralytics/assets/releases/tag/v8.3.0)).
- Runtime: Python 3.13.13, Ultralytics 8.4.194, PyTorch 2.15.0 CPU, CUDA unavailable, Intel i7-11800H. Local Development experiment only; no product integration or distribution.
- Parameters: 640 image size, batch 2, CPU, deterministic seed `20261101`, AdamW learning rate 0.001, no augmentation, 20-epoch maximum, patience 8. Training early-stopped after **9 epochs**, with best validation at epoch **1**. No `unknown` boxes occurred in train or validation.
- The saved training summary records the run directory under the scratch work root because of the runner path error. The transfer summary now includes `epochsCompleted=9` and `bestEpoch=1`; both summary files are ignored work artifacts and are not checked into the repository.

## Measured result

Ultralytics validation reported precision **0.6239**, recall **0.0878**, mAP50 **0.0698**, and mAP50–95 **0.0444**. The separate source-image audit used class-aware greedy matching at confidence ≥0.001 and IoU ≥0.50, with `max_det=100` per image:

| Frame | GT | TP | FP | FN | Matched class | Black proposals when absent | Complete layout |
| ---: | ---: | ---: | ---: | ---: | --- | ---: | --- |
| 461 | 15 | 4 | 96 | 11 | Yellow only | 3 | No |
| 375 | 16 | 4 | 96 | 12 | Yellow only | — | No |
| 477 | 15 | 4 | 96 | 11 | Yellow only | — | No |
| 1049 | 16 | 4 | 96 | 12 | Yellow only | — | No |
| **Total** | **62** | **16** | **384** | **46** | **16 yellow** | **3** | **0/4** |

Matched source-image centre error was median **2.21 px**, maximum **13.67 px**, on only 16 matches; this describes the matched subset and is not an accuracy pass. The audit hit the 100-detection per-image cap on every frame, so the high FP count is also bounded by that cap. Four single-image desktop CPU calls took 74.75–78.27 ms; these timings are not a Pixel or end-to-end latency benchmark.

## Artifact bookkeeping and rerun rule

The copied runner's output root still pointed to `work/o09/training-baseline-v1/`. The transfer checkpoint and run directory are intact at `work/o09/training-baseline-v1/runs/transfer-n640-e20-seed20261101/`; the original scratch run is intact at `work/o09/training-baseline-v1/runs/scratch-n640-e20-seed20261101-pathfix1/`. The transfer summary was copied to `work/o09/training-transfer-v1/training-summary.json` (SHA-256 `1A15511BB971789270713048539BBF2085F93B0C16E958C8E49450F43A511A40`), the scratch JSON summary was reconstructed from its committed report and retained run (SHA-256 `B13E2542A57691FD1848B543FDEF22CAA9A6496B03A86A5637B48F3CAC25ECAC`), and each best-checkpoint hash was verified. The ignored transfer runner now points to `work/o09/training-transfer-v1/` for any future invocation. **Neither training run was repeated.** The artifact repair does not change the measured outputs.

The transfer checkpoint is not a candidate winner. Do not tune thresholds, claim black/cue detection, open Holdout, or begin Pixel benchmarking from these results. The Development sample has only two train setup groups and one validation group, no unknown-class examples, and very limited layout variation. The user reported darker lighting during the preceding physical controls; adequacy was not freshly checked during this offline follow-up. The next target is **`O09-DEV-CORPUS-EXPANSION`**: collect and independently review the missing Development layout/lighting coverage before another bounded candidate comparison, once lighting is stable. Keep the production C# path unchanged.

## Gate status and remaining work

- Data preparation: reviewed apparent image-space radii and derivative/export complete; the user approved all 155 displayed guides. This is not a physical cloth-plane radius validation.
- Candidate evidence: scratch **0/4** complete layouts; pretrained transfer **0/4** complete layouts. No candidate passes.
- Holdout: **0 sessions opened**. Proposed O08 acceptance coverage remains at least 12–20 distinct reviewed Development layouts with 1–2 anchor frames, then at least two held-out recording sessions across two lighting strata and at least five layouts. The proposed contract also calls for ≥100 non-overlapping request windows per lighting stratum, ≥100 unsafe/source/control windows, representative 30-second unchanged-table checks, and separate timing definitions. These are proposed gates, not achieved metrics.
- Physical: detector inventory remains **0/3**; D1/D2/E remain **0/3 proven**. Stable, adequate table lighting is still a prerequisite for the next staged capture batch; it was not rechecked in this offline turn.
- Planning estimate: this is several evidence cycles, not one more quick code fix. At minimum, expect one Development expansion/training cycle, two independent Holdout capture sessions, an installed-build D2 stationary run covering both lighting strata and the request/unsafe denominators, and a separate bounded event/clip play sequence. Some D2 checks may share the Holdout sessions if the frozen protocol allows it. The number of engineering iterations is not yet estimable because no learned or geometry candidate has passed Development.

No Pixel inference, ONNX/TFLite export, memory/thermal/battery test, Holdout score, detector-inventory result, or physical D1/D2/E acceptance follows from this run.
