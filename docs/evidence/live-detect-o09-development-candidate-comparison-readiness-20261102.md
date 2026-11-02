# O09 Development candidate-comparison readiness — 2 November 2026

## Result

**`O09-DEVELOPMENT-CANDIDATE-COMPARISON`: BLOCKED before scoring; no valid candidate comparison was available.** The bounded readiness audit found one current-source C# detection file for the new four-frame validation Layout03 set, but no three-output comparison triplet with common reviewed-sample coverage. No candidate comparison, model inference, detector replay, training, Pixel benchmark, or Holdout access occurred.

The new spatial baseline remains **59/62 TP, 0 FP, 3 FN, 100% precision and 95.16% recall**. This does not change the known detector failure: black is missed in all three black-present frames, and all four frames remain uncertain.

## Blocking evidence

- `O09Comparison.Compare` requires exactly **three uniquely named, fingerprinted candidate outputs**. Every output must include all reviewed frames in its selected frozen split, and an O08-backed candidate also needs complete per-frame replay provenance, a bound export manifest, and the exact split-lock hash.
- The new validation Layout03 score file contains the current C# output for four selected samples. There are no matching learned or hybrid output files or bound O08 export manifests for this same four-sample set. The v3 corpus/export remains incomplete, so those four samples are not a complete frozen Development comparison split.
- The available transfer checkpoint `best.pt` (5,478,554 bytes; SHA-256 `67ebc3d7a3ca28554e820429c6a90ce5a18f5781392ce7ace006965bd9438752`) is a real model artifact, but it has not been inferred on these four samples. Its frozen seven-frame new-controls probe used different sample IDs: 109 visible labels, 36 TP, 664 FP, 73 FN, and **0/7 complete layouts**. It reported CPU inference and no timing. It is not a passing Development candidate or Pixel-runtime evidence.
- The Android C# path has no integrated YOLO/ONNX/TFLite proposal runtime in the searched Vision Android or Companion Hybrid sources. No hybrid candidate output or on-device resource measurement exists. Do not invent a hybrid arm from the current detector output or duplicate a candidate to satisfy the comparison API.

## Provenance

| Artifact | SHA-256 |
|---|---|
| Four-sample current-source detections | `4df2a7b1ff064e3380aed8e1d1254b420198712003c20b1eabfa5331b0207df3` |
| Frozen 0/7 new-controls probe report | `d38a32031711d47f27f0fae9ad65eb9dba8cc7aad772b25beaec8d9d40da7bc` |
| Transfer checkpoint | `67ebc3d7a3ca28554e820429c6a90ce5a18f5781392ce7ace006965bd9438752` |
| `O09Comparison.cs` | `50812a917f509ce521ba8384c3e9da1c0e173f1b3fff0941b49733c5732b6d3` |
| Workbench `Program.cs` | `8a4237b1ef31e22c11a0d481071d886af2b602503168148cdeb9e12fa2e941d7` |

The current-source detections are under `work/o09/development-v3/reviews/layout03-validation-spatial-baseline-current-source-20261102/`. The existing failed probe remains at `work/o09/transfer-new-controls-probe-v1/report.json`; it was read only. Holdout remains unopened. No device action occurred; the camera remains stopped.

## Next work

Do not retry this comparison unchanged. Continue with **`O09-LIGHTING-STRATA-METADATA-EXISTING-RECORD-AUDIT`**: reconcile only already-saved sensor/receipt data and the user's condition descriptions, report which lighting metadata is measured versus qualitative, and identify what remains unavailable without asking for another daylight capture. Then prepare the remaining Development layout references for a future playable lighting session.
