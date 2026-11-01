# Live Detect pass/fail scorecard

**Report-only seed-source audit — 1 November, reused frame1179 report:** packed-red has 19/32 intermediate support, then fails its 20/32 sparse-colour add gate; the confirmed raw proposal has 20/32 and fails the 24/32 generic gate; the nearest Hough seed is 373.71 pixels from the raw proposal and is rejected as cloth. Both report arms remain at six reds. This narrows the next colour-aware experiment but is not a new inventory score or a matched-control result; aggregate metrics do not change. See [boundary trace report](live-detect-boundary-trace-telemetry-20261101.md#same-frame-seed-source-reuse).

Run `./scripts/Run-LiveDetectScorecard.ps1` for software suites. Add `-DetectorReport <path>` to evaluate the exact reviewed three-frame positive inventory contract (frames1691/1726/1764, pixel hashes checked). Output is `work/live-detect-scorecard/scorecard.json` plus suite logs. Exit1 means at least one suite or declared detector case failed. This reports suites, not invented assertion counts. Detector report identity and report-generation checkout are recorded separately; the latter is not the APK build identity.

Latest completed software run: **3/3 suites PASS** (GameState, Observation, general Checks). Detector replay `allseed-fit64-174259`: **0/3 positive inventory cases PASS**; each has15 balls and uncertainty, rather than16 and clear. D1/D2/E: **0/3 gates proven**. These denominators are different units and must not be added into one percentage. The three positive frames are correlated Development evidence, not three independent layouts.

**Focused boundary trace cycle — 1 November, source commit 6b1b4cb8d0fe0ac993b46b3c54f3bc6dc06cb5e8:** synthetic diagnostics contract PASS3/3; one exact-profile frame1179 trace emitted the expected 20/32 support record; detector-output parity PASS2/2 arms. These are diagnostic checks, not detector inventory cases. A strict audit-record equality check failed because the current source includes eight additional colour-body separation-rejection rows per arm from commit484f2c5; normalized detections, centres and uncertainty were identical. This cycle did not rerun the full software scorecard or focused motion set. Current aggregate remains software3/3 suites, focused motion20/20, production inventory0/3, physical D1/D2/E0/3 proven. Details: [trace report](live-detect-boundary-trace-telemetry-20261101.md).


The64-fit diagnostic still misses black in all three frames; analysis wall times1784.62/1416.80/1398.58ms. Increasing the fit budget does not resolve these saved failures. Do not promote this diagnostic or broaden fit-budget sweeps. Next development should address boundary geometry/evidence and measure negatives with the same contract.

The scorecard is an initial visible regression set, not the full acceptance contract. Expand it with frozen reviewed positive/negative cases and temporal scenarios; untested acceptance remains NOT PROVEN. A complete inventory count does not establish unique matched centres, freshness, safe readiness, events or clips.

## Subsequent combined run

The combined run at2026-11-01T17:45:29Z reports **2/3 software suites PASS**, **0/3 detector cases PASS**, exit1. General Checks failed at MotionFeedChecks.cs line42 while waiting for stale acquisition accounting; the exception is Motion-feed check timeout. The earlier3/3 pass remains historical, not current green status. Preserve both outcomes: an intermittent timeout requires diagnosis. Failed log and JSON are retained in work/live-detect-scorecard/SkyEye.Checks-motion-timeout-20261101.log and scorecard-failed-20261101.json. Do not retry until green or lengthen the timeout as a substitute for finding the cause.

Correction: MotionFeedChecks line42 waits for Processed==3 after the source-time gap; stale acquisition is line45. The failed test is source-gap processing, not stale rejection. Current metrics remain2/3 software suites,0/3 detector cases,0/3 physical gates proven; no new run.

Target motion-feed source-gap processing: added --motion-feed focused entry and failure snapshot diagnostics. One focused run passed; bounded20-attempt reproduction is active, results not yet counted. Aggregate scorecard remains2/3 suites,0/3 detector cases,0/3 physical gates proven.

Motion-feed reproduction completed:17/20 pass,3/20 fail. Timeout snapshots show Acquired3/Processed2/Replaced1,Pending0,Active0,Stale0,ScopeRejected0. Acquisition dropped frame3 before starting its next blocking read. DropPendingBeforeReadback clears pending before source.ReadAsync; this is the next handoff target. Preserve two-image ownership and replacement behaviour when designing fix. Raw attempts/results retained under work/live-detect-scorecard/motion-feed-reproduction. Aggregate remains2/3 suites,0/3 detector,0/3 gates proven.

SOFTWARE-HANDOFF completed:3/3 software suites PASS after handoff fix; detector0/3 FAIL, combined exit1 as intended. Results/logs: work/live-detect-scorecard/handoff. MOTION-GAP-HANDOFF20/20 focused passes retained. Fresh camera Stopped/rev63. Next INVENTORY-POSITIVE: raw crop shows upper black silhouette merges into dark cushion; evaluate partial-arc geometry against reviewed empty-rail controls before any integration.

ARC-PAIR result: both the reviewed positive frame678 and expected-empty frame844 produce diagnostic accepted arcs. The experiment cannot discriminate black from background. See live-detect-arc-discrimination-20261101.md. Software current3/3 PASS; motion focused20/20; inventory positive0/3; physical0/3 proven.

ARC-NEGATIVE-CONTROL fails: present375 and expected-empty461 both pass partial-arc diagnostics; different camera timelines, so negative is geometric-only. No promotion. Current software3/3, motion20/20, detector0/3, physical gates0/3. See live-detect-arc-negative-audit-20261101.md.

BLACK-MARKER-CUE location-conditioned check passes4/4; it is not whole-table detection and does not change production inventory0/3. See live-detect-black-marker-cue-20261101.md. Overall software3/3, focused motion20/20, detector inventory0/3, physical0/3 proven.

BLACK-MARKER-DISCOVERY exploratory full-frame feature4/4 location cases, one/zero/one/zero candidates on positive678/negative844/positive375/negative461, no collisions with reviewed other-ball centres. Post-hoc threshold and all timelines distinct; not acceptance. Production inventory remains0/3. Details in live-detect-black-marker-discovery-20261101.md.

BLACK-MARKER-CUE-FROZEN-DEV scores8/10 point cases (present5/7, absent3/3), zero non-black reviewed-label collisions. This corpus has0 Holdout and is not independent acceptance. Production inventory remains0/3. See live-detect-black-marker-dev-v4-20261101.md. Software3/3, motion20/20, physical0/3 proven.

BLACK-MARKER-CUE-REVIEWED-NEGATIVE-EXPANSION applies the unchanged frozen whole-frame feature to separately reviewed, black-absent frame978: **1/1 Development negative screens PASS** (7 bright circular components,0 selected dark-annulus candidates,0 reviewed visible-ball-centre collisions). This is outside corpus-v4; it has no reviewed black point, is dim and unmatched, and is not Holdout. It does not alter the production inventory result. See `live-detect-black-marker-frame978-negative-screen-20261101.md`. Current aggregate remains software3/3, focused motion20/20, production detector inventory0/3, physical D1/D2/E0/3 proven.

`CUE-EDGE-REJECTION-TRACE` reused the saved Code67 frame554 diagnostics (report SHA-256 `f3ab9848...f75c52cc`): 0 production image-edge rejects at widths800/1000; the mask-clipped 1.5R and 1.7R components still fail aspect. This is a scoped path diagnosis, not another detector pass or acceptance case. No repeat; calibrated cushion-aware comparison still needs matched, adequately lit controls. Aggregate counts unchanged.

`BOUNDARY-SAMPLE-REJECTION-TRACE` artifact audit found the Code65 frame1179 confirmed red proposal at 20/32 sectors, rejected below24/32; per-sector and bounds-rejection details were not retained. The Code67 frame554 cue trace has8 unexplained sectors and width-dependent neighbor explanation. No replay was repeated. Next: add diagnostics-only rejection reason/sector telemetry, then use one receipt-bound reviewed candidate and a matched empty-pocket/rail control. This audit does not change aggregate metrics.
