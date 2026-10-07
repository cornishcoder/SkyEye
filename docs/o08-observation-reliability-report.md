# O08 implementation and evidence

7 October 2026. Baseline `5453d0b`. **Runtime foundation implemented; O08 reliability/release acceptance is incomplete.** Do not mark the mission complete or describe automatic advice as dependable. [Small evidence summary](evidence/o08-runtime-smoke.json); large media, raw receipts and retained APKs are under `work/o08`.

## Delivered runtime

- Explicit `ObserveTable.Mode`: old commands/default `IncludeChoices` remain single-frame; the Hybrid **Show candidate shots** button requests bounded verification. One owner in `TableAnalysisSession`; no second tracker, game ledger or renderer.
- Five to six independent exact decoded samples, at least two seconds of source PTS, 0.5-second target cadence, maximum 0.8-second source gap and four-second monotonic deadline. Metadata polling precedes native readback. Expensive detection runs at first/middle/final anchors, with one extra current recovery anchor.
- Frozen camera/source/decoder/calibration/group/settings; scoped operation IDs, duplicate handling, cancellation and late completion suppression. An uncooperative retiring acquisition/worker retains its lease so retries cannot build another native-work queue. Pixels are disposed after their actual owner finishes.
- Separate motion, layout quality and reason metadata. Bootstrap whole-playable-pixel changes against recent and fixed anchors, perimeter texture/pose health, exposure change and current pocket-reference checks. Unknown coverage or small unsupported changes never establish stillness. This pixel baseline is provisional; it is not an accepted slow-roll/registration model.
- Deterministic one-to-one spatial/radius association, robust centres and colour support; ambiguous assignments, extra/merged bodies and persistently missing final bodies remain unresolved. No stale ball is silently carried forward and no disappearance becomes a pot. Current supporting detection IDs/PTS/ages remain explicit, including when motion sampling ends after the latest full analysis.
- Per-burst raw geometry branch and frozen gains. A provisional dark-body/rim classifier rechecks a black ball with a large cream patch using current appearance, without forcing an inventory. Reference conflicts and unsupported normalization remain unclear. The connected-body geometry experiment degraded colour availability and is disabled by default (`VerificationBodyRefinement` is available only for explicit development ablations).
- Checking/cancel/retry/review messages, clearing old advice before replacement, one candidate computation per verified catalogue, existing phone/TV selection/preset/clear/expiry. Explicit review retains only the latest supported anchor image. Native pixels stay outside app/agent/game projections.
- Freshness checked at acquisition, verification completion, candidate generation and coordinator publication: at most 1.5 seconds with trustworthy source-clock metadata. Network uncertainty and native copy/processing/writer delay count toward age. Cached status cannot renew its clock receipt. No claim that the table remains still throughout the fifteen-second catalogue lifetime.

`ObservationPolicy` separates the session's fifteen-second legacy gap, two-second settlement, six-second review/advice freshness, fifteen-second catalogue TTL and short-burst policies. The final provisional configuration is `o08-provisional-v7`; no supported-condition accuracy claim attaches to these thresholds.

## O01 corpus extension

Schema version 1 additively supports reviewed real `StillVerification` and `NegativeVerification` holdouts, with separate denominators from timed shots. Still windows require increasing scoped receipts. Explicit reviewed `source-invalid` negatives may preserve duplicates/backwards/scope changes for rejection evaluation. Session/source-hash leakage fences remain. Optional reviewed ellipse radii enable radius-relative centre-error scoring with explicit labelled-match coverage.

The opt-in capture sink saves at most six paired JPEG derivatives/receipts per request within a 40 MiB budget. It preserves native timing, decoder/calibration, gains, raw labels, warnings and hashes; it does not automatically delete media. `import-verification` creates unreviewed development material in the existing workbench, not invented labels or a second evaluation store. Six real 1920x1080 snapshots (about 1.5 MiB) were imported and frozen at `work/o08/corpus-v1`. Human centres/radii, reviewed condition intervals and a blind session holdout remain absent. These are decoded JPEG derivatives, not preserved original source clips.

## Observed live results and ablations

The user declared a clear stationary table with all sixteen balls. The received frames were full 1920x1080 decoded bitmaps; ordinary ball detection uses those pixels directly. One captured derivative is visibly clear. A comprehensive image-quality measurement was not performed. Radius swings refer to fitted Hough circles: normalisation could fit the cream patch on the black, while jittering body/shadow edges also changed circle size. They are not measurements of changing physical ball size.

