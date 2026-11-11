# Black-absent dim rack control and the packed-black dark-body check (11 November 2026)

Follows [black in touching groups](black-touching-groups.md), which left the adjacent-ball rule off for want
of a real black-absent rack control.

**Target test:** `BLACK-ABSENT-DIM-RACK-CONTROL` (new). **Intended:** a real control for "no false black in
a black-absent dim rack", then a decision on the adjacent-ball rule.

## Capture (physical, newly executed)

Richard racked the 14 reds and yellows **without the black** (gap showing cloth), cue in baulk, evening room
light. Companion 0.8.66: Start live, **Capture verifier frames** once (run `aa485778`, 5 lossless frames
776 to 837, table result "Ready"), End recording session; camera `/status` `Stopped` revision 23; about
25 s of capture. Every PNG matches its receipt SHA-256; receipt corners equal the current-mount corners.
Table V 66 (dim). Frames: `work/production/evidence/black-absent-dim-rack-20261111/`; fixture
`tests/fixtures/detection-corpus/black-absent-dim-rack.json` (role `black-absent-control`, expect black 0).

## Finding: production already reports a false black here

On all 5 frames production (0.8.66 detector) reported **16 balls including a black at (1.48, 0.50), table
clear and not uncertain**: a black-absent rack read as a complete rack. The candidate audit shows it is the
**existing** packed-black occlusion exception: 19/32 sectors visible, 11 explained by "exterior rejected
next to a neighbour", 2 by fitted bodies, then the shadowed cloth in the gap classifies as black (it is
under the absolute dark-body ceiling of V 40). The new adjacent-ball rule plays no part (0 sectors).

## What separates a real black from the gap

Annulus 0.55 to 0.85 R (skips the white number disc and highlight), 25th percentile V, divided by the median
V of the inner table (desktop measure):

| | Ratio |
|---|---|
| Real blacks, every detection in saved captures | 0.2 or less (279 of 292 detections) |
| Gamma-brightened copies only | 0.33 to 0.57 (synthetic: brightening lifts dark pixels) |
| Gap of the black-absent dim rack | 0.36 to 0.39 |

The detector's hue-filtered cloth median (`ClothRange`) is a poor denominator: it read 47 and 91 on two dim
frames, which left a real black at 0.23 against the gap at 0.30. The fix uses the inner-table median.

## Fix (on by default)

`VisionSettings.PackedBlackOcclusionDarkBody` (default **true**; identity `-packed-black-darkbody28`; false
replays the old output): a packed black accepted on **occlusion** evidence must also have its annulus p25 V
at most 0.28 x the inner-table median V, measured at the expected ball radius (a fit inside a gap can be
half a ball wide). Blacks with full boundary support are not affected. Audit evidence gains
`bodyAnnulusP25V` and `tableMedianV`; a rejection reads `rejected-occluded-body-not-dark`.

## Result (desktop detector, newly executed)

| Set | Before | After |
|---|---|---|
| Black-absent dim rack control (5 frames) | 5/5 false black, read clear | **0/5**; 15 balls, uncertain (black missing) |
| 35 labelled frames | black 14/28, 0 FP | **identical** |
| 438 unlabelled frames | | **0 frames change** (blacks 260, obstructed 20) |
| 31 hand/arm controls | | identical |
| Afternoon device racks (6) | black found | black kept, 6/6 |

A first version using `ClothRange` at 0.22 cleared the control but lost 2 real blacks (frame 1473, two
copies; measured 11/47 = 0.23); the inner-table median removed that loss.

Software, newly executed: `Build-SkyEye.ps1 -Target All` 0 errors; `SkyEye.Checks` and
`SkyEye.AgentUi.Checks` pass; Layout03 parity 4/4.

## The adjacent-ball rule, re-scored with the dark-body check

`PackedBlackAdjacentBallOcclusion` (still **off**): with both on, the control stays 0/5; labelled frames
identical; unlabelled blacks 260 to 269 (+9, all real dim racks of 27 and 29 October), obstructed 20 to 14;
the earlier g16/314 cue regression is gone (gamma copies no longer gain blacks). It now meets the bar
except a device run.

## Limits

One control layout under one light level; the after-break touching group from the device run is still not
saved, so not tested.

Detector inventory 0/3 and physical gates D1/D2/E 0/3 carried forward. Not run on the device.
