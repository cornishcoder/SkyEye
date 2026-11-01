# Phone MPEG-TS stream load diagnostic — 1 November 2026

## Scope

This bounded diagnostic follows the earlier startup where camera `/status` was Active but the phone player showed no image and the server had zero viewers. The earlier result and its limits remain in [the physical control attempt report](live-detect-bounded-control-attempts-20261101.md). This run checks the current `/status`-advertised phone stream after adding redacted Media3 load events. It is playback evidence only; no verifier, Live Detect, readiness, match, or event/clip check was run.

## Checkout and installation

- Source checkout: commit `438696d` (`Add redacted Media3 load diagnostics`). `PlayerBridge` logs the media path with the access token redacted, plus Media3 load state, bytes, duration, and error class.
- Production companion: Pixel 11 Pro XL `66181FDDV001HS`, package `uk.co.skyeye.companion`, version `0.8.59` / code 67. Release build succeeded with 8 warnings and 0 errors. Installed APK SHA-256: `0751393c12afbbad838522c9a69f2a8ea8c97369ae794b3fc47c2c248d9e1a55`.
- The in-place install retained UID and private storage; previous APK: `work/production/backups/20261101-103910/before.apk`. The mounted-camera package was not changed.

## Readback

The initial status was `Stopped`, revision 15, timeline `4c1802ca0c294b0bbd9ec950bed670ff`, 8,642 completed frames, 139 clips, and zero viewers. One normal Home **Start live** action created a fresh camera timeline `c1a726f97fde4f119c8ce8a1da26f4e6`. While Active, `/status` reported revision 18, 8,681 published transport frames, one viewer, and still 139 clips.

The active phone screenshot shows the table image rendered behind Home; its SHA-256 is `4e29f52729b8a169c81e2311e98c0eb777dccdbd17e8dc46554bfb78ead284a`. Media3 then logged:

```text
11-01 10:42:51.912 I/SkyEyePlayer: Media load completed path=/live/stream.ts?timeline=c1a726f97fde4f119c8ce8a1da26f4e6&access=[present] bytes=137435896 durationMs=173187
```

The path contains no access token. The completed load, one server viewer, and visible table image establish that this restarted companion session subscribed to and rendered the advertised MPEG-TS stream. The previous zero-viewer result remains unexplained; this check does not establish why the earlier attempt failed.

The Home **End recording session** action stopped the source. The final `/status` read reports `Stopped`, revision 19, timeline `c1a726f97fde4f119c8ce8a1da26f4e6`, 13,836 completed frames, `clipCount=139`, and zero viewers. Relative to the pre-start read, 5,194 frames accumulated; at the reported 30fps that is about 173.13 seconds, consistent with the 173.148-second retained history and Media3's 173.187-second load duration. The run remained Active longer than intended while its readback was inspected. It is not a bounded stillness or readiness result. The after-stop screenshot shows Home **Start live session** and Live Detect off.

No clip was added, no verifier frame was requested or saved, and Live Detect remained off. No match, Cast/TV, or camera-package action occurred. Safe, token-redacted status and load-event artifacts are under `work/o08/frame978-development-v1/device-state/`: `phone-stream-load-diagnostic-20261101.json`, `pixel10-status-after-stream-metrics-20261101.json`, `pixel11-player-load-events-20261101.log`, and the active/after-stop screenshots and UI trees.

## Gate status

The stream path worked on this restarted session, but the previous zero-viewer startup remains unexplained. The four lower-left controls still ended `SampleGap`; they are Development labels and one-frame observations, not settled checks. D1, D2, and E remain open. The latest camera status is `Stopped`, the phone is at Home **Start live session**, and the stream has zero viewers.
