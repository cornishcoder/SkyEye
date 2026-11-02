# O09 verifier sample-gap latency isolation — 2 November 2026

## Result

`O09-VERIFIER-EVIDENCE-LATENCY-ISOLATION` **PASS 4/4 focused queue assertions** and **Production Release build PASS, 0 errors / 9 warnings**. The dedicated `SkyEye.Checks --verification-capture` run also passed the operation-scoped receipt/budget checks and the deferred-write checks. No detector threshold, freshness limit, sample minimum, readiness rule, or capture result was changed.

The diagnostic trigger was the first normal/train rack operation, `274aea9e-a149-4645-aa42-851854c29a5b`, which saved one frame and failed its physical verifier at `1/5`, `Unsettled (SampleGap)`. The accepted frame's measured `processingMs` was **930.3129 ms**, including **626.2462 ms** in the synchronous evidence callback and **299.6069 ms** worker measurement. The next source frame arrived **966.744 ms** after the accepted frame, exceeding the existing **800 ms** maximum gap by **166.744 ms**. Cadence wait was zero. The next frame was rejected before analysis. This identifies synchronous evidence preparation/storage as a substantial avoidable contributor in this operation; it does not prove that it was the only cause of the gap or that the change below will pass the verifier.

The accepted frame reported 16 detector objects (cue 1, red 7, yellow 7, black 1), `detectionUncertain=false`, and `obstructed=false`. Those are detector outputs, not independent truth. Because the run had one accepted sample, it proves neither stillness nor readiness. Do not count it as a request window, soak, inventory pass, or spatial training label.

## Change under evaluation

`ShellVerificationCapture` now snapshots the exact decoded bitmap pixels while the borrowed frame is valid, then queues PNG compression, hashing, quota checks, and file/receipt writes on an ordered background writer. JPEG input bytes are copied and written by the same queue. The capture operation drains the queue before publishing its final saved count and reports failed writes. Existing receipt validation and storage quotas remain in force. The queue continues later writes after an individual write failure so partial evidence remains auditable.

Focused checks cover worker execution outside the verifier caller, serialization while a write is blocked, ordered completion at drain, and failure reporting without blocking later frames. These checks establish queue behavior; they do not measure Pixel throughput or guarantee a smaller source gap. The next physical run must independently measure `evidenceCaptureMs`, `processingMs`, and source gap. If `SampleGap` recurs, retain the 800 ms limit and inspect the new timing fields rather than accepting a partial run.

## Provenance and validation

- Diagnostic source checkout: `7960b9828491a1b7486c2f3601b8cb00fe35943f` (Production 0.8.60 / code 68 installed for the measured run); the reported timing is historical and is not a current device observation.
- Matching operation report: `work/o09/device-state/normal-anchor-historical-verification-20261102.json`, SHA-256 `F0061B6C91309169A128EF5E2220BC244AE259162B0B1B2EAFC96825EEF4B9C7`.
- Source image: frame 1530, PTS `4093819211` µs, timeline `bc9a6aadfd7345b49414a46b548b4690`, calibration `0ebf6a4965914c8bb3159f97686a6419`; pixel SHA-256 `b50d30a90e59d6626e897993485e1faffcffc2ccecf542c02dfabbbe416d5807`; receipt SHA-256 `06a8767f6514475b252d9b52cfb66bfe2394955d4192d9e03a5817bddfa787a2`.
- Focused command: `dotnet run --project tests/SkyEye.Checks/SkyEye.Checks.csproj -c Release --no-restore -- --verification-capture` — exit 0. The four new queue assertions passed; the existing receipt, hash, partial-run, and quota checks also passed.
- Build command: `dotnet build src/SkyEye.Companion.Hybrid/SkyEye.Companion.Hybrid.csproj -c Release -p:SkyEyeProduction=true --no-restore` — exit 0, 0 errors, 9 warnings. Warnings are in generated OpenCV, unavailable WebRTC observer, existing VisionDiagnosticLabeler, and duplicate native-library groups; no new nullable warning remains.
- Candidate signed APK: Production **0.8.61 / code 69**, `work/production/bin/Release/net11.0-android/android-arm64/uk.co.skyeye.companion-Signed.apk`, SHA-256 `9F3465F4D4A5BD3F5F848BA0D8DE299BA4D9E50AE05F7808DA8F7F033F3A0DCA`. It has not yet been installed or physically exercised.

## Acceptance counts and next work

- O09 Development: **0/12** independently reviewed usable layout anchors; the plan covers 24 layout/control-state acquisitions.
- Criteria-complete lighting strata: **0/2**. Normal daylight has a partial handset ALS observation; exposure remains unknown. The current table is staged in the distinct six-region Layout02 spread, and the user confirmed hands clear.
- Detector inventory: **0/3**; Holdout unopened; physical D1/D2/E: **0/3 proven**. Software suites **3/3 PASS** and focused motion **20/20 PASS** are carried forward, not rerun here.
- Last documented post-capture device state belongs to the earlier operation: camera `/status` `Stopped` and companion Home `Start live session`. Obtain fresh readbacks before device action; an old stopped state is not current evidence.

Next target: **`O09-NORMAL-TRAIN-LAYOUT02-BLIND-ANCHOR`**. First install this candidate with `scripts/Install-ProductionCompanion.ps1` so the existing app identity and private data are preserved; then verify installed version/hash, camera `/status:Stopped`, and Home `Start live session`. Use the already-staged layout for one bounded verifier operation. Stop at its first result boundary and record its source-gap and processing timings. Keep raw manual centres blind to detector output and show the actual raw frame inline before asking for label review.
