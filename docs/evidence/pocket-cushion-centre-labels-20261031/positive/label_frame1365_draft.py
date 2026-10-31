from pathlib import Path
import hashlib
import json

from PIL import Image, ImageDraw, ImageFont


ROOT = Path("work/production/evidence/code70-daylight-pocket-capture-20261031/exact-replay-input")
OUT = Path("docs/evidence/pocket-cushion-centre-labels-20261031/positive")
image = Image.open(ROOT / "1365.png").convert("RGB")
font = ImageFont.load_default()

# Full-frame 100px grid for ball inventory and coarse position estimates.
full = image.resize((960, 540), Image.Resampling.LANCZOS)
draw = ImageDraw.Draw(full, "RGBA")
for x in range(0, 1921, 100):
    px = round(x * 0.5)
    draw.line((px, 0, px, 540), fill=(255, 255, 255, 110), width=1)
    if x < 1920:
        draw.text((px + 2, 2), str(x), font=font, fill=(255, 255, 255, 230), stroke_width=1, stroke_fill=(0, 0, 0, 200))
for y in range(0, 1081, 100):
    py = round(y * 0.5)
    draw.line((0, py, 960, py), fill=(255, 255, 255, 110), width=1)
    if y < 1080:
        draw.text((2, py + 2), str(y), font=font, fill=(255, 255, 255, 230), stroke_width=1, stroke_fill=(0, 0, 0, 200))
full.save(OUT / "frame1365-grid-100px.png")

# Bottom-left pocket enlarged 3x with a 10px grid and 20px labels.
bounds = (0, 760, 300, 1080)
crop = image.crop(bounds).resize(((bounds[2] - bounds[0]) * 3, (bounds[3] - bounds[1]) * 3), Image.Resampling.NEAREST)
draw = ImageDraw.Draw(crop, "RGBA")
for x in range(bounds[0] - bounds[0] % 10, bounds[2] + 1, 10):
    px = (x - bounds[0]) * 3
    major = x % 20 == 0
    draw.line((px, 0, px, crop.height), fill=(255, 255, 255, 105 if not major else 155), width=1 if not major else 2)
    if major:
        draw.text((px + 2, 2), str(x), font=font, fill=(255, 255, 255, 240), stroke_width=1, stroke_fill=(0, 0, 0, 220))
for y in range(bounds[1] - bounds[1] % 10, bounds[3] + 1, 10):
    py = (y - bounds[1]) * 3
    major = y % 20 == 0
    draw.line((0, py, crop.width, py), fill=(255, 255, 255, 105 if not major else 155), width=1 if not major else 2)
    if major:
        draw.text((2, py + 2), str(y), font=font, fill=(255, 255, 255, 240), stroke_width=1, stroke_fill=(0, 0, 0, 220))
crop.save(OUT / "frame1365-lower-left-grid-10px.png")

