# Frame 978 Development screening baseline — 1 November 2026

## Scope and source

This record closes the first frame 978 O08 scoring-path milestone with two separate width-800 offline arms: the current-source `.65` baseline and an exact captured-settings `.4` reproduction. It is a single reviewed Development still, not a matched control, Holdout, physical acceptance result, or D1/D2/E pass.

- Source frame ID 978; source PTS `48922017388`; 1920×1080 PNG SHA-256 `b368348c63d924f5c7834e8e849b7287225ab525b470bdc3b85f7a8278cbbe70`.
- Raw source receipt SHA-256 `653e1ea4bc6c927ba592ad796a6c084e49103c27003d13e5d23d0c98a3144034`; corpus-derived receipt SHA-256 `12910e79eaf9d5189a7c31bd572bbe9fd5636d6ca905e39da1d58746b5b10b52`.
- Corpus sample `code65-frame-978-v1` maps to that exact frame/PTS/scope/pixel hash. The user-reviewed label record SHA-256 is `4f8a2c1139b6e007e4a4789b1e6a9592b4d880d042772a709b36b4ff210b2fc1`: cue 1, red 7, yellow 7, black absent; rail chalk is excluded.
- The Development corpus is `o08-code65-frame978-development-v1`, sequence `code65-frame978-v1`, purpose `StillRegression`. Manifest SHA-256 `09d383902f0cd2c271a555f1ed41be7016d958dd7d251eb2b5b0affbb4366950`; split-lock SHA-256 `49f8e735f49669b860cd0447a9db53fc4292b1194d957a76246b4504a2f93e76`; labels SHA-256 `10276ef090853643e6be8167afe05299120e8526d33c3defe8e4ef7fcad20022`.
- Calibration ID `6d304dd864f84c8486ee30844fe685b1`; the four corners were copied from Code64 source receipt 715 because Code65 did not retain corners. The recorded transform is internally round-trip checked, but does not establish physical cloth-plane centre accuracy. The 128-sample label-uncertainty envelope is `0.0097943` table units, greater than the proposed `0.25R = 0.00675` bound. The Development matching tolerance `0.010` does not replace that bound.
- Validation remains one Development sample, zero Holdout samples, `accuracyGateReady=false`.

## Checkout, package and device

- Vision implementation source checkout: commit `56a8476` (`Allow explicitly idle camera for saved-frame replay`). The isolated Vision Device APK was built from this source; SHA-256 `880792e6a25cec539b6d9c0cdba3ca6f4bdfa51a0cd9d9e263f1685198ee900e`.
- Replay-manifest producer fix checkout: commit `1871d54` (`Preserve replay source frame arrays`). This PowerShell-only change did not alter the already-built Vision Device APK or detector implementation.
- Pixel 11 Pro XL serial `66181FDDV001HS`; isolated package `uk.co.skyeye.visionchecks` version `0.1`, separately installed for these saved-frame runs. Production companion remained `uk.co.skyeye.companion` version `0.8.59` / code 67, installed APK SHA-256 `76f4b22037df10679fc07a2200dfbcb8d26499046cdd53e809974ff150b71720`.
- Mounted-camera `/status` before and after both runs was `Ready`, not `Stopped`: `armed=true`, `host=null`, zero history, zero completed frames, `lastFrameSourceTimeUs=0`, empty timeline, `clipCount=139`, and zero live-transport viewers. `Ready` is the service's initial non-capturing state. The companion remained foreground at Home with `Start live session`; Live Detect reported unavailable until a live session starts. No camera capture, camera package update, Cast, or TV action occurred.
- Screen-only batches contained one frame (`independentFrames=false`). No explicit warm-up call was excluded; the first measured analyzer call is retained. Offline analyzer timings below are single-call timings on this run/device, not live cadence or source freshness. Runner total time includes install, transfer and instrumentation and is not live latency.

## Configuration and score

Both runs used width 800; edge margin 23; pocket exclusion 48; minimum Hough support `.75`; sparse-colour support `.625`; sparse-black extent `1.3`, support `.65625`, top 12 candidates, radius-first ranking and minimum peak factor 8; local illumination normalization off. The two boundary extents are recorded independently below.

