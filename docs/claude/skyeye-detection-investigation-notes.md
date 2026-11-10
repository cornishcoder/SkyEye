# SkyEye detection investigation: working notes (handover)

Running log for the independent computer-vision review started 10 November 2026.
Purpose: let another engineer or model resume without repeating work. Newest
status first; findings below are marked VERIFIED (read in code or measured) or
HYPOTHESIS. The brief is diagnostic only: do not modify production detection
code; do not request physical staging until the evidence audit shows it is
necessary. Final deliverable is `docs/claude/skyeye-detection-evidence.md` plus a
diagnosis with sections A-F (coverage, pipeline, failure table, ranked
solutions, plan, unknowns).

## Status (update this block each time)

Last update 10 Nov 2026, about 23:00 (model switched from Fable 5.2 to Opus 5.5
mid-task; nothing was lost).

- [done] Evidence register: `docs/claude/skyeye-detection-evidence.md`.
- [done] Diagnosis A-F: `docs/claude/skyeye-detection-diagnosis.md`.
- [done] Tools in `tools/evidence/` (listed in the evidence register,
  section 1); outputs in git-ignored `work/evidence-audit/`.
- [done] Per-ball tracker replay `tools/evidence/prototypes/ball_track_replay.py`:
  current rule 26/42 still bursts settle, 31 false changes; 2-of-3 hysteresis
  38/42, 5 false changes.
- [done] Spot-checked the concurrent `docs/argon/` review's code claims
  (evidence register section 10). Do not edit `docs/argon/`; it belongs to
  that other session.
- Committed on master. Findings are in `docs/claude/` (3 files),
  `tools/evidence/` (13 scripts). `docs/argon/` is also untracked.
- NEXT (suggested): plan step 0 (off-device detector harness) and step 1
  (label review sheets for about 40 existing dim/warm/jaw frames). Optional
  quick trace: `live-detect.jsonl` around the two code62 false-event clips.

## Environment facts (VERIFIED)

- Repository: `C:\code\SkyEye`, HEAD `f3431ec7` (2 Nov 2026), 823 commits,
  clean working tree. `work/` is git-ignored and holds most evidence.
- The disk holding the repository is 99% full (about 7.8 GB free). Keep
  generated outputs small; do not copy the corpus.
- Analysis shell has Python 3.10 with OpenCV 5.0.0, numpy, Pillow, ffmpeg and
  ffprobe. It has no .NET SDK, so the C# checks cannot be run from it.
- Media outside build folders: about 4,782 PNG, 241 JPG, 113 MP4, 71 BGR raw
  frames. 3,681 media files sit under `work/production/evidence`; next largest
  are `work/o08/live-detect-black-pair-v1` (414) and
  `work/o09/development-v3` (252).

## Pipeline findings so far

- VERIFIED: the detector (`src/SkyEye.Vision.Android`) targets
  `net10.0-android` / `android-arm64` only (see its csproj) and calls the
  OpenCV Java binding. There is no desktop build of the detector. Every
  "offline replay" therefore installs an APK and runs instrumentation on a
  phone (docs/live-detect-workflow-efficiency-20261101.md quotes 6.8 s per
  replay including install). This is the main reason iteration has been slow
  and why evaluation kept depending on device sessions.
- VERIFIED: `VisionAnalyzer.AnalyzePixelsCore` (VisionAnalyzer.cs 255-552) is a
  single ~300-line function plus a ~170-line `Detect` (647-822) with ordered,
  mutually dependent rescue stages: packed-cluster bodies, colour bodies, dark
  bodies, Hough on a binary cloth mask, cue validation, neutral-cue recovery,
  cue-based white balance, optional re-detect, inventory constraint.
- VERIFIED: whole-frame uncertainty is forced whenever the cue count or black
  count is not exactly one (VisionAnalyzer.cs 507-508), or a packed cluster is
  not exactly 7 red / 7 yellow / 1 black (514-518). One missed black ball
  therefore blocks readiness for the entire table. The scorecard's persistent
  "inventory 0/3" result is each frame reading 15 balls with black missing.
