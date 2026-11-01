# Black/cue Development spatial baseline — 1 November 2026

This report combines the user-reviewed lower-left black pair with the four-step lower-right black/cue control group. It records one reproducible current-source spatial baseline. It does not pass D1 inventory/readiness, D2 installed-phone readiness, or E physical events/clips.

## Frozen Development evidence

Corpus `live-detect-20261101-black-cue-development-v3` contains six real `StillRegression` frames. The two lower-left controls remain in setup group `live-detect-20261101-lower-left-controls`; the four lower-right controls remain in `live-detect-20261101-lower-right-controls`. All six are Development. The lock has no Holdout sample, and `accuracyGateReady=false`. Workbench validation reports six real still regressions, zero timed-shot sequences, zero verification sequences, and no unscored sample IDs.

The versioned acceptance contract is `work/o08/live-detect-black-pair-v1/corpus-v3/acceptance-contract.json`. It was not changed after scoring. The selected explicit Development matching tolerance was `.010` table-2x1 units (about 0.37 of the labelled radius), matching the earlier lower-left screening score; it is not the `.00675` maximum accepted centre-error p95 and is not a pass threshold. The four lower-right labels have a separate user-confirmed approximate ±3–4 source-pixel precision estimate. Sampling a 4px source-space uncertainty circle through the receipt-bound `TableCalibration` gives a maximum of `.0056026032` table units across those 62 visible labels, below `.00675`. This is an uncertainty estimate, not measured detector accuracy or ball-radius review. The two lower-left frames retain rough ±10px visible-centre and ±15–20px pocket-black uncertainty; those labels cannot support the contract's geometry accuracy claim and must be refined for a future accuracy set.

The source-to-table transform records 1920×1080 source pixels, top-left origin, x-right/y-down axes, ordered receipt corners, and the mapping to `(0,0),(2,0),(2,1),(0,1)`. Forward/inverse checks use `SkyEye.Core.TableCalibration`; the maximum truth-point round-trip error is `8.72e-13` source pixels. [The full source overlay](../../work/o08/live-detect-black-pair-v1/exports/merged-v3-current/source-overlay-contact-v3.png) and [zoomed score-point crops](../../work/o08/live-detect-black-pair-v1/exports/merged-v3-current/source-overlay-precision-checks-v3.png) were visually inspected before scoring. Green circles mark reviewed visible truth, orange dashed rings mark expected absent locations only, and magenta crosses mark transformed detector outputs.

## Replay provenance

- Source checkout at replay: HEAD `25e8b26d38a86eb3dc725131d632f2321ca2d493`; the code tree matches code commit `438696d` (HEAD adds documentation only). No detector source or thresholds changed in this baseline.
- All six source frames were replayed serially on Pixel 11 Pro XL `66181FDDV001HS` with the VisionChecks package `uk.co.skyeye.visionchecks`, APK SHA-256 `2c670059d2d1104c431524ed83be76dac8eb85c3d983caad0eb45651ef4e879c`.
- The installed Production companion was verified separately as `uk.co.skyeye.companion` version 0.8.59 / code67, pulled APK SHA-256 `0751393c12afbbad838522c9a69f2a8ea8c97369ae794b3fc47c2c248d9e1a55`. It was not used for this offline score and was not installed or changed during this work.
- Report mode and O08 export mode are both `screening`; each export binds its source pixel hash, receipt, run manifest, replay report, APK, sequence, corpus manifest and split-lock hash. Batch size is one, `independentFrames=false`, no explicit warm-up call is excluded, and the first measured analyzer call is retained. `CaptureConfigurationExact=true` for all six exports.
- All six captured/replayed analyzer identities match: `6d4e4f3e2fc28bba1a56c637f6783e1d743e082d7807362324344a102fefa414`, algorithm `body-priority-packed-colour-peaks-v25-neutralceiling94-edge23-pocket48-sparse20-review75-redyellow-black-radius13-support21`, width 800, packed-colour and production boundary extents `.65`, edge margin 23, pocket radius 48, minimum boundary support `.75`, sparse-colour support `.625`, sparse-black extent `1.3`, sparse-black support `.65625`, candidate cap 12, radius-first ranking enabled, minimum peak-radius factor 8, and local illumination normalization disabled. The complete recorded/replay settings are preserved in each report.
- Four lower-right one-frame offline analyzer wall times were 819.6255, 950.6694, 846.9551, and 952.4275ms for frames 1049, 461, 477, and 375. These are single offline analysis measurements, not live request completion, frame age, camera freshness, or a latency pass. No run used multiple independent frames.

