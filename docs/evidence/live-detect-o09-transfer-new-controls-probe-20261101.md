# O09 frozen transfer checkpoint on new controls — 1 November 2026

## Result

**`O09-TRANSFER-NEW-CONTROLS-PROBE` — FAIL 0/7 complete layouts.** The already trained YOLO11n transfer checkpoint was run once on the seven newly user-reviewed Development source images. No training, augmentation, threshold tuning, Holdout access or production integration occurred. The checkpoint hash matches its retained v1 training summary; the exact v1 training export enumerates its train samples, with **0/7** new-image overlap.

At the fixed prior source-audit settings, every image hit `max_det=100`. Across **109** reviewed visible balls, the model returned **700** predictions: **36 TP, 664 FP, 73 FN**, precision **5.14%**, recall **33.03%**, and **0/7** complete layouts. The 664 false positives are the count within the 100-detection cap; additional proposals may have been truncated. Matches were **31 yellow and 5 red**; cue and black had **0** correct matches. It proposed black **7** times in the black-absent frame 542 and cue **5** times across cue-absent frames 295 and 562. The checkpoint is not a useful play-test or integration candidate.

Matched source-image centre error on only 36 matches was median **2.31 px**, nearest-rank p95 **7.30 px**, maximum **7.71 px**. This is descriptive of a small matched subset. Centres/radii are user-reviewed visual labels without numeric matching tolerance; no geometry gate is passed.

## Exact inputs and execution

- Tested checkout: `d914dfc Record O09 Development control corpus expansion`.
- Corpus: `live-detect-20261101-black-cue-development-v4-o09-reviewed-radii-v2`.
- Corpus manifest SHA-256: `347c4342a1ba184d6f3dde39c7a488d839d2cd00b34df5ab762bf1ec0ad65322`.
- Source audit report: `work/o09/transfer-new-controls-probe-v1/report.json`.
- Probe report SHA-256: `d38a32031711d47f27f0fae9ad65eb9dbba8cc7aad772b25beaec8d9d40da7bc`.
- Probe harness SHA-256: `9dc185b923e8995e422ed5748493ef9d9785e656a468f8b3f8889796741f7b8d`; the harness is committed with this report. The inference ran once from a working tree based on `d914dfc`.
- Fine-tuned checkpoint SHA-256: `67ebc3d7a3ca28554e820429c6a90ce5a18f5781392ce7ace006965bd9438752`.
- Training summary SHA-256: `1a15511bb971789270713048539bbf2085f93b0c16e958c8e49450f43a511a40`.
- Original v1 training export SHA-256: `8daf9a9d9098dec7dfe52d087ebb0fe024df8c41c499817a9b0c7e3cb13e562c`.
- Runtime: Ultralytics 8.4.194, PyTorch 2.15.0+cpu, CUDA unavailable; CPU only.
- Frozen inference: source PNGs, `imgsz=640`, confidence `0.001`, NMS IoU `0.7`, `max_det=100`; class-aware greedy scoring at IoU `0.50`. No warm-up or timing metric was used.

The new corpus's split lock assigns all seven frames to train, but this checkpoint was trained only on the prior v1 corpus (6 train images / 93 boxes). Thus these exact frames were unseen by this checkpoint. This remains a correlated Development probe from two setup/control groups, not Holdout or an independent estimate of room-lighting generalization.

## Per-frame result

| Frame | GT | Predictions | TP | FP | FN | Matched classes | Black proposals when absent | Cue proposals when absent | Complete |
| ---: | ---: | ---: | ---: | ---: | ---: | --- | ---: | ---: | --- |
| 288 | 16 | 100 | 5 | 95 | 11 | 5 yellow | — | — | No |
| 295 | 15 | 100 | 5 | 95 | 10 | 5 yellow | — | 3 | No |
| 542 | 15 | 100 | 5 | 95 | 10 | 5 yellow | 7 | — | No |
| 562 | 15 | 100 | 6 | 94 | 9 | 6 yellow | — | 2 | No |
| 959 | 16 | 100 | 5 | 95 | 11 | 5 red | — | — | No |
| 1691 | 16 | 100 | 6 | 94 | 10 | 6 yellow | — | — | No |
| 2831 | 16 | 100 | 4 | 96 | 12 | 4 yellow | — | — | No |
| **Total** | **109** | **700** | **36** | **664** | **73** | **31 yellow, 5 red** | **7** | **5** | **0/7** |

Frame 2831 remains photometrically unmatched. Frames 959 and 562 form a photometrically unmatched cue-present/absent pair; frame 1691's lighting stratum remains unverified. No matched-light or causal claim is supported. Frame-level output cannot establish settled inventory, freshness, Ready, physical centre accuracy or canonical outcomes.

## Attempts, gates and next action

The first harness run stopped before loading the model because it parsed the one-line O01 JSONL index as a JSON object. The JSONL reader was corrected, then the seven-frame inference completed once. No checkpoint call or prediction artifact was produced by the failed attempt. The source images, labels and receipts were unchanged.

The frozen checkpoint is rejected as a proposal candidate on these frames; do not repeat the same model run, raise confidence to hide false positives, retrain it on these seven frames, or use them as Holdout. The v2 exporter currently leaves only two validation frames in one correlated group, so it cannot support another useful training comparison. The next target is **`O09-DEVELOPMENT-LAYOUT-LIGHTING-EXPANSION`**: with stable, playable lighting, collect varied full-table Development layouts across the supported lighting strata; precompute the session/setup split and resulting validation coverage before staging. Keep the capture stopped until each bounded evidence step, and review the saved images in one batch.

Current metrics remain separate: software suites **3/3 carried forward**, motion **20/20 carried forward**, detector inventory **0/3 pass**, D1/D2/E **0/3 proven**. At this turn's start the camera endpoint read `Stopped` at revision 64; no device action or capture occurred during this probe. The earlier transfer candidate, both scratch/transfer validation results, and this new probe remain Development-only failures. See the [targeted ledger](live-detect-targeted-test-ledger-20261101.md) and [scorecard](live-detect-scorecard-20261101.md).
