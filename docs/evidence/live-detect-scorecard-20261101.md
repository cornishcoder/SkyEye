# Live Detect pass/fail scorecard

Run `./scripts/Run-LiveDetectScorecard.ps1` for software suites. Add `-DetectorReport <path>` to evaluate the exact reviewed three-frame positive inventory contract (frames1691/1726/1764, pixel hashes checked). Output is `work/live-detect-scorecard/scorecard.json` plus suite logs. Exit1 means at least one suite or declared detector case failed. This reports suites, not invented assertion counts. Detector report identity and report-generation checkout are recorded separately; the latter is not the APK build identity.

Latest completed software run: **3/3 suites PASS** (GameState, Observation, general Checks). Detector replay `allseed-fit64-174259`: **0/3 positive inventory cases PASS**; each has15 balls and uncertainty, rather than16 and clear. D1/D2/E: **0/3 gates proven**. These denominators are different units and must not be added into one percentage. The three positive frames are correlated Development evidence, not three independent layouts.

The64-fit diagnostic still misses black in all three frames; analysis wall times1784.62/1416.80/1398.58ms. Increasing the fit budget does not resolve these saved failures. Do not promote this diagnostic or broaden fit-budget sweeps. Next development should address boundary geometry/evidence and measure negatives with the same contract.

The scorecard is an initial visible regression set, not the full acceptance contract. Expand it with frozen reviewed positive/negative cases and temporal scenarios; untested acceptance remains NOT PROVEN. A complete inventory count does not establish unique matched centres, freshness, safe readiness, events or clips.

## Subsequent combined run

The combined run at2026-11-01T17:45:29Z reports **2/3 software suites PASS**, **0/3 detector cases PASS**, exit1. General Checks failed at MotionFeedChecks.cs line42 while waiting for stale acquisition accounting; the exception is Motion-feed check timeout. The earlier3/3 pass remains historical, not current green status. Preserve both outcomes: an intermittent timeout requires diagnosis. Failed log and JSON are retained in work/live-detect-scorecard/SkyEye.Checks-motion-timeout-20261101.log and scorecard-failed-20261101.json. Do not retry until green or lengthen the timeout as a substitute for finding the cause.

Correction: MotionFeedChecks line42 waits for Processed==3 after the source-time gap; stale acquisition is line45. The failed test is source-gap processing, not stale rejection. Current metrics remain2/3 software suites,0/3 detector cases,0/3 physical gates proven; no new run.

Target motion-feed source-gap processing: added --motion-feed focused entry and failure snapshot diagnostics. One focused run passed; bounded20-attempt reproduction is active, results not yet counted. Aggregate scorecard remains2/3 suites,0/3 detector cases,0/3 physical gates proven.

Motion-feed reproduction completed:17/20 pass,3/20 fail. Timeout snapshots show Acquired3/Processed2/Replaced1,Pending0,Active0,Stale0,ScopeRejected0. Acquisition dropped frame3 before starting its next blocking read. DropPendingBeforeReadback clears pending before source.ReadAsync; this is the next handoff target. Preserve two-image ownership and replacement behaviour when designing fix. Raw attempts/results retained under work/live-detect-scorecard/motion-feed-reproduction. Aggregate remains2/3 suites,0/3 detector,0/3 gates proven.
