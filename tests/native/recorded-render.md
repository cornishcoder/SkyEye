# Recorded compositor gate

Build `SkyEye.Vision.Device` in Release. It intentionally produces APK only to avoid unnecessary app-bundle disk overhead. Install its signed APK on the authorised Pixel 11. These tests decode existing files and do not activate or control a camera.

Use the T08 controlled video fixture with a declared audio tone (`work/t09-render-fixture.mp4`), copied to the test external directory as `render-input.mp4`. Run:

```text
adb -s <serial> shell am instrument -w -e mode fixture uk.co.skyeye.visionchecks/uk.co.skyeye.visionchecks.RecordedRenderInstrumentation
adb -s <serial> shell am instrument -w -e mode fixture -e portrait true uk.co.skyeye.visionchecks/uk.co.skyeye.visionchecks.RecordedRenderInstrumentation
```

Require `passed: true`, rather than relying on shell exit status. Pull `recorded-render-checks.json` and each `rendered-landscape.mp4`/`rendered-portrait.mp4` from `/sdcard/Android/data/uk.co.skyeye.visionchecks/files/`. Archive each report before the next run overwrites it. `tests/native/verify_recorded_render.py` compares their decoded frames to an independent OpenCV projection and original-frame fallback using the labelled source geometry.

For a real replay, use its checksum-verified MP4 and safe clip acknowledgement/recording origin as described in `recorded-geometry.md`, then pass `-e mode real`. The native renderer verifies original AAC sample hashes/timestamps, output shape/rotation/duration and actual corrected callbacks. It refuses an input checksum that does not match the render geometry.

`RecordedRenderPreviewActivity` is a test-only phone/Cast surface. It plays the rendered portrait file directly, and serves the rendered landscape file using a scoped random media token for buffered TV playback. Phone/TV pause, resume and seek use the baked MP4, with no new perspective operation at playback time. Its `/status` contains safe playback metrics; media URLs/tokens must stay out of committed evidence. Stop the preview and its owned port forwarding after the gate, then restore production live TV. Normal production saved-clip/export routing remains T10.
