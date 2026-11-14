# T5 surplus-only inventory demotion

11 November 2026, branch `claude/live-detect-d1-d3`. Thread T5 of the
[detection plan](skyeye-detection-fix-plan.md). Scored on desktop with the T2
runner ([t2-desktop-parity.md](t2-desktop-parity.md)).

**Target test:** T5 on the 35 labelled T1 manifest frames. **Before:** colour
accuracy 0.970; Layout03 frames 957 and 2263 at 7/15 colours, because eight
"yellows" turned every yellow into `unknown`. **Intended:** keep the seven
real yellows without changing balls found, false positives or any other frame.

## Result

| 35 labelled frames, desktop, production settings | Before | After |
|---|---|---|
| Balls found | 529/546 | 529/546 |
| False positives | 0 | 0 |
| Colour accuracy | 0.970 | **0.996** |
| Yellow colour accuracy | 0.943 | **1.000** |
| Black / cue found | 14/28, 25/28 | 14/28, 25/28 |
| Frames fully correct | 19/35 | 19/35 |

Only 957 and 2263 change: from red 7 and unknown 8 to red 7, yellow 7 and
unknown 1. The one unknown is the cue, which is still not identified. Both
frames stay flagged uncertain, as before, because any `unknown` sets the
layout flag. On the four Layout03 frames, correct colours go from 43/59
to 57/59. Frames fully correct do not change, because the cue is still
unknown on both.

On the 438 unlabelled manifest frames (no labels, so no score), 20 change, all
the same way: eight balls of one colour become seven plus one `unknown`, and
every changed frame stays flagged uncertain. In 19 of them the eighth "yellow"
sits where the cue rests: (0.155, 0.843) on Layout03 (973, 987, 2219, 2234,
2248, 2278, 2293, 2309) or about (0.46, 0.487) in rack and break frames (250
twice, 267, 280, 303, 314, 328, 340, frame-0001 to frame-0003). None of the 19
reports a cue. In the 20th, 143 (hand-obstruction pair), an eighth "red" at
(1.614, 0.397) is the one demoted. Holdout was not opened.

## Rule

`BallClassifier.ConstrainInventory`, behind `VisionSettings.SurplusOnlyInventoryDemotion`
(default on):

- More than one cue or black: unchanged, every one becomes `unknown`. Choosing
  between two candidate identities is not a colour clean-up.
- More than seven reds or yellows: rank them by the saturation of their most
  saturated colour cluster. Demote only the surplus (the lowest ranked), and
  only when it sits at least 40 below the lowest ball kept. Otherwise demote
  the whole colour, as before.
- Algorithm identity gains `-surplus-sat40` when on. Off reproduces the old
  identity and output exactly: Layout03 parity against the old device proofs
  still passes 4/4 with the setting off.

### Why saturation and not quality

The plan said "least confident first". The classifier's quality does not
support that. It saturates at 0.9: on 957 and 2263 all eight "yellows",
including the cue, score exactly 0.9000. On 143 the eight reds score 0.8982
to 0.9000, so ranking by quality demoted a well-saturated red at
(1.379, 0.500) on a 0.0005 difference. An earlier version of this change
ranked by quality and then saturation; it gave the same score on the 35
labelled frames but made that arbitrary choice on 143. It was replaced.

Saturation separates every case found. The most saturated cluster of a
painted yellow has S 248 to 253, and of the surplus ball 115 to 170:

| Frames | Colour | Surplus S | Lowest kept S | Gap |
|---|---|---|---|---|
| Layout03 957, 2263, 973, 987, 2219, 2234, 2248, 2278, 2293, 2309 | yellow | 161 to 170 | 250 to 251 | 80 to 89 |
| Rack/break 250, 267, 280, 303, 314, 328, 340, frame-0001 to frame-0003 | yellow | 144 to 157 | 249 to 252 | 95 to 106 |
| Second 250.png (dimmer copy) | yellow | 123 | 207 | 84 |
| 143 | red | 115 | 234 | 119 |

The lowest kept ball ranges from S 207 (the dimmer copy of 250) to 252.
A margin of 40 is half the smallest gap seen (80). It is a threshold set on
Development frames: on new frames, the smallest gap and dim light (where kept
balls drop towards 207) are what to watch.

Diagnostic used: `SkyEye.Vision.Desktop.Runner classify --names ...` prints
each published circle's raw kind, quality and colour clusters.

## Checks

`SkyEye.Checks` (PortChecks) adds: the outlier yellow is demoted and seven
kept; a slightly lower quality on a well-saturated red does not single it
out; a gap one below the margin demotes the whole colour, a gap at the margin
singles out the outlier; two clear outliers are both demoted; the old rule
stays available; duplicate cue identities stay unresolved.

Software, newly executed after the change: `Build-SkyEye.ps1 -Target All`
0 errors, no warnings in the changed files; `SkyEye.Checks` and
`SkyEye.AgentUi.Checks` pass. Detector inventory 0/3 and physical gates 0/3
carried forward. Not run on the device.

## Limits

- Desktop OpenCV 4.13 on PNG frames, parity-checked against the device on
  Layout03 only.
- The 20 unlabelled changes are unverified: no labels say which ball is
  the surplus, only that it sits where the cue rests (or, on 143, that its
  colour is far below the reds).
- The underlying fault, a cream cue read as yellow when colour normalisation
  has no cue reference, is not fixed; the cue stays `unknown`.

Regenerate:

    dotnet run --project tests\SkyEye.Vision.Desktop.Runner -- detect --manifest tests\fixtures\detection-corpus\manifest.json --output work\t2\t5-before.jsonl --settings work\t2\settings-production-previous-inventory.json --threads 4
    dotnet run --project tests\SkyEye.Vision.Desktop.Runner -- detect --manifest tests\fixtures\detection-corpus\manifest.json --output work\t2\t5-after.jsonl --threads 4
    python tools/evidence/score_detections.py --manifest tests/fixtures/detection-corpus/manifest.json --detections work/t2/t5-after.jsonl --name t5-after

(`settings-production-previous-inventory.json` is
`{"Width": 800, "PackedColourBoundarySearchExtentFraction": 0.65, "SurplusOnlyInventoryDemotion": false}`.)
