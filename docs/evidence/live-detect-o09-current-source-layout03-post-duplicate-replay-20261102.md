# O09 current-source Layout03 post-duplicate replay — 2 November 2026

**Test:** `O09-CURRENT-SOURCE-LAYOUT03-POST-DUPLICATE-REPLAY`  
**Result:** replay and scoring completed; complete-inventory screen **FAIL**. This closes the source-version gap for the four reviewed Layout03 frames. The duplicate-publication fix produced the same detector IDs, colours, centres and counts as the stored pre-fix outputs on these pixels. It did not recover the black.

## Replay identity and scope

- Detector checkout: `da161891c8a0f813249994f912428f3efd8f9b09`, including source fix `432c0a13142173eb7774f451ef714cc15717ec91` (`fix: publish exact fitted ball geometry only once`). The isolated Vision Device APK was built from this checkout; SHA-256 `e405c1cb1d3dce9d58eb53eab314229d7bd8728136242867eb6627f2caa44e62`.
- Device path: Pixel 11 Pro XL `66181FDDV001HS`, isolated package `uk.co.skyeye.visionchecks`. The runner installed only that package in place. It did not install the production companion or camera package.
- Four serial, single-frame `full-audit` runs used the receipt-bound saved PNGs. They cover four separate source timelines, so each was replayed in its own run. Width-800 outputs were scored; the captured primary analyzer configuration matched in all four reports (`configurationMatchesPrimary=true`, settings match true). Analyzer configuration identity SHA-256: `6d4e4f3e2fc28bba1a56c637f6783e1d743e082d7807362324344a102fefa414`.
- Replay settings were explicit: edge margin 23, pocket radius 48, boundary support 0.75, sparse-colour support 0.625, packed-colour and production width-800 extents 0.65, sparse-black extent 1.3, sparse-black support 0.65625, 12 ranked fits, radius-first ranking, minimum peak radius factor 8; no illumination normalization or ablation. No separate analyzer warm-up was applied. Recorded wall times are single saved-image analysis measurements, not Live Detect request-to-ready or source-age measurements.
- Frames 962 and 2263 have `detectionRequested=false` in their immutable source receipts. The `full-audit` branch analyzed their exact saved pixels and retained those false receipt flags. This is a current-source spatial baseline, not an O08 screening export or a physical capture.
- Workbench coordinates use the existing replay summary transform directly: `tableX = analysisY / 800`, `tableY = 1 - analysisX / 800`. The corrected-black v2 Development corpus and its split lock were validated/frozen. Matching tolerance remains 0.010 table-2x1 and is only an association setting; no radius-normalized score or numeric label-accuracy claim is made.
- This four-state group is not a photometrically matched causal control. It remains Development evidence; no Holdout was opened.

## Spatial score

`DetectorBaseline.Score` scored **4/4** labels with **0 unscored** at the declared 0.010 table-2x1 tolerance.

| Frame / reviewed state | Visible | Detections | TP / FP / FN | Colour correct | p95 centre error, table-2x1 | Uncertain |
| --- | ---: | ---: | --- | ---: | ---: | --- |
| 2083 / black and cue present | 16 | 15 | 15 / 0 / 1 (B) | 15/15 | 0.002617 | yes |
| 957 / black absent | 15 | 15 | 15 / 0 / 0 | 7/15 | 0.004572 | yes |
| 962 / cue absent | 15 | 14 | 14 / 0 / 1 (B) | 14/14 | 0.003483 | yes |
| 2263 / both restored | 16 | 15 | 15 / 0 / 1 (B) | 7/15 | 0.003359 | yes |
| **Total** | **62** | **59** | **59 / 0 / 3** | **43/59** | — | **4/4** |

Pooled precision is **100%** and recall **95.16%**. Black recall is **0/3** black-present frames; the black-absent frame produced no black false positive. All three black-present outputs miss B. The absent-black frame has seven red and eight unknown circles, which preserves spatial matches but leaves colour errors. Every frame remains uncertain; none is obstructed. The score corpus has no reviewed numeric radii for these matches.

The full-audit sparse-black traces show 12 fit attempts per frame: frame 2083 had 12/12 `rejected-no-boundary-fit`; absent-black frame 957 had 11/12 with no fit and one 14/32-sector fit rejected for insufficient support; cue-absent frame 962 had 12/12 with no fit; frame 2263 had 11/12 with no fit and one 15/32-sector fit rejected for insufficient support. The production support floor remains 21/32. The absent control also has a weak sub-threshold fit, so this result does not justify lowering the floor.

## Comparison with the stored pre-fix output

Compared with the exact-pixel score refresh from checkout `8dfd6a97503bac07085562ff9ffa6ffba0d1581a`, all four frames have identical ball IDs/counts, colours, and table-coordinate centres (zero changed centres; zero added or removed IDs). Both scores are **59 TP, 0 FP, 3 FN**, with **43/59** colour-correct matches and all four outputs uncertain. This shows the published-fitted-geometry fix has no effect on these selected frames; it does not prove the fix has no effect on other inputs.

