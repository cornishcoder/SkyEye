# D3 slice 4: live wiring, shadow mode

11 November 2026, branch `claude/live-detect-d1-d3`. Fourth slice of
[D3](d3-settled-change-design.md), after the [comparer](d3-slice1-comparer.md),
[settle detection](d3-slice2-settle.md) and [shadow rules](d3-slice3-shadow-rules.md).
Live Detect now produces settled-change evidence from live frames, passes it
to the reducer's shadow rules and logs everything. **Nothing it finds is
applied.** Built and checked offline only: it has not run on the Pixel, and
the camera was not started.

**Target test:** live wiring per the design (sections 1 and 4) with Richard's
answers of 11 November. **Before:** the processor and rules existed but no
live code fed them. **Outcome:** wired, compiled for the device, and checked
offline; the on-device check is pending.

## Richard's answers (11 November), as built

| Question (design section 6) | Answer | Where |
|---|---|---|
| Shadow on by default? | Yes | `skyeye` preference `live-detect-settled-change`, default `shadow` |
| Cost | Turn itself off for the run when the median of the last 20 passes exceeds 40 ms | `SettledChangeCostGuard`, `settled_change_disabled` run event |
| Raster sidecars | Only on a ball-sized region, a proposal or a lighting-drift gate (plus 1 in 10 other comparisons as controls); at most 60 pairs per run | `ShellLiveDetect`, own folder `live-detect/settled-change/` capped at 120 files / 32 MB |
| Hold time limit | None while the chain is intact | Policy (slice 3) |
| Obstruction | The flag alone does not void a comparison; holds are refused while the current pass is obstructed | Policy (slice 3) |
| Gap 60 s, drift 0.08 | Provisional, tune from shadow logs | `SettledChangeSettings` |
| Setting control | Private preference; read-only line in the Live Detect panel; no Home control | Live Detect activity screen, Current status |

The mode line is on the Live Detect activity screen, not Home, so Home stays
compact and non-scrolling as `AGENTS.md` requires.

## What was built

- `SettledChangeLiveObserver` (Vision.Android): one per Live Detect run,
  called in the existing `frameObserver` while the bitmap is alive. Converts
  the bitmap (RGBA to BGR, reused scratch Mats), rectifies to the 440x840
  raster with the run's calibration, runs `SettledChangeProcessor`, and
  returns a pass. Resets on a new timeline or decoder generation (Live
  Detect already stops on a calibration change). Catches its own errors
  (resets and returns an unavailable pass). Keeps no frame or bitmap.
- `SettledChangeCostGuard` and `SettledChangePass.Unavailable` (Core).
- `ShellLiveDetect`: reads the mode, logs it with the settings in the
  `started` run event, creates the observer, passes its pass on the reducer
  observation, saves raster pairs under the rules above, logs schema 15, and
  shows the mode line.
- `LiveDetectLogStore`: schema 15, `frame.settledChange`
  (`LiveDetectLogSettledChange`), `SaveSettledChangeRaster`, pruning of
  unreferenced rasters, separate caps so rasters never crowd out frame
  evidence.
- `docs/device-control-guide.md`: schema 15, the preference, the status
  line, handlers and readback.

## Checks (software, newly executed)

- `SkyEye.Checks`: cost guard (waits for 20 passes; occasional 90 ms
  comparison passes among 18 ms passes do not trip it; a median over 40 ms
  does) and unavailable passes.
- `SkyEye.AgentUi.Checks`: schema 15 round trip (pass, evidence, change
  grid, proposals, ball states, raster references); referenced rasters
  kept, an unreferenced one pruned on reopening; the raster file cap holds
  and does not block frame evidence; older records without the field still
  read. Slice 3's golden shadow test still passes.
- Desktop runner `settled-change-selfcheck`: a frame without a raw bitmap
  gives "no raw image"; settle, move, settle gives exactly one comparison
  with its rasters kept; a raster pair encodes to JPEG (71 KB at 440x840,
  so 60 pairs is about 8.5 MB per run).
- Build: `Build-SkyEye.ps1 -Target All` 0 errors for the solution and the
  companion app (warning counts unchanged; none in the new or changed files).

## Not verified

- Anything on the Pixel: per-pass cost (desktop 15 to 19 ms plus the
  bitmap copy, which only exists on the phone), memory, the mode line, and
  the log and sidecars from a real run. Running it needs a bounded live
  session, which needs Richard's go-ahead under `AGENTS.md` (camera stopped
  unless an authorised task is collecting live evidence).
- The companion app was not installed; the camera package is untouched.

## Next

1. On-device check (needs Richard): install the build, run one bounded
   Live Detect session in shadow, stop capture and confirm `/status`
   `Stopped`, then read back `frame.settledChange`, the mode line, cost and
   the raster sidecars.
2. Score shadow sessions against reviewed labels (T9 sheets) before any
   rule is ever applied.
