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

An earlier combined physical Lab install/launch/API admission command was rejected by automatic approval review with only “blocked by policy”; none of that command ran. The later O02.05 Release performance build was installed separately, and the mounted-camera startup/gating was verified by the real-source captures below. The normal guarded update preserves signer, UID and private storage; its SHA256 and observed production launch are in delivery evidence. The diagnostic captures save no camera images and perform no camera configuration, Cast or personal-key operation.

## Reproduce and continue

```powershell
dotnet run --project tests/SkyEye.Checks
./work/python-reference/Scripts/python.exe scripts/Make-DecodedFrameFixture.py work/NEW-MOTION-FIXTURE/decoded-frame-fixture.mp4 --motion
./scripts/Test-MotionFeed.ps1 -CompanionSerial '<unlocked authorized Pixel>' -FixtureDirectory work/NEW-MOTION-FIXTURE -OutputDirectory work/NEW-MOTION-REPORT
```

The fixture generator and report script refuse overwrite. Original barcode pixels remain outside the moving-texture patch. The test installs only the isolated vision-check package, uses its own loopback TS and restores production afterward. Neither test activity is part of production.

O02.05 has started with a mounted-camera baseline; sustained cadence, resource and lifecycle acceptance remain open. The opt-in snapshot reports requested cadence, decoded dimensions and a bounded recent processed cadence calculated from source PTS, alongside acquisition/worker/result-age timings. The Lab endpoint uses loopback port 8769 because the corrected-clip relay already uses 8768. [Capture-O02LiveMotion.ps1](../scripts/Capture-O02LiveMotion.ps1) guards its ADB forward, saves no images, and records the terminal snapshot and stop reason if a run ends early. Continue O02.05 resource/interruption work and collect/review the real O01 session-grouped corpus in parallel. O03 can then establish conservative moving/settled shot boundaries; O04 adds ROI recentering and persistent association, followed by O05 evidence review. T13/T14 TV/annotation/endurance gates and deferred voice/physics/detector replacement remain separate.

## O02.05 first mounted 1080p baseline — 8 October 2026

[Raw metric evidence](evidence/o02-live-motion-real-source-20261008.json) contains three valid foreground runs from the Pixel 11 Pro XL Lab performance build: 15 Hz requested for 30 seconds, 30 Hz for 60 seconds, and 60 Hz for 30 seconds. Each used the live camera at 1920×1080 with sixteen fixed regions. The feed seeded those regions from a fresh detector sample that matched the manually confirmed layout; the samples were 0.9–2.0 seconds old. No images were saved.

| Requested rate | Processed rate from counter delta | Processed source cadence p50/p95* | Acquisition snapshot p50/p95 ms* | Worker snapshot p50/p95 ms* | Gap resets during sample span |
| --- | ---: | ---: | ---: | ---: | ---: |
| 15 Hz | 13.9 Hz | 15.38 / 29.73 Hz | 11.35 / 78.10 | 22.97 / 31.76 | 38 |
| 30 Hz | 22.5 Hz | 23.76 / 29.74 Hz | 9.06 / 57.94 | 19.43 / 55.54 | 27 |
| 60 Hz | 24.9 Hz | 25.80 / 28.97 Hz | 9.19 / 13.62 | 20.55 / 53.36 | 9 |

*Percentiles are computed from one-second metric snapshots, not every processed frame. The source-cadence field is a rolling source-PTS measure, bounded to ten seconds and reset after a gap. Processed rate is the processed-counter delta divided by the first-to-last sample span. These short runs are an initial baseline, not sustained-performance acceptance; the 15 and 60 Hz runs are only 30 seconds. Every valid run reported zero stale, acquisition, scope-rejection and worker failures, and sampled active/pending ownership stayed within one each. The 15/30 Hz gap-reset counts show that continuity needs investigation before motion output can be trusted.

An earlier 15 Hz attempt stopped after about five seconds with `Source, calibration or enablement changed`; its repeated terminal snapshot was excluded. The valid 15 Hz retry completed. The collector now saves partial metrics and the terminal stop reason rather than discarding a run that stops early. CPU, memory, temperature, battery, interruption/recovery, moving-ball accuracy and real O01 shot coverage have not been measured.

## O02.05 source-gap follow-up — 8 October 2026

