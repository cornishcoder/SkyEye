# Mid-cushion black/cue exact-stage replay — 1 November 2026

## Scope and reviewed inputs

This is a Development-only saved-frame replay of the mid-cushion four-step black/cue control. It traces the current detector through sparse-black seeds, fitted boundary support, classification, duplicate admission and uncertainty. No production threshold or acceptance gate changed.

The user reviewed the displayed full-frame overlays and enlarged crops for frames 2831, 542, 582, 622, 295, 334, 373, 288, 331 and 370, replying “Looks correct.” This confirms the approximate Y1–Y7/R1–R7 positions, visible cue/black positions and B?/C? expected-empty locations. The user estimated visible-centre uncertainty at roughly 3–4 source pixels. That is a visual estimate, not a numeric matching tolerance or physical cloth-plane accuracy bound. B?/C? remain expected locations only, and rail chalk is excluded from the ball inventory. The [review record](mid-cushion-user-reviewed-centres-20261101.json) binds each PNG and receipt; SHA-256 `d0f889bd91645dc1fbaea53cfb316a6859a7462fc2527fffd9b6280c97d7e4a8`.

The first positive, frame 2831, is image-colour mismatched from the later controls and is excluded from current-condition comparisons. Four fixed cloth patches support a current source-image match among the black-absent, cue-absent and restored states; this does not prove equal physical illumination or camera processing. All ten captures share calibration ID `00a2e8e6d0eb4c90bec72c9bfea9c28f` and identical ordered calibration corners.

## Replay identity

- Checkout HEAD: `1f21aef88cada670e19229d2053217ca065ca487`; VisionAnalyzer source commit `484f2c593c2582519cd9637216e34da362976814`; replay instrumentation commit `8e2edd43854c3b387f99fbdbecb04516ab4a5bcc`.
- Build: `dotnet build tests/SkyEye.Vision.Device/SkyEye.Vision.Device.csproj -c Release --nologo --verbosity minimal`; exit 0, 0 errors, 23 warnings.
- VisionChecks APK: `uk.co.skyeye.visionchecks`, version 0.1/code1, SHA-256 `9b9e1658b781a8773bf3fc3e70a54f889e7132ad5fb600224c6963b6cb754be3`. A fresh read-only installed-APK pull matches this hash.
- Each run used serial `full-audit` replay with exact recorded width-800 settings. Key values: packed-colour extent `.65`; sparse-black extent `1.3`; black support minimum `21/32` (`.65625`); Hough support `.75`; sparse-colour support `.625`; radius-first sparse-black ranking; dark-body ceiling 40; pocket exclusion radius 48; packed-cluster image margin 23; illumination normalization off. Recorded and replay analyzer identities match.
- The [machine summary](../../work/o08/live-detect-black-pair-v1/physical-controls/stage-replays/midcushion-20261101/mid-cushion-stage-replay-summary-current-source-20261101.json), SHA-256 `fa9824d9969a7c7c53066991fa81a5771a4624d82f9bdfb11b70d0e987855972`, binds frame IDs, source PTS, image/receipt hashes, report/manifest hashes, settings, APK, review record and device state.

## Results

| Control | Frame / source PTS (µs) | Expected | Width-800 output | Boundary trace |
| --- | --- | --- | --- | --- |
| Initial positive, unmatched | 2831 / 14696486611 | Black + cue | 7 red, 7 yellow, cue 1, black 0; uncertain | Nearest black fit 18/32; insufficient support. Excluded from matched-condition findings. |
| Black absent, cue present | 542 / 24933215444; 582 / 24934549788; 622 / 24935883000 | Black absent, cue present | Each: 7 red, 7 yellow, cue 1, black 0; uncertain | No false black in these three negatives. |
| Black present, cue absent | 295 / 25265044544; 334 / 25266344655; 373 / 25267644777 | Black present, cue absent | Each: 7 red, 7 yellow, black 1, cue 0; uncertain | Black fits have 24, 25 and 24 supported sectors and classify as black. |
| Restored black + cue | 288 / 25564671166; 331 / 25566104555; 370 / 25567404811 | Black + cue | Each: 7 red, 7 yellow, cue 1, black 0; uncertain | Nearest black fits have 18, 19 and 18 supported sectors; all fail the unchanged 21/32 sparse-black gate. The trace reports 14, 13 and 14 exterior-rejected sectors, with no fitted red/yellow neighbour or body sectors credited. |

Across all ten frames, `layoutUncertain=false`; the broad `uncertain` flag remains true. The expected reduced inventories are observed in the absent controls, but the detector does not claim a clear layout or readiness.

