# T4: change between settled states — experiment report

Date: 11 November 2026. Thread T4 of
[skyeye-detection-fix-plan.md](skyeye-detection-fix-plan.md). Python only; no
device, no staging, nothing under `src/` changed.

**Verdict: promising, with conditions.** On short gaps (seconds) in steady light,
the brightness-compensated difference between two settled frames finds the black
arriving at, or leaving, a cushion and a pocket jaw every time it was tested (9 of 9
pairs). It gave zero ball-sized regions on 18 clip controls and 12 still-burst
controls. It fails when the light direction changes between the two frames (8
false regions on one pair 22 minutes apart). Merged or overlapping balls and
shadow tails need handling. This is not a held-out score: the "truth" is my
reading of the sheets plus the user-reviewed black/cue centres for the 1 November
stills, and no T9 labels exist yet.

## Hypothesis

A ball coming to rest at a cushion changes those pixels relative to the settled
frame before the shot; an empty cushion or jaw does not. After per-tile gain
compensation, a ball-sized change region should therefore mark an arrival or
departure even where single-frame edge support cannot (the dark black against a
dark cushion or jaw).

## Method

Script: `tools/evidence/prototypes/settled_change.py`.

- **Rectify.** Receipt corners (VisionAnalyzer.Warp order), 800x1600 raster plus a
  40 px margin so that cushions and jaws are included. The 29 October live
  calibrations (49782e…, 83bd5f…) have no stored corners. I used the nearest
  same-day receipt corners (58e37e…, code43, 12:07) for 29 October and 27a809… for
  31 October. Checked by eye on `work/evidence-audit/t4/calib/c44-c.jpg`,
  `c57-c.jpg` and `c62-c.jpg`: they sit on the outer cushion edge, and the
  candidate sets differ by under 4 px.
- **Settled segments.** Decode at 5 fps and measure motion as the tile-gain-
  compensated change between frames 0.4 s apart (blurred and opened against
  compression speckle). Settled means motion below 0.0025 of the table for at least
  1.2 s, with gaps of 0.4 s or less merged. A shot pair is the last second of one
  settled segment against the first second of the next, each taken as the median
  of up to five frames. A control pair is the first against the last second of
  one long settled segment, or the start against the end of a motionless code62
  clip.
- **Change map.** Grey: log(grey + 8) difference minus a smooth per-tile median
  (80 px tiles), absolute value, threshold 0.18. Chroma: the same per BGR channel,
  taking the maximum over channels, threshold 0.22. Then a 5x5 Gaussian blur, a
  5x5 opening, a 9x9 elliptical closing and connected components.
- **Ball-sized test (v3).** Largest inscribed disc 0.6–1.45 R (R = 21.6 px), area at
  most 5 ball areas, and at most 5 % of the region outside the outer cushion edge.
  The **rail rule** in that last condition rejects hands and forearms on the rail.
  - v1 (area, bounding box and circularity) rejected real cushion arrivals whose
    shadow tail stretches the region.
  - v2 had the same test without the rail rule.
- **Arrival or departure.** Whichever frame looks like cloth inside the region
  (hue, saturation and value against the bed) is the empty one.
- **Drift measures per pair.** Fraction of the cushion band and of the open bed
  above threshold.
- **Stills.** The 1 November staged black/cue bursts: black absent, cue absent and
  restored states at the mid top cushion, the lower-right jaw and the upper-right
  jaw, plus the opposite-cue set. All use calibration 00a2e8 and are scored
  against the user-reviewed B/C centres
  (`docs/evidence/mid-cushion-user-reviewed-centres-20261101.json` and the
  lower-right, upper-right and opposite-cue JSONs under
  `work/o08/live-detect-black-pair-v1/physical-controls/`). A "hit" is a v3 region
  centred within 1.6 R of the reviewed point.

Commands (repo root, Linux shell; repeat `scan` and `extract` until they print
"finished"):

```
python3 tools/evidence/prototypes/settled_change.py scan
python3 tools/evidence/prototypes/settled_change.py plan
python3 tools/evidence/prototypes/settled_change.py extract
python3 tools/evidence/prototypes/settled_change.py diff   --variant grey
python3 tools/evidence/prototypes/settled_change.py diff   --variant chroma
python3 tools/evidence/prototypes/settled_change.py stills --variant grey
python3 tools/evidence/prototypes/settled_change.py stills --variant chroma
```

