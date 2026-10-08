# O02 bounded motion feed and ROI measurements

7 October 2026. This slice adds an explicitly enabled source-scoped motion feed and bounded native fixed-region measurements. It is a foundation for O03/O04, with no canonical ball identity, shot boundaries or automatic GameState mutation. The normal release includes the implementation but starts no motion feed. Its developer enablement endpoint is present only in opt-in performance builds.

## Delivered behavior

`TableMotionFeed` owns one active frame and one replaceable latest frame. Pending ownership is released before another bitmap is acquired, so even transient acquisition stays within two image leases. A native metadata-only candidate query avoids another bitmap copy for repeated source frames and preserves a valid pending frame when polling faster than the decoder. Candidate identity is an allocation hint; the actual pixel copy still requires O02's exact surface timestamp receipt and before/after fencing.

The source, recording timeline, decoder generation, calibration reference and dimensions are frozen for each run. Changes terminate the run. Duplicate/backwards receipts, stale frames and late worker results are refused. Source PTS determines delta time; gaps over 125 ms reset motion history. Stopping disposes pending ownership immediately, waits for active processing and discards its late result. Worker/acquisition failure and cancellation drain the same ownership. A result that ages beyond admission bounds cannot advance processor history. Core tests also cover an acquisition that ignores cancellation and returns late.

`RoiMotionProcessor` accepts at most sixteen fixed pixel regions, each 32–256 pixels on either side. It crops the native bitmap before grayscale OpenCV conversion, retaining only previous gray ROIs. No full-frame Mat, JPEG, colour/Hough analysis or detector loop is introduced. Forward/backward pyramidal Lucas-Kanade feature flow uses bounded features, error/return consistency, image-bound checks and displacement agreement. Weak texture, occlusion and inconsistent evidence remain unresolved. Measurements are image-pixel displacement/velocity per source second, not ball coordinates, pot proposals or calibrated confidence. Fixed regions are not recentered tracks; region IDs are temporary sampling slots.

The implementation uses the existing OpenCV 4.14.0 native library and exposes only its needed video tracking bindings. Bulk coordinate reads remove the per-point JNI field traffic found in the initial benchmark. No upstream source code is copied. [OpenCV's tracking API](https://docs.opencv.org/4.13.0/dc/d6b/group__video__track.html) documents the status/error outputs and source-image requirements; the repository's pinned native binding is exercised directly by the device proof.

The companion's explicit diagnostic session freezes ROIs around a fresh, clear, confirmed settled layout. Background, playback/source changes, decoder/calibration invalidation and table editing end collection. Session completion disposes ROI history after the worker exits. No user-facing tracking switch is added before real-source accuracy/performance acceptance. Opt-in Lab diagnostics support `POST /motion-feed?enabled=1&hz=30`, explicit disable and read-only motion metrics; ordinary production has no such endpoint.

## Checked evidence and limits

- [Native ROI proof](evidence/o02-roi-motion.json): a controlled 2×1-pixel translation is measured at approximately 2.00001/1.00001 pixels, with source-time velocity around 60/30 pixels per second. Stationary texture, occlusion, reset, cancellation and a whole-image crop that must retain its borrowed source all pass. Synthetic native bitmaps establish those cases, not real ball accuracy.
- [Physical feed proof](evidence/o02-motion-feed.json): source-paired loopback H.264 TS with a translating hash-texture region; 15/30/60 Hz requested acquisition, one-region flow, a repeated sixteen-region workload and an injected 100 ms worker delay. Every run drains active/pending ownership without worker/scope errors. [APK/fixture provenance](evidence/o02-motion-provenance.json).
- [Initial benchmark](evidence/o02-motion-baseline.json): per-point JNI reads produced one-region worker p95 about 27.5 ms at 30 Hz. The final 30 Hz experiment measures worker p95 about 8.6 ms after bulk reads. These are short sequential runs with warm-up/scheduling variation, not a controlled sustained A/B performance acceptance.
- Pure Core checks prove the two-image bound, exact disposal, overload replacement, native candidate retention, source/generation fences, source-time gaps, stale/cancel/failure behavior and late-return suppression. Agent UI checks and signed test/Lab/production/retained build gates are recorded in [delivery evidence](evidence/o02-motion-delivery.json).

| Final controlled run | Processed rate | Worker p95 | Acquisition p95 |
| --- | --- | --- | --- |
| 15 Hz requested, one ROI | 13.1 Hz | 28.5 ms | 31.4 ms |
| 30 Hz requested, one ROI | 18.5 Hz | 8.6 ms | 14.3 ms |
| 60 Hz requested, one ROI | 25.3 Hz | 10.3 ms | 30.8 ms |
| 30 Hz requested, sixteen repeated ROIs | 20.9 Hz | 39.3 ms | 11.5 ms |

The source contains only 30 encoded frames/sec. Requesting 60 cannot establish 60 unique frames/sec. Separate development runs varied around 18.5–25.9 processed Hz at a 30 Hz request; acquisition, cold start, frame-copy refusals and duplicate source polling remain material. Sixteen repeated regions measure workload rather than sixteen independent ball motions. Its worker p95 exceeds the proposed 33.3 ms target. The final proof remains below the proposed sustained cadence gate, and its 640×360 source does not establish mounted 1080p behavior.

Full bitmap GPU readback still occurs for an accepted acquisition. ROI cropping bounds conversion/flow but does not implement direct GPU ROI extraction or remove the UI readback cost. Real calibrated mapping, moving balls leaving fixed regions, low light, crossings, hands, near-mouth evidence, latency/thermals/battery and interruption accuracy still need their own gates. O01 remains at zero real timed-shot/held-out sequences. No score, turn, group, foul or winner state is inferred from these measurements.

The combined physical Lab install/launch/API admission check was rejected by automatic approval review with only “blocked by policy”; none of that command ran. Shared native/C# feed behavior is physically tested in the separate vision-check package. The companion diagnostic enablement path builds but its real-table startup/gating remains unverified. The normal guarded update preserves signer, UID and private storage; its SHA256 and observed production launch are in delivery evidence. No explicit camera capture/control, Cast or personal-key operation is performed.

## Reproduce and continue

```powershell
dotnet run --project tests/SkyEye.Checks
./work/python-reference/Scripts/python.exe scripts/Make-DecodedFrameFixture.py work/NEW-MOTION-FIXTURE/decoded-frame-fixture.mp4 --motion
./scripts/Test-MotionFeed.ps1 -CompanionSerial '<unlocked authorized Pixel>' -FixtureDirectory work/NEW-MOTION-FIXTURE -OutputDirectory work/NEW-MOTION-REPORT
```

The fixture generator and report script refuse overwrite. Original barcode pixels remain outside the moving-texture patch. The test installs only the isolated vision-check package, uses its own loopback TS and restores production afterward. Neither test activity is part of production.

Next is O02.05: reduce and measure actual 1080p readback/ROI costs and resource/lifecycle behavior before claiming a sustained enabled tracking feed. The opt-in motion snapshot now includes requested cadence, decoded dimensions and a bounded recent processed cadence calculated from source PTS; the sample count and existing acquisition/worker/result-age timings remain available for interpretation. This telemetry helps collect O02.05 evidence but does not itself establish real-source cadence, resource or lifecycle acceptance. Collect/review the real O01 session-grouped corpus in parallel. O03 can then establish conservative moving/settled shot boundaries; O04 adds ROI recentering and persistent association, followed by O05 evidence review. T13/T14 TV/annotation/endurance gates and deferred voice/physics/detector replacement remain separate.
