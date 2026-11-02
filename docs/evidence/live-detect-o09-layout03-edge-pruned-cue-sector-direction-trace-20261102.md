# Layout03 edge-pruned cue sector direction trace — 2 November 2026

**Target:** `O09-NORMAL-TRAIN-LAYOUT03-EDGE-PRUNED-CUE-SECTOR-DIRECTION-TRACE`
**Result:** PASS as a bounded directional diagnostic; HOLD integration.

## Finding

The candidate-specific 32-sector traces repeat the same directional pattern on all three reviewed cue-present frames. The exterior-rejected arc faces the rectified `y=0` table boundary, while the fitted centre shifts in positive `y`, away from that arc. The rejected outer samples are 35.0–53.9 width-800 pixels from the `y=0` boundary and 98.8–128.7 pixels from their nearest calibrated pocket centre. They are outside both the nominal 48-pixel pocket radius and its width-800 effective radius of 38.4 pixels.

This supports an association with the projected table edge/background, not a pocket-mouth explanation. It does not prove the exact physical cause of the background contrast or establish a generally accurate cue fit. All three positives are from one Development setup.

| Frame | Reviewed state | Refined centre (width-800 px) | Radius | Support | Exterior-rejected sectors | Weak sectors | Distance to reviewed centre |
| ---: | --- | ---: | ---: | --- | --- | --- | ---: |
| 1274 | Black and cue present | `(106.70, 57.88)` | 17.8 | 20/32 | 17–28 | none | 8.08 px |
| 3360 | Black absent; cue present | `(105.57, 56.86)` | 18.6 | 21/32 | 17–27 | none | 6.94 px |
| 382 | Black and cue restored | `(109.08, 57.01)` | 19.4 | 22/32 | 18–27 | none | 8.04 px |

Sector directions are `x=cos(iπ/16), y=sin(iπ/16)` with image `y` increasing downward. Thus the repeated rejected runs around indices 17–28 face upward toward `y=0`. Every unsupported sector in these three selected fits is marked `exteriorRejected`; none is a weak non-exterior sector. The reviewed cue point was `(105.274832, 49.924167)`, so each refined fit is biased down from it by 6.94–7.96 pixels. This matches the earlier centre-error range and keeps spatial accuracy unaccepted.

## Replay provenance and output parity

The three positive frames were replayed separately because each receipt belongs to a different source timeline. They used the same exact analyzer configuration and the new diagnostics-only Vision Device APK SHA-256 `C9A2C3F9E08F708014A45E46D482167BB48F1541874354F0B43F88489FB94474`.

| Frame | Source pixel SHA-256 | Receipt SHA-256 | Replay report SHA-256 |
| ---: | --- | --- | --- |
| 1274 | `56a2ff84523db8dffdecac7d53e9c75936f2b5efdf4b62e014a61aa07ad10d99` | `d4d980ff925d5965a3d2bea64d168f8170ec063bec7739d8b58ba326a5d55902` | `1c24be02aadfbe78e8bda8dfb166677c6a00919d148adb825cbfc263360fa3ff` |
| 3360 | `01631fc5dc122f077f93cf07949ce6afb556809d8b37eb0bff04d72cc8195dd8` | `6cd682539ee9e70ae4e9801846010b153130735ae28a07058e53a4a9fa73f710` | `8a3d9cfa972f20cc677da044b3288a8a3b9b32788fcc4a4a3e14e5d492805ad7` |
| 382 | `6e7be3dab3f31cfe62eec39b4bad75d69a5acdb25eb4a305402b14499e7f0093` | `e0e919de2f48a8b427507a3752ab9b95c6d36e73daafbad0fae4d837b39b4859` | `ddfbeceacbc15b4bd5e3fd581fad86456679791c2a39951eacd935943e9cdd2a` |

Each report hash matches its manifest and each replay reports `configurationComparison: exact-match`. On all three frames, the ordinary detector counts, uncertainty flags, cue count/reason, and full circle centre/radius lists match the earlier full-audit runs exactly: 7 red + 7 yellow, 14 circles, zero cue detections, and both `uncertain` and `layoutUncertain` true. The diagnostic collection did not alter these detector outputs.

The cue-absent frame 395 remains a reused exact-profile full-audit negative: report SHA-256 `26aecb8bbc95ffeec265628094e2cd2500f71f0ff633fe1c7d797cecdf6e7499`, source pixel SHA-256 `dacc2a270864dfad5daaa6a858f27f8b48e337c4ea133d18629da26d88d2cec2`, receipt SHA-256 `8f87349c05e4debbf4d5f172ffaf96bd578bf22cdf25d8df587c2d84c69950e7`, and older replay APK SHA-256 `EFB3E7EE4B30782D8A1697FAF18147D183818AB1632B690F7D76E2301E1F9625`. It had no cue recovery candidate. It was not rerun because neither its pixels nor the negative path changed; the new sector telemetry is emitted only for a fitted candidate.

One initial frame-1274 `probe-only` invocation is excluded: its receipt had `detectionRequested=false`, so the report had no width-800 analysis. The corrected `full-audit` run above is the scored positive. The probe-only invocation is retained at `work/o09/development-v3/reviews/layout03-current-source-baseline-v1/edge-pruned-cue-proposal-experiment-20261102/sector-direction-trace-20261102/cue-sector1274-153544/` and did not start live capture.

## Validation and decision

- `dotnet run --project tests/SkyEye.Checks/SkyEye.Checks.csproj -c Release --no-restore`: all 69 named groups reported PASS, including the existing boundary-refinement differential checks.
- Vision Device Release build: 0 errors, 23 warnings; tested replay APK SHA-256 `C9A2C3F9E08F708014A45E46D482167BB48F1541874354F0B43F88489FB94474`.
- Three corrected full-audit positives: 3/3 complete, exact configuration, report-hash valid. Reused cue-absent control: 1/1 prior exact-profile report, not rerun.
- Camera before and after every replay was `Stopped`, clip count stayed 139, and no live capture was started.
- Fresh final readback at `2026-11-02T15:46:59.806Z`: `/status` `Stopped`, revision39, 37,352 completed frames, `clipCount=139`, battery100%, zero viewers. Pixel 11 Home hierarchy showed `Camera ready` and enabled `Start live session`; Live Detect was unavailable while stopped. The prior verifier message remained historical. Token-free snapshot: [final safe-state readback](../../work/o09/device-state/edge-pruned-sector-trace-final-safe-state-20261102.json).

Keep production thresholds and cue admission unchanged. This trace explains a repeatable directional support loss for this reviewed setup; it does not fix the 0/4 ordinary cue detections, pass detector inventory acceptance, or satisfy D1, D2, or Holdout. Next priority is the D2 stillness matrix coverage audit so the remaining physical window/acquisition work can be scheduled without repeating completed evidence.
