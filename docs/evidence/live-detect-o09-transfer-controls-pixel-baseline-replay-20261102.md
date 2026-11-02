# O09 transfer controls Pixel baseline replay — 2 November 2026

## Result

**`O09-TRANSFER-NEW-CONTROLS-PIXEL-BASELINE-REPLAY`: completed; spatial baseline scored; inventory result 4/7 complete.** The corrected, receipt-bound current-source C# replay produced 106 spatial matches from 109 reviewed visible-ball labels across seven already-collected Development frames. There were 0 false positives and 3 false negatives. All 106 matches had the correct colour. Aggregate precision was 100%; recall was 97.25%. Four frames have complete inventory; frames 288, 1691 and 2831 each miss the labelled black (`B`). Six of seven outputs remain uncertain. This is a Development baseline, not a stillness/readiness acceptance.

The score association tolerance is `0.010` in table-2x1 coordinates. It is only the scorer's spatial association setting; it is not a label-centre uncertainty bound or a product acceptance threshold. The user-reviewed centres carry no numeric uncertainty bound in the source labels. The reviewed apparent source-image radii were preserved in the immutable source corpus and omitted from the derived table-coordinate score copy.

| Source frame | Labels | Detections | TP / FP / FN | Miss | Uncertain | Result |
|---:|---:|---:|---:|---|:---:|---|
| 1691 | 16 | 15 | 15 / 0 / 1 | B | Yes | incomplete |
| 2831 | 16 | 15 | 15 / 0 / 1 | B | Yes | incomplete |
| 288 | 16 | 15 | 15 / 0 / 1 | B | Yes | incomplete |
| 295 | 15 | 15 | 15 / 0 / 0 | — | Yes | complete |
| 542 | 15 | 15 | 15 / 0 / 0 | — | Yes | complete |
| 562 | 15 | 15 | 15 / 0 / 0 | — | Yes | complete |
| 959 | 16 | 16 | 16 / 0 / 0 | — | No | complete |

The corpus contains 17 labelled Development frames; 10 were not scored by this seven-frame test. No Holdout data was opened. The selected controls are related setup sequences, so the result is not an independent lighting-stratum or D1 accuracy estimate. The corresponding earlier YOLO probe is not directly comparable: it used class-aware IoU, while this C# score uses table-coordinate centre association. YOLO inference was not rerun.

## Replay and coordinate audit

The installed isolated `uk.co.skyeye.visionchecks` Release APK (`e405c1cb1d3dce9d58eb53eab314229d7bd8728136242867eb6627f2caa44e62`) ran seven serial single-frame `VerificationReplayInstrumentation` replays against saved images. Source checkout was `da161891c8a0f813249994f912428f3efd8f9b09`, including source fix `432c0a13142173eb7774f451ef714cc15717ec91`. No live capture started.

The first attempt omitted the explicit `sparseBlackRadiusRank=1` instrumentation argument. Since the instrumentation defaults an omitted value to false, these seven retained reports did not match the receipt configuration and were excluded from scoring. After detecting the mismatch, each frame was replayed once with the receipt-matching argument, in a unique run. All seven corrected reports match the primary width and settings identities, including `SparseBlackRankByRadius=true`. The reports and the excluded first-attempt outputs remain available under `work/o09/transfer-new-controls-pixel-baseline-replay-20261102/`; no completed replay was repeated after correction.

**`O09-TRANSFER-NEW-CONTROLS-PIXEL-BASELINE-REPLAY-TRANSFORM`: PASS.** Existing `SkyEye.Core.TableCalibration.Map/Unmap` mapped the reviewed normalized image centres into table-2x1 for scoring, after validating the receipt pixel hashes, sample/calibration identities and calibration corners against the frozen source transform. The audit covered 112 mapped label points (109 visible and 3 expected absent) and 106 detections. Maximum source-pixel round-trip residuals were `6.41e-13` for labels and `4.89e-13` for detections. This numerical round-trip checks the transform implementation; it does not increase the human labels' accuracy. Apparent radii were not transformed or scored.

The inverse-mapped detections were rendered over the source images and visually checked for systematic axis, reflection or scale errors. The contact sheet is `work/o09/transfer-new-controls-pixel-baseline-replay-20261102/transform-overlays/source-overlay-contact-sheet.png` (SHA-256 `3ac196ddccb080ddb9f95ca3f854957f76c8a29bcc2c34fbde9c9e430eeabbdb`).

## Reproducible artifacts

- Corrected replay reports: `work/o09/transfer-new-controls-pixel-baseline-replay-20261102/runs-config-corrected/{frame}/o08-replay-proof-xfer{frame}r1-20261102.json`.
- Transform audit: `work/o09/transfer-new-controls-pixel-baseline-replay-20261102/geometry-transform-audit.json` (SHA-256 `78a1b869395d99e66543d8ac7169ff53766d244046097b76ce8439568c5e0a57`).
- Derived Development score corpus: `work/o09/transfer-new-controls-pixel-baseline-replay-20261102/score-corpus/`; derived manifest SHA-256 `1d49530b2f004d27f03a4ab5a12e57a9dc32466fe3afa021ad657de271751c02`; derived split-lock SHA-256 `681b6e0d15d7367adf0cdddd435a31905c85440d7237a0b4b756f3c6f37334cf`.
- Detections: `work/o09/transfer-new-controls-pixel-baseline-replay-20261102/detections-current-source-transfer-controls.json` (SHA-256 `f0e8207f1e898cd109f80967f4fa9d14fa771dff6936796403df63cd7e37aeb3`).
- Scored baseline: `work/o09/transfer-new-controls-pixel-baseline-replay-20261102/baseline-development-tolerance-0.010-table2x1.json` (SHA-256 `8081f9f42c0ca25e53d5b371bdcd93531ce948451f38d3396d03120aecd86a3e`).
- Transform overlay renderer: `work/o09/transfer-new-controls-pixel-baseline-replay-20261102/geometry-transform/render_transform_overlays.py`.

The original corpus `work/o09/radius-reviewed-corpus-v2-verified` and its image-normalized labels were not modified. Source transform SHA-256: `a61559e221c46ead6fc50599168ba67d6939099ec31cf45a22b30861be0994d`.

## Device safety and next step

The Pixel 11 Pro XL (`66181FDDV001HS`) ran only the isolated verification package. At preflight `/status` was `Stopped`, revision 62, zero viewers, `clipCount=139`; Home showed `Start live session` and `Sample collection off`. After the last corrected replay the camera remained stopped and Home remained on those labels. No capture, clip, app-data clearing, production companion update, mounted-camera package update or Cast/TV action occurred.

The next physical target remains **`O09-DEVELOPMENT-REMAINDER-BATCH`**, starting with Warm/Train Layout02 once the table reopens under stable playable warm lighting. No new daylight shots are planned. D1/D2/E remain 0/3 proven; the stillness/readiness gate remains D2 0/200 supported requests, 0/100 held-out unsafe windows and 0 soak runs. This offline replay does not add stillness request windows or change those counts.
