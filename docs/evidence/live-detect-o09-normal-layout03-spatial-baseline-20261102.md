# O09 normal/train Layout03 current-source baseline and cue trace — 2 November 2026

**Status:** the Development baseline is complete and fails the complete-inventory screen. Its four selected frames yielded 56/62 reviewed balls, with every frame still uncertain. The trace and exact cue-point probe localize candidate-stage and boundary-support failures without changing detector code or thresholds. This is spatial Development evidence only; it is not a physical stillness result, Holdout, D1, D2, or play-test readiness.

## Frozen inputs and analyzer

The reviewed source-pixel labels are frozen in `work/o09/development-v3/reviews/layout03-control-raw-only/reviewed-centres-source-pixels-v1.json` (SHA-256 `249f77ed1154513e77b109ec7d9489f7e0a1a2efb4adefad942bb5127de75a2a`). The user accepted the visible centres and provisional apparent-rim guides for frames 1274, 3360, 395, and 382. Labels retain the previously disclosed aggregate Table-awareness count exposure during reconnect preflight; this is not a strictly blind review. The review did not set a numeric centre tolerance or establish cloth-plane accuracy.

The selected raw receipts say `detectionRequested=false`. A fresh Vision Device Release APK built from commit `2af8229ba6d25e982cdd702a045a513e84830342` succeeded with 0 errors and 23 warnings; APK SHA-256 is `7c20bfb51e63474397ee9394aff09170d58fca76fc147725a1cfe4928bc4b325`. Replay used the captured analyzer identity `6d4e4f3e2fc28bba1a56c637f6783e1d743e082d7807362324344a102fefa414`, width 800, packed-colour extent 0.65, production settings, and no illumination normalization. Replay was serial, one source timeline per run. The selected files and receipt hashes are bound in `work/o09/development-v3/reviews/layout03-current-source-baseline-v1/replay-audit.json` (SHA-256 `c6b51f175d4e4415ea53bea540866cc12dd5cfb90960d098ac47669f53eab24a`).

## Test: O09-NORMAL-TRAIN-LAYOUT03-SPATIAL-BASELINE

The existing `DetectorBaseline.Score` path scored all four frames at the declared `0.010` table-2x1 tolerance. Every frame scored; none was unscored.

| Frame | Reviewed inventory | Score | Misses |
| ---: | ---: | --- | --- |
| 1274 | 16, both present | TP 14, FP 0, FN 2 | black, cue |
| 3360 | 15, black absent | TP 14, FP 0, FN 1 | cue |
| 395 | 15, cue absent | TP 14, FP 0, FN 1 | black |
| 382 | 16, both restored | TP 14, FP 0, FN 2 | black, cue |

Across 62 reviewed visible labels, TP=56, FP=0, FN=6: precision 1.000 and recall 0.9032. All 56 red/yellow detections matched colour; the detector returned seven reds and seven yellows in each frame and no cue or black in any frame. Black recovery is 0/3 present frames and there is no black in the one absent frame. Cue recovery is 0/3 present frames and there is no cue in the one absent frame. All four outputs remain `uncertain=true` and `layoutUncertain=true`; obstruction is false.

For the 56 matched red/yellow labels, descriptive raw source-pixel centre error is median 1.2773 px, nearest-rank p95 4.0581 px, and maximum 4.6798 px. The per-frame table-space p95 values are 0.00637528, 0.00633912, 0.00554843, and 0.00559990 for frames 1274, 3360, 395, and 382. These values describe this reviewed Development sample only; they do not introduce a numeric acceptance tolerance. No radius-normalized matching was used. The score is in `baseline-current-source-tolerance-0.010.json` (SHA-256 `7523a06454b68303445a10a9019a3b687fcd79b6dc09de700ef509d5a856d00b`), with detections in `detections-current-source.json` (SHA-256 `f8df81fb0cbaad22cdde33b3e1440be6b9d369b4289871e23b93a858ecae73bc`).