Outputs, all in `work/evidence-audit/t4/` (git-ignored):

- `pairs.csv` (46 pairs)
- `pairs-{grey,chroma}.csv` (per-pair counts and drift)
- `regions-{grey,chroma}.csv`
- `stills-{grey,chroma}.csv`
- review sheets in `sheets-{grey,chroma}/` and `stills-{grey,chroma}/`; each shows
  before | after | change map with regions drawn, plus zoomed crops.

`motion/` is an obsolete first attempt; `motion2/` is current.

## Data

- **Clips.** 37 clips were scanned: 35 from 29 October (code44, code45, code47,
  preroll, code52 and code57) and 2 motionless code62 clips from 31 October.
  - 6 short event clips had too little settled time for any pair (code52
    test-018e, 599b had only one, and others). See `pairs.csv`.
  - code52 `test-nopot` and `0a89…` probably duplicate event clips; they were kept,
    but this does not change the totals much.
- **Pairs.** 28 shot pairs, 16 same-segment controls and 2 code62 controls.
  - Of the 28 shot pairs, 3 are breaks and 2 contain no shot: the settled segments
    were split by a 0.6 s blip, leaving 23 ordinary shots.
- **Stills.** 33 pairs: 12 same-burst controls and 21 cross-state pairs, 9 of
  which change the black.

## Results

### Controls (no-change pairs)

| Set | Pairs | Ball-sized regions, grey | Ball-sized regions, chroma |
|---|---|---|---|
| Same settled segment, 29 Oct clips (gaps 2–38 s) | 16 | 0 | 0 |
| code62 motionless clips (balls at every jaw) | 2 | 0 | 0 |
| Same still burst, 1 Nov (black at cushion or jaw, cue beside it) | 12 | 0 | 0 |

In the grey variant, the 95th percentile of the change map inside the black's
disc in same-burst controls is 0.03–0.36.

### Black at a cushion or jaw (1 November stills, grey)

| Location | Pairs where the black changes | Hit | 95th percentile at black | Unexpected ball-sized regions |
|---|---|---|---|---|
| Mid top cushion (the 18/32 single-frame miss) | 3 | 3 | 1.98–2.05 | 0 |
| Lower-right jaw | 3 | 3 | 1.91–2.08 | 0 |
| Upper-right jaw | 3 | 3 | 2.79–2.82 | 0 |

Separation is about 5x between the weakest black change (1.91) and the strongest
no-change control (0.36). When the black and the cue both change, the adjacent
balls merge into one region, which is credited to the black only, so the cue
counts as a miss there (3 cases). Where only the cue changes, it is hit in 4 of 4
same-session pairs.

Chroma finds the same black hits (9 of 9) with similar separation, but it adds 1–6
unexpected regions on most lower-right and upper-right cross-state pairs where
grey adds none. **Grey is the better variant.**

### Shot pairs (29 October clips, grey v3, every sheet viewed)

These counts are my reading of the sheets, not labels.

| Measure | Count |
|---|---|
| Ordinary shots examined | 23 (plus 3 breaks, plus 2 pairs with no shot) |
| Balls that moved, by eye | 52 |
| Moved balls with a ball-sized region at the old or new position | 50 |
| Missed | 2: a red departure region too thin (599b s0); a yellow/cue swap where overlapping discs merged (6c74 s0) |
| Arrivals at a cushion or jaw (non-break) | 5 of 6 (1785 s0, 593e s0, 926c s0 at cushions; 7730 s0 at the middle-pocket jaw; 265b s1 one of two). The other 265b s1 arrival merged with a neighbour into an oversize region |
| Departures from a cushion or jaw (non-break) | 14 of 14, including 4 at jaws |
| Breaks | every isolated landing ball found (about 10 per break), including a red at the top-left jaw and several cushion arrivals; the rack is one oversize region |
| Black moves in the clips | 1, on open table, found. **No black arrives at a cushion or jaw in the clips** |
| False ball-sized regions not explained by a moving ball | 1 in 23 shots: a player-shadow or lighting change on the bottom cushion beside the corner pocket (b699 s0, region 6) |
| Duplicate regions from shadow tails | seen in the break f748 s0 (2 tail regions beside true ones) |

### Failure modes

