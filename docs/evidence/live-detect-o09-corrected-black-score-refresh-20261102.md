# O09 corrected black-label score refresh — 2 November 2026

## Result

**`O09-CORRECTED-BLACK-LABEL-SCORE-REFRESH`: PASS** for refreshing the saved spatial score and saved-stage distances against the accepted black-centre corrections. The detector output and the earlier stage trace are unchanged; the original v1 score and labels remain preserved. No detector replay or live capture occurred in this test.

The user accepted B at source pixels `(160, 858)` in frame 962 and `(160, 859)` in frame 2263, with apparent radius 22 source pixels for those two balls. The versioned centre review SHA-256 is `5510c528ad8e8b070aeb20783b6639a1e0f9c05c26c18553b3cf35f0ab293613`; it supersedes v1 review SHA-256 `e3f348bd22b14bb696f7f79ff7c9631c3c14b6fd7266155663e39eed25037ab6`. No other centre or expected-empty point changed. Other Layout03 rim guides remain pending.

The corrected points were mapped with the receipt-bound `SkyEye.Core.TableCalibration.Map` and checked with `Unmap`. Source-pixel round-trip residual was 0 px for both corrected centres. A copied Development-only score corpus and split lock were created; corpus v1/v4 and prior reports were not changed. The 0.010 table-2x1 matching tolerance remains an association setting, not an agreed centre-accuracy tolerance.

## Refreshed saved-output score

Input detections are the exact stored width-800/.65 outputs from analyzer source checkout `8dfd6a97503bac07085562ff9ffa6ffba0d1581a`; detector pixels, receipts and output bytes are reused unchanged. Workbench validation and freeze succeeded. `DetectorBaseline.Score` scored **4/4** reviewed frames with **0 unscored**:

| Frame / state | Visible | Detections | TP / FP / FN | Correct colour | Recall | p95 centre error (table-2x1) | Uncertain |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| 2083 / both present | 16 | 15 | 15 / 0 / 1 (B) | 15/15 | 93.75% | 0.002617 | true |
| 957 / black absent | 15 | 15 | 15 / 0 / 0 | 7/15 | 100% | 0.004572 | true |
| 962 / cue absent | 15 | 14 | 14 / 0 / 1 (B) | 14/14 | 93.33% | 0.003483 | true |
| 2263 / both restored | 16 | 15 | 15 / 0 / 1 (B) | 7/15 | 93.75% | 0.003359 | true |
| **Total** | **62** | **59** | **59 / 0 / 3** | **43/59** | **95.16%** | per-frame above | **4/4 true** |

The refreshed counts, matched identities, colour outcomes and centre-error percentiles equal the prior score. The corrected black remains a false negative in all three black-present frames; there is no false positive in the black-absent frame. Colour correctness is 43/59 (72.88%). The score did not use the two newly reviewed radii because corpus radius fields remain unset; no radius-normalized accuracy is claimed. All four frames are spatial Development evidence, not a photometrically matched causal control, full inventory acceptance, Holdout, D1, D2 or play evidence.

## Corrected-point stage-distance refresh

Distances below are recalculated from the accepted B points to proposal/fit centres already stored in the exact-source stage trace. Boundary decisions and support counts come from the saved trace and were not re-run.

| Frame | B point shift in width-800 analysis space | Nearest saved seed distance to corrected B | Saved boundary-fit result |
| --- | ---: | ---: | --- |
| 962 | 13.515 px | 88.492 px (was 93.331 px) | All 12/12 ranked fit attempts had no boundary fit. |
| 2263 | 11.738 px | 45.817 px for the nearest saved seed | The one fitted candidate is 10.620 px from corrected B (was 10.801 px), with 15/32 sectors; the unchanged floor is 21/32. Eleven other ranked fits had no boundary fit. |

This correction does not change the measured failure interpretation: frame 962 still has no boundary fit near the corrected point, and frame 2263 still rejects its nearest fit below the fixed support floor. It is not evidence to lower that floor. Frame 2083 remains a black miss without a nearby fit. The four-state photometric audit remains unmatched, so the states do not establish a causal black-present/absent effect.

## Verification and artifact identities

- Workbench `validate`: exit 0; four real still-regression frames; `accuracyGateReady=false`.
- Workbench `freeze`: exit 0; corrected derivative split lock created.
- Workbench `baseline --tolerance 0.010`: exit 0; 4/4 scored, 0 unscored.
- Corrected-point distance refresh: exit 0; original trace SHA-256 remains `d79a19fe407373c6dfb2d2ab1cc3efdeee0deb114c2d24197bae1184c8eb6152`.
- An initial compile of the scratch refresh helper failed with `CS9348` before producing artifacts; the invalid top-level property was changed to a local value. The helper then compiled and completed. This is a harness correction, not a detector/test failure.
- Camera `/status` read at the start of this offline work returned `Stopped`, revision 62. No capture or device change occurred.

| Artifact | SHA-256 |
| --- | --- |
| Corrected corpus manifest | `e7781506f40d110d08353d87b7244d8820af9e85b36e825b7b561db02518df77` |
| Corrected derivative split lock | `99f57856088b608a7b8064173a197220f9ea69caa681a36fa48bc594fc83fb84` |
| Workbench validation | `08229876b5deab0fc45982bd5aff9d34ed2c132ff88bcb524c64c01f022d2fc7` |
| Reused stored detections | `4df2a7b1ff064e3380aed8e1d1254b420198712003c20b1eabfa5331b0207df3` |
| Refreshed baseline score | `a2b52d7248888f22d16f7eddc5d3adbbbca747f02010f7ded5b44f3c3282e08e` |
| Corrected-point distance refresh | `e5a43d5b3a878fc01bcbc0105a4a7db50faf9d071915b24f6e0390021262d8fc` |
| Source-point mapping audit | `9d23eb95a96e5a5e6f9d2e7c14ff74c6fbbda48ec33e52da5d206692b3068642` |

Ignored detailed outputs are under [`work/o09/development-v3/reviews/layout03-corrected-black-score-refresh-20261102/`](../../work/o09/development-v3/reviews/layout03-corrected-black-score-refresh-20261102/). The bounded helper source and exact Workbench inputs are retained there.

## Goal status and next test

No acceptance gate changed: D1, D2 and E remain **0/3 proven**. The frozen D2 matrix remains **0/200 supported request windows, 0/100 held-out unsafe windows, and 0 completed 30-second soaks**. Criteria-complete lighting strata remain **0/2**, and there is no accepted Holdout result.

Next test: **`O09-CURRENT-SOURCE-LAYOUT03-POST-DUPLICATE-REPLAY`**. The scored outputs above predate the exact fitted-duplicate publication fix in `VisionAnalyzer.cs`. Replay the same four saved pixels with the current source and fixed recorded analyzer configuration, then score those outputs against this corrected v2 label derivative. This closes the source-identity gap without new capture; it does not replace the missing independent layouts, lighting sessions, Holdout, or D2 windows.
