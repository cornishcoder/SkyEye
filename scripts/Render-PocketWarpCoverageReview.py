#!/usr/bin/env python3
"""Render the calibrated raw/rectified coverage around the lower-left pocket.

This is a geometry review, not ball detection. The marked positive-frame point
is an approximate black-ball interior reference, not a centre label.
"""

from __future__ import annotations

import hashlib
import json
import math
from pathlib import Path

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont


POSITIVE = Path("work/production/evidence/code70-daylight-pocket-capture-20261031/exact-replay-input")
NEGATIVE = Path("work/production/evidence/code65-source-frame-check-20261031/exact-replay-input")
OUTPUT = Path("docs/evidence/pocket-warp-coverage-audit-20261031")
RAW_POINT = np.array([102.0, 903.0], dtype=np.float64)
TARGET_WIDTH = 1000
TARGET_HEIGHT = 2000


def digest(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def project(matrix: np.ndarray, point: np.ndarray) -> np.ndarray:
    mapped = matrix @ np.array([point[0], point[1], 1.0], dtype=np.float64)
    if abs(mapped[2]) < 1e-12:
        raise ValueError("Point maps to infinity")
    return mapped[:2] / mapped[2]


def point_segment_distance(point: np.ndarray, a: np.ndarray, b: np.ndarray) -> float:
    segment = b - a
    denom = float(np.dot(segment, segment))
    if denom <= 0:
        return float(np.linalg.norm(point - a))
    t = min(1.0, max(0.0, float(np.dot(point - a, segment) / denom)))
    return float(np.linalg.norm(point - (a + t * segment)))


def homography(receipt: dict, image_shape: tuple[int, int], width: int = TARGET_WIDTH) -> tuple[np.ndarray, np.ndarray, np.ndarray, np.ndarray]:
    height, source_width = image_shape
    corners = receipt["calibrationCorners"]
    source = np.array([[c["x"] * source_width, c["y"] * height] for c in corners], dtype=np.float32)
    target = np.array([[width, 0], [width, width * 2], [0, width * 2], [0, 0]], dtype=np.float32)
    forward = cv2.getPerspectiveTransform(source, target).astype(np.float64)
    inverse = np.linalg.inv(forward)
    return source, target, forward, inverse


def make_panel(
    raw_path: Path,
    receipt_path: Path,
    title: str,
    mark_raw_point: bool,
) -> tuple[Image.Image, dict]:
    receipt = json.loads(receipt_path.read_text(encoding="utf-8-sig"))
    source = cv2.imread(str(raw_path), cv2.IMREAD_COLOR)
    if source is None:
        raise RuntimeError(f"Cannot decode {raw_path}")
    height, width = source.shape[:2]
    indexed = receipt["indexedFrame"]
    source_hash = digest(raw_path)
    if source_hash.lower() != indexed["pixelSha256"].lower():
        raise RuntimeError(f"Pixel hash mismatch for {raw_path}")
    if (width, height) != (indexed["width"], indexed["height"]):
        raise RuntimeError(f"Pixel dimensions mismatch for {raw_path}")

    source_quad, target_quad, forward, inverse = homography(receipt, (height, width))
    warped = cv2.warpPerspective(source, forward, (TARGET_WIDTH, TARGET_HEIGHT), flags=cv2.INTER_LINEAR)
    # VisionAnalyzer emits portrait (1000 x 2000); rotate back for a landscape contact sheet.
    warped_landscape = cv2.rotate(warped, cv2.ROTATE_90_COUNTERCLOCKWISE)
    raw_rgb = cv2.cvtColor(source, cv2.COLOR_BGR2RGB)
    warped_rgb = cv2.cvtColor(warped_landscape, cv2.COLOR_BGR2RGB)

    canvas_w, canvas_h = 960, 540
    scale = canvas_w / width
    warp_scale = canvas_w / warped_landscape.shape[1]
    warp_h = int(round(warped_landscape.shape[0] * warp_scale))
    warp_y = (canvas_h - warp_h) / 2
    raw_panel = Image.fromarray(raw_rgb).resize((canvas_w, canvas_h), Image.Resampling.LANCZOS)
    warp_panel = Image.new("RGB", (canvas_w, canvas_h), (20, 20, 20))
    warp_resized = Image.fromarray(warped_rgb).resize((canvas_w, warp_h), Image.Resampling.LANCZOS)
    warp_panel.paste(warp_resized, (0, int(round(warp_y))))

    font = ImageFont.load_default()
    raw_draw = ImageDraw.Draw(raw_panel)
    raw_draw.text((10, 8), f"{title}: decoded source; calibration quadrilateral", font=font, fill="white", stroke_width=2, stroke_fill="black")
    q_points = [(float(x) * scale, float(y) * scale) for x, y in source_quad]
    raw_draw.line(q_points + [q_points[0]], fill=(0, 235, 255), width=3)
    if mark_raw_point:
        px, py = float(RAW_POINT[0] * scale), float(RAW_POINT[1] * scale)
        raw_draw.ellipse((px - 7, py - 7, px + 7, py + 7), outline=(255, 45, 210), width=3)
        raw_draw.line((px - 12, py, px + 12, py), fill=(255, 45, 210), width=2)
        raw_draw.line((px, py - 12, px, py + 12), fill=(255, 45, 210), width=2)
        raw_draw.text((px + 10, py - 20), "approx. black interior point", font=font, fill="white", stroke_width=2, stroke_fill="black")

    warp_draw = ImageDraw.Draw(warp_panel)
    warp_draw.text((10, 8), f"{title}: VisionAnalyzer-style rectification", font=font, fill="white", stroke_width=2, stroke_fill="black")
    # cv2 ROTATE_90_COUNTERCLOCKWISE maps source (x,y) to (y, W-1-x).
    # Draw the exact target rectangle boundary in the displayed landscape plane.
    rect_corners = [(0.0, 0.0), (float(TARGET_WIDTH * 2), 0.0), (float(TARGET_WIDTH * 2), float(TARGET_WIDTH)), (0.0, float(TARGET_WIDTH))]
    rect_panel_points = [(x * warp_scale, y * warp_scale + warp_y) for x, y in rect_corners]
    warp_draw.line(rect_panel_points + [rect_panel_points[0]], fill=(0, 235, 255), width=3)
    mapped_point = project(forward, RAW_POINT) if mark_raw_point else None
    if mark_raw_point and mapped_point is not None:
        landscape_point = np.array([mapped_point[1], TARGET_WIDTH - mapped_point[0]])
        px, py = float(landscape_point[0] * warp_scale), float(landscape_point[1] * warp_scale + warp_y)
        warp_draw.ellipse((px - 7, py - 7, px + 7, py + 7), outline=(255, 45, 210), width=3)
        warp_draw.line((px - 12, py, px + 12, py), fill=(255, 45, 210), width=2)
        warp_draw.line((px, py - 12, px, py + 12), fill=(255, 45, 210), width=2)
        warp_draw.text((px + 10, py - 20), "mapped interior point", font=font, fill="white", stroke_width=2, stroke_fill="black")

    panel = Image.new("RGB", (canvas_w * 2 + 8, canvas_h), (40, 40, 40))
    panel.paste(raw_panel, (0, 0))
    panel.paste(warp_panel, (canvas_w + 8, 0))
    if mapped_point is None:
        metrics = {
            "frameId": indexed["frameId"],
            "sourceSha256": source_hash,
            "timeline": indexed["scope"]["timeline"],
            "calibrationId": indexed["scope"]["calibration"],
            "calibrationCorners": [[float(x), float(y)] for x, y in source_quad],
            "sourceQuadAndRectifiedTargetAreMappedExactly": True,
        }
    else:
        point_in_quad = cv2.pointPolygonTest(source_quad.astype(np.float32), tuple(map(float, RAW_POINT)), False) >= 0
        dists = [point_segment_distance(RAW_POINT, source_quad[i], source_quad[(i + 1) % 4]) for i in range(4)]
        target_distances = [float(mapped_point[0]), float(TARGET_WIDTH - mapped_point[0]), float(mapped_point[1]), float(TARGET_HEIGHT - mapped_point[1])]
        metrics = {
            "frameId": indexed["frameId"],
            "sourceSha256": source_hash,
            "timeline": indexed["scope"]["timeline"],
            "calibrationId": indexed["scope"]["calibration"],
            "calibrationCornersNormalized": [[float(c["x"]), float(c["y"])] for c in receipt["calibrationCorners"]],
            "blackInteriorReferenceSourcePixel": RAW_POINT.tolist(),
            "referenceSemantics": "approximate user-reviewed black-ball interior point; not a ball centre",
            "pointInsideCalibrationQuadrilateral": bool(point_in_quad),
            "pointToSourceQuadEdgeDistancesPx": [round(d, 3) for d in dists],
            "mappedRectifiedPortraitPixel": [round(float(mapped_point[0]), 3), round(float(mapped_point[1]), 3)],
            "mappedRectifiedLandscapePixel": [round(float(mapped_point[1]), 3), round(float(TARGET_WIDTH - mapped_point[0]), 3)],
            "pointToRectifiedOutputEdgeDistancesPx": [round(d, 3) for d in target_distances],
            "doesNotQuantifyBallClipping": "The reference is an interior point, not a centre. No black-ball boundary/body fraction is computed.",
        }
    return panel, metrics


def main() -> int:
    positive_path = POSITIVE / "1365.png"
    positive_receipt = POSITIVE / "1365.json"
    negative_path = NEGATIVE / "978.png"
    negative_receipt = NEGATIVE / "978.json"
    for path in (positive_path, positive_receipt, negative_path, negative_receipt):
        if not path.is_file():
            raise FileNotFoundError(path)

    positive, positive_metrics = make_panel(positive_path, positive_receipt, "Black-present daylight", True)
    negative, negative_metrics = make_panel(negative_path, negative_receipt, "Black-absent dimmer control", False)
    combined = Image.new("RGB", (positive.width, positive.height * 2 + 8), (25, 25, 25))
    combined.paste(positive, (0, 0))
    combined.paste(negative, (0, positive.height + 8))

    pos_receipt = json.loads(positive_receipt.read_text(encoding="utf-8-sig"))
    neg_receipt = json.loads(negative_receipt.read_text(encoding="utf-8-sig"))
    same_corners = all(
        math.isclose(float(a[key]), float(b[key]), rel_tol=0.0, abs_tol=1e-9)
        for a, b in zip(pos_receipt["calibrationCorners"], neg_receipt["calibrationCorners"])
        for key in ("x", "y")
    )
    summary = {
        "schemaVersion": 1,
        "purpose": "geometry/crop review around one pocket; not ball detection or detector accuracy",
        "warp": {
            "implementation": "VisionAnalyzer.Warp source corners to [W,0], [W,2W], [0,2W], [0,0], then cv2.warpPerspective to W x 2W",
            "outputWidth": TARGET_WIDTH,
            "outputHeight": TARGET_HEIGHT,
            "displayRotation": "90 degrees counter-clockwise back to landscape for the contact sheet",
        },
        "positiveFrame": positive_metrics,
        "emptyMouthControl": negative_metrics,
        "controlCaveat": {
            "calibrationCornersMatchExactly": same_corners,
            "lightingAndTimelineMatch": False,
            "note": "The empty-mouth frame is from a separate earlier, dimmer capture timeline. It is a spatial visual control only, not a matched-light or same-session comparison.",
        },
        "limitations": [
            "The black-ball reference is an approximate interior pixel and is explicitly not a centre label.",
            "No ball body/area clipping fraction can be computed without an independently reviewed centre and boundary.",
            "The calibration quadrilateral is the saved playable-table crop; the overlay shows its relation to the pocket, not a verified physical ball contour.",
            "The control uses a separate, dimmer capture timeline despite identical saved calibration corners.",
            "This is a geometry visualization, not evidence of detection accuracy or proof that warp causes the miss.",
        ],
    }
    OUTPUT.mkdir(parents=True, exist_ok=True)
    (OUTPUT / "summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    combined.save(OUTPUT / "raw-and-rectified-pocket-coverage.png", optimize=True)
    print(json.dumps({
        "overlay": str(OUTPUT / "raw-and-rectified-pocket-coverage.png"),
        "summary": str(OUTPUT / "summary.json"),
        "positivePointInsideQuad": positive_metrics["pointInsideCalibrationQuadrilateral"],
        "positiveRectifiedPoint": positive_metrics["mappedRectifiedPortraitPixel"],
        "sameCalibrationCorners": same_corners,
        "negativeIsMatchedLighting": False,
    }, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
