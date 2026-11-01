# Opposite-side cue-positive restaged control — 1 November 2026

## Result

After the cue was restaged and the user reported it visible, the bounded Production Home verifier operation `884a3abb-6040-4837-9cc1-48185728df73` saved six source/receipt pairs on timeline `c5d9a60d9fb44210888abaabc77545ed`, media generation 14. Home reported **Saved 6 verifier frames (minimum met; maximum 7)** and `Unsettled (TimedOut)` at 17:13:48 UTC. Three frames requested detection; three were motion-only. The source images show the black and cue separated and all other balls in place.

Each requested live result was uncertain and counted 7 red, 7 yellow, 1 cue and 0 black. This is a black miss in a user-staged positive image, not a readiness result. The visible Home line `Sample saved · 31.4s clip · 5s target timing unverified` is not treated as a saved camera clip: camera `clipCount` remained 139.

| Frame / source PTS (us) | Detection | Live inventory result | PNG SHA-256 | Receipt SHA-256 |
| --- | --- | --- | --- | --- |
| 1691 / 30592009633 | Requested | 15: red 7, yellow 7, cue 1, black 0; uncertain | `fc1ad7873fa6e35d539b6b28f5fafd1fa7840691221db6cbe7408b29e1e58e3d` | `4b814a03414a002afdda1f742e61bad425d4bfa7b85e1981a07f483569f3914b` |
| 1711 / 30592682188 | Motion-only | No detector result | `840da83d14ddfe0b35ef70464a1fac74ce74e72cc2d1a20e1963f1af8c7dec1d` | `16f2aadfb71a9e51f2ddddb31d0c3eb72a9e8b0194432fff70399412127d6e8e` |
| 1726 / 30593186600 | Requested | 15: red 7, yellow 7, cue 1, black 0; uncertain | `bba5df8d3198218cd06af903e1b1071a999931e6e900463c000e0c788406544d` | `eabddf9c011a30933447ad12b4bda6abdd494667c81334e165437aa41c28222f` |
| 1743 / 30593758277 | Motion-only | No detector result | `683d6642eb9e40f54e3386d7b6e782bc3e09b839b2b783c2682cef8e3615a3ea` | `7c0a2ded951e5dfbffb18e469938164a4492ddfaa28b2106ff0a82fbc83e3485` |
| 1764 / 30594464455 | Requested | 15: red 7, yellow 7, cue 1, black 0; uncertain | `a2b0bf0c0a51564b4c8a8e12f50a946bc7671f6b56118bda1404a7bc8085b19e` | `3b30ccadac32316a151886a2075a41f4e690d09289119d6981c4f0e6916b6432` |
| 1783 / 30595103388 | Motion-only | No detector result | `fb5b19948e2372881873e09eee520f6726aee82666a25f78e1d3f11bb792d06c` | `fde9901dceea7795471be7a33db8ccd937864498309687958071e22638c522ed` |

All six PNG hashes match their receipt pixel hashes. Receipts share one ordered calibration-corner set, identical analyzer identity, 1920×1080 dimensions, operation ID and generation. The corner coordinates exactly match reviewed cue-absent frame 562 and earlier cue-positive frame 959; this is receipt geometry agreement, not independent proof of camera pose.

## Raw-image review

The full-frame draft sheet and enlarged black/cue crops were displayed before detector output was shown. The user replied “Looks correct” for Y1–Y7, R1–R7, B and C in all six frames. The review record is `work/o08/live-detect-black-pair-v1/physical-controls/cue-present-opposite-cue-restaged-20261101/review-v1/user-reviewed-centres-20261101.json` (SHA-256 `9b96729fbb4466dd832a49b3bfdde6e35643249d1b2d8b8a10042e87ac114bc1`). The full overlay sheet hash is `f1ec14bb40f5f8ed71a5f5fffc2f9bcc1c52fe7732308492a5f9d239f58cbbad`; the enlarged crop sheet hash is `fdf6ee7543db0806b314a1f22df2b524b7d499ea393fdd6a22a0b7d266275f85`. These remain approximate image-centre labels; no numeric error tolerance or physical cloth-plane accuracy is claimed.

## Exact current-source replay

Run `mccuepos-1715-171924` replayed the three requested source frames in `full-audit` mode. The host checkout at replay was `36ebe10c4a1b7bd8cad1aef19b9a8d8ae1a364de`; the relevant `VisionAnalyzer.cs` source commit was `484f2c593c2582519cd9637216e34da362976814`. The isolated VisionChecks package was version 0.1 / code 1, APK SHA-256 `9b9e1658b781a8773bf3fc3e70a54f889e7132ad5fb600224c6963b6cb754be3`; a read-only pull from Pixel 11 independently produced the same hash. The APK source checkout commit is not embedded in the runner manifest.