| Frame | Replay manifest SHA-256 | Replay report SHA-256 |
| --- | --- | --- |
| 678 | `ffbf43c813972dfeba496dec2d7f6dae7f4c1f76f0603399524f170146736584` | `093649e4b7480a81aaa208c3d19ca3589d2af768bae94c4ead2a04d4d3f3628b` |
| 844 | `220af6dd346de0999e4d35340e5f40ea351d5cc0c268bbd152ea197531f0d929` | `af01ad8f29e28fe2c32dd9ab9cad807ac43b6e86a89f64a66847099b4360b3a2` |
| 1049 | `4be9d0057a03b8785a92fa28e4be2594884f0af686d16e6f04601e396359b08a` | `37fc1d4a01fd7aaea9be9d2de4a53959e2d73d39c1e8b98a3ab3cda49ed1d8cf` |
| 461 | `796b9c2c7638f9634120fe34baa7d7c8a64aa96e791d054184cbd756452b956c` | `23e8c1525809173a89c668ad189baffe2971adb117bdb5af585faa5e8a84f216` |
| 477 | `685f15f53f52cb23802fcdbd6c77eb21dab75e1ced3950714e67d4b2901f8059` | `9d0b096972ca99ef83b059f7006162cedf40ee87e6c6e4d299a332107b6fc1d2` |
| 375 | `bc8a4fd94a4895fea4bf248e6b701a14570d8e7bce8a9a3dacfb537e20eb2f53` | `04f0d7dedfc4c819cefe87bedf9840a365cefa7a3fb3aaaa21a5549ea222973b` |

## Development baseline at `.010` table units

Workbench scores stored replay detections; it does not rerun CV or infer a split. Frame p95 is nearest-rank over matched visible centres only; unmatched labels are separately reported. Colour correctness is reported for matched pairs.

| Frame | Control | Visible | TP / FP / miss | Matched colours correct | `uncertain` | Matched-centre p95 | Unmatched labels |
| --- | --- | ---: | ---: | ---: | --- | ---: | --- |
| 678 | Lower-left black+cue present | 16 | 15 / 1 / 1 | 15 | false | `.0082367341` | `black-01` / `black-1` |
| 844 | Lower-left black absent, cue present | 15 | 15 / 0 / 0 | 15 | true | `.0082432373` | — |
| 1049 | Lower-right black+cue present | 16 | 15 / 1 / 1 | 15 | false | `.0083761600` | `Y3` / `yellow-2` |
| 461 | Lower-right black absent, cue present | 15 | 15 / 0 / 0 | 15 | true | `.0054134817` | — |
| 477 | Lower-right black present, cue absent | 15 | 15 / 0 / 0 | 15 | true | `.0068218255` | — |
| 375 | Lower-right black+cue restored | 16 | 16 / 0 / 0 | 16 | false | `.0099638523` | — |

Across the six frames, 91 of 93 visible labels matched at `.010` (two false positives and two misses); aggregate descriptive precision and recall are both `91/93` (`97.85%`). The nearest-rank p95 of the 91 matched centre errors is `.0082367341` table units; median is `.0016382604`, maximum `.0099638523`. The score has no reviewed radii, so radius-normalized error is unavailable. The matched-centre p95 exceeds the frozen `.00675` table-unit geometry proposal, even before accounting for the two unmatched labels, and the lower-left label uncertainty is too rough for acceptance. It does not establish an accuracy pass.

The lower-right present/absent controls behave as follows in these exact stills: frame 461 detects all 15 visible balls while black is absent; frame 477 detects all 15 visible balls while cue is absent; frame 375 detects all 16 restored balls. The initial frame 1049 detects 16 objects but its nearest yellow output is `.012584` table units from reviewed `Y3`, so Workbench records a miss and an extra candidate at the `.010` tolerance. The source crop confirms that offset at the left cushion. The lower-left frame 678 black point remains a `.010999613` miss against its rough pocket-centre label; this is still label-limited and should not drive a threshold change.

All three reduced-inventory frames (844, 461, 477) correctly set broad `uncertain=true`, consistent with the analyzer requiring both cue and black for a complete known inventory. This is only a one-frame spatial observation. It does not test a fresh settled baseline, history-supported absence, visible phone readiness, unchanged-table stillness, false-ready windows, or event/clip behavior.

## Artifact binding