1. **Hands and arms on the rail in a "settled" frame.** These were seen in 9 shot
   pairs.
   - Under v2, 5 became ball-sized regions at cushions or a jaw (1a0d s0, 593e s0,
     b699 s0 ×2, d8dc s0).
   - The **rail rule** rejects all 5: 27–81 % of each region lay outside the outer
     cushion edge.
   - It rejected no true cushion or jaw arrival: every real cushion or jaw region
     has 0 % outside.
   - A hand at the top-right jaw (926c s0) was already rejected by size.
2. **Light direction changing between the two frames.**
   - `opp_cueabs-562_restaged-1691` (16:51 against 17:13, separate stagings) gave 8
     unexpected ball-sized regions: every ball's shadow moved, and the cushion faces
     brightened.
   - Two other cross-session opposite-cue pairs gave 6 and 2.
   - The drift measure separates them: cushion-band fraction above threshold is
     0.105–0.335 on those three, against at most 0.065 on every other still pair and
     at most 0.047 on every clip pair. Thresholds of 0.08 on the cushion band would
     flag them, but that rests on 3 bad pairs and should be treated as tentative.
   - **Gap length is not the cause:** clip controls 38 s apart were clean. The cause
     is daylight moving. Between shots in real play the gap is seconds.
3. **Player shadow on a cushion.** One ball-sized false region in 23 shots (b699
   s0). Not rejected by any rule tried.
4. **Merged neighbours.** Two balls arriving together at a cushion, a ball
   replacing another at the same spot, and black and cue touching all give a
   single region that is either oversize (rejected) or credited to one ball.
5. **Arrival/departure call.** Cloth-likeness works in daylight clips but is
   mostly "unclear" in the dim code57 clips. Whether a ball is present has to come
   from the detector or from the frame with the darker or more ball-like content,
   not from this test.

Not tried: a "darker-and-ring-shaped" test and an obstruction-flag gate. The rail
rule alone removed every hand region I saw; the single shadow false region would
need something else (for example rejecting regions whose two frames differ only
in brightness with no edge structure). That is untested.

## Verified against hypothesis

**Verified** (by viewing sheets, or against reviewed centres):

- Black arrival and departure at the mid cushion and both jaws stand out from
  no-change controls by about 5x.
- 0 false ball-sized regions on 30 control pairs.
- The rail rule removes the hands seen and keeps the true cushion and jaw events.
- A daylight change across separate stagings breaks the method.

**Not verified:**

- Black arriving at a cushion or jaw during real play: no such event exists in the
  clips.
- Behaviour under lamp switching or an exposure step between shots: none in these
  pairs.
- Players standing over the table for long periods.
- Any rate on held-out data.
- Whether a reducer can use this without per-ball tracking (T6).

## What would settle it

- **T9 sheet 2.** Labels for the 29 October clips: shot, rolling and settled
  intervals, pots, and the rest position of each moved ball. These turn the
  by-eye counts above into a score, and show whether the 2 misses and 1 false
  region are typical.
- **Real-play black-at-cushion events.** Any existing clip where the black
  finishes on a cushion or in a jaw (none found in this set). Without one, the
  black result rests on staged stills alone.
- **Drift gate.** More pairs with lighting changes (lamp on/off, cloud) to set the
  cushion-band drift threshold honestly.
- **Next step if pursued.** Feed ball-sized v3 regions into T6 per-ball state as
  "something arrived or left here", with the detector deciding identity, and
  re-measure on the T1 manifest. The rail rule and a drift gate should come with it.

## Files

- `tools/evidence/prototypes/settled_change.py` (the prototype)
- `work/evidence-audit/t4/` (all outputs and sheets; git-ignored)
- `docs/claude/progress/t4-settled-change.md` (progress note)

## Sheets viewed

- **Calibration:** `calib/c44-c.jpg`, `c57-c.jpg`, `c62-c.jpg`.
- **Clip shot sheets:** all 28 in `sheets-grey/`. Eight were seen under the v1
  filter (7730 s0, d8dc s0, f748 s0, 926c s0, 265b s0, 265b s1, 931d s1, and the
  code62 control `code62_false-pot-cl_m`); the rest under v2 or v3. Region
  positions are the same across versions; only the ball-sized flag changed.
- **Chroma:** `sheets-chroma/code44_8c42…_c0` (v1).
- **Stills:** `mid_absent-542_restored-288`, `lowright_absent-461_restored-375`,
  `opp_cueabs-562_restaged-1691` (v1 or v2 drawing; the hits quoted are from v3).

Same-burst still controls were judged from the numbers, not viewed.
