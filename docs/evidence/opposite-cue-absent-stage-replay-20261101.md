# Opposite-side mid-cushion cue-absent control — 1 November 2026

## Result

With camera capture stopped, the user removed only the cue from the previously reviewed opposite-side black+cue mid-cushion layout. The black, chalk, other balls and physical lighting were held unchanged by user report. The bounded verifier saved seven receipt-bound frames, then returned `Unsettled (TimedOut)`. Capture was stopped at that result boundary.

Four frames requested detection. Each live result and the exact current-source width-800 replay returned red 7, yellow 7, black 1 and cue 0. Every requested result remained uncertain. Three other saved frames were motion-only and have no detector output. The result is useful Development evidence for a reduced inventory; it does not establish readiness, stillness, or a cue-position effect.

The user reviewed the displayed full-frame overlays and enlarged black/cue crops and replied “looks good.” This confirms the approximate Y1–Y7, R1–R7, visible black B, and expected-empty C? markers in all seven images. The [user-review record](../../work/o08/live-detect-black-pair-v1/physical-controls/stage-replays/oppositecue-20261101/opposite-cue-absent-user-reviewed-centres-20261101.json) preserves the per-frame labels and response (SHA-256 `0c9874ef5aef55ca6ec78f9a1c9e1c6986e986e588d19fb5c71510b869fc4ea9`). Coordinates remain approximate source-pixel estimates; no numeric matching tolerance or detector centre-error score was agreed. Blue chalk on the rail is excluded as a ball.

## Source frames and receipts

All seven rows bind to operation `3e6bdb25-31a9-486e-95cb-2322e5ee0eae`, timeline `972b743103a54820afdc4484ffaf7042`, media generation 19, calibration `00a2e8e6d0eb4c90bec72c9bfea9c28f`, and identical ordered calibration corners. Source images are 1920×1080. PNG hashes were checked against each indexed receipt pixel hash; original receipts were checked against the capture operation and the replay copies where a detector replay was run.

| Frame / source PTS (us) | Detector request | PNG SHA-256 | Receipt SHA-256 |
|---|---|---|---|
| 562 / 29267368033 | Yes | `34f94b1610f848055476738950294a856c34ebfd77b693469ecf02c571b014bb` | `59f247d45520a398e10b2982c0de77dddf1b5583e97047ccd6c5df74e46905d9` |
| 583 / 29268074211 | No; motion-only | `f6618d8b861abced698e767fbad1bcbb0bc7bc8940050ad27863debbb6346838` | `9f032b2ac9a53705b2f989d10d6f56c65641185c35ab41a477b96e03a79f1696` |
| 598 / 29268577744 | Yes | `6dbe2f0549ad4b8a5f6c92e3fa4b9467006919dafbbb039874a86b8bc497fef8` | `ab705f61b1ba6c7a10c62544ebb08011cfdaeb6d65b90add28ec3a5dcd8e38c1` |
| 616 / 29269179844 | No; motion-only | `828480269852db204088e67e38b2b48437dec4366859cce7d6b3a917271fe003` | `ce109bfe06a06de8ca589c7aa2e4668a8cb1646768a01cb0d200ce3b471f446f` |
| 631 / 29269681600 | Yes | `a9f5c198012720f2c605ec7bc554ac12fd67ea96593642401de80cf4c77fbf2f` | `f3e847fe6460abc417a56832183fd1e7390a2b16549aad553e32ea31bf9c4fd9` |
| 650 / 29270317155 | No; motion-only | `da11d17e7c87f287db2e51032c9e7a607831e020a86bf60ee2e904189c4ec09c` | `229ef89f50d2747676182d0ad1a69717951194426cd2b9d92dc3e7ee5f4e3f5c` |
| 660 / 29270651666 | Yes | `e293025654bc9212f7ae4bd7920915e76d8453d61e12fd3c9586395ef7e8fd55` | `0d17bf0834b5d016eb74b828cdc07414081cbbcc55b172374852fe1784770d63` |

## Exact current-source replay

Run `mcsopcueabs-1652-165436` used the `full-audit` path for detector-requested frames 562, 598, 631 and 660. The replay manifest is SHA-256 `4b4c41cd0e5a56c37009f7c1e54239b21d54642d6a72ee544b17bdd9766f4c56`; the report is SHA-256 `86f1e5f19536c1397b95797fd2d0fd0fc7527d83fb482707c4ad14af205e773e`. Isolated VisionChecks APK SHA-256 is `9b9e1658b781a8773bf3fc3e70a54f889e7132ad5fb600224c6963b6cb754be3`. Recorded and replay analyzer identities and settings match at width 800; algorithm identity did not change. The replay used no Production install and made no source or threshold change.

Relevant captured settings were width 800, pocket exclusion radius 48, minimum Hough boundary support 0.75, sparse-colour support 0.625, sparse-black search extent 1.3, sparse-black minimum support 0.65625 (21/32), radius-first sparse-black ranking, candidate limit 12, production and packed-colour boundary extents 0.65, and packed-cluster edge margin 23. Exact replay returned the 15-ball reduced inventory in all four requested frames and kept `uncertain=true`. The accepted black Hough fits had 25–26/32 boundary sectors. This is fit-stage evidence, not a centre-error measurement.

The source/replay machine summary is [here](../../work/o08/live-detect-black-pair-v1/physical-controls/stage-replays/oppositecue-20261101/opposite-cue-absent-stage-replay-summary-current-source-20261101.json), SHA-256 `0015dd0495655875cfbc612480659b48cd40af9a5ee05a2553bc2756ed8acb43`. It distinguishes all seven saved frames from the four requested/replayed frames. The replay source checkout head was not recorded in the run manifest; the summary records the checkout at summary time (`6b339fb75c5570f9e56c7023fb577351ceefd953`), the relevant `VisionAnalyzer.cs` commit (`484f2c593c2582519cd9637216e34da362976814`), and the APK hash separately.

## Match limits and device state

The user reported unchanged physical lighting during staging, but fixed image-patch comparisons do not match this run to the preceding opposite-side cue-present frame 959 or earlier cue-absent frame 295. Maximum absolute patch luminance deltas versus frame 959 were 13.303–14.520; versus frame 295 they were 27.181–28.398. Within this capture, patch ranges across requested frames were 1.0–1.434. The measured cross-run differences do not establish that physical lighting changed; they do prevent a matched-image or causal cue-direction claim.

After capture, Home **End recording session** was used. At `2026-11-01T16:56:51.3389068Z`, camera `/status` read `Stopped`, revision 59, timeline `972b743103a54820afdc4484ffaf7042`, 34,376 completed frames, 139 clips, zero viewers and 61% battery. The token-free readback SHA-256 is `F424BEBFCA5A353E6EFC908A6B040C92227198F7ADCC022A6EEDC8145342189C`. The last post-replay Pixel 11 Home screenshot visibly showed **Camera ready** and **Start live** (SHA-256 `5632C848C97ED53CE4BCE801AAE66AD37DA03510C06C48825D9CA2AC799A5FFA`). The verifier UI mentioned `Sample saved · 31.4s clip · 5s target timing unverified`; camera `clipCount` stayed 139. Saved clips and app data were preserved. Cast/TV and camera packages were not touched.

This control remains Development-only. Its timed-out verifier is not a D1 inventory/readiness pass, and it is not D2 or E event/clip acceptance. Keep production settings unchanged and require a current-condition repeated positive/negative comparison before drawing a directional cue-support conclusion.
