# D3 slice 2: settle detection at Live Detect cadence

11 November 2026, branch `claude/live-detect-d1-d3`. Second slice of
[D3](d3-settled-change-design.md), on top of [slice 1](d3-slice1-comparer.md).
Adds per-pass motion, the settle state machine, the reference window and an
automatic comparison each time the table settles (design section 2.1 steps 2
to 5). **Still not wired into Live Detect**: no bitmap-to-raster step in the
live path, no reducer rules, logging or setting, so the device is unchanged.

**Target test:** design section 5.3 C, the live-cadence simulation on the 29
and 31 October clips. **Before:** no settle detection; slice 1 compared
hand-picked frames. **Outcome:** decoding all 25 T4 clips (538 s) at a
jittered 0.35 s pass interval, the processor's own comparisons cover 26 of
T4's 28 shots, find 94 of T4's 102 ball-sized regions, and give no ungated
ball-sized region in any of the 18 control windows.

## Result

Simulation: `SkyEye.Vision.Desktop.Runner settled-change-clips`, one frame per
pass at 0.35 s plus uniform jitter of 0.1 s (seed 1), the clip day's receipt
corners, production settings. Scored by `tools/evidence/d3_clips.py score`
against T4's 5 fps segmentation (`work/evidence-audit/t4/pairs.csv`) and T4's
v3 ball-sized regions (`regions-grey.csv`).

| Measure | Result |
|---|---|
| Passes / settled passes | 1,474 / 692 |
| Evidence records | 55: 30 comparisons, 25 first-segment "no reference" |
| T4 shots covered by a live comparison or chain | **26/28** |
| T4 ball-sized regions found on covered shots | **94/102** |
| Ungated ball-sized regions inside the 18 T4 control windows | **0** (3 comparisons fell inside them) |
| Ungated ball-sized regions not at any T4 region | **1** |
| Comparisons gated | 1 (`lighting_drift`) |

Including both motionless code62 clips (the "false pot" and "false shot"
clips): each produced one first-segment record and no comparison, so no
change at all.

How the live machine differs from T4, and why the scorer allows it:

- **Splits.** On `7730` s0 the live machine found a still period inside
  T4's motion span (8.2 to 8.8 s, a player aiming). The first comparison
  shows only a rail-rejected region (a hand or cue on the rail); the next
  contains all six of T4's regions at T4's positions. A shot can be covered
  by a chain of comparisons. Three control windows were split the same way,
  each comparison with no ball-sized region.
- **Merges.** On `265b` the short middle segment (8.4 to 10 s) never settled
  at live cadence, so one comparison spans both shots. It finds 3 of the 6
  T4 regions: in a two-shot comparison a ball that arrives in the first shot
  and leaves in the second shows no change. The one region not at a T4
  region is in this comparison (a cushion arrival at raster (41, 259));
  unverified.
- **Not covered.** `599b` s0 and `f49d` s0: the table did not settle again
  before the clip ended (B starts at 7.0 s and 8.4 s; the clips end at 16.0 s
  and 9.8 s). Evidence is lost, not wrong.
- **Missed regions** besides `265b`: one each in `f748` s0 (11/12) and
  `b699` s0 (5/6).
- **Gated.** `8c42` seq 2 (2.4 s to 9.7 s, before any T4 segment) is gated
  `lighting_drift` with 10 ball-sized regions, so it would not be used.

Software, newly executed: `Build-SkyEye.ps1 -Target All` 0 errors (Android
and desktop), no warnings in the new files; `SkyEye.Checks` (with the new
settle checks) and `SkyEye.AgentUi.Checks` pass.

Compute on desktop: the processor costs 15 to 19 ms per pass (median per
clip; grey conversion, quarter-size motion; warp not included) and about
51 ms per comparison (max 73 ms). Not measured on the phone.

## What was built

- `SettledChangeContracts.cs`: settle settings (motion threshold 0.08,
  still below 0.0025, 3 passes over at least 1.2 s, pass gap 1.5 s,
  comparison gap 60 s, window of 3), `SettledChangeState`,
  `SettledChangeStatus`, `SettledChangeEvidence`, `SettledChangePass`, and
  the pure `SettledSegmentTracker` state machine.
- `SettledChangeMath.cs`: 5x5 Gaussian blur, 3x3 opening (OpenCV border
  rules), 2x2 halving, the T4 `scan` motion measure, per-pixel window
  median.
- `SettledChangeProcessor.cs` (Vision.Android): per pass, motion against
  the previous pass; a window of the last three still rasters; on leaving a
  settled segment, freezes their median as the reference; on settling,
  compares the new window's median with it and adds `gap_too_long` past 60
  s. Keeps only managed copies (about 4.5 MB); `Reset` starts a new epoch.
- `SettledChangeComparer`: `RectifyBgr` for decoded video frames and
  `Grey`.
- `SkyEye.Checks`: motion (no motion for identical passes or a global
  exposure step; a ball moving between passes is motion), halving, median,
  opening, and the state machine (settling needs 1.2 s, one settle per
  segment, leaving reports the span once, a source gap restarts the count).
- Runner `settled-change-clips`; `tools/evidence/d3_clips.py` (inputs and
  scoring).

Not in this slice: the obstruction, `too_much_change` and window-quality
gates; Timeline, media and calibration identity on the evidence; the 3 s
recheck for inferred balls.

## Next slices

1. Reducer rules in shadow (U1 to U5 as proposals only) on synthetic
   evidence, with the golden shadow test (design 5.2): outcomes and events
   byte-identical with and without evidence.
2. Live wiring: bitmap to raster in the Live Detect frame observer, schema
   15 logging, the mode setting. This is where Richard's open questions in
   the design (section 6) need answers: shadow on by default and its cost,
   hold time limits, obstruction handling, setting control.

Regenerate:

    python -I tools/evidence/d3_clips.py inputs work/d3/clips.json
    dotnet run --project tests\SkyEye.Vision.Desktop.Runner -- settled-change-clips --clips work\d3\clips.json --out work\d3\clips-live.jsonl
    python -I tools/evidence/d3_clips.py score work/d3/clips.json work/d3/clips-live.jsonl work/d3/clips-score.json