- VERIFIED: colour correction depends on first finding a unique cue ball; the
  gains are the cue's 95th-percentile channel values (362-396, 412-417). If the
  cue is missed or duplicated, colours are classified without correction
  (line 469). Detection and colour constancy are circularly dependent.
- VERIFIED: Hough runs on the binary cloth mask, not on image gradients
  (661-664); candidate list is capped at 32 (688); candidates within 23 px
  (scaled) of the raster edge are rejected (715-720) and within
  `PocketExclusionRadius` 48 px of a pocket centre (722) before any fit.
- VERIFIED: `VisionSettings.cs` thresholds carry comments tying each value to
  one or a handful of specific frames (for example 20/32, 21/32, 24/32 boundary
  sectors; DarkBodyValueCeiling 40; pocket radius 48). No held-out set exists:
  the scorecard records Holdout 0, lighting strata measured 0.
- VERIFIED from docs: the learned-detector spike (O09, YOLO11n) was trained on
  6 then 15 images and scored 0/4 and 0/7 complete layouts; it was
  under-trained rather than disproven.
- HYPOTHESIS (to test on images): black ball against dark cushion/pocket
  shadow is the dominant residual miss; absolute HSV value ceilings (40) make
  it exposure-dependent.

## Process findings (VERIFIED from docs)

- Prior work repeatedly asked for staged layouts under named lighting strata
  (see docs/evidence/live-detect-o09-development-layout-lighting-batch-plan-20261101.md,
  which planned 24 further staged states). The user has said no more daylight
  shots. Lighting strata were never measured from the images already held.
- Only a few dozen frames carry reviewed centre labels; bursts of 5-8 frames
  per state were captured but only one frame per state reviewed.

## Key documents already read

docs/live-detect-pipeline.md, docs/observation-corpus-guide.md,
docs/live-detect-workflow-efficiency-20261101.md,
docs/evidence/live-detect-scorecard-20261101.md,
docs/evidence/live-detect-o09-lighting-strata-existing-record-audit-20261102.md,
AGENTS.md (camera must stay stopped unless explicitly authorised).

## Still to read

VisionAnalyzer.cs 823-1395 (cloth mask, colour bodies, classify, obstruction),
BallClassifier.cs, BallBoundaryRefiner.cs, CueBallRecovery.cs,
PackedBallClusterRecovery.cs, TableTracker.cs, StableTableChangeTracker.cs,
ObservationPolicy.cs, TableVerification*.cs, TableMotionFeed.cs,
docs/o08-observation-reliability-report.md, docs/o09-learned-ball-detection-spike.md,
docs/live-detect-handoff-20261029.md, docs/temporal-tracking-design.md.

## Evidence numbers (VERIFIED, 10 Nov; regenerate with the tools above)

- 5,248 media files, 5.7 GB: 5,019 images, 136 videos (127 unique, about
  2,256 s total), 93 raw-pixel files. Dates 3 Oct to 2 Nov 2026 (one stray
  16 Sep file). 2,922 unique by SHA-1; 3,041 files sit in 715 exact-duplicate
  groups. One unreadable image: `work/companion-candidates.png`.
- Image classes by size: 1,868 camera frames 1920x1080 (899 unique), 1,044
  rectified table rasters 1000x2000 / 800x1600 (401 unique), 884 phone
  screenshots, 801 small crops, 422 other (overlays, sheets).
- Frame receipts: 1,032 JSON receipts -> 492 unique camera frames whose pixels
  exist, from 90 source timelines / 83 capture operations / 48 calibration
  IDs, on 10 dates (7 Oct, 25 Oct to 2 Nov). 202 of the 492 embed the
  production detector's result for that exact frame.