The captured and replay analyzer identities match at width 800; the algorithm identity is unchanged. Settings include width 800, pocket radius 48, Hough boundary support 0.75, sparse-colour support 0.625, sparse-black extent 1.3, sparse-black minimum support 0.65625 (21/32), radius-first sparse-black ranking, packed-colour extent 0.65 and packed-cluster edge margin 23. The report hash is `5badbae7857125252d4f58d4414dbcd2baad9abe29904e97b37133c45fc68484`; manifest hash is `896e0a56b5188ffc5bb8a73361598eefb4bcb4a2617777206049c1708f5b43f8`.

All three requested frames replayed as 15 balls at both width 800 and diagnostic width 1000: 7 red, 7 yellow, 1 cue, no black, with `uncertain=true` and `layoutUncertain=false`. The exact black-mask trace in this first run used the runner's implicit point `(1282,536)`, which is not the reviewed black location in this image. Its mask statistics are excluded. The underlying detector results and packed-colour candidate audit are still valid; no black-mask conclusion is drawn from the wrong point.

After the user reviewed the raw-only B markers, corrected trace `mccuepos-labeltrace-172616` on frame 1691 used reviewed point `(1224,152)` (manifest/report SHA-256 `2a3e8415029c738319227023002d0779e31e11cf8f13d2406997ebdce85b8ef3` / `12e8ef1619e26ca1e39593a42c3a7619c0a86b22dd68119b5a8bca3f1676e625`). The raw dark mask contains 1,187 pixels before and 1,186 after opening; the sampled body region has median value 9. At the fixed reviewed centre, only 8/32 sectors have combined normal/dark-transition support. Sweeping the dark-body ceiling from 30 through 55 keeps combined support at 8/32. The production audit still returns no black. A separate evaluation-only partial-arc fit reaches 15 supported sectors, a 7-sector contiguous run and 67.5° span, passing its exploratory checks. It does not change production output and is not promoted: this group lacks the required matched empty-rail negative.

For context, corrected trace `mccueabs-labeltrace-172726` on the earlier user-reviewed cue-absent frame 562 measured 25/32 fixed-centre support and 27/32 partial-arc candidate support (manifest/report SHA-256 `a384f564d5eb5951ad583d4672cc8f77701998a6a3f386e768985c80a1a80cdf` / `40df7fa8d6b13bcf3474da7bf7e6071f7770625a94d6e3e46d7193f07c175a4b`). That prior frame is not matched to this capture's image condition. Its four cloth-patch luminance medians differ from the new requested frames by up to 20.45 levels; the differences versus earlier cue-positive frame 959 and cue-absent frame 295 reach 33.61 and 47.48. Within the new requested frames, the maximum patch difference is 0.57. This supports internal stability for this capture only; it cannot isolate a cue effect or prove equal physical illumination.

The runner issue was corrected in commit `02c5405`: `-TraceBlackMask` now rejects omitted source-image coordinates. Review status remains supplied evidence; the runner does not validate a human review. The missing-coordinate rejection was checked before device work. No production vision threshold, companion or camera package changed.

## Device state and next step

After capture, camera `/status` was `Stopped`, revision 63, timeline `c5d9a60d9fb44210888abaabc77545ed`, 39,676 completed frames, 139 clips and zero viewers. A fresh Home screenshot after the final replay visibly showed **Camera ready** and **Start live session** (SHA-256 `2c3e63365e3eb900128358e68dc71c3ae0d657dfaa4652a76678f41e490c5d28`); accessibility also reports `Start live session`. No Cast/TV, camera package, clip deletion or app-data reset occurred.

This remains Development evidence. D1, D2 and E remain open. The next useful control is to remove only the cue under the current staging and image conditions, then restore it, stopping capture before each table change. Compare fixed cloth patches before calling the pair matched; preserve the current and prior unmatched groups separately.

The machine-readable provenance, review and stopped/Home readbacks are in [the current-source summary](../../work/o08/live-detect-black-pair-v1/physical-controls/stage-replays/oppositecue-20261101/opposite-cue-present-restaged-stage-replay-summary-current-source-20261101.json), SHA-256 `5ba91e4caeb1caecdd0b41e810668a66dfedd3cd51c3ee6d2552ef0878b5af25`. The [photometric comparison](../../work/o08/live-detect-black-pair-v1/physical-controls/cue-present-opposite-cue-restaged-20261101/photometric-roi-check-restaged-cue-present-20261101.json) is SHA-256 `649a429c80356e9a270a6d5f44a396ae05a339e7ae7fa60d9047359cfa8406ab`.

### Follow-up seed-list audit

The width-800 frame-1691 trace reports 134 cropped minimum-radius-qualified components and a hard component-index limit of 64. Its productionSeeds list contains 64 entries; three nearby qualified seeds are explicitly recorded in nearbySeedsSkippedByHardIndexLimit. The nearest retained seed is approximately 649 analysis pixels from the reviewed point. This distance does not establish absence of a nearby mask maximum: the trace truncates the list before ranking. AnalysisCoordinates.FromTable uses (width*(1-table.Y), width*table.X), consistent with the portrait warp destination ordering in both trace and analyzer. No production candidate association or acceptance is inferred. Next inspect analyzer selection and the three skipped components before proposing a change.

