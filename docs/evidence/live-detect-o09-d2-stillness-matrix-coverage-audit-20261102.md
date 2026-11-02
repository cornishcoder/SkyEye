# D2 stillness matrix coverage audit — 2 November 2026

**Target:** `O09-D2-STILLNESS-MATRIX-COVERAGE-AUDIT`
**Result:** PASS for denominator and collection-path reconciliation; no D2 evidence added.

## Frozen D2 minimums

This audit checks the criteria-frozen v3 contract, SHA-256 `462bbed9c4f10d8ef5e139f4fb95380cd270ca19581deffc52cdc0ec8daef6d9`. It does not change acceptance rules.

| Evidence | Minimum and pass rule | Current accepted count |
| --- | --- | ---: |
| Supported clear/still request windows | 100 non-overlapping requests per lighting stratum: 100 `normal-pool-room` and 100 `warm-dim-playable`. At least 99% must publish the exact reviewed inventory within 3s. Timeouts, retries, required corrections, unresolved results, and incorrect Ready all remain in the denominator. | **0/200** |
| Held-out unsafe/source/control windows | 100 total across the frozen movement, obstruction, unexplained appearance/disappearance/occlusion, stale/source/clock, reconnect/authority/cancellation categories. Zero false Ready; all categories represented. | **0/100** |
| Unchanged-table soak | At least 30s per representative layout, retaining every request, result, and source interval. The contract does not fix the number of representative layouts, so this audit does not invent one. | **0 recorded** |

These are separate safe and unsafe conditions: plan for at least **300 distinct window outcomes** (200 supported plus 100 held-out unsafe), in addition to the 30-second soaks. This is a minimum, not a wall-clock estimate. Each supported stratum needs at least 99 successes if exactly 100 requests are attempted; failed requests stay counted and additional attempts may be needed to reach 99%. Any false Ready fails the unsafe gate.

## What counts as a request

The frozen contract starts a request window when the phone accepts a Live Detect request and ends it at the first publication of a fresh, settled, clear, exact reviewed inventory. In [`ShellLiveDetect.cs`](../../src/SkyEye.Companion.Hybrid/ShellLiveDetect.cs), each Home start creates one new `RunId` and starts a continuous loop; every accepted frame is logged under that same run. The loop's repeated frame records provide evidence for that request and its unchanged-table soak, but they do not create additional request windows. Count one supported request at most per accepted run start; retain and compare its source/frame timing.

This is an operational reading of the frozen request definition and the current run/log model. The Home action toggles a single continuous Live Detect run; no existing multi-request acceptance runner was found. Multiple starts can share a stable layout, lighting condition, and recording session, but each must remain a separately recorded request. Unsafe controls need their own predeclared, non-overlapping intervals and category labels; a run's frame count alone is not an unsafe-window count.

## Current blockers and efficient order

- The latest guarded physical verifier returned four post-anchor `Still` measurements across 1.500122s. Layout03 has two narrow motion-stillness passes among seven attempts, but the verifier is not a Live Detect request-window series or a 30s soak. It contributes **zero** to the D2 counts above.
- The latest physical detector samples still returned 14/16 (7 red, 7 yellow; cue and black missed) and correctly stayed `Unclear`. The current detector has not passed its inventory gate, so the 200 supported clear/Ready windows cannot pass on this build. Do not spend 200 repetitions scoring a known-failing candidate.
- D1 still needs a frozen candidate and session-separated Holdout evaluation. Current Development coverage is 2/12 reviewed anchors, 6/24 staged states, 3/12 additional matched states label-frozen, measured light strata 0/2, detector inventory 0/3, and Holdout unopened; 19 planned Development acquisitions remain. Five Holdout layouts across at least two sessions are still required by the contract.
- The efficient path is to finish the Development layout/lighting batch and freeze a passing candidate first. Then collect D2 on that installed build, batching by lighting/session/layout: keep each table stable for repeated supported requests and its 30s soak; stop capture before table/light changes; group staged unsafe controls into predeclared blocks; retain every run ID, request result, source interval, and failure. Do not inflate denominators with frames from one run.

Physical gates remain **D1/D2/E = 0/3 proven**. Gate E has seven defined event/clip scenarios and starts only after D1 and D2 pass. There is no supportable calendar estimate yet: the matrix needs at least 300 separately classified window outcomes, multiple unchanged-table soaks, a passing detector build, and independent Holdout sessions, while per-request phone interaction time has not been measured.

**Next named test:** `O09-DEVELOPMENT-LAYOUT-LIGHTING-EXPANSION` — continue the predeclared Development layouts and measured lighting strata before opening Holdout or spending the D2 denominators. No live capture was started for this audit; the camera remains stopped.
