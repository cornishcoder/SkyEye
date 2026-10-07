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

`ObservationPolicy` separates the session's fifteen-second legacy gap, two-second settlement, six-second review/advice freshness, fifteen-second catalogue TTL and short-burst policies. The current provisional configuration is `o08-provisional-v10`; no supported-condition accuracy claim attaches to these thresholds.

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

### Repair follow-up, 7 October 2026

The [remaining-issues audit](o08-remaining-issues.md) led to these implemented repairs:

- Existing Hough proposals receive bounded local colour-edge refinement, with a calibrated radius range, angular support and exterior-cloth checks. Unsupported fits remain ambiguous. Raw proposal geometry and support are retained. The classifier samples the supported outer rim (0.80–0.95 radius); its black thresholds remain unchanged.
- Invalid colour references can recover within a burst; gains freeze only after a supported reference. An explicitly unsupported middle fit cannot permanently poison radius history, but three supported appearances and a supported current final detection remain required. Consensus reports its specific conflict.
- Motion uses a direct 400×800 colour warp, fixed-anchor and adjacent body-template displacement/residual, and independent whole-area colour/edge changes. Every initial/current possible body needs support. Blank or ambiguous patches remain unknown. Border/tile geometry scales with resolution. Regions measured by body templates remain part of tile coverage, preventing tiny exposed slivers from producing large false change fractions.
- Acquisition, analysis and capture failures have distinct evidence and messages. Native source diagnostics expose playback/buffering/copy state without media URLs or credentials.
- The existing corpus workbench scores complete recorded verification windows against reviewed unsafe/still intervals and final geometry. It reuses the publication proof validator, keeps session/stratum membership, counts false Ready outcomes and refuses to treat offline replay as live timing evidence.

Final Pixel native replay of the same six development JPEGs: **6/6 clear 16-ball detections, 16-ball consensus, no consensus conflict, and all five post-seed motion comparisons still with all 16 bodies supported**. Full worker times with detection on every replay frame were 430–691 ms. This improves on the earlier 3/6 enhanced replay; it does not establish reviewed centre accuracy, live request latency or held-out reliability. Intermediate failed experiments were retained in `work/o08`; the generic connected-component refiner remains disabled.

New controlled regressions cover low-contrast cumulative motion, same-gray colour movement, template displacement/ambiguity, a black body with a cream patch, unsupported middle/final fits, fault-stage attribution, and safe/unsafe request scoring including wrong final geometry. Core/corpus checks and diagnostic/normal Android Release builds passed. Generated binding/native-library warnings remain.

The user confirmed the physical table was clear and stationary. Camera status reported active 1920×1080, 30 fps, 6 Mbps capture, but the companion buffered intermittently. Two live requests yielded an acquisition refusal (`no-playing-texture`, player buffering) and a four-second timeout with zero samples. Neither published Ready or candidates; no positive live repair acceptance is claimed. The camera capture service was not restarted or reconfigured. Live transport stability and the full physical gates below remain open.

A subsequent direct PC read of the same camera transport returned HTTP 200 and 6,118,488 bytes in 8.017 seconds without EOF; the longest individual read was 238 ms. This confirms transport availability on that client/path only. The camera's cumulative slow-viewer count was 273 before and after this probe; it cannot attribute the phone's current failure. Phone network/player lifecycle remains the next investigation, not a proven transport fix.

The focused phone follow-up reproduced a persistent buffering state (position 0, reported buffer 10 seconds, only five decoded frames across eight observations) while camera slow-viewer disconnects increased. A subsequent diagnostic run recorded four `STATE_ENDED` recoveries within roughly 43 seconds and 148 decoded frames overall, with no surfaced player error code. Playback metrics now retain state, loading, play intent, suppression, last error code and end count. This establishes interrupted delivery/player recovery as an acquisition blocker; it does not distinguish network backpressure from decoder consumption as the root cause. No speculative timeout/queue expansion or weakened freshness policy was applied. A direct TCP phone probe was rejected by automatic approval review as "blocked by policy" with no further reason. Normal-app restoration and diagnostic-forward cleanup were repeated after this follow-up.

The temporary diagnostic APK was removed by guarded restoration of `work/production/backups/20261007-172908/before.apk`; UID/private storage were retained and the owned TCP 18768 forward was removed. The newly built normal v10 APK is not installed because O08.08 acceptance remains open. See [repair evidence](evidence/o08-repair-smoke.json) for hashes and compact results.

### Post-restart live and motion follow-up, 7 October 2026

The user restarted the Pixel 11 Pro XL after the earlier player stalls. Subsequent phone playback checks showed a ready, playing 1920×1080 texture with no player error or ended recovery. Live acquisition then completed multiple bounded windows. The restart restored the current session, but it did not identify or remove the underlying cause of the previous stalls; acquisition recovery after a future stall remains unverified.

The first synchronized slow-roll request captured six live frames but the then-current worker left motion unknown. Replaying those exact decoded frames on-device with the updated worker classified all five post-seed samples as moving. Supported centre displacement began at 70.6 detector pixels; one sample also raised the large-object obstruction warning. The request published no Ready result or candidates. A separate stationary replay of the six post-roll frames produced six certain 16-ball detections, a 16-ball consensus, and five still comparisons.

Those stationary images repeatedly gave one visible ball 18–20 supported boundary sectors out of 32. The provisional minimum is now 18/32, with the existing multi-frame association, final-current support and consensus gates intact. On the same mounted camera, a live stationary request then completed in 2.639 seconds using five independent frames (maximum source gap 600 ms), returned 16 balls and three candidate shots, and had no consensus conflict. The largest supported centre displacement in its sampled detections was 2.6 pixels. This is a successful single live development check, not a reviewed-geometry result or a held-out reliability estimate.

The captured 1920×1080 table images are visibly sharp and show all sixteen balls; the failures here came from edge-support and motion-evidence thresholds. This visual check does not replace a measured focus, compression or edge-resolution study. The ordinary production Release APK built successfully from the final source; after diagnostics the original installed APK was restored byte-for-byte, the app UID and private storage were retained, the owned 18768 forward was removed, and camera capture was left unchanged. See the compact follow-up receipts in [repair evidence](evidence/o08-repair-smoke.json).

### Physical acceptance still required

O08.01 still needs independent labelled baseline/development/holdout sessions; O08.03/O08.04/O08.05 still need validated motion/appearance/geometry thresholds and the labelled slow-roll, obstruction, changed-light/pose and recovery strata. O08.06 has runtime/controlled/actual-tap evidence but lacks the full physical target/correction/source/control matrix. O08.07 lacks the phone-plus-corrected-TV resource comparison. O08.08 requires the planned per-stratum sample sizes, at least two lighting strata, five layouts, held-out sessions, reviewed geometry and 100 unsafe windows with all categories. O08.09 remains gated by that acceptance. Existing O01/O02.05/T13/T14 and V03/V04 limits remain open.