The restored positives have a directional diagnostic: unsupported sectors 22–28 point toward the independently detected cue centre. In frame 288 the cue centre is about 52 analysis pixels from the fitted black centre, in sector 24; the balls are visibly separate. In the cue-absent positive, the corresponding fit has 24/32 support. This is an association in one staged setup, not proof that the cue caused the rejection. The cue's shadow, nearby cushion appearance, local background sampling and other image effects remain possible contributors. No cue or neighbour support was credited to the black.

The candidate-to-reference offsets (about 3.6, 4.8 and 3.6 width-800 analysis pixels for frames 288/331/370) are diagnostic only. Raw reviewed points are source pixels. The replay maps them through `calibration.Map` and `AnalysisCoordinates.FromTable` to rectified analysis pixels for the trace. Its `circles` fields are normalized table coordinates; black-fit centres and transformed reference points are rectified analysis pixels. Mixing those coordinate systems would produce an invalid distance. No centre-accuracy score is reported.

## Run artifacts

| Run | Frames | Report SHA-256 | Manifest SHA-256 |
| --- | --- | --- | --- |
| `mcs2831-161119` | 2831 | `2926f7e8e88eb3280e43a2e3cffaba5af997c269b1cb01060c92e35c141b91e2` | `851eda7e4bc649803419cbea79581d06da413b1cb37cc1cb8e63a0d40f09ce6b` |
| `mcsblackabs-161133` | 542, 582, 622 | `4ec11e1cf2314293e04b5ea71b9f6cf58adf5d8a40dd33cac33b81831325829d` | `8865550a3247f0269aef7a94396fecb1feb5b3dc392b82c1dd0272cdc9efc196` |
| `mcscueabs-161203` | 295, 334, 373 | `a1abd03d015dba37f50fe4aa2e22091c904f73d048fa5f9dd433753f1f0c809e` | `0e30ddaf88b34b626534e8a2c0a2a5119ad0b148ca76c6f73dffea4cdd65d257` |
| `mcsrestore-161224` | 288, 331, 370 | `8eb2a83f1d776791336e07b4bb64836bb44679c5faebd23abdc8859b7a0623df` | `23e17d7d8715b2dfa052a127f80e52019ec078f267f974c9d67f99929daff505` |

Run files are under `../../work/o08/live-detect-black-pair-v1/physical-controls/stage-replays/midcushion-20261101/runs/`. The original PNG/receipt pairs remain unchanged.

## Interpretation and next experiment

This is one correlated Development setup, not a held-out score, accepted accuracy measurement, live freshness/readiness result, or physical acceptance. Frame 2831 remains unmatched. The data localize the restored-positive miss to sparse-black boundary support before classification; they do not establish a causal cue effect or justify lowering support. The 21/32 gate, calibrated geometry, classification, pocket, duplicate and uncertainty safeguards remain unchanged. Do not promote a relaxed gate, arc fit, or cue/black mutual-occlusion credit from this evidence.

The next bounded candidate should inspect the cue-facing boundary/background samples and demonstrate at least 21 independently supported sectors on the positives while retaining zero black proposals at the reviewed expected-empty location in the matched black-absent controls. Keep the same source frames, calibration, execution shape and settings except for one predeclared candidate delta. Reject it if the absolute support and existing admission safeguards cannot be met; then collect the independent pocket/cushion and lighting Development groups before Holdout.

## Device state after the replay

At `2026-11-01T16:23:45Z`, fresh mounted-camera `/status` was `Stopped`, revision 55, timeline `9c88cf01eebe4b51a368dd16fa506665`, 30,544 completed frames, 139 clips, zero viewers and 67% battery. The token-free readback SHA-256 is `165c8f9843249142cb00cfdb6f0ba0a0d8bfda8d235c8bebb97002441e215583` (file: `../../work/o08/live-detect-black-pair-v1/physical-controls/stage-replays/midcushion-20261101/device-state/pixel10-status-final-20261101.json`).

Pixel 11 Pro XL `66181FDDV001HS` is at Production companion Home, visibly showing **Camera ready** and **Start live**; the video surface is black and the old verifier result text remains historical. The fresh screenshot SHA-256 is `92e77e3a7776acc6ff12fffa1feebd251aed853e83fa38d1fe978b254f315a50` (`../../work/o08/live-detect-black-pair-v1/physical-controls/device-state/pixel11-home-final-current-20261101.png`). Installed Production companion remains 0.8.60/code68, SHA-256 `bf26e48a44769f352dc9ea09270c85fc0d0ee86d19de0a5fc22ffd456ea11c7d`; isolated VisionChecks matches the replay APK above. No capture started, no clip was added, and Cast/TV and camera packages were untouched. D1, D2 and E remain open.
