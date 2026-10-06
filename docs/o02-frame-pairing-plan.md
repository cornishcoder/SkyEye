# O02 decoded-frame pairing and feed

6 October 2026. Continue from [O01's offline workbench](o01-workbench-report.md). First prove that requested observation pixels and source time name the same decoded frame. Then build the enabled tracking feed and ROI measurements around that ownership contract.

| Chunk | Deliverable and gate |
| --- | --- |
| O02.01 | Bounded native metadata ledger, exact surface timestamp lookup, collision ambiguity and decoder-generation fencing; host Java checks |
| O02.02 | One owned raw bitmap/receipt acquisition; reject a changed surface during copying, missing/stale clock mapping and changed source; retain frame identity/dimensions through requested observation |
| O02.03 | Controlled B-frame H.264 fixture on the Pixel: independently read pixel barcodes against local decoder PTS and loopback TS source PTS; stop/restart, TS epoch conversion and C# binding/cancellation proof |
| O02.04 | Enabled observation feed with one active frame and one replaceable latest frame; dispose replaced/stopped/cancelled ownership, reject stale/duplicate source receipts and benchmark native ROI extraction/flow |
| O02.05 | Actual mounted 1080p source acquisition and 15/30/60 Hz tradeoffs: processed cadence, source gaps/age, acquisition/worker p50/p95, CPU/GPU/thermal/battery/resource and interruption evidence |

O02.01-O02.03 are the first delivery slice. O02.04-O02.05 remain open; a request-driven full analysis is not repurposed as a tracking loop. Expensive perception stays requested or phase-triggered. High-resolution master recording is preserved. O03 shot boundaries and O04/O05 association/review depend on the enabled feed and real O01 footage.

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
