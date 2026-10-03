# SkyEye 0.1 validation — 3 October 2026

Implemented in `C:\code\SkyEye` using Maeve Watch as a read-only reference. Maeve Watch was not modified. This is a working prototype with emulator evidence; the physical Pixel/Chromecast acceptance gate remains open.

## Verified

| Check | Observed result |
|---|---|
| ARM64 Android builds | Camera and companion compile and produce standalone debug-signed APKs with embedded managed assemblies. |
| Core checks | Atomic simultaneous host claims; guest rejection; lease expiry, renewal and stale-token rejection; state transitions; all five replay selections; short history and pause gaps. |
| Geometry/perception checks | Perspective calibration, legal target-group filtering, blocked paths and stale/moving/occluded/unconfirmed state rejection. Four ball colours detected in synthetic fixtures only. |
| Local server checks | Fragmented requests, request-size limits, duplicate lengths and unsupported chunking; actual range transfers, invalid ranges and HEAD responses. Real companion command client tested against the camera HTTP server. |
| Native transport fixture | Actual Java MPEG-TS writer tested with both Annex-B and AVCC H.264 input. All 60 decoded frames in each output match the source. Live transport has video only. |
| Emulator lifecycle | Android 14 / API 34, `sdk_gphone64_x86_64`, 4 KB pages, virtual rear camera. Three cycles of display-off start, replay save, verified download, pause, resume and stop passed. |
| Replay media | H.264 1920×1080 plus AAC 48 kHz mono. Final repeated-cycle durations 12.877 / 13.044 / 12.500 seconds match the saved media within the test's 150 ms tolerance. Video and audio decode successfully. Short history was deliberately used; this does not verify ten-minute capture. |
| Companion transfer/export | Native companion connects, lists camera clips, downloads and verifies them, and finishes Media3 export. Inspected exported file is H.264 1080×1920 with AAC and no rotation metadata. |
| Offline playback | Companion reports playing the downloaded clip after the camera process is force-stopped. |
| Companion live/control | Native app claims and releases ownership, starts/stops capture and recovers from initial media unavailability into live playback. Downloaded live TS inspected as H.264 1920×1080 with no audio track. Same-emulator loopback network only. |
| Save while viewing | Companion's 30-second selection produces a 30.482-second clip (keyframe boundary allowance), with matching download hash and FFprobe duration 30.481811 seconds; video and audio tracks are present. Evidence: `docs/evidence/emulator-full-replay.json`. |
| Native WebRTC packaging | Modern M150 Android library replaces Maeve's old binary. ARM64 ELF load segments are 16 KB aligned. Physical 16 KB-page-device execution has not been tested. |

Capture run evidence is `docs/evidence/emulator-capture.json`; the reproducible commands are in README and `scripts/`. Automated tests do not establish audible room-sound quality, echo cancellation, synchronization judged by visible events, pool-table recognition accuracy or physical-phone battery runtime. The emulator has irregular frame cadence under software encoding/load; it cannot establish the physical target frame rate.

## Corrections found during validation

- Embedded managed assemblies so the APKs work independently of development fast deployment.
- Split microphone PCM into the codec's actual input-buffer capacity.
- Recover correctly when the camera is re-armed following a fault; handle repeated launch requests.
- Queue audio against video boundaries rather than allowing audio to lengthen earlier segments. Read clip duration from the finished file.
- Assemble tracks by media type when an individual segment lacks audio samples; preserve source timestamps and exclude samples after the save request's camera time. Use Android's elapsed-realtime clock to match Camera2 realtime timestamps.
- Force portrait video encoding and inspect output dimensions; a metadata-only rotation is rejected.
- Keep host renewal available during clip assembly and companion downloads.
- Serialize companion command bodies with a known length, avoiding Android's chunked JSON requests to the bounded camera server.
- Retry live playback after transient media startup failures; stopping/changing the stream cancels the retry.

Fresh companion Java binding generation emits four BG8605/BG8606 diagnostics about private WebRTC callback classes. The public Java adapter and native Java compilation succeed. The final incremental solution build completed with zero warnings/errors. The generation warnings are recorded rather than suppressed; actual GPT-Live operation remains unverified.

## Physical-device and API checks outstanding

The supplied Pixel 10 Pro debugging endpoint `100.70.143.12:36015` timed out. Pixel 11 Pro `192.168.1.103:43513` actively refused the port. No APK was installed on either physical phone. Wireless-debugging connection ports can change; an unpaired computer also needs the separate pairing address and code.

Required next checks:

1. Deploy on both phones and repeat display-off control after a 30-minute armed idle.
2. Verify actual LAN discovery/viewing and silent landscape Chromecast Ultra playback, full-table coverage and measured delay.
3. Exercise full 30s/1m/2m/5m/10m history, interrupted transfers, concurrent viewers/saves and host handover.
4. Check physical microphone synchronization/quality, thermal behavior, storage and unplugged endurance.
5. Test GPT-Live voice, interruptions, microphone foreground service, local tool calls and confirmed session shutdown with an API key entered on the host. No paid API session was opened during implementation.
6. Label real mounted-table images and assess detection/shot suggestions before relying on coaching.

Saved clips use independent atomic metadata journals, rather than the proposed SQLite catalogue. There is no complete referee, calibrated physics simulation, whole-session archive, Internet hosting or measured battery-runtime claim. These limits and the current daily-use flow are documented in README.