- Human-reviewed labels: only 26 unique frames (401 visible balls: 182 red,
  179 yellow, 20 cue, 20 black; 10 expected-absent). 24 are image-normalised
  with radii. So about 5% of unique receipt-bound frames are labelled.
- A receipt holds: capturedAt, source timeline, frameId, sourceTimeUs, four
  calibration corners (order matches `VisionAnalyzer.Warp`: corner0->(W,0),
  1->(W,H), 2->(0,H), 3->(0,0)), full analyser settings, and
  `measurement.vision` (balls in table-2x1 coordinates, uncertain, warnings,
  colourGains, clothHue). No exposure, ISO or white-balance metadata exists in
  any receipt.

## Finding: detector output flickers on still tables at constant light (VERIFIED)

From the 202 frames with embedded detector output, grouped into capture
operations of 3+ frames (42 bursts, 126 consecutive pairs, each burst 2-3.5 s,
all declared still by the user): `TableTracker.SameLayout` semantics (same
count, same kinds, each centre within 0.015) hold for only 95/126 pairs; the
ball count changes in 21 pairs; 16/42 bursts are not internally stable.
Within those bursts the cloth median brightness moves by 0-1 levels (for
example 2 Nov 12:12: counts 15,15,16,15 at V=127 throughout). Lighting change
therefore cannot be the cause of this flicker; single-frame detection is
unstable at fixed illumination, and `TableTracker` (TableTracker.cs 33-41, 77-88)
resets its stillness clock on any count/kind/position mismatch. Of the 202
frames 161 are flagged uncertain; black count != 1 in 122, cue count != 1 in 69
(some scenes deliberately omit black or cue, so these are upper bounds until
joined to labels).

## More code findings (VERIFIED)

- `BallClassifier.Classify` (BallClassifier.cs 28-57) uses absolute HSV
  thresholds throughout (dark: V<40 or V<80 and S<160; cue: S<100 and V>130;
  yellow hue 18-75 deg; red <18 or >325). `Quality` is 0.72 + 0.18*fraction,
  not a probability, and is never combined across frames.
- `TableTracker.Observe` is frame-level all-or-nothing: no per-ball tracks, no
  hysteresis on individual balls; an uncertain frame freezes the last layout
  and marks it occluded (TableTracker.cs 23-30).

## Visual inspection results (VERIFIED by viewing the sheets, 10 Nov)