The two initial baseline replays used a pre-existing APK hash and were excluded from scoring. The four authoritative runs are `l03c-1274-132328`, `l03c-3360-132335`, `l03c-395-132342`, and `l03c-382-132348`, all using the fresh APK hash above. The 800px source-to-table transform round-trip error is 4.263e-13 source pixels; its mapping record is `source-to-table-transform.json` (SHA-256 `15c53cfb5a9538db44f49df8af3211424a03ab4de72a5ba8b70d221de563cd14`).

## Test: O09-NORMAL-TRAIN-LAYOUT03-STAGE-REJECTION-TRACE

This trace reuses the four full-audit reports; it does not replay or alter analyzer output. The complete trace is `stage-rejection-trace.json` (SHA-256 `5dfc9459d34b70ed53e495cbc8e261c220edd3b4ee8fd8e7991a835d9503ca8c`).

For the black, present frames produced local candidates but did not meet the unchanged sparse-black 21/32 boundary-support gate. The closest production-add fits had support 15/32 in frame 395 and 15/32 in frame 382; the nearest diagnostic dark-edge fits had support 19/32, 20/32, and 19/32 in frames 1274, 395, and 382 respectively. The black-absent frame 3360 also produced a black-classified diagnostic fit at the expected empty point with only 16/32 support. Lowering the gate would admit a negative-control hypothesis, so the production threshold remains unchanged.

For the cue, all four reports say `no-unique-first-pass-cue` with zero neutral-body candidates. The saved full-audit summaries did not expose raw classification at the exact reviewed cue point, so the next probe targets that specific evidence gap.

## Test: O09-NORMAL-TRAIN-LAYOUT03-NEUTRAL-CUE-POINT-PROBE

The same reviewed width-800 point (105.27483218228699, 49.924167257192515) was probed in cue-present frame 1274 and cue-absent frame 395, serially and with their original receipt-bound sources. Both runs used the same analyzer identity and settings. The exact manifests and reports are under `work/o09/development-v3/reviews/layout03-current-source-baseline-v1/neutral-cue-probe/`.

| Frame and run | Point evidence in the radius-14.04 analysis-pixel probe | Boundary / component result |
| --- | --- | --- |
| 1274 cue present — `l03r1274-133350` | 618/618 pixels pass cue appearance and adaptive value; mean value 232.01 against mean field 126.33; centre value 232 against field 126. | The post-morph point joins a 691×75 component, rejected as oversized. Diagnostic edge pruning at 1.25R, 1.5R, and 1.7R produces local 38×48, 38×42, and 38×38 candidate components. The direct labelled-point boundary fit classifies as cue with local contrast, but supports only 21/32 sectors (0.65625), below the unchanged 24/32 (0.75) gate. |
| 395 cue absent — `l03r395-133402` | 618/618 pixels pass appearance, but 0 pass adaptive value; mean value 52.62 against mean field 121.62; centre value 54 against field 122. | No post-morph mask pixels at the expected point and no direct boundary fit. |

This narrows the cue failure to candidate formation around the positive point and insufficient direct boundary support. The edge-pruned components are diagnostic counterfactuals, not production proposals; this two-frame pair does not establish whole-frame false-positive safety or justify relaxing the support gate. Raw reports have SHA-256 `4eedcf4ca55ee8242d58d22cfe4c322b4460124b157a14fa3798f9d52ffa05ee` for frame 1274 and `eca5d4182589dc248dbb335ffc15752427cd2185424475913e45defc8af62ea1` for frame 395. Each replay manifest records `/status` as `Stopped` before and after, clipCount 139, the same width-800 analyzer configuration, and APK hash `7c20bfb51e63474397ee9394aff09170d58fca76fc147725a1cfe4928bc4b325`.