| Evidence | Observed result | Limit |
| --- | --- | --- |
| Six legacy baseline observations | Six sixteen-ball detections, two settled/ready; detector 170–318 ms; final reported age 913–1,146 ms | One correlated session; count/colour checks, no reviewed geometry accuracy |
| Actual Pixel **Show candidate shots** tap, prototype v1 | Five independent samples, sixteen balls and three candidates; receipt duration 2.322 s, tap-to-observed-result about 2.640 s | Phone interaction observed; setting Both modes is not connected-TV acceptance |
| Actual Cancel button | Observed and tapped while checking; catalogue empty and no late observation | Short live cancellation smoke only |
| Ten repeated v1 checks over more than thirty seconds | Eight ready; one clock uncertainty refusal and one failed decoder/source interruption | Availability failures retained; not a 99% correctness pass or independent trials |
| Five requests after the user offered a slow roll | All unclear, zero candidates; old v1 motion metadata still reported still | Exact moving intervals were not independently labelled. Not motion-detection or unsafe-readiness acceptance; the unsupported small-change range was subsequently changed to unknown |
| Frozen JPEG native replay v2/v3 | Baseline clear 0/6, enhanced clear 0/6 | Failed development hypotheses preserved |
| Frozen JPEG native replay v4 | Baseline clear 0/6, enhanced clear 3/6 | Improvement on correlated development snapshots only; residual misses remain |
| Connected-body experiment v5 | Enhanced clear 2/6 | Worse colour availability; disabled by default |
| Four later live v4 checks | Two ready with three candidates (2.320/2.377 s); two detection-unclear results; final age 902–1,147 ms | Still far below the proposed availability/correctness gate |

During the repeated phone run most measured windows advanced roughly 29–30 decoded frames/s and 27–30 texture frames/s; the failed/restart window was lower. These window averages do not establish p95 frame-gap, no-stall or native-resource acceptance. Corrected TV was disconnected in the observed tap run. No new mounted TV/resource comparison, exposure-control experiment or sustained tracking acceptance is claimed.

## Verification and delivery state

Core executable checks pass, including source/clock/gap/age/cancellation, uncooperative retirement and exact disposal, consensus/dropout/colour drift, final-frame provenance and legacy ready-flag refusal. Corpus/import/schema/scoring checks, GameState checks and the real Agent UI suite pass. Native JPEG replay and source-clock wrap/exact-surface/duplicate/frozen/future checks completed on Pixel 11. Sequential Android diagnostic and normal Release builds passed with the recorded binding/native-library warnings. Programmatic Sky-origin requests are not real voice acceptance.

Temporary diagnostic updates used `Install-ProductionCompanion.ps1`: existing package/signature/version/UID/private-storage safeguards passed. The new normal artifact is buildable, but **normal O08 delivery is deferred** until O08.08 acceptance. The pre-O08 normal APK from `work/production/backups/20261007-142319/before.apk` was restored with retained UID/private storage. Its installed hash matches `cea5fe3b70c87a6916f2443847543720c9a3c56506ea251f0c33dd40f2b0a80f`, normal `MainActivity` is foreground, and the owned 18768 ADB forward is removed. Camera capture was not stopped or rebuilt; clips/key/journal/calibration storage was preserved. TV reconnection is not claimed. The cleanup result is recorded in the evidence summary.

The final v7 diagnostic requests refused before CV because playback reported `no-playing-texture`; final positive live acceptance did not run. Automatic approval review rejected the attempted diagnostic activity launch and live-resume request as **“blocked by policy”**, with no more specific reason. That action is not counted as successful. Guarded restoration of the original normal APK subsequently passed.

## Remaining mission gates

O08.01 still needs independent labelled baseline/development/holdout sessions; O08.03/O08.04/O08.05 still need validated motion/appearance/geometry thresholds and the labelled slow-roll, obstruction, changed-light/pose and recovery strata. O08.06 has runtime/controlled/actual-tap evidence but lacks the full physical target/correction/source/control matrix. O08.07 lacks the phone-plus-corrected-TV resource comparison. O08.08 requires the planned per-stratum sample sizes, at least two lighting strata, five layouts, held-out sessions, reviewed geometry and 100 unsafe windows with all categories. O08.09 remains gated by that acceptance. Existing O01/O02.05/T13/T14 and V03/V04 limits remain open.
