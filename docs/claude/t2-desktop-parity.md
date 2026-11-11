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

## Next

1. `detect` command over the T1 manifest writing JSONL for
   `tools/evidence/score_detections.py` (design section 4), so T5 and T8 can
   be scored before and after on desktop.
2. Before trusting desktop scores on JPEG frames, run parity on at least one
   JPEG frame with a device proof.
