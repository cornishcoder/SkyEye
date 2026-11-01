# Lower-left black present/absent Development score — 1 November 2026

## Result

This records a spatial screening score for the user-reviewed lower-left black-present frame 678 and black-absent frame 844. Both belong to the same user-confirmed setup group, `live-detect-20261101-lower-left-controls`; the group remains wholly Development despite the controls being captured on separate camera timelines.

At the explicitly selected Development matching tolerance of `0.010` table-2x1 units, frame 844 scored all 15 visible labels, including the cue and seven balls of each colour. Frame 678 matched the cue and all 14 red/yellow balls, but the approximate black label at the lower-left pocket and the detected black candidate fell just outside that matching tolerance. The score therefore reports one unmatched visible label and one extra prediction on that frame. This is a borderline spatial mismatch, not a confirmed detector miss: the user-reviewed black point has only rough `+/-15–20 px` uncertainty, and the unmatched prediction is about 7.85 source pixels from that approximate point. No numeric label tolerance or reviewed ball radii were provided.

The pair is useful Development evidence for the present/absent distinction. It does not establish held-out accuracy, physical cloth-plane centre accuracy, stillness, readiness, or event/clip behaviour. No detector threshold was changed from this score.

The user-reviewed sheets show all four control frames and the enlarged pocket region: [full-table marker sheet](../../work/o08/frame978-development-v1/physical-controls/review-contact-sheet-20261101.png) and [lower-left crops](../../work/o08/frame978-development-v1/physical-controls/lower-left-review-crops-20261101.png). The user reviewed the markers and replied “Looks correct.” The sheets remain approximate assistant-placed visual points, not detector output or exact surveyed truth.

## Source, review, and mapping

| Frame | Declared layout | Timeline / frame / source PTS | Source PNG SHA-256 | Receipt SHA-256 |
| --- | --- | --- | --- | --- |
| 678 | Black and cue present | `7a0e467a53be41d786af2b3369fc1b1b` / 678 / `4523100122` | `67c8040c35565b53541dfa1fb89d9d343ec62b0476a9983504c896300bbdf80b` | `a0452b13f2bd5b4a32a1312abc122e99a833b2c230f61b5177514869b898d834` |
| 844 | Black absent, cue present | `59e1e2774f8a4e50b6572bb21d5b612b` / 844 / `4807691744` | `5cfa1e1902e97169ca4cc7f2df769948f3e32af7252495fb69f549cc3f21df8a` | `0f24966b6f03d1c64717f0410ee5c1de6535fe7ae60f30f376bbe8a39bb35661` |

Both images are lossless `1920x1080` source pixels. The review confirms the shared Y1–Y7/R1–R7 centres, cue point, black point and black-absence point in these frames. The label record keeps rough `+/-10 px` uncertainty for most balls and `+/-15–20 px` for the lower-left black/pocket point; radii and a numeric centre tolerance remain unset.

The source-to-table transform is preserved at `work/o08/live-detect-black-pair-v1/corpus-v2/source-to-table-transform.json` (SHA-256 `624E93DBE45A929D59E2A8287C64205F9B9D161F1D0EFD46898CFF88B203A6A7`). It records source axes/origin, each receipt's ordered normalized calibration corners, the `SkyEye.Core.TableCalibration` homography to table corners `(0,0),(2,0),(2,1),(0,1)`, and the mapped points. Calibration ID `106b54dacb38418697106473e7c12dc0` and all four corner coordinates are identical across both receipts. Forward/inverse round-trip error is at most `2.45e-13` source pixels. This checks the transform implementation and receipt consistency; it does not verify physical camera pose or remove perspective/parallax error.

Photometric review places frames 678 and 844 among the same three reference images; the separate cue-absent frame 455 shows the measured chroma shift. Lighting was held by user report during the pair. This is not a calibrated illumination measurement.

## Checkout, replay, and configuration

- Source checkout: `438696d0639efa1b1bed4808f230fb1a836925f3` (`Add redacted Media3 load diagnostics`). The score uses the rebuilt isolated VisionChecks package `uk.co.skyeye.visionchecks` version `0.1`, APK SHA-256 `2c670059d2d1104c431524ed83be76dac8eb85c3d983caad0eb45651ef4e879c`, Pixel 11 Pro XL serial `66181FDDV001HS`, one frame per run, and `screening` mode. The separately installed Production companion remains `uk.co.skyeye.companion` `0.8.59` / code 67, APK SHA-256 `0751393c12afbbad838522c9a69f2a8ea8c97369ae794b3fc47c2c248d9e1a55`; no install occurred for this score.
- Both export manifests report `CaptureConfigurationExact=true`, identical recorded/replay configuration identity `6D4E4F3E2FC28BBA1A56C637F6783E1D743E082D7807362324344A102FEFA414`, and the same width-800 analyzer settings. No explicit warm-up call was excluded; the first analyzer call is retained.
- Analyzer identity: `body-priority-packed-colour-peaks-v25-neutralceiling94-edge23-pocket48-sparse20-review75-redyellow-black-radius13-support21`.
- Settings: width `800`; radius `18–35`; separation `45`; morphology kernel `5`; Hough dp `1.3`, edge threshold `150`, initial/normalized votes `15/14`; hue band `7`; pocket exclusion radius `48`; local illumination normalization off (kernel width `151`); fresh cue recovery and body proposal priority on; minimum Hough support `.75`; sparse-colour support `.625`; sparse-black extent `1.3`, support `.65625`, radius-first ranking; dark-body value ceiling `40`; verification body refinement off; packed-cluster edge margin `23`; packed-colour extent `.65`.

