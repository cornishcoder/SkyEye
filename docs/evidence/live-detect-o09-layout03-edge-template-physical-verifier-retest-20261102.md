# Layout03 current-source physical verifier retest — 2 November 2026

**Test:** `O09-NORMAL-TRAIN-LAYOUT03-EDGE-TEMPLATE-PHYSICAL-VERIFIER-RETEST`  
**Motion result:** PASS for this bounded physical setup.  
**Inventory/readiness result:** FAIL safety check. The app returned `Ready` even though the user-confirmed full 16-ball setup contained a visible cue and black that all three detector samples missed.

## Build and run provenance

- Code under test: edge-template worker commit `2e40e2b`; checked-out documentation HEAD was `304d4088830fc2bd1c2f71e6d4dfdf4c511f2335`.
- Production Release build: 0 errors, 9 warnings. `SkyEye.Checks` had exited 0 with 68 named `PASS` checks before capture.
- Installed package: `uk.co.skyeye.companion`, version `0.8.62`, version code 70, APK SHA-256 `03767BB703B500AA51A5004BF2261F68E146489C529B7212FD489EBEFF803549`. The package was installed in place; UID/private app data were preserved and the installed APK hash matched the build artifact.
- Operation: `6bc26123-4959-4617-983d-4284cb42297a`.
- Timeline: `ddaa85ac1d84470892bba8b028d90134`; calibration: `59a6e70087ae4170a1706f7b4c30812a`.
- Analyzer identity is bound in every receipt and the manifest: width 800, packed-colour extent 0.65, edge-template source under test. Five original 1920×1080 PNGs each match the adjacent receipt hash.
- Token-free per-frame manifest: [`physical-verifier-manifest.json`](../../work/o09/edge-template-physical-verifier-retest-20261102/captured-verifier/6bc2612349594617983d4284cb42297a/physical-verifier-manifest.json), SHA-256 `3A9A1B5FE31E84428A5CE362F034E2A94C8C04252C3AFABA25274BFCBDAF73D7`.

## Measurements

| Frame | Detection | Motion / reason | Coverage | Supported bodies | Detector inventory |
| --- | --- | --- | --- | ---: | --- |
| 3316 | Yes | Unknown / InsufficientEvidence (bootstrap sample) | Valid | 0 | 14: 7 red, 7 yellow; `Uncertain=true`, `HasUncertainLayout=false` |
| 3331 | No | Still / None | Valid | 14 | Motion-only sample |
| 3347 | Yes | Still / None | Valid | 14 | 14: 7 red, 7 yellow; `Uncertain=true`, `HasUncertainLayout=false` |
| 3362 | No | Still / None | Valid | 14 | Motion-only sample |
| 3377 | Yes | Still / None | Valid | 14 | 14: 7 red, 7 yellow; `Uncertain=true`, `HasUncertainLayout=false` |

All five source samples had valid coverage. The four post-anchor samples were `Still`; first-to-last source PTS spans 10.329 seconds. The app reported: **“Saved 5 verifier frames (minimum met; maximum 7). Table result: Ready.”** This is a narrow motion-stillness pass for the tested Layout03 arrangement. It does not meet the D2 temporal denominator or duration requirements.

## Safety finding

The user staged and confirmed the full table with cue and black present. The detector returned only the seven reds and seven yellows in each detection frame, missing the visible cue and black. The broad `VisionResult.Uncertain` flag was true, but `HasUncertainLayout` was false. `TableAnalysisVerification.PublishVerification` publishes `Ready` when the reason is `None`, the still span exists and `VerificationConsensus.Resolve` returns a layout. That consensus rejects `HasUncertainLayout`; it does not reject `Uncertain` by itself. Existing checks deliberately allow stable cue-absent or black-absent inventories when `LayoutUncertain=false`, while keeping shot advice unavailable.

That rule may support a genuinely reviewed reduced layout, but it cannot establish that this initial full-table setup intentionally lacked two visible balls. The app's `Ready` result is therefore an observed false-Ready case against the staged expected inventory. Count motion stillness separately from correct inventory/readiness. Do not change production detection thresholds or reinterpret this run as D2 success.

The next task is `O09-NORMAL-TRAIN-LAYOUT03-READY-WITH-INCOMPLETE-INVENTORY-TRACE`: inspect existing typed evidence/history seams and define a conservative distinction between a verified reduced layout, an unexplained initial absence, and a ball missed or occluded by the detector. Preserve the valid motion-only result; add focused checks for intentional absence, unexplained initial absence/disappearance and obstruction before changing readiness behavior.

## Device stop

After the bounded operation, capture was stopped at the first safe boundary. The final camera `/status` read reported `Stopped`, revision 35, 30,406 completed frames, 139 clips, zero viewers and 100% battery. Pixel 11 Home retained the verifier result. No clip was added and no Cast/TV action occurred.

## Progress counts

- Layout03 physical motion-stillness attempts: **1/6 passed overall**, including **1/1 current-source** bounded run. The other five attempts were pre-fix failures and remain recorded as such.
- Development anchors reviewed: **2/12**; staged states captured: **6/24**; additional matched-control states label-frozen: **3/12**; criteria-complete lighting strata: **0/2**.
- Detector inventory acceptance: **0/3**; Holdout unopened; D1/D2/E: **0/3** proven.
- D2 windows: **0/200** supported request windows and **0/100** unsafe/source/control windows; 30-second untouched-table checks not run.
- Current-source `SkyEye.Checks`: **68/68** named checks passed, but the existing incomplete-inventory readiness case needs review and a regression test.

**Next named test:** `O09-NORMAL-TRAIN-LAYOUT03-READY-WITH-INCOMPLETE-INVENTORY-TRACE`.