- Corpus manifest SHA-256: `4756b8cd42d926f24dffc4d9c288d23f4af78ba7312ba16da821ae82ef1d3dd9`
- Split-lock SHA-256: `1f4b9950396d48797f47bccf06f608bef08d2e8acde422b1fa28ec9a8956d210`; a new `freeze` result matched the saved lock exactly.
- Contract SHA-256: `1574245e516431ea3087c9202875d4b26c17701f4d424457f722f987ead47563`
- Source-to-table transform SHA-256: `c039970db7e6886908aba8b50bcd364ec905bc69ae801a7086351700b8d92f48`
- Lower-right precision-review record SHA-256: `b3d3652761aacc4fab59ce531bb77ae5bd8278ff89260d2eca73e5daf26ad150`
- Merged O08 export manifest SHA-256: `7db9cf296a98b24f987f4a9c090f902f77142efb032ebbafd39fe326babc38aa`
- Merged detections SHA-256: `4d2b20f1278f0f3ddbdd277eaaa5c5dbee406616c6d93a1bcb9acb4e8042f2cd`
- Baseline report SHA-256: `51e16be52b6379008e2e7f48279f441d861aa5014cd2850d2a09599cfeaa1c81`
- Source overlay sheet SHA-256: `1fd02c4dea520de49ea1957af7f27dc640f267e238f010a721bcb220b26f207c`
- Precision crop sheet SHA-256: `45dc0e5e92f3b14e2d020b7f3f7a9f7316ebe5e99909a2abbbf5927e8a1d5582`
- Source overlay point file SHA-256: `3892729e1a6342b5ae4dc060d51d4681864410b5b70e1e527a786922fd3168c1`
- Six-export directory list SHA-256: `aad92272823c30773868af648c42a726e468a5351c69048ae60f551ec4ea38d4`

Per-frame source PNG and receipt hashes, operation IDs, PTS, timelines, generations and verifier outcomes are in the [physical control record](live-detect-bounded-control-attempts-20261101.md). The four lower-right runs each retained their original screening report and manifest hashes in the merged O08 export. Their verifier result remains `1/5 SampleGap`, not a stillness or readiness pass.

The checks and score were produced with these commands from the repository root (the export directory list contains the six versioned per-sequence export folders):

```powershell
dotnet src/SkyEye.Observation.Workbench/bin/Release/net10.0/SkyEye.Observation.Workbench.dll validate --root work/o08/live-detect-black-pair-v1/corpus-v3 --manifest work/o08/live-detect-black-pair-v1/corpus-v3/manifest.json --output work/o08/live-detect-black-pair-v1/exports/merged-v3-current/validate.json
dotnet src/SkyEye.Observation.Workbench/bin/Release/net10.0/SkyEye.Observation.Workbench.dll freeze --root work/o08/live-detect-black-pair-v1/corpus-v3 --manifest work/o08/live-detect-black-pair-v1/corpus-v3/manifest.json --output work/o08/live-detect-black-pair-v1/exports/merged-v3-current/freeze-verification.json
dotnet src/SkyEye.Observation.Workbench/bin/Release/net10.0/SkyEye.Observation.Workbench.dll merge-o08 --root work/o08/live-detect-black-pair-v1/corpus-v3 --manifest work/o08/live-detect-black-pair-v1/corpus-v3/manifest.json --lock work/o08/live-detect-black-pair-v1/corpus-v3/split-lock.json --exports work/o08/live-detect-black-pair-v1/exports/v3-export-dirs.json --output-dir work/o08/live-detect-black-pair-v1/exports/merged-v3-current
dotnet src/SkyEye.Observation.Workbench/bin/Release/net10.0/SkyEye.Observation.Workbench.dll baseline --root work/o08/live-detect-black-pair-v1/corpus-v3 --manifest work/o08/live-detect-black-pair-v1/corpus-v3/manifest.json --detections work/o08/live-detect-black-pair-v1/exports/merged-v3-current/detections.json --tolerance 0.010 --output work/o08/live-detect-black-pair-v1/exports/merged-v3-current/baseline-development-tolerance-0.010.json
```

## Device state and next work

Before the offline export/build, the mounted camera endpoint returned `Stopped`, revision 28, timeline `94748b6e18d648f18b3709842afba4cb`, 18,800 completed frames, and 139 clips. No camera capture, companion install, clip deletion, Cast/TV action, or camera-package action occurred during scoring. The companion's last post-capture Home readback showed **Start live session**; during later image review the phone foreground was ChatGPT, so its current Home state is not claimed.

Next: keep this Development score immutable; refine the lower-left labels before using them in a geometry accuracy set; trace the exact lower-right `Y3` cushion-side candidate on frame 1049 and its matched negatives; add distinct reviewed layouts and separate held-out sessions/lighting strata. Then run D1 offline coverage, D2 installed-phone stationary/readiness controls, and only after both pass, E physical events, bounded clips and canonical review. No detector setting was changed as part of this baseline.
