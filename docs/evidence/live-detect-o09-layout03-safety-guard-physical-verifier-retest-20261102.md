# Layout03 safety-guard physical verifier retest — 2 November 2026

**Test:** `O09-NORMAL-TRAIN-LAYOUT03-SAFETY-GUARD-PHYSICAL-RETEST`  
**Result:** PASS for the targeted false-Ready guard. The bounded motion result was still; the staged full inventory remained `Unclear (DetectionAmbiguous)`.

## Build and installation

- Source commit: `fee0653869f4c4c18d7dfa258eee4832ea84a203` (`fee0653 fix: keep unreviewed initial ball absence unclear`).
- Production Release build succeeded with 0 errors and 9 warnings.
- Installed package: `uk.co.skyeye.companion` 0.8.62 / versionCode 70. The pulled installed base APK matches the built signed APK SHA-256 `B601D0943503E9FB77DD2145C46A875776EA1B6FFCDF520B05895171BD7BB88E`. The in-place installer retained UID and private app data.
- Analyzer: `o08-provisional-v21`; exact analyzer-identity SHA-256 and settings are in the token-free [physical-verifier manifest](../../work/o09/safety-guard-physical-verifier-retest-20261102/physical-verifier-manifest.json). Width 800 and packed-colour extent 0.65 match the preceding edge-template run.

## Physical run

The user-staged Layout03 table had seven reds, seven yellows, cue and black, with the black beside the lower-left pocket and the cue separated nearby. Lighting was normal daylight with no direct glare per the user's report. The user had required the camera and lighting to stay fixed for this four-state group. After reconnect, the in-app prompt to reuse the saved calibration was accepted on that basis. The new calibration ID is `060359311cdb47ca88d1f91d06070af2`; its four saved corner coordinates exactly match the prior run's calibration ID `59a6e70087ae4170a1706f7b4c30812a`. The camera timeline changed to `a2c31ce783e0441891e55d6c96820b73`.

The verifier operation `76e7ea15-f976-403b-914b-f005f1d5e771` saved five frames and five matching receipt/image pairs. Every frame had valid coverage. The first measurement anchored as `Unknown / InsufficientEvidence`; all four later measurements were `Still`. The total source span was 2.000366 seconds, with a 1.500122-second span across the four post-anchor still measurements and a maximum source gap of 500.244 ms.

| Frame | Detection requested | Motion / reason | Inventory from detection | Uncertainty |
| ---: | --- | --- | --- | --- |
| 532 | Yes | Unknown / InsufficientEvidence | 7 red, 7 yellow; cue 0, black 0 | `Uncertain=true`, `HasUncertainLayout=true` |
| 547 | No | Still / None | Motion-only sample | — |
| 562 | Yes | Still / None | 7 red, 7 yellow; cue 0, black 0 | `Uncertain=true`, `HasUncertainLayout=true` |
| 577 | No | Still / None | Motion-only sample | — |
| 592 | Yes | Still / None | 7 red, 7 yellow; cue 0, black 0 | `Uncertain=true`, `HasUncertainLayout=false` |

Home showed **Saved 5 verifier frames (minimum met; maximum 7). Table result: Unclear (DetectionAmbiguous)**. The guard therefore prevented the stable 14-ball detector result from becoming an initial full-table `Ready`. This is a targeted safety-control pass, not a detector-recall pass: the visible cue and black were still missed. No centre labels were added from these frames.

![Live Layout03 table view before the verifier](../../work/o09/device-state/safety-guard-live-preflight-20261102.png)

![Home verifier result](../../work/o09/device-state/safety-guard-home-verifier-result-20261102.png)

## Stop and acceptance limits

The bounded operation ended before leaving the device. A fresh `/status` read returned `Stopped`, revision 39, 37,352 completed frames, 139 clips, zero viewers and 100% battery. Pixel 11 Home showed `Camera ready` and enabled `Start live session`. The token-free [post-stop status snapshot](../../work/o09/device-state/safety-guard-poststop-status-20261102.json) omits camera read credentials. No clip was added, no app data was cleared, the mounted-camera package was not changed, and Cast/TV and match controls were untouched.

This run adds one Layout03 bounded motion pass: four post-anchor `Still` samples over 1.500122 seconds, while correctly returning `Unclear` for the incomplete inventory. Layout03 has **2/7** motion-stillness attempts passed overall; the earlier pre-guard `Ready` remains a safety failure and is not reclassified. This run does not satisfy D2: the required 30-second unchanged-table checks, 200 supported request windows across two light strata (**0/200**), and 100 unsafe/source/control windows (**0/100**) remain outstanding. Detector inventory acceptance remains **0/3**, Holdout is unopened, and D1/D2/E remain **0/3** proven. Physical play testing is still gated on D1 and D2.

**Next named test:** `O09-NORMAL-TRAIN-LAYOUT03-EDGE-PRUNED-CUE-PROPOSAL-EXPERIMENT`, a bounded Development replay of the already staged present/absent cue frames. Preserve the production boundary-support gate; this physical run does not change detector thresholds.
