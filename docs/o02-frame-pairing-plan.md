# O02 decoded-frame pairing and feed

6 October 2026. Continue from [O01's offline workbench](o01-workbench-report.md). First prove that requested observation pixels and source time name the same decoded frame. Then build the enabled tracking feed and ROI measurements around that ownership contract.

| Chunk | Deliverable and gate |
| --- | --- |
| O02.01 | Bounded native metadata ledger, exact surface timestamp lookup, collision ambiguity and decoder-generation fencing; host Java checks |
| O02.02 | One owned raw bitmap/receipt acquisition; reject a changed surface during copying, missing/stale clock mapping and changed source; retain frame identity/dimensions through requested observation |
| O02.03 | Controlled B-frame H.264 fixture on the Pixel: independently read pixel barcodes against local decoder PTS and loopback TS source PTS; stop/restart, TS epoch conversion and C# binding/cancellation proof |
| O02.04 | [Experimental feed/ROI foundations delivered](o02-motion-feed-report.md): explicit one-active/one-latest ownership, candidate skip, scoped stop/reset and native fixed-region flow; real-source companion enablement and performance acceptance remain open |
| O02.05 | Actual mounted 1080p source acquisition and 15/30/60 Hz tradeoffs: processed cadence, source gaps/age, acquisition/worker p50/p95, CPU/GPU/thermal/battery/resource and interruption evidence |
| O02.06 | Ball-centred template motion and diagnostic phase machine implemented; verify moving/still separation, white cue-ball response, variable-light false positives, cadence and recovery on labelled live shots |

O02.01-O02.03 are the first delivery slice. O02.04 foundations now have controlled device proof; O02.05 performance and O02.06 movement acceptance remain open. A request-driven full analysis is not repurposed as a tracking loop. Expensive perception stays requested or phase-triggered. High-resolution master recording is preserved. O03 shot boundaries and O04/O05 association/review depend on the enabled feed and real O01 footage.

## Receipt contract

Media3's frame metadata callback supplies presentation microseconds and scheduled release nanoseconds. Match the actual `SurfaceTexture` timestamp to that exact release timestamp. Do not choose the newest release or the nearest timestamp. Metadata is bounded to 64 stamps, collisions are ambiguous and every playback/stop/release resets the decoder scope.

The copy occurs on the player's UI looper. Require the same surface, timestamp, metadata object and media generation before/after the bitmap copy. A changed copy is recycled and refused. The immutable receipt owns its raw bitmap until the C# adapter takes it; closing an untaken receipt recycles it. Cancellation, errors and changed camera/host/live scope dispose acquired pixels. Analysis does not encode JPEG; only the existing explicit review path does.

`DecodedFrameReceipt` carries the decoder generation, release-receipt ordinal, presentation PTS, surface timestamp and dimensions. The ordinal is not a camera hardware frame counter. Application source, recording timeline and calibration remain in their existing scoped observation contracts. The command consumer refuses disagreement between the decoded receipt's generation and the requested current player generation.

For live TS, use the exact matched release entry in the existing source-clock mapper. Preserve extractor timestamp offset and recording-status wrap-epoch reference; refuse missing, ambiguous, old-clock or unrelated-generation mappings. Surface release time is a scheduled render deadline: a TextureView can latch the actual pixels before it. The pairing gate permits at most 100 ms of early release scheduling and 250 ms of lateness; source-age admission remains separate. Controlled pixel-ID proof is required for this policy, rather than equating the deadline with capture time.

This follows [MediaCodec's surface timestamp contract](https://developer.android.com/reference/android/media/MediaCodec#releaseOutputBuffer(int,%20long)), [SurfaceTexture's latched image timestamp](https://developer.android.com/reference/android/graphics/SurfaceTexture#getTimestamp()) and the pinned [Media3 1.10.1 renderer's release/metadata path](https://github.com/androidx/media/blob/1.10.1/libraries/exoplayer/src/main/java/androidx/media3/exoplayer/video/MediaCodecVideoRenderer.java). No upstream code is copied.

## Reproduce the controlled proof

```powershell
./scripts/Test-DecodedFrameLedger.ps1
# Use the existing Python reference environment and FFmpeg on PATH.
./work/python-reference/Scripts/python.exe scripts/Make-DecodedFrameFixture.py work/NEW-FIXTURE/decoded-frame-fixture.mp4
./scripts/Test-DecodedFramePairing.ps1 -CompanionSerial '<authorized unlocked Pixel>' -FixtureDirectory work/NEW-FIXTURE -OutputDirectory work/NEW-REPORT
```

The generator refuses overwrite and records MP4/TS checksums, dimensions, cadence, barcode definition and actual TS origin. It encodes 240 frames at 640×360/30 Hz with H.264 B frames. The test installs only `uk.co.skyeye.visionchecks`, serves its own TS on loopback, runs the production shared player/adapter code, and restores the production companion activity afterward. It has no camera capture/control or Cast operation. The test bitmap loop runs only in the test activity. Reports include APK/fixture provenance and reject stale run IDs. The synthetic setup proves the tested pairing/handoff cases; it does not establish real table accuracy, 1080p acquisition latency, ROI throughput or sustained tracking cadence.

Read the [delivery report](o02-frame-pairing-report.md) for measured results and remaining gates. Existing T13/T14 annotation/TV/endurance acceptance, real-shot holdout collection and downstream tracking/physics scope remain separate.

## Moving-object response follow-up — 9 October 2026

O02.05 now has clean short stationary timing runs and a separate illumination-normalized per-region changed-pixel metric. Two no-shot controls produced per-region maxima of 0.000732 and 0.000488; a user-reported red pot produced a stable-source maximum of 0.002441 before the decoder restarted and stopped the capture. This is a promising activity response, not ball identity or shot adjudication. The larger value at the restart boundary is excluded. See the [sanitized local-change capture evidence](evidence/o02-local-change-live-response-20261009.json), the earlier [weak-flow shot trace](evidence/o02-live-moving-ball-response-20261009.json), and the [motion-feed report](o02-motion-feed-report.md).

Next, repeat the labelled shot while keeping the live decoder generation stable. If scalar signals remain ambiguous, inspect a bounded set of ROI crops. Acceptance requires repeatable response during known ball movement and low response on an unchanged table across tested lighting; neither metric may claim identity or game events. O02.05 remains open until moving response, cadence/gaps, resources and recovery are evidenced.

## Ball-centred motion analyser — 9 October 2026

The opt-in feed now places a grayscale matching patch on each seed ball and follows its centre across paired frames, including smooth cue-ball bodies that may not provide Lucas–Kanade corners. The matcher searches ±12 pixels within a 192-source-pixel seed crop. A separate diagnostic state machine reports `Unknown`, `Settling`, `Settled` or `Moving`. It needs bounded template coverage, uses displacement hysteresis for single-region motion, records a rolling two-second peak, and waits for 700 ms of quiet before `Settled`. Source gaps reset the analyser. Dashboard snapshots expose the phase and compact per-region measurements; the capture report preserves the phase and template fields even when it samples only once per second.

These thresholds are provisional. The phase describes visible patch movement and quiet, not whether every physical ball is present or a shot outcome. It does not change canonical `SkyEye.GameState`, assign durable ball identity, infer pots/turns, or replace O03/O04. Confirm on the Pixel with a labelled still control, a white cue-ball move and a potted-ball shot under steady and changing light. Require repeatable movement peaks, no sustained false motion while still, a useful Settling-to-Settled transition, and acceptable processing cadence/resources before treating O02.06 as accepted. O02.05 performance/resource/recovery gates remain open.

## Stable table layout as a Lab event heuristic — 9 October 2026

The diagnostic phase feed remains useful for understanding transient motion, but the Scenario Lab no longer depends on sampling `Moving` to recognize a shot. It retains the last accepted clear layout while the same camera/calibration source is temporarily obstructed or samples are stale/missing, then waits for three clear stable layouts and compares before with after. A uniquely associated layout with moved or absent balls can propose a likely shot; gaps that hide the shot's start lower confidence and leave exact motion timing unavailable. An absent ball is recorded as Missing, not Potted, until the user confirms the pot. This event bridge writes only to the isolated Scenario Lab, not the active match.

The 9 October 60-second Pixel capture provides continuity diagnostics, not a validated motion/pot result: 59 one-second samples, 1,609 acquired and 1,591 processed-frame deltas, decoder generation 4 throughout, and zero reported source-gap resets, stale samples, acquisition failures, scope rejects or worker failures. Forty-eight snapshots reported Settled and eleven Settling; none sampled Moving. One two-second template peak reached 20.985 px/frame, which is not a speed measurement or a verified ball track. The user reported potting a yellow during this capture, but the exact time is unknown and no images were saved. The run neither proves yellow-ball recognition nor exercises the one-minute obstruction hold. See [sanitized capture evidence](evidence/o02-obstruction-aware-motion-20261009.json).

This supports a simpler Lab review workflow, not accepting O02.06. Keep collecting labelled still/moving footage, distinguish one-off template outliers from repeatable ball-centred response, and explicitly test a player/cue obstruction lasting up to one minute followed by three clear stable views. Fast motion, same-colour association, pot adjudication and live automatic state changes remain outside this heuristic.
