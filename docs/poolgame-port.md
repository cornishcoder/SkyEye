# Poolgame C# port progress

Reference source: `C:\code\Poolgame-AI-shot-prediction`, upstream https://github.com/tomasz-pawlaczyk/Poolgame-AI-shot-prediction, revision `a7a803177c63ccb51f715c6920ba650fc341f76a`. The local source checkout is retained unchanged. Its executable pipeline is OpenCV/Hough/HSV plus K-means; YOLO weights and the README's post-collision physics are absent. No LICENSE file was found in this checkout; preserve provenance and establish reuse terms before distributing incorporated upstream work.

## Checkpoints

- `f20f5f7`: pre-port checkpoint containing the earlier overlay, calibration and colour-detector debugging. Core checks and ARM64 companion build passed. Not deployed.
- Reference fixtures: unmodified Python generates geometry/validation outputs with source hashes, three layouts and 270 candidates. Pixel camera image and hand-labelled centres/calibration are recorded separately; they are a regression fixture, not a held-out accuracy evaluation.

## Validation commands

```powershell
python scripts/Generate-PoolgameReference.py --source C:/code/Poolgame-AI-shot-prediction
dotnet run --project tests/SkyEye.Checks -c Release
dotnet build SkyEye.sln -c Debug -m:1
```

## Next gates

1. Android OpenCV loading, image primitives and native disposal proven from C# on the Pixel companion.
2. Coordinate mapping and geometry checked against Python outputs; intentional corrections separately tested.
3. Perspective, masks, normalization and red/yellow/cue/black classification checked on actual camera frames.
4. UI/tool integration and performance under live playback; held-out images and ambiguous states tested before accuracy claims.

The full implementation plan is in `python-port-plan.md`. Python remains a development reference only; the app's algorithms are implemented in C# with native OpenCV image primitives.
