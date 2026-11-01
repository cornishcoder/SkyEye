# Live Detect targeted test ledger

Consult this ledger before selecting a test. Reuse a valid result. Rerun only for a relevant code/configuration change, a changed input contract, or a declared bounded reproducibility check. Record every failed attempt. Current aggregate: software2/3 suites (carried forward; full rerun active), detector0/3 cases, physical0/3 gates proven.

| ID | Question / exact scope | Result | Decision and rerun trigger | Evidence |
| --- | --- | --- | --- | --- |
| SEED-12 | Does full sparse-black enumeration with12 fits recover reviewed1691/1726/1764? | FAIL0/3 | Nearby seed reaches fit, support18/19/18 below21. Do not repeat unchanged. | seed-enumeration-20261101/allseed-positive-174021 |
| SEED-64 | Does expanded64-fit budget find a successful alternate seed on the same frames? | FAIL0/3 | Stop fit-budget sweeps. Revisit only after boundary/candidate model changes. | seed-enumeration-20261101/allseed-fit64-174259 |
| MOTION-GAP-BEFORE | Is focused motion-feed suite reproducible on original handoff? |17/20 PASS,3 FAIL | Snapshots show pending third frame replaced before processing. Reproduction complete. | work/live-detect-scorecard/motion-feed-reproduction/results.json |
| MOTION-GAP-HANDOFF | Does idle-consumer handoff preserve source-gap processing and two-image ownership? |20/20 PASS, plus initial focused PASS | Keep fix pending full-suite regression. Do not run another unchanged20-attempt set. | work/live-detect-scorecard/motion-feed-handoff/results.json |
| SOFTWARE-HANDOFF | Do all three software suites pass after handoff fix? | RUNNING | Collect existing handle; do not start duplicate. | work/live-detect-scorecard/handoff |
| INVENTORY-POSITIVE | Complete certain inventory for exact-hash reviewed three-frame positive group | FAIL0/3 | Regression contract; rerun when detector output changes. Does not establish centre accuracy. | scripts/Run-LiveDetectScorecard.ps1 |
| D1/D2/E | Full physical acceptance contract | NOT PROVEN | Execute only with prerequisites and frozen contract; no shot tests before D1/D2. | docs/live-detect-continuation-goal-20261101.md |

Seed run paths are under work/o08/live-detect-black-pair-v1/physical-controls/stage-replays. Detailed results: sparse-black-full-seed-enumeration-20261101.md and live-detect-scorecard-20261101.md in this directory. Earlier physical groups remain catalogued in live-detect-control-ledger-20261031.md; this ledger indexes the current focused engineering cycles and does not invent outcomes for historical runs.
