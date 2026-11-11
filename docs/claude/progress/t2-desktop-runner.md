# T2 desktop runner: progress (11 Nov 2026)

Done:

- Design: `docs/claude/t2-desktop-runner-design.md`.
- Built on Windows: `src/SkyEye.OpenCv.Desktop` (compat shim over OpenCvSharp
  4.13.0.20260627), `src/SkyEye.Vision.Desktop` (detector sources linked
  unchanged) and `tests/SkyEye.Vision.Desktop.Runner` (`parity` command). All
  three are in `SkyEye.sln`.
- Layout03 parity PASS 4/4 (frames 2083, 957, 962, 2263): same circles and
  kinds, 58 of 59 centres identical, one 0.85 px. Report:
  `docs/claude/t2-desktop-parity.md`.

Remaining:

- `detect` command over the T1 manifest, writing JSONL for
  `score_detections.py`. Bind to the manifest field names; refuse Holdout rows.
- Parity on one JPEG frame with a device proof before trusting JPEG scores.
- Optional: `docs/claude/data/opencv-surface.csv`; the compat shim now lists
  the surface in code.
