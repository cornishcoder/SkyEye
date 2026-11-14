# Obstruction mask: low-chroma dim cloth (11 November 2026)

Follows [black-missing racks](black-missing-rack-obstructions.md), group B.

**Target test:** `LIVE-FALSE-OBSTRUCTION-RACK-AFTERNOON`, manifest side. **Before:** 22 of 438 unlabelled T1
frames obstructed: the 3 hand/arm controls and 19 false, 9 of them (group B: #10 to #16, #20, #27) dim
two-toned cloth where blue cloth blocks read as table-sized objects. **Intended:** clear group B without
losing any control or changing any ball.

## Rule

Obstruction mask only (`VisionAnalyzer.LargeObstruction`): also treat as cloth any pixel with absolute chroma
S x V / 255 at most 14 and brightness 0.6 to 1.15 times the cloth median, whatever its hue. At V 60 and
above that limit is S <= 60, which the neutral and shadow bands already cover, so the pass runs only over
V below 60 (dim cloth) and is skipped when 0.6x the cloth median is 60 or more. Behind
`VisionSettings.ObstructionDimChromaTolerance` (default on); identity gains `-obstruction-chroma14`; off
reproduces the old output.

Why chroma rather than saturation: on the 29 October cloth (median V 27) the rejected blue blocks were
S 64 to 116 at V 20 to 28. At that brightness saturation is noise: S 106 at V 28 is a 12-level spread
between channels; chroma is about 10 to 13. Skin chroma measured about 22 in the dimmest hand control and
about 62 in a bright one.

Tried and rejected on the way:
- S 61 to 90 at 0.6 to 1.0x brightness (relative saturation): cleared #27 only; #10 and #16 kept their
  blue blocks at S 91 to 116.
- A "hue spread across the table" test: the blue tone sits in only 10 to 12 of 32 table cells, while the
  warm hue in hand control #3 spans 16, so it does not separate cloth from skin.
- Chroma floor at 0.25x (the neutral fallback's floor), to also take the vignetted head end of the table
  (V 7 to 15): cleared #16 but **lost control #3**. See below.

## Result (desktop, newly executed)

| Set | Before | After |
|---|---|---|
| 35 labelled frames | | identical: 0 balls, flags or warnings changed |
| 438 unlabelled frames obstructed | 22 | **20** (#20, #27 cleared; 0 new) |
| Hand/arm controls #0, #3, #35 | obstructed | still obstructed |
| Balls / uncertain / layout-uncertain changed, 473 frames | | **0 / 0 / 0** |
| Afternoon device racked frames | 0/6 obstructed | 0/6 |
| Median desktop analysis time, 438 frames, 4 threads | 397 ms | 404 ms (within run-to-run noise) |

Group B is only partly fixed: #10 to #16 (7 frames) stay obstructed by two things the chroma rule does not
reach: the vignetted head end of the table (V 7 to 15, below 0.6x the median of 27) and the rack itself,
which without its black has body coverage 0.49 to 0.53 against the 0.52 gate (it varies frame to frame).

Software, newly executed: `Build-SkyEye.ps1 -Target All` 0 errors; `SkyEye.Checks` and
`SkyEye.AgentUi.Checks` pass; Layout03 parity 4/4 (the proofs predate the rule, so parity runs with it off).

## Finding: control #3 is caught by accident

In hand/arm control #3 (dim, cloth median V 48, warm cloth hue estimate), the arm and the cue are counted
as cloth because their hue matches the warm estimate. The frame is obstructed only because of a near-black
shadow band along the top cushion (V 3 to 20, 473 x 130 px). The obstruction check would miss that arm
without the band. This predates the change and is why the chroma floor stays at 0.6x. The three controls
are too few to tune obstruction rules safely; more hand/arm controls in dim warm light are needed before
the obstruction cloth estimate is changed further.

## Next

1. Add a few dim hand/arm controls to the T1 manifest (from existing saved captures; no new staging), then
   revisit the warm cloth-hue estimate that makes #3's arm read as cloth.
2. Group A and the #10 to #16 racks: black recall (do not lower the 21/32 gate).
3. Companion build with the obstruction changes and the D3 chain-fix device check.

Detector inventory 0/3 and physical gates D1/D2/E 0/3 carried forward. Not run on the device.
