# Poolgame port fixtures

- `geometry-reference.json`: 270 candidates from the unchanged Python source at `a7a803177c63ccb51f715c6920ba650fc341f76a`, generated with `scripts/Generate-PoolgameReference.py`. Coordinates use portrait 1000 × 2000 pixels and upstream radius 23. C# generation, ghost positions, angles, path lengths and difficulty are checked against these outputs.
- `pixel-table.jpg` / `pixel-table.json`: 3 October 2026 mounted Pixel camera frame with manually labelled red/yellow/cue/black centres and four cloth corners. Labels contain 7 reds, 7 yellows, 1 spotted cream cue and 1 black with a cream number patch.
- `pixel-later-light.jpg`: same unmoved camera and ball layout, acquired from the live stream around 14:54 BST on 3 October. User confirms this is the standard SkyEye lighting. The cloth appears less saturated; this exposed the upstream mask's fixed saturation floor. It is an additional regression frame, not a held-out layout or an accuracy dataset.
- `pixel-cushion-shadow.jpg`: another live frame from the same layout at approximately 15:36 BST. Occasional cushion-shadow circles in the live stream motivated rejecting border circles whose interior is overwhelmingly cloth-coloured. All three real frames must retain the complete 7/7/1/1 layout. This frame also belongs to the tuning set.
- `synthetic-*.jpg`: deterministic empty, dim, very dark, interior obstruction, edge-connected obstruction and pocket-adjacent ball variants. Regenerate with `scripts/Generate-VisionNegatives.py` using the pinned Python reference environment. These are controlled regression probes, not real-world precision/recall evidence.
- `upstream-vision-reference.json`: original Python pipeline output on the first calibrated frame using OpenCV 4.11.0. It detects 16 circles but misclassifies the cue's red spot as a stripe. The C# port adapts colour labels rather than treating solids/stripes as red/yellow.
- `device-vision-reference.json`: first successful C# OpenCV 4.14.0 device pipeline run before later-light adaptations. Later device performance/negative runs are recorded separately.

Colour quality is a heuristic feature score. Native runtime version, cue gains, circle centres/radii and colour clusters are retained in device results so detection, classification and coordinates can be inspected independently.
# Racked table with lights off

`pixel-racked-dim.jpg` is an unannotated camera frame captured on 4 October 2026. The user confirmed the balls were racked for the start of a game; the image shows the separate cue ball and touching 15-ball triangle. Its JSON records human ground truth and approximate cloth corners solely for a negative regression. It does not claim automatic rack recognition, reliable dim-light classification or exact image capture timing. The device check requires shot advice to remain ineligible for this unresolved cluster, even if a caller attempts to confirm the detected layout.

# Fresh touching rack with lights on

`pixel-packed-rack-2026-10-25.jpg` is the exact bright live frame captured by Production's read-only dashboard on 25 October. Its JSON stores the matching frame receipt/calibration and manually reviewed visible ball centres/categories, with fixture-local entries rather than persistent identities. It is an initial-rack inventory regression, not a held-out accuracy set. At the production 800px setting, an incomplete detected compact cluster gets one same-frame 1000px retry; an unresolved result remains uncertain and cannot enable shot advice. Replay records each pass's time, analysis pixel area and sampled process working-set change.

