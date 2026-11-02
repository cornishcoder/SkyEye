# O09 lighting metadata preflight — 2 November 2026

## Result

`O09-LIGHTING-METADATA-PREFLIGHT` is **READY 4/4 for collection prerequisites**. Pixel 11 is connected, the mounted-camera endpoint responds, the companion is idle at Home, and the user confirms both `normal-pool-room` and `warm-dim-playable` are playable and the camera is charged. The fresh camera battery reading is 73%.

This does not measure either lighting stratum. Table-centre lux, light source/colour-temperature description, exposure state and direct glare remain unrecorded (**0/2 strata measured**). The orientation-A rack/phone-position staging confirmation is still pending. No live session, verifier operation, clip, detector run, match, Cast action, package update or app-data reset occurred in this check.

## Readback

At `2026-11-02T11:23:08.2988228Z`, `/status` reported API state `Ready` (revision 0), 73% battery, 33.7 °C, zero completed frames, zero viewers, no host and 139 saved clips. The camera advertises 1920×1080 at 30 fps. `Ready` is the fresh idle state here; this is not a literal `/status: Stopped` readback.

ADB sees Pixel 11 Pro XL serial `66181FDDV001HS`. Its current accessibility tree shows the Home action **Start live session** enabled and **Capture verifier frames** disabled. The companion did not start capture. The user reports that both contract lighting conditions are playable and that the camera is charged; these are user-reported playability conditions, not measured lighting readings.

Token-free readback: `work/o09/device-state/lighting-metadata-preflight-20261102.json` (SHA-256 `21f9fedfe404a77a0f97491176b88033807a99611a0924650fc28c704ea24b8c`). Home accessibility readback: `work/o09/device-state/pixel11-home-readback-20261102.xml` (SHA-256 `4b33549d0ec0aad63e2792f73ab54f204ec1a482bf8746a07c290ff7230b092f`).

## Readiness checks

| Check | Result |
| --- | --- |
| Pixel 11 connected on ADB | PASS |
| Mounted-camera `/status` reachable | PASS |
| Home idle with `Start live session`; verifier action disabled | PASS |
| User confirms both playable strata and sufficient camera charge | PASS |
| Measured table illuminance, source/colour temperature, exposure, glare | NOT RUN; 0/2 strata |
| First layout staged and hands clear | PENDING |

The first light measurement will use the Pixel 11 TMD3743 ambient-light sensor with the phone face-up at table centre for five seconds, as prearranged. Record the raw sensor reading and identify it as a handset sensor measurement, alongside source description, exposure state and glare. Do not infer lux from the lighting label or treat the phone sensor as a calibrated meter.

## Metrics and next action

- Software suites **3/3 PASS, carried forward**; focused motion checks **20/20 PASS, carried forward**. Neither suite was run for this device preflight.
- `O09-TRANSFER-NEW-CONTROLS-PROBE` remains **FAIL 0/7 complete layouts**; 109 reviewed visible balls, 36 TP, 664 capped FP, 73 FN, 5.14% precision, 33.03% recall, zero correct black/cue matches. Do not repeat or tune on that frozen set.
- Detector inventory **0/3**; measured lighting strata **0/2**; Holdout unopened; physical D1/D2/E **0/3 proven**.
- Checkout: `9372d79` (`Record O09 camera rediscovery preflight`).

Next target: **`O09-NORMAL-STRATUM-LUX-RECEIPT`**, once the user confirms the normal-light full 16-ball rack (orientation A) and hands-clear phone placement. Then capture one bounded saved-evidence operation and stop before any table change. Continue with the frozen Development batch and split plan; no candidate or acceptance result follows from this readiness check.

The remaining work before a play test is still measured in gates rather than calendar time: Development acquisition/review and one frozen candidate decision; independent Holdout plus D1; then installed-phone D2. Event/clip gate E remains after those and is separate. Capture/review throughput and the candidate outcome are not yet measured, so a calendar estimate would be invented.
