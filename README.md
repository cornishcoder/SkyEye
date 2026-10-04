# SkyEye

C# Android apps for a mounted pool-table camera. The camera and retained native companion use .NET 10; the production portrait companion uses .NET 11. Initial Pixel 10 Pro camera and Pixel 11 Pro XL companion capture, LAN viewing and replay checks pass. Extended device/Chromecast acceptance is still required before calling the prototype dependable.

## Apps

- **SkyEye Camera:** arm once on the mounted phone, then switch its display off. Session controls exist on the companion. A camera/microphone foreground service serves the local network on port 8765.
- **SkyEye:** discover or enter the camera address, watch as a guest, or deliberately claim host control. The first successful claim wins. Guests browse/download clips; the host starts/pauses/resumes/stops, saves replay clips and casts.
- **Sky:** optional host-owned GPT-Live-1 conversation, adapted from Maeve Watch. The personal API key is entered on the companion and encrypted with Android Keystore. Camera, guests and source code receive no API credential.

## Build and install

For the production portrait companion, use the separate [production build and guarded in-place update](docs/production-companion-migration.md). The baseline instructions below remain available for the camera and retained native companion.

Requires .NET 10 with the Android workload and the Android SDK/JDK. Native integrations are Java adapters inside the C# Android projects. Dependencies are pinned in the project files.

```powershell
dotnet build SkyEye.sln -c Debug -m:1
.\scripts\Install-SkyEye.ps1 -CameraSerial '<connected camera>' -CompanionSerial '<connected host>' -SkipBuild
```

The existing solution is pinned to .NET SDK 10.0.400. The [glow-up](docs/glow-up-plan.md) is being developed in a separate .NET 11 MAUI Blazor Hybrid build lane. Use `scripts/Build-SkyEye.ps1 -Target Baseline`, `-Target Hybrid` or `-Target All`; SDK selection and Android builds are explicit and sequential. See [progress](docs/glow-up-progress.md) for compatibility gates.

APKs embed their managed assemblies and can be installed directly using ADB or Android's installer. Use the ARM64 APKs for the physical phones. Apps have separate package IDs (`uk.co.skyeye.camera`, `uk.co.skyeye.companion`), leaving Maeve Watch intact.

Wireless debugging must be paired with the computer first. A pairing address/code is different from the connection address displayed on the main Wireless debugging page; connection ports change. `adb connect` and `adb devices -l` must show authorized devices before installation.

## Daily use

1. Open **SkyEye Camera**, grant camera/microphone access, and tap **Arm camera**. Wait for **Armed · Ready**. The displayed local address uses port 8765, not the wireless-debugging port.
2. Turn that phone's display off. Keep both phones and Chromecast on a network that permits device-to-device traffic.
3. Open **SkyEye** and tap **Find camera**, or enter the address shown on the camera, e.g. `http://192.168.1.100:8765`.
4. Tap **Become host** to claim available control, then **Start**. Other companions remain guests and can watch/download.
5. Choose a replay duration and **Save last…** after a shot. Save ends at camera time, while displayed video is delayed. Short history produces a shorter clip, with actual duration reported.
6. Download a camera clip. Transfer resumes after interruption and is verified before portrait preparation. Once available locally, play/share it without the camera connection. The camera original remains.
7. Select Chromecast with the Cast button and choose **Play on TV**. The live feed has no audio track. Phone presentation can rotate independently.
8. **Pause** preserves saved clips and suspends capture. **Resume** starts a new continuous history interval. **Stop** ends the session while the service remains armed for another start, subject to device validation.

Capture/buffering continues without viewers or a host. Host leases use five-second companion heartbeats and expire after 30 seconds without renewal. New claims invalidate old control tokens. Releasing ownership ends the old host's Sky conversation. Low battery (10%) or critically low storage stops capture; saved clips remain.

## Pool partner

Start recording, then **Calibrate** the playable surface in the current snapshot. Review any proposed corners, or tap them clockwise from top left with the long edge running horizontally. The frame stays still during selection. Check the coloured outlines and counts against every ball in that exact image. Set the current group; **I'm on the black** is an explicit declaration, not an inference from missing detections.

Calibration is saved per camera address. Recalibrate whenever the mount moves. Current ball positions and confirmations are refreshed rather than restored as current facts.

The local C# OpenCV detector rectifies perspective, measures cloth colour in the normal grey-blue lighting, normalizes from a unique cue reference, finds circles and clusters ball colours. It requires confirmation after a changed layout. **Correct layout** accepts a complete ball list in table coordinates (horizontal 0–2, vertical 0–1). Advice is suppressed for old, moving, obstructed or unconfirmed views. Corrections persist only on the same layout, calibration and capture timeline; they cannot bypass a physical obstruction. Pocket-opening colours request review. No automatic foul decisions or match scoring are made.