This baseline therefore does not pass the complete-inventory gate. No threshold was changed and no detector candidate was promoted.

## Attempts, artifacts and device state

The first runner call used the parent directory instead of each per-state receipt folder and stopped during local frame lookup, before install or replay. Three exploratory Workbench invocations also failed before producing scores: the initial four-frame file exceeded Workbench's 500-character text-field bound because it embedded the full analyzer configuration in `provenance`; an isolation-only single-frame JSON-object file was rejected because the command requires an array; and the corrected single-frame array reproduced the overlong-provenance failure. The final four-frame file uses a bounded configuration hash, with the complete configuration retained in the replay audit. No source receipt, image or reviewed label was changed.

| State / frame | Run | Pixel SHA-256 | Receipt SHA-256 | Replay report SHA-256 |
| --- | --- | --- | --- | --- |
| Both present / 2083 | `l03cur2083-215925` | `731ce91d061f12deadb568ddc85f848607ecd2f45b92e42f8388fa96eaf4a3fc` | `e5c13a341ec3d92b2073617a1d2aa0ccd4be58b37150bce436915b364817b249` | `c2c08c348c2e5c09777d8fd223bb8d28d521d3660565e7e7e9145c729a11cb21` |
| Black absent / 957 | `l03cur957-215908` | `04706a3e6d8cebd79931587e612f25c210e9a2a7d3329c89fbbf1ecb3877f3c1` | `de57d7c6c4f392e4b02c17c0de67ec39254d7974a0f538dc4e1d944db54cc94b` | `d630e5dd37a3c6143f241da57592604f4d60327f3329681bbaaff88789cc8f0b` |
| Cue absent / 962 | `l03cur962-215918` | `4d328c94165525086f141e0333e522a88b76a024496cff3306c63c8ed260cb53` | `c26c4341e654e208a30411873235030e05f9dea06ec2220c52a35b20e76c2bcd` | `4dba3a8d9264adc7d23aaeabc35d78a77bc0c349f169580aa118c19c0a8493c2` |
| Both restored / 2263 | `l03cur2263-215934` | `b256ed2824528844638c9d505dea4e02384f152ab26096c7fc8f3527c89eb6d0` | `69604a67d97af5709b88917318c5434ac7ed75fff7ff964145b3618e8d35d125` | `e83601b0476ef29025e6d892b6865775be722ac60d83b615ed38898d89bd4dd1` |

Machine-readable replay and score artifacts are under [`work/o09/development-v3/reviews/layout03-post-fix-current-source-20261102/`](../../work/o09/development-v3/reviews/layout03-post-fix-current-source-20261102/): replay audit `d59c0896fc7fd4e76e98d6b5772ab6dbe43fe94cbcdaf3ebafc3a4fe2c86f274`; detections `db9d2f2d7eb0da484196f5a1bd16d7904438bb8ac079c79214cd21dcf2314a51`; baseline score `0edfcb235e22538c6613d0881d53ea65064c68962378812949ac3bc0509f3f51`; validation `08229876b5deab0fc45982bd5aff9d34ed2c132ff88bcb524c64c01f022d2fc7`; split lock `99f57856088b608a7b8064173a197220f9ea69caa681a36fa48bc594fc83fb84`. Corrected v2 corpus manifest SHA-256 is `e7781506f40d110d08353d87b7244d8820af9e85b36e825b7b561db02518df77`.

After replay, mounted-camera `/status` returned `Stopped`, revision 62, 58,052 completed frames and 139 clips; each run manifest also records Stopped before and after. A fresh Pixel screenshot SHA-256 `878cffe9a52f6650293ca1065e59095c7c3ae419abdf98dec8fef0c116c9474b` shows companion Home, `Samples off`, and `Start live`. Its displayed `Saved 1 of 5 ... Unsettled (SampleGap)` result is an older 20:00:53 run, not this replay. Capture remained stopped; no clip was created, deleted or modified.

## Gate status and next work

This test closes only the source-identity replay and spatial-score checkpoint. Detector inventory acceptance remains **0/3**; D1, D2 and event/clip acceptance E remain **0/3 proven**. The frozen D2 denominator remains 0/200 supported request windows, 0/100 held-out unsafe windows and 0 completed 30-second soaks. Criteria-complete lighting strata remain 0/2; Holdout remains unopened. The score is not stillness or readiness evidence.

**Next exact test:** `O09-DEVELOPMENT-REMAINDER-BATCH`, beginning with the warm/train Layout02 blind anchor after the user reopens the table and confirms stable playable lighting. The current table was closed for the night; keep capture stopped until that physical prerequisite returns. Its reference sheets are already ready. Do not spend the D2 matrix on this known-incomplete detector.
