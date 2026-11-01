# Mid-cushion frame 2831 black-miss source audit — 1 November 2026

## Scope and provenance

This is a read-only audit of the Production verifier receipt and its exact source image. It is not a reviewed label record, a detector acceptance result, or a completed exact-stage replay.

- Operation: `9d2bc097-85cf-4f49-9f48-faf30da9fe10`.
- Frame: 2831, captured `2026-11-01T12:48:48.2226688Z`, source time `14696486611` us.
- Timeline: `ee05aed8310f491c88df150b8b736a50`, generation 7.
- Calibration: `00a2e8e6d0eb4c90bec72c9bfea9c28f`.
- PNG: 1920x1080, SHA-256 `d59148155e7c45c1a3b1d6e8804e95b5e5375b61fded7f82efcc5a4637b1eb6f`.
- Receipt: SHA-256 `4142fc835b5bfbba64b67c45f9a2b872ef7029e92784b04cd3f6469256a7aa09`; its indexed pixel hash matches the PNG.
- Files: `work/o08/live-detect-black-pair-v1/physical-controls/mid-cushion-present/9d2bc09785cf4f499f48faf30da9fe10/9d2bc09785cf4f499f48faf30da9fe10/2831.png` and `2831.json`.

## What the receipt and image show

The source image visibly shows a distinct black ball around source pixel `(1223,153)` beside the cue around `(1169,155)`, near the upper long cushion and away from a pocket. These are visual estimates, not user-reviewed centres. The Production analyzer emitted 15 circles: seven red, seven yellow and one cue, with no black. It reports `uncertain=true`, `layoutUncertain=false`, and `obstructed=false`. Cue boundary support is 31/32 sectors.

The receipt uses analyzer identity `body-priority-packed-colour-peaks-v25-neutralceiling94-edge23-pocket48-sparse20-review75-redyellow-black-radius13-support21`, top-level configuration `o08-provisional-v21`, and width-800 settings. Relevant gates are sparse-black radius ranking, dark-body value ceiling 40, search extent 1.3, minimum support 0.65625, packed-colour boundary extent 0.65, and pocket exclusion radius 48. The receipt remains authoritative for the full setting set.

Packed-cluster recovery was not detected (`detected=false`, `complete=false`, zero red/yellow centres and no black centre). `candidateAudits` and `boundaryAudits` are empty, so this receipt does not record a black seed or its rejection reason. The pocket-colour audit reports no pocket requiring review; its only nonzero regions have 276 coloured / 245 explained / 31 unexplained pixels and 234 / 220 / 14, both below the 64-pixel review threshold. The black is in the mid-cushion area, so those pocket audit values do not explain its omission.

## Source-path review and limit

Source inspection shows that `AddPackedClusterBodies` selects the sparse path below 12 packed-colour centres and invokes `AddSparseBlackBody`. That stage thresholds a dark mask at value ≤40, opens it with a 3×3 kernel, ranks up to 12 seeds, and then applies pocket exclusion, boundary-support, classification, and duplicate checks. The saved Production receipt does not say which stage omitted this visible black.

The `VisionAnalyzer` candidate and boundary diagnostics are optional and default off. The available exact-stage replay uses Android instrumentation (`net10.0-android` with Android OpenCV loading); no host exact-stage runner was available for this audit. No phone, build, or threshold was changed. The next diagnostic should run the exact PNG with current `.65` settings and enable per-seed/candidate/boundary rejection evidence before considering detector changes.

This audit does not measure centre error, does not promote any threshold, and does not change the one-frame result into readiness evidence. Current physical controls later in the same Development setup have a black-absent detector result and a black-present/cue-absent result; all three observed inventories remain uncertain. The initial positive is also image-colour mismatched from the later samples and must not be used as a same-light pair.
