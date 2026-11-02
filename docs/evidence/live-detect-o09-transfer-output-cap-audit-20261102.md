# O09 transfer probe output-cap audit — 2 November 2026

**Test:** `O09-TRANSFER-NEW-CONTROLS-OUTPUT-CAP-AUDIT`  
**Result:** **PASS** for locating the 100-box cap and reconciling the saved score. The earlier learned checkpoint result remains **FAIL 0/7 layouts**.

## Finding

The 100-prediction limit is applied by Ultralytics inference, not by the scoring/report layer. In the retained runner, `MAX_DETECTIONS = 100` is passed directly as `max_det` to `model.predict`; the runner then copies each `result.boxes` item to `predictions` and scores every returned item. There is no second 100-item slice in the score loop.

The saved report reconciles for every frame:

- 100 returned predictions and 100 per-class prediction counts;
- `TP + FP = 100`;
- `TP + FN = reviewed visible ground truth`.

Across seven frames, **700 predictions = 36 TP + 664 FP**, and **109 reviewed balls = 36 TP + 73 FN**. The **664 FP** are therefore real false positives within the retained, inference-capped outputs, not an artifact introduced by the scorer. The exact number and quality of boxes beyond `max_det=100` are unknown: the probe did not retain uncapped proposals. Nothing in this audit changes the checkpoint decision, and it does not justify a confidence sweep or another run on the same seven frames.

## Provenance and attempts

This is a read-only reconciliation of the existing report and runner source; model inference was **not repeated**. The score report hash is `d38a32031711d47f27f0fae9ad65eb9dbba8cc7aad772b25beaec8d9d40da7bc`. The runner hash is `9dc185b923e8995e422ed5748493ef9d9785e656a468f8b3f8889796741f7b8d`; checkpoint hash is `67ebc3d7a3ca28554e820429c6a90ce5a18f5781392ce7ace006965bd9438752`.

The first local reconciliation artifact failed because a PowerShell check read the JSON class-count object as dictionary values. It produced no conclusion and changed no source data. The corrected artifact reads the report object's properties and passes all seven per-frame and aggregate count invariants. Both audit files are preserved under `work/o09/transfer-new-controls-probe-v1/`: failed first attempt `score-boundary-audit-20261102.json` (SHA-256 `fcc27308ebdeb66a0441c5c8d8c4076845903eed214dcc6b159abeb0e30fd70c`) and corrected result `score-boundary-audit-v2-20261102.json` (SHA-256 `e95c738722d9cd47d9fa2fc42292f7cf74655f11e38bc71fb99ab2ec3672f7a9`).

## Gate and next work

The prior candidate remains rejected; no new training, inference, score tuning, Holdout access or device work occurred. D1/D2/E remain **0/3 proven**, D2 remains **0/200** supported requests, **0/100** held-out unsafe windows and **0** completed soaks; criteria-complete lighting remains **0/2**.

**Next exact test:** `O09-DEVELOPMENT-REMAINDER-BATCH`, beginning with warm/train Layout02 when the table is open under stable playable light. The current table is closed for the night, so no physical capture starts at this checkpoint.
