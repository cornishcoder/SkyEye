# O09 Layout03 exact-stage failure trace — 2 November 2026

## Result

**`O09-LAYOUT03-EXACT-STAGE-FAILURE-TRACE`: PASS for bounded saved-report tracing; detector inventory remains FAIL.** This offline trace read four already-completed, exact-configuration width-800 full-audit reports from source checkout `8dfd6a97503bac07085562ff9ffa6ffba0d1581a`. It did not run a detector replay, change a threshold, or contact the device. Saved scoring outputs matched each replay's ordinary circle output on **4/4 frames**, with identical IDs/kinds and zero table-coordinate delta.

The spatial baseline remains **59/62 TP, 0 FP, 3 FN; 100% precision and 95.16% recall**. Its three false negatives are the black in each of the three black-present states. The absent-black frame has no false black detection, but this four-state sequence is photometrically unmatched and does not establish a causal matched-control result. All four analyzer outputs remain uncertain.

## Frame-level trace

| Frame | Reviewed state | Black stage evidence | Cue/colour path |
|---|---|---|---|
| 957 | Black absent; `B?` is an expected-empty point | Nearest ranked appearance seed was 49.8 width-800 analysis px from the empty point. Its nearest fit was 32.3 px away and rejected at 14/32 support. No black false positive. | Cue recovery found no unique first-pass cue. Raw labels were 8 yellow + 7 red; the inventory cap conservatively changed the eight yellows to unknown. |
| 962 | Cue absent; black present | All 12 ranked fits failed to produce a boundary fit. The nearest ranked seed was 93.3 px from the reviewed black centre. Black was missed. | Expected cue absence; raw/final labels were 7 yellow + 7 red. |
| 2083 | Black and cue present | All 12 ranked fits failed to produce a boundary fit. The nearest ranked seed was 381.1 px from the reviewed black centre. Black was missed. | Cue geometry was retained for the current frame; labels were cue + 7 yellow + 7 red. |
| 2263 | Black and cue restored | Nearest proposal was 37.3 px from the reviewed black centre; its fitted centre was 10.8 px away. The fit had **15/32 (0.46875)** supported sectors versus the unchanged **21/32 (0.65625)** minimum, so it was rejected. The audit records `visible=15/32`, zero exterior-rejected sectors, zero fitted-neighbour explanation and zero fitted-body-occlusion explanation. This is a plausible support-gate rejection, not an accepted centre or a reason to lower the gate. | No unique first-pass cue was found. Raw labels were 8 yellow + 7 red; the inventory cap changed the eight yellows to unknown. |

The `seed-centred ball-size appearance probe` can classify an appearance sample as black, but that probe alone does not add a ball. In frame 2263, the nearby boundary fit still fails the independent support gate. In frame 957, a black-like seed and a low-support fit occur away from the expected-empty point; neither becomes a black output. Do not equate appearance classification, a nearby fit, or fit-centre distance with reviewed centre accuracy.

## Provenance and method

The existing mapping is `AnalysisCoordinates.ToTable(p) = (p.Y / Width, 1 - p.X / Width)`. The trace used its inverse for the reviewed source labels at width 800: `analysisX=(1-tableY)*800`, `analysisY=tableX*800`. It compared the saved score detections with the existing full-audit circles before reporting stage evidence. No label tolerance or ball-radius accuracy bound was inferred.

| Artifact | SHA-256 |
|---|---|
| Reviewed source labels | `e3f348bd22b14bb696f7f79ff7c9631c3c14b6fd7266155663e39eed25037ab6` |
| Score inputs | `58b51957ce30bba21b8e4be2425ebaa4ea1ce04f39709fecfb551cbbdbd07fc9` |
| Saved detections | `4df2a7b1ff064e3380aed8e1d1254b420198712003c20b1eabfa5331b0207df3` |
| Baseline score | `de03e9fac543483cc78ea2aa5cfd338ef172c2517b77f07c90980ec42128d356` |
| Replay audit | `5f411851784887ec0267a37811e56c1e1a548cead6885588fff10efdb40d1174` |
| Stage trace JSON | `d79a19fe407373c6dfb2d2ab1cc3efdeee0deb114c2d24197bae1184c8eb6152` |

Source hashes for `AnalysisCoordinates.cs`, `BallClassifier.cs`, `VisionAnalyzer.cs`, and `PackedBallClusterRecovery.cs` are recorded in the ignored trace JSON. Its four sample rows also bind exact source PTS, pixels, receipts, replay directories, and full-audit report hashes. The trace is at `work/o09/development-v3/reviews/layout03-validation-spatial-baseline-current-source-20261102/stage-trace.json`; extraction script SHA-256 is `CE97489E09E5E53EE578AC38F4823DDCC9EF8EFCAE50790E3240B0B754A45F9B`.

## Interpretation and next work

This trace identifies two separate black failure modes in these saved frames: no boundary fit near the reviewed point in frames 2083 and 962, and a plausible nearby fit below support in frame 2263. The cue/yellow path also shows why a missed cue can appear as eight yellow labels before the inventory constraint turns that conflicting group into unknown. These observations narrow Development diagnosis; they do not establish a safe geometry change or solve lighting/session transfer.

The next planned item is **`O09-DEVELOPMENT-CANDIDATE-COMPARISON`**: first check whether distinct, real candidate outputs can be produced on the identical Development samples without changing the frozen 0/7 transfer probe or opening Holdout. Any comparison must retain exact sample coverage and measure Pixel runtime/resources. If no admissible candidate exists, record that precise blocker and take the next independent offline action. Do not lower the 21/32 support floor from this unmatched four-frame evidence. No further daylight capture is planned.

Two analysis-harness attempts failed before producing data: an exploratory summary had a mismatched bracket, then the first extractor expected a field absent from the reviewed-label file. The corrected extractor used the transform report's source review points and completed successfully. These are harness corrections, not detector failures; no source data or prior reports were changed.

## Device state

No device action or live capture occurred during this trace. A fresh read at `2026-11-02T19:33:49.5944976Z` reports camera `/status` **Stopped**, revision 58, 57,125 frames, 139 clips, zero viewers, and no host. Pixel 11 Pro XL (`66181FDDV001HS`) is connected to ADB; a fresh screen capture shows companion Home with **Start live** and **Samples off**. The screenshot SHA-256 is `744FE794020EF6EED86AD3A159E85B6C9C795CE2D2D5F1DF086237EBD6A552AD`. Token-free readback: `work/o09/device-state/exact-stage-trace-final-safe-state-20261102.json`.
