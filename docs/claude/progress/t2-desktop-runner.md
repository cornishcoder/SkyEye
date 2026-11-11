# T2 desktop runner: progress (paused 11 Nov 2026)

Done:

- Read the plan (T2 and rules), all 9 detector files' OpenCV usage, the
  binding csproj (OpenCV 4.14.0 AAR), the Vision.Device replay harness,
  Workbench, SkyEye.Checks and score_detections.py.
- Located the Layout03 reference outputs (da161891, 59/62, 0 FP, 3 black
  misses) and their PNG inputs.
- Confirmed that the detector source at HEAD equals da161891.
- Wrote `docs/claude/t2-desktop-runner-design.md`. Its sections are complete
  except the TODOs.

Remaining:

- Check the call counts in section 2 line by line. Grep counts include a few
  RoiMotionProcessor sites.
- Write `docs/claude/data/opencv-surface.csv`.
- Confirm the KleidiCV and Carotene kernel list for OpenCV 4.14 Android.
- Confirm the OpenCvSharp `Compare` overload for Scalar and the `Point(double,double)`
  behaviour.
- Bind section 4 to the final T1 manifest schema.

Next steps, on Windows with .NET:

1. Create SkyEye.OpenCv.Desktop (the compat shim) and SkyEye.Vision.Desktop
   (linked files) as in section 3, then build.
2. Spike frame 2083 against
   `full-audit/l03cur2083-215925/o08-replay-proof-*.json`.
3. Add the runner and the parity command.

No src/ or tests/ changes have been made, and there were no git writes.
