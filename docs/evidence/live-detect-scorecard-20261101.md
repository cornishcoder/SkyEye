# Live Detect pass/fail scorecard

Run `./scripts/Run-LiveDetectScorecard.ps1` for software suites. Add `-DetectorReport <path>` to evaluate the exact reviewed three-frame positive inventory contract (frames1691/1726/1764, pixel hashes checked). Output is `work/live-detect-scorecard/scorecard.json` plus suite logs. Exit1 means at least one suite or declared detector case failed. This reports suites, not invented assertion counts. Detector report identity and report-generation checkout are recorded separately; the latter is not the APK build identity.

Latest completed software run: **3/3 suites PASS** (GameState, Observation, general Checks). Detector replay `allseed-fit64-174259`: **0/3 positive inventory cases PASS**; each has15 balls and uncertainty, rather than16 and clear. D1/D2/E: **0/3 gates proven**. These denominators are different units and must not be added into one percentage. The three positive frames are correlated Development evidence, not three independent layouts.

The64-fit diagnostic still misses black in all three frames; analysis wall times1784.62/1416.80/1398.58ms. Increasing the fit budget does not resolve these saved failures. Do not promote this diagnostic or broaden fit-budget sweeps. Next development should address boundary geometry/evidence and measure negatives with the same contract.

The scorecard is an initial visible regression set, not the full acceptance contract. Expand it with frozen reviewed positive/negative cases and temporal scenarios; untested acceptance remains NOT PROVEN. A complete inventory count does not establish unique matched centres, freshness, safe readiness, events or clips.
