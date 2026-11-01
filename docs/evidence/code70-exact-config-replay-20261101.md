# Code70 exact-configuration replay — 1 November 2026

## Purpose and provenance

This is a bounded offline code-comparison replay of the six retained daylight Code70 frames. The source tuple inventory and PNG hashes are in the [pocket-control ledger](live-detect-control-ledger-20261031.md). The replay used the isolated Pixel 11 VisionChecks package, APK SHA-256 `2d8ac41f44767731e1bfe6ae835fa843af0e4bf56be8ad3ba061fa4050db3938`, and run `code70exact800match-20261101`.

The primary width-800 settings match the source receipt (`configurationComparison=exact-match`), including packed-colour extent `0.4`, radius-first sparse-black ranking, illumination normalization off, and pocket radius 48. The runner passed `continueCalibrationMismatch=1` because the receipts reuse corners copied from a prior stable-mount capture; the replay reported no calibration-geometry contradiction on these six samples. Exact analyzer-settings parity does not establish independent calibration provenance. The additional width-1000 result uses the replay's `.65` extent and is diagnostic only. Audit and packed-colour fit tracing were enabled; these outputs are not a timing benchmark.

The report and verified manifest are retained at:

- [full replay report](../../work/production/evidence/code70-daylight-pocket-capture-20261031/exact-config-replay-20261101/matched-primary-profile/o08-replay-proof-code70exact800match-20261101.json), SHA-256 `03027f2a31e2770f7e24095042d3468f343f12cfcea5cf7ee1edbc6901870269`
- [manifest with source, receipt and APK hashes](../../work/production/evidence/code70-daylight-pocket-capture-20261031/exact-config-replay-20261101/matched-primary-profile/manifest.json)
- [reproduction runner](../../work/production/evidence/code70-daylight-pocket-capture-20261031/exact-config-replay-20261101/run-code70-exact800.ps1)

The initial attempt at `code70exact800-20261101` used `.4` but omitted the captured `SparseBlackRankByRadius=true` flag. Its [mismatch report](../../work/production/evidence/code70-daylight-pocket-capture-20261031/exact-config-replay-20261101/o08-replay-proof-code70exact800-20261101.json), SHA-256 `11dc06dd31eb6750706bb085ce09f455dcbb7d9dccc6fe38656d6dbe24665edb`, correctly says `configuration-mismatch`; it is retained in the parent folder and excluded from the exact comparison. The corrected invocation explicitly passed `sparseBlackRadiusRank=1`.

## Results

All six replay samples matched their recorded frame ID, source PTS, PNG SHA-256, and parsed receipt SHA-256. Each exact width-800 output contained 14 circles: seven red and seven yellow, with no black or cue. All six outputs had both broad uncertainty and layout uncertainty. Therefore these exact saved frames reproduce the black/cue miss; they do not establish a trusted inventory.

| Frame | Source PTS (µs) | Width-800 inventory | Uncertainty | Verification |
| ---: | ---: | --- | --- | --- |
| 1365 | 80806601200 | 7 red, 7 yellow; no black/cue | uncertain; layout uncertain | Unknown / insufficient evidence; coverage valid |
| 1425 | 80808601377 | 7 red, 7 yellow; no black/cue | uncertain; layout uncertain | Unknown / insufficient evidence; coverage invalid |
| 1457 | 80809668133 | 7 red, 7 yellow; no black/cue | uncertain; layout uncertain | Unknown / insufficient evidence; coverage invalid |
| 1555 | 80812935088 | 7 red, 7 yellow; no black/cue | uncertain; layout uncertain | Unknown / insufficient evidence; coverage invalid |
| 1645 | 80815935355 | 7 red, 7 yellow; no black/cue | uncertain; layout uncertain | Unknown / insufficient evidence; coverage invalid |
| 1736 | 80818968955 | 7 red, 7 yellow; no black/cue | uncertain; layout uncertain | Moving; coverage valid |

The verification reason values were decoded using `ObservationReason` and `TableMotion` in `src/SkyEye.Core/Analysis/ObservationPolicy.cs`. The last source frame is marked moving by offline replay; it must not be counted as a clear still-table sample.

Per-frame analyzer wall times are retained in `analysis800Metrics.wallMs`. This six-frame audit run uses full candidate tracing and is too small for the existing p50/p95 reporting rule (at least 20 samples); it is not a processing or source-age benchmark.

## Raw and rectified context

Existing same-source-image stage probes compare raw-perspective and rectified [Hough proposals](raw-perspective-hough-probe-20261031/README.md), [colour-body proposals](raw-perspective-colour-body-probe-20261031/README.md), [packed-colour seeds](raw-perspective-packed-colour-peaks-20261031/README.md), and [fixed-fit boundary support](raw-perspective-boundary-support-20261031/README.md). They use provisional labels and do not provide a full raw-space black/cue candidate path through the production classifier, duplicate handling, and uncertainty gates. The fixed-fit test re-samples existing rectified geometries; it is not a raw candidate-generation test. Raw-perspective detection remains an experiment.

## Candidate and rejection trace

The exact width-800 pass retained 802 candidate-audit rows across six frames:

- Hough produced 378 proposals: 262 rejected as cloth, 50 duplicate-before-fit, 29 no boundary fit, 29 margin, and 8 insufficient boundary support.
- Sparse-black search selected 12 ranked seeds per frame (72 seed probes total). None was accepted: 58 had no boundary fit, 11 failed boundary support, and 3 failed classification. The twelve per-frame seeds were ranked by dark radius then colour proximity.
- Twelve dark-body candidates were rejected by pocket exclusion. No black output was accepted.
- Cue recovery reported `no-unique-first-pass-cue` in every frame. One separate dark-edge diagnostic probe in frame 1736 classified as cue from 19/32 normal-support sectors, five dark-transition sectors, and 1.158R seed drift; it is a diagnostic probe, not an accepted cue detection.
- The packed red/yellow path accepted 42 red and 42 yellow colour peaks across the six frames (seven of each per frame). One additional red peak was rejected as a duplicate colour peak. These counts have no reviewed per-ball centre comparison.
- `tracePackedColourFits` sweeps packed red/yellow boundary fits. It does not validate a black or cue candidate and did not change analyzer decisions.

The capture's draft centres remain unreviewed, and no matched black-absent or cue-absent negative control exists on this timeline. Recall, false-positive rate, duplicate rate, and centre error therefore remain unscored. This is one offline source sequence with copied calibration geometry, not held-out accuracy, a lighting comparison, or physical acceptance. No detector rule or production setting changed.

The mounted camera remained `Stopped` at revision 94 after replay (26% battery; 139 saved clips). No live capture was started. The exact matched present/absent pocket layout described in the ledger is still required in reviewable light before comparing any detector change.
