# Automatic candidate overlay — 3 October 2026

The main live video now has a **Candidate overlay: off/on** toggle directly underneath it. The former **Show candidate shots** action and separate candidate buttons have been replaced. With an active camera, host control and a saved calibration, turning it on automatically checks the table and draws each candidate on the main video. No repeated manual confirmation is required for clear, settled detections.

Candidates rotate every two seconds. Analysis starts on a ten-second cadence, including the time spent fetching and analyzing a frame. A new or changed layout gets a second observation after a settling delay before automatic confirmation. Uncertain colours/counts, physical obstruction, unsettled balls or an unavailable camera produce a visible waiting/retry message beside the toggle and over the video. Manual calibration, target-group selection and correction remain in the table section.

The presentation cache expires fifteen seconds after its captured frame; a missed refresh removes old paths. Live pool tools retain their existing six-second freshness gate. A tracker configured for the ten-second observation schedule preserves a stable layout between samples, while fixed-anchor motion detection, calibration/timeline checks and gap invalidation remain active. Changing group or calibration clears the old presentation.

## Casting

The default Cast receiver plays video and cannot draw an Android view's separate annotation layer. The companion now produces a silent 960 × 540 H.264 feed at approximately ten frames per second containing the main live camera pixels, cycling paths and candidate caption. The same normalized-image drawing routine renders phone and TV paths. The underlying Pixel 10 camera capture, archive, replay files and original live stream are unchanged.

When the annotated feed has enough complete segments, an existing Cast session switches to it. Turning the toggle off switches Cast back to the original camera stream and stops the extra encoder/server. Like other HLS viewing, the TV can lag behind the phone; additional overlay relay latency has not been measured. Keep SkyEye visible while using the annotated TV view. Backgrounding the app returns Cast to the camera stream, and returning to SkyEye restarts the enabled overlay. No screen-capture permission or custom receiver registration is required.

The relay has two reusable frame buffers, one pending-frame slot and at most sixteen completed two-second segments. It uses the companion's interface routed to the camera, port 8766, a per-instance viewing capability and the existing bounded read-only HTTP/range server. `/status` advertises read-only viewing access in the same manner as camera discovery; it exposes no host command token or voice API key. The relay is intended for the trusted local network.

## Verification

- Core checks cover two-second boundary/wrapping behaviour, ten-second sampling, expiration when refresh stops, uncertain-result clearing and independent strict tool freshness.
- Release companion installed on Pixel 11 Pro XL. Main video visually shows calibrated cue/object paths and ghost/cushion contacts, with no manual confirmation after enabling.
- The actual annotated HLS stream was retrieved and decoded. It contains H.264 960 × 540 video, no audio, and all three candidates in sequence at two-second frame samples. Initial run: 257 encoded frames, zero skipped captures, thirteen completed segments, no encoder failure.
- Connected to the discovered Chromecast route. Its receiver reports `PLAYING` on the candidate stream with an advancing playback position. Turning the toggle off produced **TV playing live camera** and removed the phone overlay; turning it back on restarted the relay and the receiver returned to candidate playback.
- A 36-second monitor collected 150 read-only status samples. Candidate transitions occur on the two-second clock; an unclear detection temporarily cleared candidates and they returned automatically. A measured valid-refresh interval was 10.011 seconds. Completion intervals vary with analysis/settlement, and a gap between valid results is not a missed analysis attempt. The steady relay remained at its sixteen-segment bound with no skipped captures or encoder error in the inspected samples.
- Machine-readable evidence: `docs/evidence/candidate-overlay.json` and `docs/evidence/candidate-overlay-media.json`. Receiver playback status and the decoded sender feed are verified; TV pixels were not physically inspected. The overlay feed's source frames were decoded and three successive candidates visually inspected.

The app remains a geometric coaching prototype. Automatic checks on this layout are not broad held-out accuracy evidence. The new companion encoder's long-session battery/thermal behaviour remains unmeasured.

Platform references: [Android TextureView](https://developer.android.com/reference/android/view/TextureView), [MediaCodec input surfaces](https://developer.android.com/reference/android/media/MediaCodec), [Cast receiver options](https://developers.google.com/cast/docs/overview). The encoder uses EGL to render into a codec input surface, rather than relying on unsupported Canvas surface behaviour.
