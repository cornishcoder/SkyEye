# O09 camera source reachability — 2 November 2026

## Result

`O09-CAMERA-SOURCE-REACHABILITY` **PASS 4/4 for reachability and idle readback**. The Pixel 11 companion rediscovered the mounted camera, the existing `/status` endpoint responded again, and the companion returned to Home. Batch readiness remains **NOT READY** because the mounted camera battery is at 3% and the user has not yet confirmed both planned lighting conditions. No live session or verifier capture was started.

The four preflight checks were: Pixel 11 is present on ADB; **Find camera** discovers the mounted **SkyEye Camera**; the configured `/status` endpoint responds after discovery; and Home shows **Camera ready** with **Start live session**. This is a manual device preflight count, separate from software assertion and detector metrics.

## Readback

At `2026-11-02T10:29:24Z`, the latest token-free status snapshot reported:

| Field | Value |
| --- | --- |
| Camera API state | `Ready` (not `Stopped`; no live session was started) |
| Revision | 0 |
| Battery / temperature | 6% / 29.9 °C |
| Completed source frames | 0 |
| Viewers / host | 0 / none |
| Saved clips | 139 |
| Companion Home | `Camera ready`; action `Start live session` |

The current phone screenshot is retained as `work/o09/device-state/pixel11-home-after-camera-rediscovery.png`; the safe status subset is `work/o09/device-state/camera-readback-20261102.json`. The status snapshot deliberately excludes the access token. The earlier screenshots show the initial notification shade, Settings disconnected state, finder result, and selected camera.

## Recovery sequence and attempt history

The initial direct `/status` reads at the saved address timed out, and the neighbor/port check found no responder on port 8765. Pixel 11 was reachable over ADB. I brought the existing companion to the foreground; its Settings page showed `Connect your camera` at `http://192.168.1.124:8765`. The visible **Find camera** control discovered **SkyEye Camera**. Selecting it restored `Camera ready · Ready`; the same `/status` address then responded. The companion was returned to Home using the fresh accessibility node for **Back to home**, and its current Home tree confirms **Start live session**.

Two command-wrapper errors did not change device state: the one-line Python address probe had invalid newline quoting, so a bounded PowerShell TCP check was used; the first Home readback script used PowerShell's reserved `$HOME` variable, so I re-read the UI tree with a different variable. No tap was repeated after the Home tap.

The camera status rose from 1% to 2%, 3%, then 6% over successive readbacks; the latest is at `10:29:24Z`. No minimum safe batch reserve is assumed. Keep capture off until the mounted camera has enough charge to finish the planned bounded batch. The user reports the table is free; lighting readiness is not yet verified. Do not start training or another detector probe until the v3 Development collection has been captured, reviewed, and exported with actual group-isolation checks.

## Acceptance metrics

- Detector: `O09-TRANSFER-NEW-CONTROLS-PROBE` remains **FAIL 0/7**; do not replay or tune that frozen set.
- Software suites **3/3** and focused motion checks **20/20** remain carried forward; they were not run in this preflight.
- Detector inventory **0/3**; measured lighting strata **0**; Holdout unopened; physical D1/D2/E **0/3 proven**.
- Next physical target remains `O09-DEVELOPMENT-LAYOUT-LIGHTING-EXPANSION`, after camera charge and confirmation of playable `normal-pool-room` and `warm-dim-playable` conditions.
