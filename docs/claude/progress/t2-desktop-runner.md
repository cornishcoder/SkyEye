# T2 desktop runner: progress (11 Nov 2026)

Done:

- Design: `docs/claude/t2-desktop-runner-design.md`.
- Built on Windows: `src/SkyEye.OpenCv.Desktop` (compat shim over OpenCvSharp
  4.13.0.20260627), `src/SkyEye.Vision.Desktop` (detector sources linked
  unchanged) and `tests/SkyEye.Vision.Desktop.Runner`. All three are in
  `SkyEye.sln`.
- `parity`: Layout03 PASS 4/4 (frames 2083, 957, 962, 2263), including
  final ball kinds. Report: `docs/claude/t2-desktop-parity.md`.
- `detect` over the T1 manifest, scored with `score_detections.py`; desktop
  versus stored comparison in `tools/evidence/compare_desktop_detections.py`.
  35 of 37 labelled frames run (2 have no corners).

Remaining:

- Parity on one JPEG frame with a device proof before trusting JPEG scores.
- Optional: run `detect --roles unlabelled-reference` for a wider desktop
  versus stored comparison (440 rows, a few minutes).
