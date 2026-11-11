# T2 desktop detector runner: build and Layout03 parity

11 November 2026, branch `claude/live-detect-d1-d3`. Thread T2 of the
[detection plan](skyeye-detection-fix-plan.md); design in
[t2-desktop-runner-design.md](t2-desktop-runner-design.md).

## Result

The production detector now runs on Windows, and on the four Layout03 frames
it gives the same output as the phone. **PARITY PASS 4/4.**

| Frame | State | Circles device / desktop | Kinds | Max centre delta | Max radius delta | uncertain / layoutUncertain |
|---|---|---|---|---|---|---|
| 2083 | both present | 15 / 15 | same | 0.854 px (red-2) | 0.800 px (red-2) | same (true / false) |
| 957 | black absent | 15 / 15 | same | 0 | 0 | same (true / true) |
| 962 | cue absent | 14 / 14 | same | 0 | 0 | same (true / false) |
| 2263 | both restored | 15 / 15 | same | 0 | 0 | same (true / true) |

59 circles in all, which is the device replay's 59 of 62 found. 58 centres are
bit-identical. One red in 2083 differs by 0.85 px, inside the 1 px gate
(0.00125 table units at width 800). `packedClusterFallback` was absent on
both sides for all four frames. For each frame the pixel SHA-256 matches the
proof, the configuration identity equals the device replay's
`replayAnalyzerConfigurationIdentity`, and a second run on the desktop gives
identical output. Runtime is OpenCV 4.13.0 on desktop against 4.14.0 on the
device; about 0.7 to 0.8 s per frame at 4 threads.

Reference: `work/o09/development-v3/reviews/layout03-post-fix-current-source-20261102/full-audit/l03cur*/o08-replay-proof-*.json`,
`samples[0].width800.analysis` (checkout da161891; the detector source differs
from it only by the D1 region fields in `VisionAnalyzer.cs`, which do not
touch circles). Inputs: `.../layout03-validation-spatial-baseline-current-source-20261102/source-inputs/*/{2083,957,962,2263}.{png,json}`.
Per-frame outputs: `work/t2/parity-<frame>.json`.

What this does and does not show:

- It compares circles (ids, kinds, table-2x1 centres, radii) and the
  uncertainty flags, the same shape as the device harness's `Summarize`. It
  does not compare boundary or candidate audit details.
- Four PNG frames from one layout and one light. JPEG inputs (decoder
  differences) and other lights are untested. The design's warp check against
  `work/o08/native-pixels` was not needed and was not run.
- It is a software parity result, not a detector score: the same three black
  misses are reproduced, as they should be.

## What was built

No detector file was edited; Android projects are untouched.

- `src/SkyEye.OpenCv.Desktop`: a desktop look-alike of the `Org.Opencv`
  binding members the detector uses, over OpenCvSharp4 4.13.0.20260627
  (`OpenCvCompat.cs`, about 250 lines). Mirrors the binding's defaults: Java
  `Point`/`Size` truncate to int, `MatOfPoint2f` is float32, `HoughCircles`
  fills a 1xN CV_32FC3 Mat, `Get(r, c)` widens by depth.
- `src/SkyEye.Vision.Desktop`: links `src/SkyEye.Vision.Android/*.cs` except
  `BitmapTableImage.cs` (replaced by a stub that is never constructed) and
  `RoiMotionProcessor.cs`.
- `tests/SkyEye.Vision.Desktop.Runner`: `parity` command.
- All three added to `SkyEye.sln`, so `Build-SkyEye.ps1` compiles the desktop
  copy and a new OpenCV call in the detector without a compat member fails the
  build. The runner and the desktop library are not in `SkyEye.Checks`.

Run one frame:

    dotnet run --project tests\SkyEye.Vision.Desktop.Runner -- parity --image <frame>.png --receipt <frame>.json --proof <o08-replay-proof>.json --threads 4 --out work\t2\parity-<frame>.json