Turn on **Candidate overlay** beside the main live video to cycle up to three direct, bank and kick pots every two seconds. The layout refreshes every ten seconds and clear, settled detections are checked automatically; waiting/retry messages appear in the main view. White cue paths, cyan object-ball paths, ghost contact and cushion contacts are drawn on the video. Cast receives the annotated view while SkyEye is visible; turning the overlay off returns Cast to the original camera stream. See [overlay operation and device evidence](docs/candidate-overlay.md).

Each ball can rebound at most once. Every segment is checked for obstacles, finite cushion bounds and pocket-mouth rebounds. Sky's `get_table_state`, `suggest_shots` and `explain_shot` tools use the same state and stable candidate IDs, with their stricter fresh-frame gate. Scores rank geometric difficulty, not pot probability. Group eligibility is based on the existing [WEPF International Eightball Revision 2c](https://www.wepf.org/docs/International%20Eightball%20Rules%20%28Revision%202c%29%20-%20A4%20Booklet.pdf) reference. This does not simulate spin, pace, cushion jaws or the resulting cue-ball position.

The implemented [Poolgame port and intentional adaptations](docs/poolgame-port.md) run entirely in C# on the companion with OpenCV 4.14.0 native image primitives. Python reference fixtures use OpenCV 4.11.0 and are development references only; no Python runtime, model download or analysis server is needed in the app. See the port report for the difference between regression checks and held-out accuracy evidence.

In the production portrait app, open **Controls → More controls** and choose **Talk to Sky** on the host. Set your personal key through the protected dialog in the portrait controls. Microphone mute/end controls are separate from recording. A microphone foreground service supports an ongoing conversation while the activity backgrounds; removing/destroying the activity closes voice. Shutdown waits for `session.closed` or a bounded timeout, recording whether usage finalization was confirmed. Voice/transcript events are stored privately on the host. No key values are logged.

## Validation

```powershell
dotnet run --project tests\SkyEye.Checks
.\scripts\Test-NativeMedia.ps1
.\scripts\Test-DeviceCapture.ps1 -CameraSerial '<connected camera>'
```

The native media fixture needs FFmpeg and the configured JDK. It checks the actual MPEG-TS writer using AVC fixtures in Annex-B and AVCC formats and compares every decoded frame. Device capture checks use ADB forwarding to test repeated screen-off control, replay accumulation and verified download; that does not prove LAN viewing or casting.

See [device acceptance](docs/device-acceptance.md), [validation evidence](docs/validation.md) and [the plan](docs/implementation-plan.md). Native WebRTC is updated to `io.github.webrtc-sdk:android:150.7871.01`, retaining Maeve's adapter approach while using a library whose ARM64 load segments have 16 KB alignment.

## Storage and prototype limits

- Rolling history retains roughly ten minutes plus live-playback grace. Video-only TS and audio/video MP4 coexist, increasing storage above the single-video bitrate calculation.
- Saved clips use independent atomic metadata journals and MP4 files rather than the plan's proposed SQLite catalogue. Completed files survive process death; rolling history is ephemeral. Saved-file deletion/retention controls remain a product decision.
- Camera pixels are encoded in native landscape sensor coordinates. Full-table coverage and upright portrait rotation must be checked with the actual mount. Export forces portrait encoding instead of depending on rotation metadata.
- Runtime metrics are private `benchmarks.jsonl` records with battery, temperature, storage and completed-frame estimates. Frame count is derived from completed segment duration; it is not a hardware dropped-frame counter.
- Capture, audio and replay selection use Android's monotonic clock, matching Camera2's default MediaCodec output timestamp base. Sensor timestamps may use a different clock. Physical microphone synchronization still needs an audible/visible event check.
- Stream latency, battery runtime, echo quality, Cast playback and recognition accuracy are not established by builds/unit fixtures.
- The local network is the audience boundary. Viewing capability URLs are distinct from private host-control tokens. Internet hosting and authentication are outside scope.
- Reboot/force-stop recovery, whole-session recording, a separate guest APK and complete refereeing are deferred.

## References

- [Android foreground service types](https://developer.android.com/develop/background-work/services/fgs/service-types)
- [Cast sender integration](https://developers.google.com/cast/docs/android_sender/integrate)
- [Media3 transformations](https://developer.android.com/media/media3/transformer/transformations)
- [GPT-Live server controls](https://developers.openai.com/api/docs/guides/voice-server-controls)
- [WebRTC Android dependency](https://github.com/webrtc-sdk/android)
