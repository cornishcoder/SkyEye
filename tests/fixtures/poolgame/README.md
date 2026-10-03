# Poolgame port fixtures

- `geometry-reference.json`: 270 candidates from the unchanged Python source at `a7a803177c63ccb51f715c6920ba650fc341f76a`, generated with `scripts/Generate-PoolgameReference.py`. Coordinates use portrait 1000 × 2000 pixels and upstream radius 23. C# generation, ghost positions, angles, path lengths and difficulty are checked against these outputs.
- `pixel-table.jpg` / `pixel-table.json`: 3 October 2026 mounted Pixel camera frame with manually labelled red/yellow/cue/black centres and four cloth corners. Labels contain 7 reds, 7 yellows, 1 spotted cream cue and 1 black with a cream number patch.
- `pixel-later-light.jpg`: same unmoved camera and ball layout, acquired from the live stream around 14:54 BST on 3 October. User confirms this is the standard SkyEye lighting. The cloth appears less saturated; this exposed the upstream mask's fixed saturation floor. It is an additional regression frame, not a held-out layout or an accuracy dataset.
- `synthetic-*.jpg`: deterministic empty, dim, very dark, interior obstruction, edge-connected obstruction and pocket-adjacent ball variants. Regenerate with `scripts/Generate-VisionNegatives.py` using the pinned Python reference environment. These are controlled regression probes, not real-world precision/recall evidence.
- `upstream-vision-reference.json`: original Python pipeline output on the first calibrated frame using OpenCV 4.11.0. It detects 16 circles but misclassifies the cue's red spot as a stripe. The C# port adapts colour labels rather than treating solids/stripes as red/yellow.
- `device-vision-reference.json`: first successful C# OpenCV 4.14.0 device pipeline run before later-light adaptations. Later device performance/negative runs are recorded separately.

Colour quality is a heuristic feature score. Native runtime version, cue gains, circle centres/radii and colour clusters are retained in device results so detection, classification and coordinates can be inspected independently.
