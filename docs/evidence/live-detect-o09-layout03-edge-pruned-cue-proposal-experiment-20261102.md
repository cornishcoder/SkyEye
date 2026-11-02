# Layout03 edge-pruned cue proposal experiment — 2 November 2026

**Target:** `O09-NORMAL-TRAIN-LAYOUT03-EDGE-PRUNED-CUE-PROPOSAL-EXPERIMENT`  
**Result:** PASS as a bounded proposal-generation screen; candidate is not accepted for integration because its refined centres drift from the reviewed cue point and the negative sample is small.

## Reused inputs and replay identity

This report reuses the four existing receipt-bound full-audit runs. Each source receipt belongs to a different recording timeline, so the runner correctly replayed each frame separately rather than combining unrelated timelines. Every report hash matches its run manifest and every run says `configurationComparison: exact-match`. The signed Vision Device APK recorded by all four runs is SHA-256 `EFB3E7EE4B30782D8A1697FAF18147D183818AB1632B690F7D76E2301E1F9625`. The subsequent local Release validation build succeeded with 0 errors and 23 warnings and produced APK SHA-256 `D2A73831C7B56D29942F0DD0B43DC8CF05FA5EC45036F2F1984D838092CDFC76`; this differs from the replay APK, so the saved results remain bound only to `EFB3E7…`. The full `SkyEye.Checks` run exited 0 with 69/69 named groups passing. Analyzer settings match the captured width-800 profile, including 0.65 packed-colour extent, 0.75 Hough support, 0.65625 sparse-black support and disabled illumination normalization. The diagnostic evaluates 1.7R-pruned components only; it does not add detections or change production thresholds.

| Frame | Reviewed state | Source pixel SHA-256 | Source receipt SHA-256 | Replay report SHA-256 |
| ---: | --- | --- | --- | --- |
| 1274 | Black and cue present | `56a2ff84523db8dffdecac7d53e9c75936f2b5efdf4b62e014a61aa07ad10d99` | `d4d980ff925d5965a3d2bea64d168f8170ec063bec7739d8b58ba326a5d55902` | `c31299bc11a2059491eff5bf3e84de31d9d5f794b05741b2daeab310152f7075` |
| 3360 | Black absent; cue present | `01631fc5dc122f077f93cf07949ce6afb556809d8b37eb0bff04d72cc8195dd8` | `6cd682539ee9e70ae4e9801846010b153130735ae28a07058e53a4a9fa73f710` | `1e57bc0d636e2ebf1989ab894be0795bd2a48af6ac299294c9492d7530f7c608` |
| 395 | Cue absent; black present | `dacc2a270864dfad5daaa6a858f27f8b48e337c4ea133d18629da26d88d2cec2` | `8f87349c05e4debbf4d5f172ffaf96bd578bf22cdf25d8df587c2d84c69950e7` | `26aecb8bbc95ffeec265628094e2cd2500f71f0ff633fe1c7d797cecdf6e7499` |
| 382 | Black and cue restored | `6e7be3dab3f31cfe62eec39b4bad75d69a5acdb25eb4a305402b14499e7f0093` | `e0e919de2f48a8b427507a3752ab9b95c6d36e73daafbad0fae4d837b39b4859` | `55ea81ed0751d1d22ba19483057ef9678b35418bd6d9e48e00141fde418c6098` |

Raw runs and manifests are under `work/o09/development-v3/reviews/layout03-current-source-baseline-v1/edge-pruned-cue-proposal-experiment-20261102/` (`cuefull1274-142309`, `cuefull3360-142346`, `cuefull395-142355`, and `cuefull382-142403`). The reviewed point supplied to each diagnostic was `(105.274832, 49.924167)` in the width-800 rectified analyzer image, corresponding to the user-reviewed cue centre/expected-empty point.

## Candidate evaluation

At 1.7R edge pruning, the three cue-present frames each formed one plausible geometry component. Each fitted candidate classified as `cue`, had local contrast, and cleared the existing general cue-recovery support floor of 18/32 sectors. The cue-absent frame formed no geometry candidate.

| Frame | Candidate | Boundary support | Component-centre distance to reviewed point | Refined-centre distance to reviewed point |
| ---: | --- | ---: | ---: | ---: |
| 1274 | One; cue; local contrast true | 20/32 | 3.96 | 8.08 |
| 3360 | One; cue; local contrast true | 21/32 | 3.86 | 6.94 |
| 395 | None | — | — | — |
| 382 | One; cue; local contrast true | 22/32 | 5.35 | 8.04 |

Distances are in rectified width-800 analyzer pixels, descriptive only. The fitted offsets were predominantly toward positive image Y; these centres have not passed a predeclared spatial-accuracy criterion. The separate direct reviewed-point diagnostic still reports 21/32 support in frame 1274, below its 24/32 Hough-probe threshold. The edge-pruned evaluator's `passes-neutral-cue-recovery-gates` label means it cleared the existing 18/32 recovery floor; it does not mean it cleared the separate 24/32 Hough threshold or a centre-accuracy gate.

The ordinary detector output remained unchanged in every replay: zero cue detections and zero neutral-body recoveries; all four results remained uncertain. Thus the diagnostic found a promising cue-present proposal signal on **3/3** present frames and no cue proposal on the single absent frame (**0/1** negative false proposal), but it did not itself recover the cue. The absent control and all positives come from one Development setup; this is not independent-session or lighting evidence, not a complete false-positive screen, and not D1/Holdout acceptance. Do not integrate the proposal as-is or reduce any support gate.

No new capture occurred. The current mounted camera read is `Stopped`, revision39, `clipCount=139`, 37,352 completed frames, 100% battery and zero viewers; the Pixel 11 is on companion Home showing **Camera ready** and **Start live session**. No match, Cast/TV action, clip deletion, app-data reset or camera-package update occurred.

**Next named test:** `O09-NORMAL-TRAIN-LAYOUT03-EDGE-PRUNED-CUE-SECTOR-DIRECTION-TRACE`. Retain the current candidate and labels; add diagnostic sector evidence to determine whether support deficits and positive-Y centre drift align with the calibrated cushion/pocket boundary. Replay only these exact receipt-bound Development frames, keep all production thresholds fixed, and do not open Holdout.