| Arm | Production 800 / packed-colour extent | Captured configuration match | Analyzer call | Result at Development tolerance `.010` |
| --- | ---: | --- | ---: | --- |
| Current source `.65` | `.65 / .65` | No; deliberate packed-colour delta from recorded `.4` | 1133.0855 ms | 15/15 matched; 0 false positives; 0 missed; 15 correct colours; centre-error P95 `0.00864613`; `uncertain=true` |
| Captured `.4` | `.4 / .4` | Exact match | 1190.3498 ms | 15/15 matched; 0 false positives; 0 missed; 15 correct colours; centre-error P95 `0.00800051`; `uncertain=true` |

The labels contain 15 visible balls and no black; both outputs contain cue 1, red 7, yellow 7, no black, with `layoutUncertain=false` and `obstructed=false`. The scored centre metric is in table-2x1 units for this single frame. No reviewed ball radii are available to express centre error in radii. The two arms differ at 12 of 15 detected centres; maximum paired displacement is `0.00106066` table units and mean is `0.00043709`. Counts and uncertainty did not change. The timing values are not a matched performance result.

The `.65` report marks `configuration-mismatch` / `CaptureConfigurationExact=false`; its recorded and replay configuration identity hashes are respectively `95d18efd8f550106669206f2132627c90d58a7bca7a713d104c93a7188849149` and `6d4e4f3e2fc28bba1a56c637f6783e1d743e082d7807362324344a102fefa414`. The `.4` report marks `exact-match` / `CaptureConfigurationExact=true`; both configuration identity hashes are `95d18efd8f550106669206f2132627c90d58a7bca7a713d104c93a7188849149`. These labels are not generalized accuracy results.

## Hash-bound run and score artifacts

| Artifact | `.65` current-source run | `.4` captured-settings run |
| --- | --- | --- |
| Run directory | `device-replays/f978current65v2-095056/` | `device-replays/f978captured04-095130/` |
| Runner manifest SHA-256 | `0e8ab51518dbbf6958870209c8ee88cac941771af7dba431a565eb7bc18db2de` | `688b133ea692a24a160875fb1d7e2c0c2f7d201ed07d288e41edf3d0893e1258` |
| Device report SHA-256 | `fba3a6e9a0743afd43d19786fe67fadba5186f75a052e049c3c8312ad237eeef` | `051f15c088afeb5209be5ab338f8fce56633477aaa5ac49d4035d57ac940265e` |
| O08 export directory | `device-replays/f978current65v2-o08-export/` | `device-replays/f978captured04-o08-export/` |
| Export manifest SHA-256 | `dbf947a290466c958951ef72d49c25e580a1666cc8328c81de23a1072c503c0a` | `7e447faf79cd689127e9b0d1ac75caa1544ebea7118b2af1c6fe2c3e07d28b1e` |
| Detections SHA-256 | `bff076b5b3eeccef950246b8ebbdae8b7e66ee6182b1798287a480a4143dcdbf` | `5e85aad0b151b2e86ec8bba49198fed2222b55fd59d1d434eb081d6dca0641ab` |
| Development baseline score SHA-256 | `a7dcc527d6a507829cd0a39f9495ebc52e5000d6493347a9c7416449a465ddb3` | `6f6e87d7abd2a1fa15bb61f932cd85288add8c9bfa071963de16f57e0fc30d49` |

Commands used for each run:

