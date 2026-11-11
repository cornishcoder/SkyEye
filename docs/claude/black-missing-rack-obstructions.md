# Racks with the black missing: why they read as obstructed (11 November 2026)

Follows [packed cluster local unknowns](packed-cluster-local-unknowns.md). Diagnostic only; no detector change.

**Target test:** `LIVE-FALSE-OBSTRUCTION-RACK-AFTERNOON`, its manifest side: the 19 unlabelled T1 frames still
falsely obstructed after `8ae48c2`. **Current result:** 15 of them are full 7/7 racks whose black is not
detected; 11 of those also have one `unknown` ball, which I assumed was the black. **Intended:** find the
actual cause per frame, so that the next fix targets it.

## The `unknown` ball is the cue, not the black

In all 11 frames the `unknown` ball sits at x 0.43 to 0.46, y 0.487, about 1.07 table units from the rack
centroid: the cue on the baulk side, dim and beige in this light. No frame finds a cue. The black, in the
centre of the rack, is not detected at all (15 balls = 7 red + 7 yellow + the unknown cue). Contact sheet of
rectified #4, #7, #10, #20, #27 confirms this visually: the dark centre of every rack is the black.

The unknown cue is far from the rack, so the local-unknowns rule already ignores it; the rack stays
unresolved because the black count is 0.

## Two different causes of the obstruction

Measured with `analyze --mask` (desktop, newly executed, production settings) on #4, #10 and #27, with
HSV percentiles on the rectified frame (white in the mask = not cloth).

| Group | Frames | What triggers | Cause |
|---|---|---|---|
| A: dim warm replay `o08-code30-live-detect-20261027` | #4 to #9 (6) | Rack component 119-122 x 164-165 px, 4 ball centres, body coverage 0.37 to 0.39 | Cloth mask is fine (only 1,225 rejected inner pixels, V p50 12: gaps between balls). The rack is unresolved because the black is missed, so only the localised-group exemption (coverage at least 0.52) is left, and the undetected black and the dark gaps fail it. **Fix: find the black.** |
| B: device check `20261029` (plain, gamma-diagnostic, gamma-sweep) | #10 to #16, #20, #27 (9) | Table-sized blocky components (#10: 696 x 609 px containing 14 ball centres, plus a 696 x 296 one with none; #27: 315 x 180 px with none) | The obstruction cloth estimate lands on a warm hue: kept cloth H p50 45 (#10) and 53 (#27). Large 8x8-block patches of the real blue cloth, H p50 104 to 105, S 64 to 89, at cloth brightness (V 21 to 44), are outside the hue band and above the S <= 45 neutral fallback, so they read as an object. **Fix: cloth estimation in dim, compressed frames.** |

The dim cloth is two-toned under the warm lamp: warm-tinted low-saturation areas and blue blocks at
moderate saturation from video compression. `ObstructionClothRange` estimates hue from pixels with S <= 70,
weighted by saturation, so it takes the warm tone and the bluer blocks (S 64 to 89) are under-sampled.

Noted, not chased: on #20 `analyze` reports `obstructed False` (rack exempted, coverage 0.557) but its
warnings still include "A large object covers part of the playable surface", and `detect` reports it
obstructed. Probably a second analysis pass (the packed-cluster fallback width) contributing the warning;
check before relying on #20.

The other 4 of the 19: #25 (rack a yellow short), and #1, #2, #29 (no packed cluster detected). Not examined.

## Next

1. **Group B, obstruction cloth estimate** (9 frames): also accept as cloth, for the obstruction mask only, a
   second hue mode when the low-saturation sample is two-toned, or sample up to S 90 at cloth brightness.
   Must keep hand/arm controls #0, #3, #35 obstructed (skin S p50 93 to 94, brighter than cloth: close to
   S 90, so check the margin) and change no ball. Score the same way.
2. **Group A, black in a dim rack** (6 frames): belongs to the black-recall thread (black 14/28 on the
   labelled set); do not lower the 21/32 black gate.
3. Companion build with both obstruction fixes and the D3 chain-fix device check.

Detector inventory 0/3 and physical gates D1/D2/E 0/3 carried forward. Not run on the device.