Scene families across the 101 capture operations (tile numbers refer to
`sheets/operations-NN.jpg` and `sheets/sheet-index.csv`):
- Full rack + cue on its spot: about 45 operations (#2, #7-#47, #58, #85, #94)
  on 25-29 Oct and 2 Nov, from near dark (V about 30) to daylight (V about 180),
  including warm light where the blue-grey cloth turns neutral grey. This is
  the same arrangement restaged repeatedly.
- "Pocket-lip" layout, balls in every pocket jaw plus one centre yellow:
  16 operations (#49-#64), 30-31 Oct, mostly dim.
- Grid along cushions with black/cue moved between pockets ("black pair"):
  20 operations (#65-#84), 1 Nov.
- Cushion layout: 8 operations (#86-#93), 2 Nov. Open spreads: #3-#6 (7 Oct),
  #48, #95-#100. Break aftermath #1. Hand over rack #15. Cue stick #58.
What the detector misses (sheets `detector-fail-*`): to the eye every ball in
every tile is clearly visible, including at V 50-70. Reds and yellows are found
almost everywhere from V about 65 up, including on rails. Misses concentrate in:
(a) the black ball whenever it touches a cushion or sits in a pocket jaw, at
every light level (D2-D9, D11-D18, D21, D26, D31-D34, D37, D38, D41-D43);
(b) the cue ball when it sits in a corner jaw beside the black (D18, D31-D34,
D38, D41-D43); (c) dim warm-light pocket-lip frames (V 68-76, cloth
saturation 6-17) where one ball of each jaw pair is dropped (D3, D5-D9, D13);
(d) near darkness (D1, V 28): every detection labelled unknown, half the rack
missed; (e) label cascades: D20 has seven reds correct and eight "unknown",
because the cream cue was called yellow, making eight yellows, and
`BallClassifier.ConstrainInventory` (BallClassifier.cs 18-27) then relabels
every yellow as unknown. Many tiles flagged U are correct readings of control
scenes where black or cue was deliberately removed (D28, D30, D35, D39, D40,
D44, D45).

## Measurements (VERIFIED; rerun the tools to reproduce)

- `score_detections.py` (production result embedded at capture time, 19
  labelled frames that have one): 292/295 balls found, colours 292/292, zero
  false positives, centre error median 0.03 / p95 0.15 ball diameters. Red
  133/133, yellow 133/133, cue 14/14, black 12/15 (all three misses are the
  black touching the top cushion at table (1.48, 0.07)). 16/19 frames are
  entirely correct, but only 7/19 were reported clear: 9 fully correct frames
  were flagged uncertain solely because the scene had no black or no cue
  (VisionAnalyzer.cs 507-508).
  CAVEAT: all labelled frames are mid or bright light (table V >= 80) from
  1-2 Nov. No dim or warm-light frame has reviewed labels.
- The receipts' "User declared ... N balls" field is always 16, including for
  frames whose reviewed labels show 15, so it is boilerplate and not truth.
- `robust_light.py` over 492 frames: median table V ranges 28 to 143 (10th
  to 90th percentile 64 to 134); median saturation 0 to 89 (the cloth is
  effectively grey, S < 40, in roughly half the frames); brightness across the
  table differs by a median factor 1.7 and up to 2.1 between tiles in one
  frame. No frame carries exposure or white-balance metadata.
- `burst_stability.py`: only 28 of 617 tracked balls (4.5%) are intermittent
  within a still burst (open 4.0%, cushion 3.3%, pocket 8.5%), no kind label
  ever flips, persistent centres jitter by median 0.02 / p95 0.08 ball
  diameters. Yet frame-level SameLayout fails for 31/126 pairs and 16/42
  bursts. A per-ball majority vote would put 41/42 bursts within one ball on
  every frame. Conclusion: the all-or-nothing frame comparison multiplies a
  small per-ball dropout rate into a large readiness failure rate.

## Prototype result (negative, recorded so it is not retried blindly)

`tools/evidence/prototypes/disc_contrast_detector.py` (cloth-relative log-ratio
contrast + disc matched filter, no absolute thresholds, about 50 ms per frame
in Python): on 25 labelled frames recall 0.915 but precision 0.275 (937 false
positives, mostly on cushion edges, cushion shadows and pocket mouths called
black/unknown), black recall 15/19, cue 10/19. A naive illumination-invariant
contrast filter is not enough; the cushion/pocket context is the hard part,
which is the same place the production detector fails. Do not iterate
thresholds on this prototype.

## Code facts added (VERIFIED)

- Pocket exclusion: any candidate centred within 48 px (at width 1000; 0.048
  table units = 1.8 ball radii) of a pocket point is discarded before fitting
  and again after (VisionAnalyzer.cs 722-723, 769-770; DarkBodyRecovery.cs
  33-34), and the same discs are painted into the cloth mask as cloth
  (VisionAnalyzer.cs 939).
- Black recovery (`AddDarkBodies`, DarkBodyRecovery.cs 12-52) thresholds raw
  V <= 40 (absolute), takes connected components, requires aspect 0.6-1.6 and
  fill >= 0.55, examines at most 4. A black ball touching a dark cushion
  shadow or pocket mouth joins that dark region and fails the shape test.
- Cloth mask (`ClothMask`, VisionAnalyzer.cs 911-943) is one global HSV range
  (hue +/- 7 around the sampled cloth hue) plus a neutral fallback S < 45.
