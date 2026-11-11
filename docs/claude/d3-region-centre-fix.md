# D3: shadow-free region centres and no "unexplained" on untrusted passes (11 November 2026)

Follows [the D3 shadow proposal review](d3-shadow-proposal-review.md): all 10 live `unexplained_change`
proposals were real changes misjudged, 8 on obstructed passes and 2 from region centres pulled toward the
cast shadow.

**Target test:** `D3-SHADOW-PROPOSAL-REVIEW` (fix). **Before:** 10/10 false unexplained proposals; region
centres 0.03 to 0.05 table units from the ball under the evening lamp. **Intended:** region centres on the
ball, and no unexplained proposal without a trusted layout, with nothing else changing.

## Changes

1. **Region centre from ball-like pixels** (`SettledChangeSettings.ShadowFreeCentre`, default on; false keeps
   the plain component centroid). `SettledChangeMath.BallCentre` averages only the region pixels that are
   neither cloth nor cast shadow in the before or the after raster (`ClothOrShadow`: cloth hue and
   saturation at 0.2 to 1.4 x cloth V; below S 40 on both pixel and cloth, saturation alone, because
   near-grey cloth has no stable hue: the same cloth read H 165 before and H 114 after). Falls back to the
   centroid when fewer than 20 % of the pixels are ball-like. The zone also follows the new centre.
2. **Unexplained change needs a trusted current layout** (`LiveDetectSettledChangePolicy`): on an obstructed
   or uncertain pass nothing can explain a change, so nothing is logged.
3. Desktop `settled-change-rasters --before REF.jpg --after CUR.jpg [--shadow-free off]` re-compares a saved
   device raster pair.

A first version kept the existing cloth test (0.45 x V, hue required): it moved nothing, because a lamp
shadow falls below 0.45 x V and the grey cloth's hue is noise.

## Result (newly executed)

**Device raster pairs (4 saved comparisons):** distance from each ball-sized region to the nearest Live
Detect ball, plain centroid versus new centre:

| Comparison | Plain centroid | New centre |
|---|---|---|
| `7c151fba` seq 3 (break) | 0.001 to 0.043, 7 of 10 over 0.025 | 0.001 to 0.035 |
| `3a434fbb` seq 3 (break) | 0.016 to 0.053, 7 of 8 over 0.035 | 0.001 to 0.035 |
| `5a519dc2` seq 4 (break, pot) | 0.015 to 0.043 | 0.001 to 0.028 (6 of 7 within 0.016) |
| `755281db` seq 3 (shot) | 0.009 to 0.043 | 0.003 to 0.031 |

The two shadow-biased proposals: the cushion yellow (`5a519dc2`) moves from 0.043 to 0.002 from its ball;
the morning cue departure from 0.050 to 0.022 from the baseline cue. Both now fall inside the 0.0432 match
radius, so they are explained.

**Policy replay (`--golden-shadow` on the pulled logs, logged regions):** the 8 proposals on the obstructed
`3a434fbb` break are gone; the remaining 2 use the old logged centres, which the new comparer moves inside
the match radius (above). Live Detect output unchanged (golden-shadow PASS on both pulls).

**Staged still pairs (33, criteria A):** unchanged except one gain (`mid_absent-542_cueabs-295`: the cue
change is now found).

**T4 clips (25, live cadence):** "T4 regions found" 94 to 88 and "ungated ball regions not at a T4 region"
1 to 7. This is a reference artefact: the T4 reference regions are the Python prototype's plain centroids,
which carry the same shadow bias, and the score matches within 1.6 R. On the six clips with the largest
moves (up to 21 px, about 2 R, in daylight) the new centres sit on the balls and the old ones on the shadow
side (overlay checked). Median move 6.7 px (0.6 R) over 104 ball-sized regions.

Software, newly executed: `Build-SkyEye.ps1 -Target All` 0 errors; `SkyEye.Checks` (with a new ball-centre
check: a yellow disc with a lamp shadow on near-grey cloth whose hue differs before and after) and
`SkyEye.AgentUi.Checks` pass; D3 self-check PASS; Layout03 parity 4/4.

## T4 reference regenerated (later the same evening)

The prototype (`tools/evidence/prototypes/settled_change.py`) gained the same rule (`cloth_or_shadow`,
`ball_centre`) and a `ballcentre` stage, which writes `work/evidence-audit/t4/regions-grey-ballcentre.csv`
beside the original (cx/cy = ball centres, plain_cx/plain_cy = old centroids; the original T4 files are
untouched). Ball-sized reference centres moved by a median 12.2 px (0.56 R) and at most 40.4 px (1.9 R) at
full raster scale, consistent with the C# comparer's 0.6 R median on the same clips.
`tools/evidence/d3_clips.py inputs ... --regions regions-grey-ballcentre.csv` builds the clip inputs from
it (same 25 clips, 46 pairs, windows and region counts).

| Live run | Reference | T4 regions found (of 102) | Ball regions not at a T4 region |
|---|---|---|---|
| plain centroid | plain (original) | 94 | 1 |
| shadow-free | plain (original) | 88 | 7 |
| plain centroid | shadow-free | 91 | 4 |
| **shadow-free** | **shadow-free** | **93** | **2** |

Like for like, the new method is within one region of the old. That one region is the cue leaving baulk in
`break-red`: the live comparer kept its plain centroid (fewer than 20 % ball-like pixels, so it fell back
by design) while the reference, from full-resolution 5 fps frames, moved onto the cue's actual position,
2.7 R away. No other pair differs.

## Still open

- U1 to U4 have no live evidence yet; next is a staged top-cushion black session (the black that Live Detect
  misses at baseline) with source frames saved.
- Score D3 against the shadow-free reference (`clips-ballref.json`) from now on.

Not built for or run on the device. Detector inventory 0/3 and physical gates D1/D2/E 0/3 carried forward.