One invocation pointed to the derived corpus copy, whose receipt filenames do not match the device replay runner's required frame-ID naming. The instrumentation found no numeric frame files and stopped before analysis. The attempt is retained as `l03-cp1274-133305`; the corrected runs use the original `<frameId>.json` and `<frameId>.png` sources and passed the runner's receipt, timeline, configuration, and stopped-camera preflight. It produced no score and was not counted as a test result.

## Test: O09-NORMAL-TRAIN-LAYOUT03-VERIFIER-FAILURE-TRACE

This is a read-only analysis of all **30** already saved source receipts; it performs no capture or replay. The trace is `verifier-failure-trace.json` (SHA-256 `1bb01749fcdf92519c48b6b918525d0f88f307de15cfc4fb7a8b81cfdcd3f963`). All 30 per-frame measurements report `InsufficientEvidence` and `Motion=Unknown`. Four first samples establish an anchor with `CoverageValid=true`; the other **26/30** samples have `CoverageValid=false` and only **10 or 11** supported body templates. The four physical operations ended as one `InsufficientEvidence` and three `TimedOut` verifier outcomes.

Source timing is not the blocker in these saved runs: maximum adjacent source gap is **533.378 ms**, below the unchanged **800 ms** gap limit in every operation. The common rejection is incomplete template coverage. `TableVerificationWorker` sets `CoverageValid=false` when fewer body templates are supported than regions being tracked. At width 800, the worker warps to 400×800 and scales body centres/radii by 0.5; `VerificationTemplate.Compare` returns no match when the complete template radius plus its four-pixel search reach falls outside that motion image.

Applying those exact size and bounds rules to the first captured detection in each operation predicts that only 10/14 or 11/14 circles can receive full-window template comparisons. In the first three layouts, four IDs fall outside the window (`red-0`, `yellow-1`, `red-4`, `red-6`); in the restored layout, three do (`red-0`, `yellow-1`, `red-6`). The predicted counts match the measured 10/11 supported bodies. Later detection frames sometimes have one additional unsupported region even when its centre passes the simple bounds calculation, so edge clipping is a strong structural contributor, not a complete per-region attribution. This is enough to explain why the existing verifier cannot accumulate a covered stillness span on these layouts; it does not show the table moving.

Stillness motion confirmation remains separate from complete inventory confidence. This trace does not produce a stillness pass, and the missing black/cue detections remain a separate play-test readiness blocker. It points the next test at conservative edge-template coverage while keeping the existing coverage requirement intact.

## Stillness and next test

The four physical verifier outcomes remain **0/4 stillness passes**. The immediate software gate is one bounded edge-template coverage experiment on the saved Layout03 sequence; if it preserves motion sensitivity and restores supported coverage, a single bounded physical verifier retest can check whether that setup reaches stillness. This is a measured next step, not a claim that one change will pass. The 19 acquisition operations are for the broader pre-play-test Development matrix, not a count of steps required for one stillness result. Current broader counts remain reviewed Development anchors **2/12**, captured staged states **6/24**, additional matched-control states frozen **3/12**, complete lighting strata **0/2**, detector inventory acceptance **0/3**, Holdout unopened, and D1/D2/E **0/3 proven**. The edge-pruned cue proposal experiment remains queued afterward for the separate inventory miss.

**Next named test:** `O09-NORMAL-TRAIN-LAYOUT03-EDGE-TEMPLATE-COVERAGE-EXPERIMENT`. Evaluate a conservative clipped-template diagnostic on the four receipt-bound Layout03 sequences. Compare supported-body coverage and known motion evidence against the current full-window result; do not relax the coverage requirement or promote the candidate from this Development group alone. The cue-proposal experiment remains a later detector-completeness target.

At 13:36 UTC after replay, mounted-camera `/status` was `Stopped`, revision 26, 22,043 completed frames, clipCount 139, zero viewers, and no host. The Pixel 11 companion activity was foreground. No live capture, new clip, Cast action, production package update, or camera package update occurred.