```powershell
./scripts/Run-PocketLipBoundaryAudit.ps1 -SourceRelativePath 'work/o08/frame978-development-v1/replay-input/978' -Frames 978 -ScreenOnly -RunPrefix f978current65v2 -Purpose 'Current-source width-800 0.65 Development baseline on exact saved frame 978; source implementation APK built from 56a8476; source-frame array manifest fix at runner 1871d54; separate from captured 0.4 reproduction.' -EvidenceRelativePath 'work/o08/frame978-development-v1/device-replays' -EdgeMargin 23 -PocketRadius 48 -BoundarySupport 0.75 -SparseColourSupport 0.625 -Production800BoundarySearchExtent 0.65 -PackedColourBoundarySearchExtent 0.65 -SparseBlackSearchExtent 1.3 -SparseBlackSupport 0.65625 -SparseBlackCandidateLimit 12 -SparseBlackRankByRadius $true -SparseBlackMinimumPeakRadiusFactor 8
./scripts/Run-PocketLipBoundaryAudit.ps1 -SourceRelativePath 'work/o08/frame978-development-v1/replay-input/978' -Frames 978 -ScreenOnly -RunPrefix f978captured04 -Purpose 'Historical captured-settings 0.4 configuration reproduction on exact saved Development frame 978 only; not the Code70 six-frame result or physical acceptance. Replay manifest producer fix at 1871d54.' -EvidenceRelativePath 'work/o08/frame978-development-v1/device-replays' -EdgeMargin 23 -PocketRadius 48 -BoundarySupport 0.75 -SparseColourSupport 0.625 -Production800BoundarySearchExtent 0.4 -PackedColourBoundarySearchExtent 0.4 -SparseBlackSearchExtent 1.3 -SparseBlackSupport 0.65625 -SparseBlackCandidateLimit 12 -SparseBlackRankByRadius $true -SparseBlackMinimumPeakRadiusFactor 8
dotnet run --project src/SkyEye.Observation.Workbench/SkyEye.Observation.Workbench.csproj -c Release -- export-o08 --root work/o08/frame978-development-v1/corpus-v1 --manifest work/o08/frame978-development-v1/corpus-v1/manifest.json --sequence code65-frame978-v1 --run-dir <run-directory> --output-dir <new-export-directory> --lock work/o08/frame978-development-v1/corpus-v1/split-lock.json
dotnet run --project src/SkyEye.Observation.Workbench/SkyEye.Observation.Workbench.csproj -c Release -- baseline --root work/o08/frame978-development-v1/corpus-v1 --manifest work/o08/frame978-development-v1/corpus-v1/manifest.json --detections <export-directory>/detections.json --tolerance 0.010 --output <export-directory>/baseline-development-tolerance-0.010.json
```

The report mode `screening` matches its runner manifest. Export verifies the frozen Development lock, report/manifest mode, sample-to-sequence mapping, source frame ID/PTS/scope/dimensions and PNG hash, copied/derived receipt hashes and calibration corners before writing detections. The baseline scorer enumerated one labelled frame, scored it, and returned `unscoredSampleIds=[]`; it scores stored detections and does not rerun CV or select a corpus split.

## Producer failure retained and correction

The first `.65` device run `device-replays/f978current65-094817/` completed, but its one-frame runner manifest serialized `sourceFrames` as an object instead of the array required by O08 export. The report/manifests were left unchanged and that run was not scored. Commit `1871d54` fixes one-frame serialization and adds `tests/CameraReplayManifest.Checks.ps1`, which verifies both one- and two-frame array shape and receipt/pixel provenance. `tests/CameraReplayPreflight.Checks.ps1` also passed for Stopped and fully evidenced Ready-idle handling. The corrected `.65` run `f978current65v2-095056` exports and scores successfully. The earlier producer mode fix in `d3bd491` and its focused Observation checks keep the runner's `screening` mode aligned with the device report and reject mode mismatch; retained reports were not relabelled.

## Next gate

Do not tune from these single-frame scores. The user later confirmed suitable lighting and hands-clear staging for the four-step lower-left control sequence. Those captures are indexed in [the physical-control attempt record](live-detect-bounded-control-attempts-20261101.md); each verifier ended `SampleGap` at 1/5 or 2/5, and no centres or absence regions have been independently reviewed. Keep the complete sequence on the Development side. The timing phase audit is implemented, but the installed companion's bounded startup attempt decoded no frames and produced no verifier sample; diagnose stream request initiation/routing while capture is stopped. Display the exact source pixels with clearly marked draft centres for user review before scoring; retain the black/pocket ambiguity and possible frame-455 lighting mismatch unless resolved. Then collect additional matched Development groups; freeze a separate protocol and candidate before any independent Holdout capture. The latest camera readback after the startup attempt is Stopped, revision 15, with 139 clips and zero viewers. D1, D2, and the physical event/clip gate remain open.
