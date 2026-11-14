# D3 chain-anchor fix: device check, third attempt (11 November 2026, evening)

Follows [the second attempt](d3-chain-fix-device-check2.md) (stopped by the NaN logging bug, fixed in
0.8.66). Companion 0.8.66 / code 74 (commit `fff5f27`). Evidence:
`work/production/evidence/d3-chain-fix-check3-20261111/` (`after/` log pull, `rasters-sheet.jpg`).

**Target test:** `D3-CHAIN-ANCHOR-FIX` on the device. **Before:** never exercised live (no baseline formed,
then the logging bug). **Outcome: PASS.** The settled-change chain anchored to Live Detect's rack baseline and
stayed intact through the break.

## Run `3a434fbb`

- Start live; reconnect guard after the reinstall, **Confirm camera has not moved** (Richard confirmed the
  mount about 20 minutes earlier; only the balls had been racked); Start live detect at 16:33:24 on a
  full rack. Richard broke at about 16:34:32 and stepped away.
- Stopped once the table had settled (evidence 4, chain intact): Live Detect stopped (Detecting), then End
  session; camera `/status` `Stopped` revision 21, Home **Camera ready / Start live**. About 2.5 minutes of
  capture; no Cast or TV. One stray tap during the stop landed on the disabled Show shots control (stale
  accessibility dump); no effect.

| Check | Result |
|---|---|
| Baseline | `rack_detected` and `settled_layout_baseline` at 16:33:32 (first time on the device in these checks) |
| Evidence logged | 481/481 frames carry `settledChange` (Moving 32, Settling 23, Settled 426); none unavailable |
| Cost per pass | median 15.2 ms, p95 23.0 ms, max 135.6 ms (the break comparison); guard not tripped |
| Chain | seq 2 (still rack, 0 regions) intact, length 1; seq 3 (break: 9 ball-sized regions plus the rack's oversize region) intact, length 2; seq 4 (settled, 0 regions) intact, length 3 |
| Decoder restart | 16:35:23, new epoch, seq 1 no reference, chain reset as designed |
| Shadow proposals | 8 `unexplained_change` at the break (balls moved, reducer inventory unchanged), none applied |
| Live Detect unchanged | `--golden-shadow` on the pulled log passes (4 runs, 819 frames, 78 table events identical with and without shadow evidence) |
| Stop | `Stopped`, Home Start live |

## Detector: obstruction still frequent (330 of 481 frames)

- **Rack phase** (206 frames, 105 obstructed): 100 of the 105 triggering components contain **no ball
  centre**, so it is not the rack. Most likely Richard at the table lining up the break (a real
  obstruction), but no source frames were saved, so unverified. The baseline formed regardless.
- **After the break** (275 frames, 225 obstructed): one triggering component about 164 x 101 px with 3 ball
  centres and ball-body coverage 0.38 (gate 0.52); in 121 of these frames the black is not detected. The
  black is touching a yellow/red group after the break (screenshot). This is the same pattern as group A of
  [black-missing racks](black-missing-rack-obstructions.md): a missed black inside a small touching group
  fails the coverage exemption. Black recall is the fix, not the obstruction mask.

No lossless evidence frames were saved in this run, so these passes cannot be replayed exactly.

## Next

1. Black recall in touching groups (the after-break obstruction), with the 21/32 black gate unchanged and
   empty controls held at zero. A short saved still of a post-break group with the black touching would
   allow exact desktop replay.
2. D3 shadow: review the 8 `unexplained_change` proposals against the raster sheet before any rule is
   allowed to act.

Software suite carried forward from `fff5f27` (build 0 errors, both suites pass). Physical: D3 chain anchor
PASS (newly executed); D1/D2/E 0/3 carried forward; detector inventory 0/3 carried forward.