The two replay exports are separate current-source exact-frame runs, `llcur678-105415` and `llcur844-105424`. Their recorded analyzer wall times were `1183.938 ms` and `813.9396 ms`, respectively. These are single-call replay timings, not live latency, source age, or percentile estimates.

## Score

| Frame | Visible labels | Detections | Spatial matches | Unmatched label / prediction | Correct colour among matched | Centre-error P95* | Analyzer uncertain |
| --- | ---: | ---: | ---: | --- | ---: | ---: | --- |
| 844, black absent | 15 | 15 | 15 | none | 15 | `0.0082432373` | yes |
| 678, black present | 16 | 16 | 15 | `black-01` / `black-1` | 15 | `0.0082367341` | no |

`*` P95 is the scorer's table-2x1 centre error over spatially matched balls only; the unmatched black is excluded. No reviewed radii exist, so these values cannot be converted to ball-radius error or compared to the proposed `0.25R` gate.

The present-frame black centre differs by `0.010999613` table units from the reviewed approximate point, just beyond the declared scoring tolerance `0.010`. Inverse mapping puts the detector candidate at approximately `(112.84, 907.58)` source pixels versus the reviewed draft near `(105, 908)`, within the review's rough black-point uncertainty. Refine or independently review that centre before making a geometric detector claim. The absent frame has no extra black candidate.

Frame 844's broad `uncertain=true` is expected for an initial view missing black without trusted history; this spatial score does not relax that safety state. Frame 678's `uncertain=false` likewise does not imply temporal stillness or readiness.

## Frozen corpus and reproducibility

Accepted corpus files are under `work/o08/live-detect-black-pair-v1/corpus-v2/`. Both real still-regression sequences are explicitly `Development`, labelled and locked together under setup group `live-detect-20261101-lower-left-controls`. The frozen manifest SHA-256 is `9702DF436D74ACE647E206552C61BC787C99594C77772E67173E132CD806D68B`; split-lock SHA-256 is `770777F804613D20D560D06A442D0A883FF76E1330E155D2CDA5C1D832879CC8`. The copied versioned `acceptance-contract.json` SHA-256 is `1574245E516431EA3087C9202875D4B26C17701F4D424457F722F987EAD47563`. Workbench freeze and validate passed; validation reports two real still regressions, zero Holdout samples, no unscored sample IDs, and `accuracyGateReady=false`. The contract's proposed acceptance gates remain unpassed.

The exact scoring command was:

```powershell
dotnet run --project src/SkyEye.Observation.Workbench/SkyEye.Observation.Workbench.csproj -c Release -- baseline --root work/o08/live-detect-black-pair-v1/corpus-v2 --manifest work/o08/live-detect-black-pair-v1/corpus-v2/manifest.json --detections work/o08/live-detect-black-pair-v1/exports/merged-current/detections.json --tolerance 0.010 --output work/o08/live-detect-black-pair-v1/exports/merged-current/baseline-development-tolerance-0.010.json
```

The scorer consumes stored detections; it does not rerun CV or select the split. Score output SHA-256 is `8CE0109012CF6E8E93A84F7691AD4BB6D3F4FEBD3D9AFD7B0221F2A1EB6BEB5A`, merged detections SHA-256 is `7C52722EAC7A9AAD18F25516DB2166DB5DFCDEC0D89CCA1A73CF046D5D55E83C`. Per-run manifests bind the replay report/manifest, source receipt/pixels, configuration, device and APK:

| Frame | Replay report SHA-256 | Replay manifest SHA-256 | O08 export manifest SHA-256 |
| --- | --- | --- | --- |
| 678 | `093649e4b7480a81aaa208c3d19ca3589d2af768bae94c4ead2a04d4d3f3628b` | `ffbf43c813972dfeba496dec2d7f6dae7f4c1f76f0603399524f170146736584` | `0010bc2a1ad1c8f7167f5f5e10d85b1c2697b2d76b5213ce9380a1f5842463c3` |
| 844 | `af01ad8f29e28fe2c32dd9ab9cad807ac43b6e86a89f64a66847099b4360b3a2` | `220af6dd346de0999e4d35340e5f40ea351d5cc0c268bbd152ea197531f0d929` | `ce10785c4887216d26b39abc491eae1883f4bc8828aeedb12446a0c385ef5d75` |

The first corpus attempt under `corpus-v1` used `StillVerification`; Workbench correctly rejected it because these single stills have no reviewed visible interval or increasing interval receipts. It remains retained but was not scored. `corpus-v2` declares spatial `StillRegression` samples and passed validation without changing the source labels or tolerance.

## Gate status and next work

This pair adds one matched Development setup group only. D1 still lacks the required distinct reviewed layouts, independent sessions/lighting strata, reviewed radii or precision evidence, unsafe/source controls, and Holdout evaluation. D2 remains unpassed: the four verifier operations all ended `SampleGap`, and no sustained stationary-table readiness check passed. E remains gated on D1 and D2 and has no new physical event/clip evidence.

The current physical state was rechecked after replay: Pixel 10 camera `/status` at `2026-11-01T10:58:04Z` was `Stopped`, revision 19, 13,836 completed frames, 139 clips and zero viewers. Pixel 11 is at Production Home showing **Start live session**; Live Detect is unavailable while capture is stopped. A token-redacted readback is retained at `work/o08/frame978-development-v1/device-state/pixel10-status-current-safe-20261101.json`; the current phone screenshot and hierarchy are in the same device-state directory. No new capture, clip, match, Cast/TV action, or camera-package update was performed for this offline scoring step.

The stream-load diagnostic also remains relevant: the companion stream rendered successfully after restart, but that session lasted about 173 seconds before the Home stop action. See [the separate stream diagnostic](phone-stream-load-diagnostic-20261101.md); it is not D2 evidence.