[The one-minute 30 Hz capture](evidence/o02-live-motion-gap-30hz-20261008.json) processed at 22.92 Hz over its 59-second sample span at 1920×1080 with sixteen fixed regions. The gap counter increased by 18; the largest recorded source interval was 168.145 ms against the unchanged 125 ms reset threshold. The recent processed-interval P95 reached 100.889 ms. One-second snapshots reported up to 41.6 ms acquisition, 41.9 ms worker time and 697.4 ms result age; stale, acquisition, scope-rejection and worker failure counters stayed at zero. No images were saved, and the table was reported stationary.

These counters confirm intermittent source-time discontinuities in the measured feed, but do not identify whether the missing intervals arose before decoder release or while the sampler waited, copied or processed frames. The new opt-in metrics expose decoded frame-ID deltas and source-clock sample age/uncertainty. They were installed for the follow-up below. The 125 ms continuity gate remains unchanged.

## O02.05 decoder-frame gap and clock follow-up — 8 October 2026

[The one-minute 30 Hz capture](evidence/o02-live-motion-gap-followup-30hz-20261008.json) completed with 58 one-second snapshots over a 60-second span at 1920×1080 and sixteen fixed regions. Processed rate was 22.4 Hz. The gap-reset counter increased by 26; the largest source interval was 201.766 ms, above the unchanged 125 ms threshold, and the rolling recent source-interval P95 peaked at 100.878 ms. In snapshots where a new gap was recorded, the decoder frame ID had advanced by 4–6 between processed samples. The ordinary one-second samples most often showed a processed frame-ID delta of 1 (45/58), with deltas 2–4 in the other 13 samples. This confirms that decoded frames were skipped between some motion measurements, but does not isolate whether cadence limits, pixel acquisition/copy time, worker scheduling or upstream delivery caused each skip.

Acquisition and worker snapshot P95 values were 60.8 ms and 88.3 ms, with maxima of 144.9 ms and 91.8 ms. Result age ranged from 487.5 to 618.8 ms. Stale, acquisition, scope-rejection and worker failure counters remained at zero; no images were saved. Source-clock sample age ranged from 42 ms to 4.983 seconds, with uncertainty up to 53.2 ms. These are one-second metric snapshots, not per-frame percentiles. The table remained a manually confirmed, clear and settled 16-ball layout at capture start. One earlier attempt stopped with `Source, calibration or enablement changed`; it left no evidence under the old collector. The partial-capture safeguard was added, and the retry completed. The transient stop cause is still unknown.

This stationary one-minute run is useful for locating measurement discontinuities, not accepting tracking. Cadence remains below the 30 Hz request, fixed ROIs do not follow moving balls, and live CPU/memory/temperature/battery, moving-ball accuracy, interruption/recovery and real O01 shot coverage remain unmeasured. The continuity threshold was not raised.

## O02.05 acquisition and worker timing follow-up — 8 October 2026

[A corrected one-minute 30 Hz capture](evidence/o02-live-motion-gap-components-30hz-20261008.json) collected 58 snapshots over 60 seconds at 1920×1080 with sixteen fixed regions. Processed rate was 22.75 Hz, with 22 gap resets and a largest source interval of 266.7 ms. Gap-associated processed frame-ID deltas ranged from 4 to 8. Stale, acquisition, scope-rejection and worker failure counters remained at zero; no images were saved.

One-second snapshot P95 values were 64.44 ms for frame acquisition and 28.56 ms for the worker. Inside the worker, conversion P95 was 7.80 ms and optical-flow P95 was 21.89 ms. Result-age P95 was 560.43 ms. Acquisition time therefore remains the strongest measured throughput suspect; the aggregate samples do not prove the exact cause. Source-clock sample age reached 4.943 seconds, with uncertainty up to 52.89 ms.

The preceding capture's conversion/flow columns were empty because that PowerShell environment did not evaluate null-conditional property access as intended. The collector was corrected, and this repeated run contains the component timings. The next diagnostic slice adds successful exact-frame acquisition-attempt/retry counts to distinguish the main-thread sampling/copy path from worker cost. The Release Lab update is installed; reopening the calibrated live table and confirming balls is required before that capture. No tests were run. O02.05 remains open: CPU/memory/temperature/battery, moving-ball accuracy, interruption/recovery and real O01 shot coverage are still unmeasured.
