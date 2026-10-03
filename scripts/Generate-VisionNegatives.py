"""Deterministic stress variants of the labelled frame, not held-out real images."""
from pathlib import Path
import json
import cv2
import numpy as np

root = Path(__file__).resolve().parents[1] / "tests/fixtures/poolgame"
frame = cv2.imread(str(root / "pixel-table.jpg"))
assert frame is not None
variants = {"synthetic-empty": np.full_like(frame, (110, 120, 25)),
            "synthetic-dim": np.clip(frame.astype(np.float64) * .75, 0, 255).astype(np.uint8),
            "synthetic-dark": np.clip(frame.astype(np.float64) * .4, 0, 255).astype(np.uint8)}
covered = frame.copy()
cv2.ellipse(covered, (1000, 570), (170, 150), 0, 0, 360, (75, 130, 185), -1)
variants["synthetic-obstruction"] = covered
edge = frame.copy()
cv2.rectangle(edge, (100, 520), (740, 680), (75, 130, 185), -1)
variants["synthetic-edge-obstruction"] = edge
calibration = json.loads((root / "pixel-table.json").read_text())["calibration"]
source = np.array([[p["x"] * frame.shape[1], p["y"] * frame.shape[0]] for p in calibration], np.float32)
destination = np.array([[1000, 0], [1000, 2000], [0, 2000], [0, 0]], np.float32)
inverse = cv2.getPerspectiveTransform(destination, source)
patch = np.zeros((2000, 1000, 3), np.uint8)
mask = np.zeros((2000, 1000), np.uint8)
cv2.circle(patch, (38, 38), 27, (0, 140, 245), -1)
cv2.circle(mask, (38, 38), 27, 255, -1)
raw_patch = cv2.warpPerspective(patch, inverse, (frame.shape[1], frame.shape[0]))
raw_mask = cv2.warpPerspective(mask, inverse, (frame.shape[1], frame.shape[0]))
hanging = frame.copy(); hanging[raw_mask > 127] = raw_patch[raw_mask > 127]
variants["synthetic-pocket-ball"] = hanging
for name, image in variants.items():
    assert cv2.imwrite(str(root / (name + ".jpg")), image, [cv2.IMWRITE_JPEG_QUALITY, 95])
print(f"Wrote {len(variants)} synthetic regression variants")
