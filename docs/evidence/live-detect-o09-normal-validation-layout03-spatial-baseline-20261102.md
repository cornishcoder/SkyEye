# O09 normal-validation Layout03 spatial baseline — 2 November 2026

## Target and scope

Target: `O09-NORMAL-VALIDATION-LAYOUT03-SPATIAL-BASELINE`. This scores four independently reviewed source frames from the normal-validation Layout03 group as Development data. It does not establish a matched-light black/cue effect, D1 inventory acceptance, D2 stillness/readiness, Holdout performance, or play readiness.

Checkout: `8dfd6a97503bac07085562ff9ffa6ffba0d1581a`. The source review was frozen before detector outputs were opened; its SHA-256 is `e3f348bd22b14bb696f7f79ff7c9631c3c14b6fd7266155663e39eed25037ab6`. The user accepted visible centres and expected-empty locations in frames 2083, 957, 962, and 2263. Apparent radii remain provisional; numeric centre tolerance is null.

## Device and execution provenance

No live capture or detector replay was started in this continuation step. The four exact-source replays already present in this evidence cycle were retained and scored; the Android job was not repeated. Current readback at `2026-11-02T19:20:42.4403420Z` showed mounted-camera `/status` **Stopped**, revision 58, timeline `8fec1bb48f5f412898534d62d2ce58c0`, 57,125 completed frames, 1920×1080 at 30fps, 139 clips, and no reported viewers or host. Pixel 11 Pro XL `66181FDDV001HS` was connected to ADB. Companion Home showed enabled `Start live session`; `Capture verifier frames` was disabled. The visible `Unclear (TimedOut)` text belongs to an earlier run at 16:47:10 UTC and is not a result of this test. No clip or app data changed.

The installed isolated package `uk.co.skyeye.visionchecks` was pulled read-only from the connected phone and hashed to `e864d9333de952ddf1b1a52cb2142b15ef3cd839e795f03eb04caa786adf457a`, matching the replay APK. The prior Release build recorded 0 errors and 23 warnings. Production companion and mounted-camera packages were not updated.

All four replay receipts bind source-sized 1920×1080 pixels, exact source PTS, calibration and analyzer configuration. The recorded/replay configuration comparison is true on all four. The analyzer identity is `body-priority-packed-colour-peaks-v25-neutralceiling94-edge23-pocket48-sparse20-review75-redyellow-black-radius13-support21`; settings are width 800, radius 18–35, separation 45, morphology 5, Hough dp 1.3, edge 150, initial/normalized votes 15/14, hue band 7, pocket exclusion 48, illumination normalization off/kernel 151, fresh cue recovery and body proposal priority on, Hough/sparse-colour/sparse-black support 0.75/0.625/0.65625, sparse-black extent 1.3 with radius-first ranking on, dark-body ceiling 40, verification refinement off, packed edge margin 23, and packed-colour extent 0.65. The full configuration identity is retained in the replay audit.

| State | Frame / source PTS (µs) | Replay run | Source pixel SHA-256 | Receipt SHA-256 | Replay report SHA-256 |
| --- | --- | --- | --- | --- | --- |
| Both present | 2083 / 22154088911 | `l03v2083190831` | `731ce91d061f12deadb568ddc85f848607ecd2f45b92e42f8388fa96eaf4a3fc` | `e5c13a341ec3d92b2073617a1d2aa0ccd4be58b37150bce436915b364817b249` | `661475c1a9005d9f5c4b216b6cf3cc8d8d2d7d851d111e1c630e3b8a13b79c5f` |
| Black absent | 957 / 22560991255 | `l03v957190831` | `04706a3e6d8cebd79931587e612f25c210e9a2a7d3329c89fbbf1ecb3877f3c1` | `de57d7c6c4f392e4b02c17c0de67ec39254d7974a0f538dc4e1d944db54cc94b` | `2d9a9897584516eb29cc859b2482b3d282b9e0bb40920652380d04a8d64ab92d` |
| Cue absent | 962 / 22674567877 | `l03v962190831` | `4d328c94165525086f141e0333e522a88b76a024496cff3306c63c8ed260cb53` | `c26c4341e654e208a30411873235030e05f9dea06ec2220c52a35b20e76c2bcd` | `ea631a48788a84d0678a5d137d6654a59c97f2dbaaaf9ea7ec4418c323d1ff47` |
| Both restored | 2263 / 23037458944 | `l03v2263190831` | `b256ed2824528844638c9d505dea4e02384f152ab26096c7fc8f3527c89eb6d0` | `69604a67d97af5709b88917318c5434ac7ed75fff7ff964145b3618e8d35d125` | `c5709d8743cc37f20213c611e05ce5246339e0e9cf89a88644be8a3daddee072` |