# Provisional centres placed by visual inspection of the raw frame and grids.
# The black ball is substantially occluded by the jaw and cue; its centre is
# not readable from this frame, so it is intentionally left unlabelled.
centres = [
    {"id": "yellow-01", "colour": "yellow", "xPx": 85, "yPx": 200, "uncertaintyPx": 4, "confidence": "medium"},
    {"id": "red-01", "colour": "red", "xPx": 125, "yPx": 194, "uncertaintyPx": 4, "confidence": "medium"},
    {"id": "red-02", "colour": "red", "xPx": 785, "yPx": 145, "uncertaintyPx": 3, "confidence": "medium"},
    {"id": "yellow-02", "colour": "yellow", "xPx": 847, "yPx": 146, "uncertaintyPx": 3, "confidence": "medium"},
    {"id": "red-03", "colour": "red", "xPx": 1627, "yPx": 103, "uncertaintyPx": 4, "confidence": "medium"},
    {"id": "yellow-03", "colour": "yellow", "xPx": 1638, "yPx": 151, "uncertaintyPx": 4, "confidence": "medium"},
    {"id": "red-04", "colour": "red", "xPx": 117, "yPx": 510, "uncertaintyPx": 3, "confidence": "high"},
    {"id": "yellow-04", "colour": "yellow", "xPx": 813, "yPx": 534, "uncertaintyPx": 3, "confidence": "high"},
    {"id": "yellow-05", "colour": "yellow", "xPx": 1631, "yPx": 543, "uncertaintyPx": 3, "confidence": "high"},
    {"id": "red-05", "colour": "red", "xPx": 121, "yPx": 862, "uncertaintyPx": 3, "confidence": "medium"},
    {"id": "cue-01", "colour": "cue", "xPx": 141, "yPx": 903, "uncertaintyPx": 3, "confidence": "medium"},
    {"id": "yellow-06", "colour": "yellow", "xPx": 802, "yPx": 930, "uncertaintyPx": 4, "confidence": "medium"},
    {"id": "red-06", "colour": "red", "xPx": 866, "yPx": 931, "uncertaintyPx": 4, "confidence": "medium"},
    {"id": "red-07", "colour": "red", "xPx": 1607, "yPx": 929, "uncertaintyPx": 4, "confidence": "medium"},
    {"id": "yellow-07", "colour": "yellow", "xPx": 1659, "yPx": 909, "uncertaintyPx": 4, "confidence": "medium"},
]
receipt_path = ROOT / "1365.json"
receipt = json.loads(receipt_path.read_text(encoding="utf-8-sig"))
source_path = ROOT / "1365.png"
source_hash = hashlib.sha256(source_path.read_bytes()).hexdigest()
label_set = {
    "schemaVersion": 1,
    "evidenceKind": "independent-manual-visual-labels-before-detector-review",
    "reviewer": "assistant visual inspection of raw frame",
    "status": "assistant draft; user review needed",
    "source": {
        "file": "1365.png",
        "sha256": source_hash,
        "width": image.width,
        "height": image.height,
        "sourceTimeline": receipt["indexedFrame"]["scope"]["timeline"],
        "frameId": receipt["indexedFrame"]["frameId"],
        "sourceTimeUs": receipt["indexedFrame"]["sourceTimeUs"],
        "calibrationId": receipt["indexedFrame"]["scope"]["calibration"],
    },
    "method": "Centres estimated by visual inspection of the exact raw PNG, then checked against a 100-pixel full-frame and 10-pixel lower-left crop coordinate grid. No detector or model outputs were consulted for placement.",
    "expectedInventory": {"cue": 1, "red": 7, "yellow": 7, "black": 1, "total": 16},
    "unresolvedBallCentres": [
        {"id": "black-01", "colour": "black", "reason": "The visible black body merges into the lower-left pocket/jaw and overlaps the cue ball; a centre cannot be placed from this frame without guessing."}
    ],
    "calibrationCornersNormalized": [
        [p["x"], p["y"]] for p in receipt["calibrationCorners"]
    ],
    "blackInteriorPoint": {"xPx": 102, "yPx": 903, "semantics": "approximate interior point only; not used as centre"},
    "balls": centres,
}
(OUT / "manual-labels-frame-1365-draft.json").write_text(json.dumps(label_set, indent=2) + "\n", encoding="utf-8")

overlay = image.copy()
draw = ImageDraw.Draw(overlay)
for ball in centres:
    x, y = ball["xPx"], ball["yPx"]
    color = {"red": (30, 255, 255), "yellow": (255, 0, 255), "black": (0, 255, 0), "cue": (255, 80, 30)}[ball["colour"]]
    radius = 18
    draw.ellipse((x-radius, y-radius, x+radius, y+radius), outline=color, width=3)
    draw.line((x-10, y, x+10, y), fill=color, width=2)
    draw.line((x, y-10, x, y+10), fill=color, width=2)
    label = ball["id"]
    draw.text((x + 20, y - 18), label, font=font, fill=(255, 255, 255), stroke_width=2, stroke_fill=(0, 0, 0))
overlay.save(OUT / "manual-centres-frame-1365-draft.png")

crop_overlay = overlay.crop(bounds).resize(((bounds[2]-bounds[0])*3, (bounds[3]-bounds[1])*3), Image.Resampling.NEAREST)
crop_overlay.save(OUT / "manual-centres-frame-1365-lower-left-review.png")
