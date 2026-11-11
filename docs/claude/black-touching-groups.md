# Black in touching groups (11 November 2026)

Follows [the third device check](d3-chain-fix-device-check3.md), where after the break a 3-ball group with the
black missed kept Live Detect obstructed (coverage 0.38 against 0.52), and
[black-missing racks](black-missing-rack-obstructions.md) group A.

**Target test:** `BLACK-TOUCHING-GROUPS`. **Before:** in dim racks the black is not detected (T1 unlabelled:
group A #4 to #9 and the 29 October racks); labelled black recall 14/28. **Intended:** find the failing gate,
and recover the black where it touches other balls without any false black and without lowering the 21/32
black gate.

## Diagnosis (desktop `audit`, newly executed)

**Labelled "touching" misses are a contrast problem, not touching.** The two labelled misses with a ball at
about one diameter are Layout03 2083 and 2263 (black next to the cue near the top-left corner, mid light).
In 2263 a sparse-black candidate fits 0.003 from the label with 15/32 sectors supported against the 21/32
gate; the uncovered sectors face dark cloth (outside V 10 to 30, edge 1 to 11) at 1.5 to 3 R from any
neighbour. In 2083 no black candidate is proposed near it. Meeting these needs a lower gate, which is ruled
out.

**In a rack, the gate is the packed-black fit (`.7`, so 23/32), and support tracks brightness.** The same
29 October rack frame 250 and its gamma copies:

| Copy | Table V | Packed-black support | Result |
|---|---|---|---|
| plain | 28 | 15/32 | rejected |
| g12 | 43 | 20/32 | rejected |
| g14 | 58 | 20/32 | rejected |
| gamma-diagnostic | 66 | 21/32 | rejected |
| g16 | 73 | 22/32 | rejected |
| g18 | 86 | — | found |

Every fit is within 0.02 to 0.03 of the rack centre. The unsupported sectors are where the black presses
against a red or yellow: each sector's outer point lies 0.8 to 1.3 R from a recognised neighbour's centre. The
existing occlusion exception explains 0 of them, because it needs a measured exterior rejection (none: the
exterior is a dim red, not rejected) or a neighbour with support at least 0.7 containing both sample points.

## Candidate rule (behind a setting, OFF by default)

`VisionSettings.PackedBlackAdjacentBallOcclusion` (default **false**; identity `-packed-black-adjacent125`
when on): in packed-black recovery only, an unsupported sector also counts as occluded when its outer point
lies within the existing conservative occlusion radius (1.25 R) of a distinct recognised red or yellow. It
still needs 14/32 visible, every other sector explained, and a black classification. The 21/32 sparse-black
gate and the 0.7 packed fit gate are unchanged. Audit evidence gains `adjacentBallOccluded`.

## Result with the rule on (desktop detector, newly executed)

| Set | Rule off (production) | Rule on |
|---|---|---|
| 35 labelled frames | black 14/28, 0 FP | **identical** (14/28, 0 FP; one frame's coordinates shift slightly) |
| 438 unlabelled: blacks found | 260 | 278 (+18, all at the rack's black position, 5 to 6 neighbours at one diameter) |
| 438 unlabelled: obstructed | 20 | **14** (group A #4 to #9 cleared) |
| Hand/arm controls (31) | 14/16 obstructed | identical |
| Other position changes | | 10 frames shift by at most 0.0085 table units |
| **Regression** | | gamma g16 frame 314: true cue at (0.46, 0.49) lost, false cue inside the rack at (1.54, 0.58) |

**Black-absent safety is not established.** A synthetic control (rack #4 and #12 with the black painted over
in cloth colour) cannot discriminate: the existing detector, rule **off**, already reads the flat painted
disc (V about 25, hard round edge) as a black. A real black-absent rack shows an irregular gap with shadows,
not a clean disc. No saved frame is a dim black-absent rack.

Software, newly executed: `Build-SkyEye.ps1 -Target All` 0 errors; `SkyEye.Checks` and
`SkyEye.AgentUi.Checks` pass; Layout03 parity 4/4. Production output is unchanged (setting off).

## What it would take to turn it on

1. A real dim black-absent rack control (a short capture at the table, rack without the black, at the
   evening light level): new staging, so your call. The rule must add no black there.
2. Fix or explain the g16/314 cue regression.
3. Then score again on the T1 manifest, hand controls and device frames, and a device run.

The device's after-break group (3 touching balls) was not saved, so it is not directly tested; the rule only
covers packed clusters, so it may not reach that case either.

Detector inventory 0/3 and physical gates D1/D2/E 0/3 carried forward. Not run on the device.