The replay audit is SHA-256 `5f411851784887ec0267a37811e56c1e1a548cead6885588fff10efdb40d1174`. It records the exact-settings match and per-frame uncertainty/layout-uncertainty/obstruction fields. A first both-present replay `l03v2083190727` is excluded because its replay identity omitted `SparseBlackRankByRadius=true`; the corrected run above is the scored run.

## Transform verification

The source-to-table and table-to-source mapping uses the existing `SkyEye.Core.TableCalibration.Map` and `Unmap`, not a second transform implementation. The transform audit binds each receipt's four copied normalized-image corners in TL, TR, BR, BL order, its calibration ID and 1920×1080 dimensions. Source-pixel coordinates use origin at top left with +x right/+y down; normalization divides by receipt width/height; the calibration maps to table-2x1 (x 0–2 across table width, y 0–1 along table length). Each reviewed visible point and expected-empty point was mapped and round-tripped; each scored table-2x1 detection was inverse-mapped to source pixels. Corpus visible/absent labels were checked separately, and their mapped coordinates agree with the frozen reviewed points to the helper's 1e-9 consistency assertion. The maximum source-pixel round-trip residual over 64 review points was `5.084229945850415e-13` px. This is floating-point transform consistency, not a centre accuracy tolerance.

The four source-image overlays use green crosses for reviewed visible centres, magenta X marks for inverse-projected detector points, and yellow diamonds for expected-empty points. They are review aids, not new labels.

- [Frame 2083 transform overlay](../../work/o09/development-v3/reviews/layout03-validation-spatial-baseline-current-source-20261102/transform-overlays/frame-2083-transform-overlay.png), SHA-256 `60ce4461f8cc5a9f131e24635e0b039b0f22f379613dd3483cab2d4c68f6cfc0`
- [Frame 957 transform overlay](../../work/o09/development-v3/reviews/layout03-validation-spatial-baseline-current-source-20261102/transform-overlays/frame-957-transform-overlay.png), SHA-256 `2a15191b841e92743619f585f30a0422a36443f1c5c859083897fc1f0e71838d`
- [Frame 962 transform overlay](../../work/o09/development-v3/reviews/layout03-validation-spatial-baseline-current-source-20261102/transform-overlays/frame-962-transform-overlay.png), SHA-256 `40709c1cd24daeef7c6f85f9fbfab1c09db242a8e03645071a11c6c3ba4bbc20`
- [Frame 2263 transform overlay](../../work/o09/development-v3/reviews/layout03-validation-spatial-baseline-current-source-20261102/transform-overlays/frame-2263-transform-overlay.png), SHA-256 `04b8f30c8dbd59e14b0ec86f52ce281aa3b25768ce519b6c366f519b5f065b11`
- Transform JSON SHA-256 `b51ae1de44e573bc0516c660bf9cfe2052d6637c453a1a6715ec10d247d7e45c`; overlay manifest SHA-256 `b299b5034a7771c0bf9ad58107ab74fa576d463bdab20fd8f5c7ba2747b17d6a`

## Baseline score

`DetectorBaseline.Score` scored all **4/4** frozen frames with **0 unscored**. The explicit 0.010 tolerance is only the table-2x1 association tolerance used by this score; it was not reviewed as an accuracy criterion. Expected-empty B?/C? points are not counted as visible truth. No radius was used.

