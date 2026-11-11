# False obstruction from a warm cast shadow (11 November 2026)

**Target test:** `LIVE-FALSE-OBSTRUCTION-RACK-AFTERNOON`. **Before:** in device run `7c151fba` (0.8.64,
afternoon) 257 of 259 frames were `obstructed` with nobody at the table, so Live Detect formed no baseline
(`d3-chain-fix-device-check.md`). **Outcome:** reproduced exactly on the desktop detector and the cause found.
Diagnostic only; no detector change.

Inputs: the six lossless racked source frames the run saved (frames 1913 to 2343,
`work/production/evidence/d3-chain-fix-check-20261111/after/live-detect/evidence/`), with the current
calibration corners read from `/api/snapshot` (identical to the 31 October and Layout03 receipts).
Tool: `SkyEye.Vision.Desktop.Runner analyze --image F.png --corners "…" [--mask out.png]` (production
settings, prints flags, packed-cluster telemetry and the obstruction audit, and saves the analyser's own
obstruction mask).

## Result

| Frame | Obstructed | Triggering component (analysis px) | Ball centres | Cue found / normalised |
|---|---|---|---|---|
| 1913 (`7d6db8d0…`) | yes | 220 x 408 at (300, 1090) | 15 | no |
| `e5049c3a…` | yes | 255 x 417 | 15 | no |
| `ca842a6e…` | yes | 222 x 417 | 15 | no |
| `0f2f0a76…` | yes | 231 x 405 | 15 | no |
| `57742837…` | yes | 234 x 419 | 15 | no |
| `a449bbe4…` | yes | 228 x 404 | 15 | **yes** |

6/6 reproduce the device result. The packed-cluster limit is 320 x 320, so a component 404 to 419 px tall is
`sizeEligible = false`, and neither the complete-rack nor the localised-group exemption can apply, whatever its
ball count or body coverage. The one frame where the cue was found and colours normalised is still
obstructed, so the missing cue reference is not the cause (it only marks the packed cluster incomplete).

## Cause: the rack's shadow is warm, not dark

The obstruction mask is the rack plus a solid block running from the rack to the foot cushion. HSV on the
rectified frame:

| Region | H | S | V |
|---|---|---|---|
| Open cloth, centre | 102 | 44 | 86 |
| Right of the rack (mask block) | 17 | 53 | 86 |
| Right of the cue | 25 | 27 | 56 |
| Foot end of the cloth, no balls | 18 | 30 | 84 |

The block is not darker than open cloth; its hue is warm orange instead of the cloth's blue-grey. Mapped over
the cloth, warm hue covers 13.8 % of the interior as a cone that starts at the rack and widens towards the
foot cushion (0 % up to table x 1.31, then 12 %, 27 %, 53 % and 92 % by x 1.81), with a small matching tail on
the cue. That is a cast shadow under mixed light: daylight from the window side is blocked, and the warm room
light fills the shadow, so it keeps roughly the cloth's brightness but changes hue. The cloth mask samples the
cloth hue (103 here) with a band of 7, and its neutral fallback accepts only saturation below 45, so this
shadow (S 53) is non-cloth and joins the rack into one oversized component.

This is the warm-cloth failure that section 2 of the detection plan listed as a hypothesis ("Cloth mask is a
fixed hue band … stays a hypothesis"), now shown on real frames. The morning run at 12:38 read the same rack
with no obstruction; the light differed.

Evidence: `rack-frame1913-table.png`, `rack-frame1913-crop.png`, `mask-1913.png`,
`mask-1913-landscape.png`, `warm-cloth-1913.png` in `work/production/evidence/d3-chain-fix-check-20261111/`.

## Candidate fixes (not built; each needs scoring)

1. Shadow-tolerant cloth test for the obstruction mask only: accept low-saturation pixels at cloth
   brightness whatever their hue (for example S below about 60 and V within the cloth range). Painted balls
   (S above 100) stay non-cloth; the cream cue (S 60 to 150) needs checking.
2. Size the packed-cluster test on the ball bodies' extent rather than the whole component, when the rest of
   the component is cloth-bright and untextured.

Scoring for either: these six frames must clear; real obstruction controls (hands and arms, for example the
o08 hand-obstruction pair and the code57 cue-obstruction clip) must stay obstructed; the T1 manifest `detect`
score must not regress; empty controls must stay empty.

Detector inventory 0/3 and physical gates D1/D2/E 0/3 carried forward. Camera not touched in this step.
