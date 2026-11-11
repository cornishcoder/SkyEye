# Packed cluster: count only nearby unknown balls (11 November 2026)

Follows [obstruction shadow tolerance](obstruction-shadow-tolerance.md).

**Target test:** `LIVE-FALSE-OBSTRUCTION-RACK-AFTERNOON`. **Before:** with the shadow tolerance, 5 of the 6
racked frames saved by device run `7c151fba` were still falsely obstructed. Each had a complete 7/7/1 rack,
but the cue at the other end of the table was read as `unknown`, and an unknown ball anywhere marked the
packed cluster unresolved. That left only the localised-group exemption (body coverage at least 0.52),
which these frames miss by 0.005 to 0.016. **Intended:** an unknown ball well away from the rack no longer
denies a complete rack its exemption, with no ball or other flag changed.

## Rule

`VisionAnalyzer`, the `packedClusterUnresolved` check only: an unknown ball counts against the cluster only
when its centre is within 2.5 ball radii of a red, yellow or black ball (rack neighbours are 2 radii apart).
The whole-table colour counts (7 red, 7 yellow, 1 black) still apply. Behind
`VisionSettings.PackedClusterLocalUnknowns` (default on); the algorithm identity gains
`-packed-local-unknown25`, and off reproduces the old output. A far unknown ball still makes the frame
layout-uncertain through the existing "any unknown" rule, so the frame is still not used for advice; only
the packed-cluster warning and the obstruction exemption change. The 0.52 coverage gate is unchanged.

## Result (desktop, newly executed)

| Set | Before (shadow tolerance only) | After |
|---|---|---|
| Afternoon racked frames (device run) | 5/6 obstructed | **0/6** obstructed; the 5 still layout-uncertain (unknown cue) |
| 35 labelled frames | | identical: 0 balls, flags or warnings changed |
| 438 unlabelled frames obstructed | 22 | 22 (no change) |
| Real hand/arm controls #0, #3, #35 | obstructed | still obstructed |
| Balls / uncertain / layout-uncertain changed, 473 frames | | **0 / 0 / 0** |

Software, newly executed: `Build-SkyEye.ps1 -Target All` 0 errors; `SkyEye.Checks` and
`SkyEye.AgentUi.Checks` pass; Layout03 parity 4/4 (the proofs predate the rule, so parity runs with it off).

## What blocks the remaining 19 manifest false obstructions

None of the manifest's 30 packed-unresolved frames had a far unknown ball, so this change does not touch
them. The 22 obstructed unlabelled frames (sheet numbers from `work/t2/obstructed-36-sheet.jpg`):

| What the detector reports | Frames |
|---|---|
| Real hand/arm controls | #0, #3, #35 |
| Rack 7/7, black not found, one `unknown` ball | #4, #5, #6, #10 to #16, #27 (11) |
| Rack 7/7, black not found, no unknown | #7, #8, #9, #20 (4) |
| Rack with a yellow short | #25 |
| No packed cluster detected | #1, #2, #29 |

So in 15 of the 19 remaining false obstructions the black isn't found, and in 11 of those it is most likely
the `unknown` ball (to be confirmed). That is the existing black-recall problem (black 14/28 on the labelled
set), not an obstruction-mask problem, so the next fix belongs there, not in the obstruction rule.

## Next

1. Confirm the 11 "black missing, one unknown" frames: is the unknown at the black's rack position, and
   why does it fail classification? (detector change only with empty controls held at zero.)
2. Repeat the D3 chain-fix device check, which needs Live Detect to form a baseline: needs a companion
   build with both obstruction changes.

Detector inventory 0/3 and physical gates D1/D2/E 0/3 carried forward. Not run on the device.
