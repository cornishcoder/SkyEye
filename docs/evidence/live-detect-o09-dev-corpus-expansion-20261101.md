# O09 Development control-corpus expansion — 1 November 2026

## Result

**`O09-DEV-CORPUS-EXPANSION` — PASS for reviewed Development data preparation and export.** The user reviewed the seven displayed cyan rim-guide sheets (frames 2831, 542, 295, 288, 959, 562 and 1691) and replied **“all look correct.”** Their 109 visible-ball guides are now recorded as apparent source-image radii in a new O01-derived Development corpus. The original v1 corpus, O08 corpus-v4, source images, centre labels and receipts remain unchanged.

The versioned derivative contains **17 frames across 17 recording timelines**, **272 ball/location labels**, **264 visible-ball labels with apparent radii**, **8 expected-empty locations without radii**, **5 setup groups**, and **13 black/cue target-state IDs**. The 13 IDs describe target presence/position controls, not 13 independently varied full-table layouts. Red/yellow arrangements remain from a small family; broad rack/spread coverage is still inadequate. No measured lighting stratum or Holdout data is present. Frame 2831 is explicitly excluded from matched-light claims. The opposite-cue present/absent runs are photometrically unmatched.

The frozen export assigns **15 images / 233 boxes to train** and **2 images / 31 boxes to validation**. Validation is one correlated lower-left setup group; **all seven new frames are in train**. This is not a candidate accuracy result. The frozen transfer checkpoint predates the new frames, so a bounded, no-retraining Development probe on those seven images is the next targeted test.

## Provenance and label construction

The derivative is `work/o09/radius-reviewed-corpus-v2-verified/`, corpus ID `live-detect-20261101-black-cue-development-v4-o09-reviewed-radii-v2`. It preserves the O01 manifest, sample/index/labels, receipt and split-lock path and remains Development-only. It does not introduce a parallel dataset format.

- Manifest SHA-256: `347c4342a1ba184d6f3dde39c7a488d839d2cd00b34df5ab762bf1ec0ad65322`.
- Split-lock SHA-256: `e47ae7f7904a8cc37863a89ccddb4db90784d41e78f05468c5bbad37f3a9827e`.
- YOLO export report SHA-256: `d653d94f655b28ef3a64109ead9a633ab81cc71b0dc03f9ae28e6a0e4b409b04`.
- Radius review record SHA-256: `8103b98b5de5091b1902efb629e3024899a49e23ef4e9b9e92bd5c3cc739a3ee`.
- Expansion provenance SHA-256: `a2b8d42ab2a4f02b4c66ee28dcb69ae746205f01596a014e944397662b511c53`.
- Criteria-identical acceptance contract v2 SHA-256: `435af789cb20785286f147c52c4098bc9edb81a30f58e3cc305360ba3e4c1057`. No gate criteria changed; the new Development split is frozen before the next candidate check. Holdout exposure remains zero.

For every added frame, the builder binds the displayed guide to the exact source image hash, source-label hash, reviewed centre, per-frame user-review record and raw capture receipt. The source image and sibling capture receipt agree on frame ID, source PTS, timeline, dimensions and pixel hash. The receipts have identical ordered calibration corners; the already verified `SkyEye.Core.TableCalibration` matrix is reused only because those corner coordinates match. Calibration identities are kept separate, and no unchanged-camera-pose claim is inferred from equal corners.

Visible radii use `radiusX = radiusSourcePx / width` and `radiusY = radiusSourcePx / height`. The absent B/C rows retain user-reviewed expected locations and no radius; chalk is excluded. These are apparent source-image radii, not physical cloth-plane radii or a numeric radius-accuracy score. Centre reviews remain approximate and have no agreed numeric matching tolerance.

## Targeted checks

| Check | Result |
| --- | --- |
| Source-image, centre-label, review-record and receipt immutability | **PASS 32/32 unchanged** |
| Frame-specific review binding and transform round-trip | **PASS 112/112 ball/location rows**; maximum allowed source-pixel residual `1e-6 px`, no row exceeded it |
| O01 Workbench `validate` | **PASS**, exit 0; 17 real still regressions; `accuracyGateReady=false` as expected |
| O01 Workbench `freeze` | **PASS**, exit 0; new lock binds the exact v2 manifest |
| O09 `export-yolo` | **PASS**, exit 0; 5 setup groups remain intact; no Holdout is exported |
| O09 radius export round-trip | **PASS 17/17 samples**, **17/17 image hashes**, **17/17 PNG bytes**, **264/264 boxes**, **5/5 setup groups** |
| Acceptance coverage | **INCOMPLETE**: 0 Holdout, 0 measured lighting strata, 0 request windows, 0 unsafe windows; D1/D2/E remain **0/3 proven** |

The current shared metrics are separate: software suites **3/3 carried forward**, focused motion-feed **20/20 carried forward**, detector inventory **0/3 pass**, physical gates **0/3 proven**. No broad suite, detector replay, training, device install or capture ran in this work cycle. At the start of the cycle, mounted-camera `/status` was freshly read as `Stopped` at revision 64; it remained untouched. The companion Home screen was not rechecked.

## Builder attempts and rerun rule

The first builder attempt assumed the source manifest and `images/` folder shared a directory. The saved structure puts payloads under `source-input/`; no source file changed. After correcting that path, a second attempt found the temporary source-input manifest omits the expected-empty point IDs from its point list. The builder now binds these points from the per-frame user-review record and checks their coordinates/visibility against the source labels. The final generation and focused checks above passed. These were data-builder orchestration corrections, not detector failures.

Reuse this result unless a guide, source image/receipt, review record, corpus format, split planner, exporter or verifier changes. Do not train or score on the same data as Holdout. The next test is `O09-TRANSFER-NEW-CONTROLS-PROBE`: run the already frozen transfer checkpoint only on the seven newly reviewed frames; no retraining, threshold changes or Holdout access. Report per-frame class-aware matches, false positives, misses and complete layouts, while keeping the photometric exclusions and one-group correlation visible.

## Gate status

This closes the data-preparation portion of the O09 expansion target, not Live Detect reliability. The existing trained candidates remain failed Development baselines. No learned candidate is accepted for play testing. Stable playable lighting, varied ball arrangements, separate lighting strata, session-separated Holdout, installed-phone D2 evidence, and then the predeclared event/clip sequence remain outstanding. The goal stays open.