Later on 11 November the parity command gained a seventh check, the final
ball kinds after the inventory constraint (`VisionResult.Balls`, against the
proof's `counts`). All four frames still pass: 2083 cue 1, red 7, yellow 7;
957 and 2263 red 7, unknown 8; 962 red 7, yellow 7, the same on both sides.

## `detect` over the T1 manifest

    dotnet run --project tests\SkyEye.Vision.Desktop.Runner -- detect --manifest tests\fixtures\detection-corpus\manifest.json --output work\t2\detect-labelled-production.jsonl --threads 4
    python tools/evidence/score_detections.py --manifest tests/fixtures/detection-corpus/manifest.json --detections work/t2/detect-labelled-production.jsonl --name desktop-production --out work/t2/score-desktop-production.json
    python -I tools/evidence/compare_desktop_detections.py --manifest tests/fixtures/detection-corpus/manifest.json --detections work/t2/detect-labelled-production.jsonl --out work/t2/compare-labelled-production.json

Options: `--settings production` (default, as `ShellTable.cs`: width 800,
extent .65), `receipt` (each row's capture-time settings from its receipt) or
a settings JSON file; `--roles` (default `positive,empty-control`);
`--names`; `--limit`. Rows with role `holdout-excluded` or split `Holdout`
are never opened. Each line carries the scorer's `sha256` and `balls` plus
uncertainty, warnings, runtime, configuration identity and whether it
matches the receipt. The pixel SHA-256 is checked before analysis.

Run on 11 November: 35 of the 37 labelled rows in about 18 s; the other 2
(the O01 still fixture and the post-shot frame) have no calibration corners.
18 Holdout rows skipped unopened. `production` and `receipt` settings give
the same scores: every one of these receipts already has the production
algorithm and settings.

**Desktop detector, current source, 35 labelled frames** (detector score,
newly executed, desktop OpenCV 4.13):

| | Balls | Black | Cue | False positives | Colour accuracy | Frames complete |
|---|---|---|---|---|---|---|
| All 35 | 529/546 | 14/28 | 25/28 | 0 | 0.970 | 19/35 |

Misses, all at three spots:

- Black at the top cushion near (1.48, 0.067): 8 frames (1691, 1711, 1726,
  1743, 1764, 1783, 2831, 288), dim and bright. 1711, 1743 and 1783 had no
  stored result before; they show the same miss.
- Cue at the cushion near (0.062, 0.868) and black in the corner pocket near
  (0.03, 0.928): frames 382, 1274, 3360 (cue) and 382, 1274, 395 (black). All
  four frames had no stored result before, so this second position is new.
- Black in the open near (0.11, 0.88): Layout03 2083, 2263, 962, as known.

Colour: 957 and 2263 score 7 of 15 colours, because more than seven yellows
turn every yellow into `unknown` (`BallClassifier.ConstrainInventory`). That
is thread T5's target. All other frames have correct colours.

**Against the results stored at capture**, on the 24 frames that have both:

| | Balls | Black | False positives | Colour accuracy | Frames complete |
|---|---|---|---|---|---|
| Stored at capture | 368/374 | 13/19 | 0 | 1.000 | 18/24 |
| Desktop, current source | 368/374 | 13/19 | 0 | 0.978 | 17/24 |

Ball for ball: every frame has the same counts by kind except 957, where
capture stored cue 1 and yellow 7 and desktop gives unknown 8. The device's
own current-source replay of 957 also gives unknown 8 (parity above), so
the difference is between the capture-time build or live input and current
source, not between desktop and device. Matched centres differ from capture
by up to 5 px on 19 of 24 frames. On 4 frames (375, 697, 1726, 229) one
yellow or red near the left cushion is 5.6 to 7.1 px apart, and 957 differs
by kind as above. The receipts name the
same algorithm and settings, but the capture build read the live camera
bitmap, not the saved PNG, and may predate later fixes that kept the
algorithm name. Read these as stored-versus-current differences, not desktop
error.

## Next

1. T5 (surplus-only demotion): score before and after with `detect` on these
   35 frames; the target is 957 and 2263 colour without new false positives.
2. Before trusting desktop scores on JPEG frames, run parity on at least one
   JPEG frame with a device proof. All 35 labelled frames are PNG.
