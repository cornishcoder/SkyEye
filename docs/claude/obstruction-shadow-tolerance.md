# Obstruction shadow tolerance (11 November 2026)

Follows [the warm-shadow diagnosis](false-obstruction-warm-shadow.md).

**Target test:** `LIVE-FALSE-OBSTRUCTION-RACK-AFTERNOON`. **Before:** the six racked frames saved by device run
`7c151fba` were 6/6 falsely obstructed; in the T1 manifest, 36 of 438 unlabelled frames carried the "large
object" warning, of which only 3 show a real hand or arm (#0, #3, #35 in `work/t2/obstructed-36-sheet.jpg`)
and 33 are racks or clusters with nothing over the table. **Intended:** clear false obstructions caused by a
warm-filled cast shadow while keeping real hands and arms obstructed and changing no ball.

## Rule

Obstruction mask only (`VisionAnalyzer.LargeObstruction`): besides the cloth hue band and the S <= 45
neutral fallback, also treat as cloth any pixel with saturation 46 to 60 at 0.6 to 1.15 times the cloth's
median brightness, whatever its hue. Behind `VisionSettings.ObstructionShadowTolerance` (default on); the
algorithm identity gains `-obstruction-shadow60`, and off reproduces the old output. Ball detection and
classification masks are untouched.

Why these bounds (measured on rectified frames):

| | S p10 / p50 / p90 | V relative to cloth |
|---|---|---|
| Warm cast shadow, afternoon rack (frame 1913) | 50 / 53 / 59 | about 1.00 |
| Hand, manifest #0 | 63 / 93 / 99 | p10 1.02, median about 1.3 |
| Arm, manifest #35 | 79 / 94 / 108 | p10 1.04, median about 1.4 |

## Result (desktop, newly executed)

| Set | Before | After |
|---|---|---|
| 35 labelled frames | no obstruction | unchanged: 0 balls and 0 flags changed |
| 438 unlabelled frames flagged obstructed | 36 | **22** |
| Cleared | | 14, all false (racks #17 to #34 with nothing over the table) |
| Newly obstructed | | **0** |
| Real hand/arm controls #0, #3, #35 | obstructed | **still obstructed** |
| Balls changed, all 473 frames | | **0** |
| Layout-uncertain flag cleared | | 4 frames (#30, #31, #33, #34); the others stay uncertain for other reasons |
| Afternoon racked frames (device run) | 6/6 obstructed | 5/6 obstructed |

On the afternoon frames the rack component shrinks from 404 to 419 px to 220 to 233 px tall, inside the
320 px packed-cluster limit, with ball-body coverage 0.50 to 0.52. The frame where the cue was found clears
through the complete-rack exemption. The other five stay obstructed for a second, separate reason: the cue,
alone at the other end of the table, is read as `unknown`, and `packedClusterUnresolved` counts an unknown
ball anywhere on the table, so the rack is marked incomplete and only the localised-group exemption (body
coverage at least 0.52) is left, which they miss by 0.005 to 0.016. That coverage gate was not changed.

Software, newly executed: `Build-SkyEye.ps1 -Target All` 0 errors (warnings unchanged; none in the changed
files); `SkyEye.Checks` and `SkyEye.AgentUi.Checks` pass; Layout03 parity 4/4 (the proofs predate the rule,
so parity runs with it off and still reproduces them exactly).

Remaining 22 obstructed unlabelled frames: the 3 real controls and 19 false (#1, #2, #4 to #16, #20, #25,
#27, #29), dim racks mostly, not yet diagnosed.

## Next

1. Separate change: count only unknown balls inside or touching the packed cluster as making it
   unresolved (an unknown cue at the other end of the table does not make the rack unresolved). Score the
   same way.
2. Diagnose the 19 remaining false obstructions.
3. Then repeat the D3 chain-fix device check, which needs Live Detect to form a baseline.

Detector inventory 0/3 and physical gates D1/D2/E 0/3 carried forward. Not run on the device.
