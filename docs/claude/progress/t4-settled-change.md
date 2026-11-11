# T4 settled-change experiment: progress note (paused 11 November 2026)

Status: **paused mid-review**. No verdict yet. The report
`docs/claude/t4-settled-change-experiment.md` has **not** been written.
Nothing under `src/` or `docs/argon/` was touched. No git writes were made.

## Done

- Script: `tools/evidence/prototypes/settled_change.py` (stages `scan`, `plan`,
  `extract`, `diff --variant grey|chroma`, `stills --variant grey|chroma`).
  It runs end to end. Each stage is resumable and fits a 180 s call.
- Calibration: no corners are stored for the 29 Oct live-detect calibration ids
  (49782e…, 83bd5f…). I used receipt corners 58e37e… (29 Oct 12:07, code43) for
  the 29 Oct clips and 27a809… (31 Oct) for code62. I checked them by eye on
  `work/evidence-audit/t4/calib/c44-c.jpg`, `c57-c.jpg` and `c62-c.jpg`: they sit
  on the outer cushion edge, within a few px of each other. Raster: 800x1600 plus
  a 40 px margin.
- Clips: 37 clips (35 from 29 Oct plus 2 code62) were scanned at 5 fps. Motion
  results are in `t4/motion2/`; `t4/motion/` is an obsolete first attempt that
  could not be deleted. Settled means motion below 0.0025 for at least 1.2 s,
  with gaps of 0.4 s or less merged.
  `t4/pairs.csv`: 28 shot pairs, 16 same-segment controls, 2 code62 controls.
  Six clips had too little settled time to give any pair.
- Grey change maps use a ball-sized test (v2) based on the largest inscribed
  disc: 0.6–1.45 R, and at most 5 ball areas.
  Output: `t4/regions-grey.csv` and `t4/sheets-grey/`.
- Stills: the 1 Nov staged black/cue bursts (mid cushion, opposite-cue, lower-right
  jaw, upper-right jaw), scored against the user-reviewed B/C centres.
  Output: `t4/stills-grey.csv` and `t4/stills-grey/`.
- `t4/regions-chroma.csv` and `t4/sheets-chroma/` are **stale**: they predate v2.
  Rerun before quoting them.

## Provisional findings (not yet a verdict)

- Clip controls: 0 ball-sized regions in 18 control pairs (grey, v2). With the v1
  filter, the chroma variant gave 1 false ball-sized region, inside the dark
  left-middle pocket in `code44_8c42…_c0` (viewed).
- Stills, black at cushion or jaw: every one of the 9 pairs where the black's
  presence changed produced a ball-sized region at the reviewed B position.
  The 95th percentile of the change map in the B disc was 1.91–2.82, against
  0.03–0.36 in the 12 same-burst controls. The mid-cushion "restored" black is
  the one the single-frame detector misses (18/32).
- Failure mode seen: `opp_cueabs-562_restaged-1691` (16:51 vs 17:13, separate
  staging). Daylight direction changed, every ball shadow and the cushion faces
  changed, giving **8 unexpected ball-sized regions**. Long gaps with changing
  light break the method.
- Seen in clips: a hand or arm in a "settled" frame at the top-right jaw
  (`code57_926cfd…_s0`, region 0, ball-sized under v1). Hands on the rail elsewhere
  were rejected by size. Ball shadow tails split one move into body plus tail regions.
- Arrival/departure by cloth-likeness works in daylight clips but mostly says
  "unclear" in the dim code57 clips.
- The only black move in the clip pairs is on open table (`code57_7730…_s0`). No
  black arrival at a cushion or jaw occurs in the clips.

## Viewed by me (images actually inspected)

These sheets were made with the v1 filter; the region positions are unchanged in v2:
`code57_7730…_s0`, `code52_d8dc…_s0`, `code44_f748…_s0` (break), `code62_false-pot…_m`,
`code57_926cfd…_s0`, `code57_265b…_s0`, `code57_265b…_s1`, `code57_931d…_s1`, and the
chroma sheet `code44_8c42…_c0`.

These still sheets were made with v1; the hit counts above are from v2:
`mid_absent-542_restored-288`, `lowright_absent-461_restored-375`,
`opp_cueabs-562_restaged-1691`.

## Half-done

Eight more sheets are staged but **not viewed**: code44_5abc s0, code44_8c42 s0,
code45 s0/s1/s2, code47_7099 s0/s1, code47_f277 s0. About 13 further shot sheets
have not been viewed either. There is no per-shot by-eye count yet of balls moved
versus regions detected.

## Next steps

1. View all 28 shot sheets in `t4/sheets-grey/` and fill in a table per pair:
   balls moved by eye, detected, cushion/jaw arrivals, false regions (shadow, hand,
   cue), and pots.
2. View a sample of same-burst still controls and the upper-right sheets.
3. Rerun `diff --variant chroma` and `stills --variant chroma` with v2 and compare
   them with grey.
4. Write `docs/claude/t4-settled-change-experiment.md` with the commands, results
   table and verdict. Leaning **promising but conditional**: short gaps and stable
   light only, with hands and long-gap lighting as open risks. Labels from T9 are
   needed for a real score.

Commands (from repo root, on the Linux mount):
`python3 tools/evidence/prototypes/settled_change.py scan` (repeat until "finished"),
then `plan`, then `extract` (repeat until "finished"), then `diff --variant grey`,
then `stills --variant grey`.
