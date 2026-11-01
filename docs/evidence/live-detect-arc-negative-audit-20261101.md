# Partial-arc false acceptance audit — 1 November 2026

Target `ARC-NEGATIVE-CONTROL`: test whether the diagnostic arc probe returns a passing black fit at reviewed expected-empty locations. Current isolated VisionChecks APK SHA256 `8437e3ea1030ee547af9c2ae69a03373afca5c341024c4d8c13163956ad2e552`; production defaults and output are untouched.

| Frame | Label | Timeline | Diagnostic result | Supported sectors | Longest run | Arc span | Fit offset from supplied point |
| --- | --- | --- | --- | ---: | ---: | ---: | ---: |
|375|Black visible|`94748b6e18d648f18b3709842afba4cb`|Pass|32/32|32|348.75 degrees|2.68 px|
|461|Black absent; expected point only|`78212d2f169847d5bf1bd6f699bb66ad`|Pass|10/32|8|78.75 degrees|19.81 px|

Frame461 is a different timeline. Treat it as a geometric false-accept diagnostic only, not matched light or setup evidence. It shows the current probe's minimum 8-sector/5-run/45-degree checks accept a fit at an absent point. Together with ARC-PAIR on lower-left (positive678 and absent844 both pass), this rejects partial-arc promotion. Raising one threshold to fit these samples would be tuning on a tiny correlated set and does not establish general negative specificity.

Reports/manifests are under `work/o08/live-detect-black-pair-v1/physical-controls/stage-replays/arc-negative-audit-20261101`; both used the same isolated APK hash above. Camera is freshly Stopped/revision64. Current metrics: software3/3 suites pass; focused motion20/20; detector inventory0/3; D1/D2/E0/3 proven. Next change must introduce independent discriminating evidence or a different bounded candidate model, then replay frozen positives and negatives. No more partial-arc threshold sweeps on this set.
