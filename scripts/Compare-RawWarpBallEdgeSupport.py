#!/usr/bin/env python3
"""Exploratory matched-geometry edge-support comparison for one labelled frame.

This does not detect balls. It compares luma-gradient support around the same
approximate ball boundaries in decoded source pixels and the calibrated,
rectified raster consumed by VisionAnalyzer.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
from pathlib import Path
from typing import Any

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont


DEFAULT_EVIDENCE = Path(
    "work/production/evidence/o08-holdout-20261029/raw-session-e3c02bb9dc4542948c829a512630041a"
)
DEFAULT_OUTPUT = Path("docs/evidence/raw-warp-edge-probe-20261031")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def project(matrix: np.ndarray, point: tuple[float, float]) -> tuple[float, float]:
    x, y = point
    value = matrix @ np.array([x, y, 1.0], dtype=np.float64)
    if abs(value[2]) < 1e-12:
        raise ValueError("Homography mapped a point to infinity")
    return float(value[0] / value[2]), float(value[1] / value[2])


def jacobian(matrix: np.ndarray, point: tuple[float, float]) -> np.ndarray:
    """Derivative of a 3x3 homography at an input point."""
    x, y = point
    a, b, c = matrix[0]
    d, e, f = matrix[1]
    g, h, _ = matrix[2]
    denominator = g * x + h * y + 1.0
    numerator_x = a * x + b * y + c
    numerator_y = d * x + e * y + f
    return np.array(
        [
            [(a * denominator - g * numerator_x) / denominator**2,
             (b * denominator - h * numerator_x) / denominator**2],
            [(d * denominator - g * numerator_y) / denominator**2,
             (e * denominator - h * numerator_y) / denominator**2],
        ],
        dtype=np.float64,
    )


def bilinear(array: np.ndarray, x: float, y: float) -> float:
    height, width = array.shape[:2]
    if not (0 <= x < width - 1 and 0 <= y < height - 1):
        return float("nan")
    x0, y0 = int(math.floor(x)), int(math.floor(y))
    dx, dy = x - x0, y - y0
    return float(
        array[y0, x0] * (1 - dx) * (1 - dy)
        + array[y0, x0 + 1] * dx * (1 - dy)
        + array[y0 + 1, x0] * (1 - dx) * dy
        + array[y0 + 1, x0 + 1] * dx * dy
    )


def projected_gradient(
    gx: np.ndarray,
    gy: np.ndarray,
    position: tuple[float, float],
    direction: tuple[float, float],
    mapping_jacobian: np.ndarray | None = None,
) -> float:
    px, py = position
    dx, dy = direction
    if mapping_jacobian is not None:
        dx, dy = mapping_jacobian @ np.array([dx, dy], dtype=np.float64)
    grad_x, grad_y = bilinear(gx, px, py), bilinear(gy, px, py)
    if not (math.isfinite(grad_x) and math.isfinite(grad_y)):
        return float("nan")
    return abs(grad_x * float(dx) + grad_y * float(dy))


def draw_panel(
    image: Image.Image,
    balls: list[dict[str, Any]],
    centers: list[tuple[float, float]],
    boundary_points: list[list[tuple[float, float]]],
    to_panel: Any,
    title: str,
    occluded: list[list[bool]],
    font: ImageFont.ImageFont,
) -> Image.Image:
    panel = image.convert("RGB")
    draw = ImageDraw.Draw(panel)
    draw.text((14, 12), title, font=font, fill=(255, 255, 255), stroke_width=2, stroke_fill=(0, 0, 0))
    for index, (ball, center) in enumerate(zip(balls, centers)):
        cx, cy = to_panel(center)
        outline = [to_panel(point) for point in boundary_points[index]]
        if outline:
            draw.line(outline + [outline[0]], fill=(255, 255, 255), width=2, joint="curve")
        draw.ellipse((cx - 3, cy - 3, cx + 3, cy + 3), fill=(255, 255, 255))
        for sector, point in enumerate(boundary_points[index]):
            px, py = to_panel(point)
            color = (255, 100, 80) if occluded[index][sector] else (70, 245, 130)
            draw.ellipse((px - 2, py - 2, px + 2, py + 2), fill=color)
    return panel


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence", type=Path, default=DEFAULT_EVIDENCE)
    parser.add_argument("--image", type=Path, help="Exact decoded source image (requires --receipt and --labels)")
    parser.add_argument("--receipt", type=Path, help="Frame receipt JSON (requires --image and --labels)")
    parser.add_argument("--labels", type=Path, help="Manual centre-label JSON (requires --image and --receipt)")
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    parser.add_argument("--width", type=int, default=1000, help="Rectified raster width; height is 2x width")
    parser.add_argument("--radius", type=float, default=27.0, help="Ball radius in rectified pixels")
    args = parser.parse_args()

    explicit_inputs = any(path is not None for path in (args.image, args.receipt, args.labels))
    if explicit_inputs:
        if any(path is None for path in (args.image, args.receipt, args.labels)):
            parser.error("--image, --receipt, and --labels must be supplied together")
        image_path, metadata_path, labels_path = args.image, args.receipt, args.labels
    else:
        root = args.evidence
        image_path = root / "frame-01.png"
        metadata_path = root / "01.json"
        labels_path = root / "manual-labels-frame-01.json"
    for path in (image_path, metadata_path, labels_path):
        if not path.is_file():
            raise FileNotFoundError(path)

    metadata = json.loads(metadata_path.read_text(encoding="utf-8-sig"))
    labels = json.loads(labels_path.read_text(encoding="utf-8-sig"))
    source = cv2.imread(str(image_path), cv2.IMREAD_COLOR)
    if source is None:
        raise RuntimeError(f"Could not read {image_path}")
    height, width = source.shape[:2]
    indexed = metadata["indexedFrame"]
    source_hash = sha256(image_path)
    if indexed["pixelSha256"].lower() != source_hash.lower():
        raise RuntimeError("Source PNG hash does not match frame receipt")
    if labels["source"]["sha256"].lower() != source_hash.lower():
        raise RuntimeError("Manual-label source hash does not match frame receipt")
    if (labels["source"]["width"], labels["source"]["height"]) != (width, height):
        raise RuntimeError("Manual-label dimensions do not match source pixels")

    corners = metadata["calibrationCorners"]
    source_points = np.array(
        [[point["x"] * width, point["y"] * height] for point in corners], dtype=np.float32
    )
    out_width, out_height = args.width, args.width * 2
    # Match VisionAnalyzer.Warp exactly: source corners map to the portrait raster.
    target_points = np.array(
        [[out_width, 0], [out_width, out_height], [0, out_height], [0, 0]], dtype=np.float32
    )
    source_to_rectified = cv2.getPerspectiveTransform(source_points, target_points).astype(np.float64)
    rectified_to_source = np.linalg.inv(source_to_rectified)
    rectified = cv2.warpPerspective(source, source_to_rectified, (out_width, out_height), flags=cv2.INTER_LINEAR)

    balls = labels["balls"]
    source_centers = [(float(ball["xPx"]), float(ball["yPx"])) for ball in balls]
    rectified_centers = [project(source_to_rectified, point) for point in source_centers]
    radius = float(args.radius)
    directions = np.array(
        [(math.cos(2 * math.pi * i / 32), math.sin(2 * math.pi * i / 32)) for i in range(32)],
        dtype=np.float64,
    )
    # Treat labels as circle centres on the rectified plane. Exclude sectors whose
    # expected contour is geometrically covered by any other labelled ball.
    occluded: list[list[bool]] = []
    for i, center in enumerate(rectified_centers):
        row = []
        for direction in directions:
            edge = (center[0] + radius * direction[0], center[1] + radius * direction[1])
            covered = any(
                math.hypot(edge[0] - other[0], edge[1] - other[1]) <= radius + 1.5
                for j, other in enumerate(rectified_centers) if j != i
            )
            row.append(covered)
        occluded.append(row)

    raw_gray = cv2.cvtColor(source, cv2.COLOR_BGR2GRAY).astype(np.float32)
    rect_gray = cv2.cvtColor(rectified, cv2.COLOR_BGR2GRAY).astype(np.float32)
    # Sobel derivatives are normalized to approximate luma change per pixel.
    raw_gx = cv2.Sobel(raw_gray, cv2.CV_32F, 1, 0, ksize=3, scale=1 / 8)
    raw_gy = cv2.Sobel(raw_gray, cv2.CV_32F, 0, 1, ksize=3, scale=1 / 8)
    rect_gx = cv2.Sobel(rect_gray, cv2.CV_32F, 1, 0, ksize=3, scale=1 / 8)
    rect_gy = cv2.Sobel(rect_gray, cv2.CV_32F, 0, 1, ksize=3, scale=1 / 8)

    sector_rows: list[dict[str, Any]] = []
    ball_rows: list[dict[str, Any]] = []
    for index, (ball, rect_center) in enumerate(zip(balls, rectified_centers)):
        source_center = source_centers[index]
        local_jacobian = jacobian(rectified_to_source, rect_center)
        singular_values = np.linalg.svd(local_jacobian, compute_uv=False)
        raw_values, rect_values = [], []
        peak_offsets = []
        for sector, direction_array in enumerate(directions):
            direction = (float(direction_array[0]), float(direction_array[1]))
            is_occluded = occluded[index][sector]
            raw_peak, rect_peak, best_delta = 0.0, 0.0, 0
            if not is_occluded:
                for delta in range(-4, 5):
                    q = (rect_center[0] + (radius + delta) * direction[0],
                         rect_center[1] + (radius + delta) * direction[1])
                    p = project(rectified_to_source, q)
                    point_j = jacobian(rectified_to_source, q)
                    raw_response = projected_gradient(raw_gx, raw_gy, p, direction, point_j)
                    rect_response = projected_gradient(rect_gx, rect_gy, q, direction)
                    if math.isfinite(raw_response) and raw_response > raw_peak:
                        raw_peak, rect_peak, best_delta = raw_response, rect_response, delta
            else:
                raw_response = rect_response = float("nan")
            if not is_occluded and math.isfinite(raw_response):
                raw_values.append(raw_peak)
                rect_values.append(rect_peak)
                peak_offsets.append(best_delta)
            sector_rows.append({
                "ballId": ball["id"], "colour": ball["colour"], "sector": sector,
                "angleDegrees": round(sector * 360 / 32, 2), "geometricallyOccluded": is_occluded,
                "rawGradientPerRectifiedPixel": None if is_occluded else round(raw_peak, 3),
                "rectifiedGradientPerPixel": None if is_occluded else round(rect_peak, 3),
                "rawToRectifiedRatio": None if is_occluded or rect_peak < 0.25 else round(raw_peak / rect_peak, 3),
                "bestRadialOffsetPixels": None if is_occluded else best_delta,
            })
        def percentile(values: list[float], q: float) -> float | None:
            return None if not values else round(float(np.percentile(values, q)), 3)
        ratios = [a / b for a, b in zip(raw_values, rect_values) if b >= 0.25]
        ball_rows.append({
            "ballId": ball["id"], "colour": ball["colour"],
            "sourceCenterPx": [round(source_center[0], 2), round(source_center[1], 2)],
            "rectifiedCenterPx": [round(rect_center[0], 2), round(rect_center[1], 2)],
            "manualCenterUncertaintyPx": ball.get("uncertaintyPx"),
            "projectedSourceScalePerRectifiedPixelSingularValues": [round(float(s), 5) for s in singular_values],
            "visibleSectors": len(raw_values), "geometricallyOccludedSectors": sum(occluded[index]),
            "rawGradientMedian": percentile(raw_values, 50), "rawGradientP25": percentile(raw_values, 25),
            "rectifiedGradientMedian": percentile(rect_values, 50), "rectifiedGradientP25": percentile(rect_values, 25),
            "rawToRectifiedMedianRatio": percentile(ratios, 50),
            "bestRadialOffsetMedianPx": percentile(peak_offsets, 50),
        })

    visible_raw = [row["rawGradientPerRectifiedPixel"] for row in sector_rows if not row["geometricallyOccluded"]]
    visible_rect = [row["rectifiedGradientPerPixel"] for row in sector_rows if not row["geometricallyOccluded"]]
    ratio_rows = [row["rawToRectifiedRatio"] for row in sector_rows if row["rawToRectifiedRatio"] is not None]
    summary = {
        "schemaVersion": 1,
        "purpose": "exploratory image-signal comparison only; this is not ball detection or detector-accuracy evidence",
        "source": {
            "file": str(image_path).replace("\\", "/"), "sha256": source_hash,
            "width": width, "height": height, "timeline": indexed["scope"]["timeline"],
            "sourceTimeUs": indexed["sourceTimeUs"], "frameId": indexed["frameId"],
        },
        "labels": {
            "file": str(labels_path).replace("\\", "/"), "status": labels.get("status"),
            "method": labels.get("method"), "centresAreGroundTruth": False,
        },
        "calibration": {
            "file": str(metadata_path).replace("\\", "/"), "calibrationName": indexed["scope"]["calibration"],
            "sourceCornersNormalized": [[float(c["x"]), float(c["y"])] for c in corners],
            "homographyMatchesVisionAnalyzerWarp": True,
        },
        "comparison": {
            "rectifiedSize": [out_width, out_height], "interpolation": "OpenCV INTER_LINEAR",
            "rectifiedBallRadiusPx": radius, "sectorCount": 32,
            "radialSearchOffsetsPx": [-4, 4], "occludedSectorRule": "expected contour point within radius+1.5px of another approximate labelled centre",
            "visibleSectorCount": len(visible_raw), "excludedSectorCount": sum(sum(row) for row in occluded),
            "rawGradientMedian": round(float(np.median(visible_raw)), 3) if visible_raw else None,
            "rectifiedGradientMedian": round(float(np.median(visible_rect)), 3) if visible_rect else None,
            "rawToRectifiedRatioMedian": round(float(np.median(ratio_rows)), 3) if ratio_rows else None,
            "rawToRectifiedRatioP25": round(float(np.percentile(ratio_rows, 25)), 3) if ratio_rows else None,
            "rawToRectifiedRatioP75": round(float(np.percentile(ratio_rows, 75)), 3) if ratio_rows else None,
            "gradientDefinition": "absolute Sobel luma derivative along the expected radial normal, expressed per rectified pixel; peak selected within the shared +/-4px radius search",
        },
        "limitations": [
            "Only one rack frame and one lighting condition are measured."
            if not explicit_inputs else "Only one source frame and one lighting condition are measured.",
            "Manual centres are approximate draft labels with stated uncertainty, not reviewed ground-truth centres.",
            "The projected ball circles and occlusion mask inherit those centre and radius assumptions.",
            "Gradient support is an image-signal diagnostic, not candidate recall, classification, accepted detection, or Live Detect performance.",
            "Raw gradients are mapped through the local inverse-homography Jacobian; high-frequency noise and H.264 artifacts are not separated from useful ball-edge signal.",
        ],
        "perBall": ball_rows,
    }
    if explicit_inputs:
        summary["inputManifest"] = {
            "image": {"path": str(image_path).replace("\\", "/"), "sha256": source_hash},
            "receipt": {"path": str(metadata_path).replace("\\", "/"), "sha256": sha256(metadata_path)},
            "labels": {"path": str(labels_path).replace("\\", "/"), "sha256": sha256(labels_path)},
        }
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    with (args.output / "sectors.csv").open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(sector_rows[0]))
        writer.writeheader()
        writer.writerows(sector_rows)

    # Contact sheet: raw landscape frame beside the same rectified plane rotated
    # back to table landscape. Red points are geometrically occluded sectors.
    font = ImageFont.load_default()
    raw_panel_w = 960
    raw_panel_h = 540
    raw_img = Image.fromarray(cv2.cvtColor(source, cv2.COLOR_BGR2RGB)).resize((raw_panel_w, raw_panel_h), Image.Resampling.LANCZOS)
    warped_landscape = cv2.rotate(rectified, cv2.ROTATE_90_COUNTERCLOCKWISE)
    warp_content_h = int(round(raw_panel_w * warped_landscape.shape[0] / warped_landscape.shape[1]))
    warp_offset_y = (raw_panel_h - warp_content_h) / 2
    warp_img = Image.new("RGB", (raw_panel_w, raw_panel_h), (20, 20, 20))
    warp_resized = Image.fromarray(cv2.cvtColor(warped_landscape, cv2.COLOR_BGR2RGB)).resize(
        (raw_panel_w, warp_content_h), Image.Resampling.LANCZOS
    )
    warp_img.paste(warp_resized, (0, int(round(warp_offset_y))))
    scale_raw = raw_panel_w / width
    rectified_boundaries = [
        [(center[0] + radius * float(direction[0]), center[1] + radius * float(direction[1])) for direction in directions]
        for center in rectified_centers
    ]
    raw_boundaries = [[project(rectified_to_source, point) for point in boundary] for boundary in rectified_boundaries]
    raw_overlay = draw_panel(
        raw_img, balls, source_centers, raw_boundaries,
        lambda p: (p[0] * scale_raw, p[1] * scale_raw),
        "Decoded camera frame (raw perspective)", occluded, font,
    )
    scale_warp = raw_panel_w / warped_landscape.shape[1]
    def warp_to_panel(point: tuple[float, float]) -> tuple[float, float]:
        landscape = (point[1], args.width - point[0])
        return landscape[0] * scale_warp, landscape[1] * scale_warp + warp_offset_y
    warp_overlay = draw_panel(
        warp_img, balls, rectified_centers, rectified_boundaries,
        warp_to_panel,
        "Same frame after VisionAnalyzer-style rectification", occluded, font,
    )
    canvas = Image.new("RGB", (raw_panel_w * 2, raw_panel_h + 40), (25, 25, 25))
    canvas.paste(raw_overlay, (0, 40))
    canvas.paste(warp_overlay, (raw_panel_w, 40))
    header = ImageDraw.Draw(canvas)
    header.text((14, 10), "Green = unoccluded expected boundary sample; red = excluded by approximate neighboring-ball geometry", font=font, fill="white")
    canvas.save(args.output / "overlay.png", optimize=True)
    print(json.dumps({
        "summary": str(args.output / "summary.json"),
        "csv": str(args.output / "sectors.csv"),
        "overlay": str(args.output / "overlay.png"),
        "visibleSectors": summary["comparison"]["visibleSectorCount"],
        "excludedSectors": summary["comparison"]["excludedSectorCount"],
        "rawGradientMedian": summary["comparison"]["rawGradientMedian"],
        "rectifiedGradientMedian": summary["comparison"]["rectifiedGradientMedian"],
        "rawToRectifiedRatioMedian": summary["comparison"]["rawToRectifiedRatioMedian"],
    }, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