| State / frame | Visible truth | Detections | TP / FP / FN | Precision | Recall | Colour-correct matched | p95 centre error (table-2x1) | Misses | Uncertain / layout uncertain / obstructed |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- | --- |
| Both present / 2083 | 16 | 15 | 15 / 0 / 1 | 100% | 93.75% | 15/15 | 0.002617 | B | true / false / false |
| Black absent / 957 | 15 | 15 | 15 / 0 / 0 | 100% | 100% | 7/15 | 0.004572 | none | true / true / false |
| Cue absent / 962 | 15 | 14 | 14 / 0 / 1 | 100% | 93.33% | 14/14 | 0.003483 | B | true / false / false |
| Both restored / 2263 | 16 | 15 | 15 / 0 / 1 | 100% | 93.75% | 7/15 | 0.003359 | B | true / true / false |
| **Total** | **62** | **59** | **59 / 0 / 3** | **100%** | **95.16%** | **43/59 (72.88%)** | per-frame values above | **B missed in all 3 black-present frames** | **4/4 uncertain** |

Spatial scoring finds the visible cue centre in all three cue-present states, but the analyzer classifies it as cue in only one; in two frames it is `unknown`. The black is missed in all 3/3 black-present frames and produced no black false positive in the one black-absent frame. The two black-absent states are not photometrically matched to their present siblings: the separate five-patch audit found median absolute shifts of 7.51, 6.29, and 8.93 luminance levels and a maximum of 18.73. Therefore this is not a causal matched-control result.

The score does not pass inventory acceptance: all frames remain uncertain, three visible black labels are missed, and per-frame full-layout recall is below a correct unique inventory. D1, D2, and E remain **0/3 proven**. The score reports stored detector outputs; it is not a new live stillness measurement.

## Input and harness failures retained

These failures occurred before or during score-input preparation and are not detector failures. No detector replay was repeated to fix them.

- The first score-corpus helpers emitted pretty JSON in JSONL and resolved `pixelFile` relative to the sequence folder; Workbench validation rejected the inputs. Corpus-v4 uses one JSON object per line and corpus-root-relative pixel paths, and validation passed.
- A score-input build failed because a local `Sha` helper was overloaded; it was renamed to distinct byte/file helpers. A following attempt deserialized receipt-only `analyzerPixelSource` into the narrower indexed-frame type; the builder now selects the typed indexed-frame fields explicitly.
- The first Workbench score input had provenance text length 540–541, above the 500-character schema maximum. A score-ready copy reduced it to 471–472 characters; the separate replay audit preserves the full receipt/report/configuration provenance.
- The first transform audit compared the total corpus label-array length with visible review points. It failed on frame 957 because expected-empty B? is correctly stored with `visibility: Absent`. The corrected audit checks visible and absent labels separately and passes without changing source labels or score inputs.
- The initial both-present replay is excluded for missing `SparseBlackRankByRadius=true`; corrected exact-configuration output is the only scored arm.

## Artifacts and repeat policy

- Scratch validated corpus: `score-corpus-v4/manifest.json`, SHA-256 `aab748465b996470bfb929714809f408c56df945027f08f37caf06429c8c7ad0`; scratch split lock SHA-256 `01845d15d07e36c18f1564f270105ab07c237f70d31e6420c9011c8963715d65`.
- Workbench validation SHA-256 `08229876b5deab0fc45982bd5aff9d34ed2c132ff88bcb524c64c01f022d2fc7`.
- Score-ready detections SHA-256 `4df2a7b1ff064e3380aed8e1d1254b420198712003c20b1eabfa5331b0207df3`.
- Workbench score SHA-256 `de03e9fac543483cc78ea2aa5cfd338ef172c2517b77f07c90980ec42128d356`.
- Device safe-state JSON SHA-256 `8284fc2937d2a607aee679158d8003d0e9fd3c354749847028f04fb96a56097d`.

This target is complete and should not be repeated unchanged. Reuse its exact reports, score and source overlays. Next target: **`O09-LAYOUT03-EXACT-STAGE-FAILURE-TRACE`** on the four saved frames; trace the black misses and cue/yellow classification changes through existing stages, keep thresholds fixed, and record detector-output parity. If the trace does not identify a measured mechanism, move to the next independent offline target without tuning. No daylight capture is planned.
