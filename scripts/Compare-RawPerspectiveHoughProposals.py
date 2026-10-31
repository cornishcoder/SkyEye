#!/usr/bin/env python3
"""Compare a simple Hough proposal stage on raw-perspective and rectified frames.

This is an offline screening diagnostic. It does not call the production detector,
classify balls, or make table-state decisions. Both views use cloth-mask Hough
proposals; raw centres are projected through the saved calibration before scoring.
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


RECTIFIED_WIDTH = 1000
BALL_RADIUS_RECTIFIED = 27.0
POCKET_MASK_RADIUS_RECTIFIED = 48.0
HOUGH_DP = 1.3
HOUGH_EDGE_THRESHOLD = 150.0
HOUGH_SEPARATION_RECTIFIED = 45.0
RECTIFIED_RADIUS_RANGE = (18, 35)
HUE_BAND = 7
MORPHOLOGY_KERNEL = 5


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def transform_points(matrix: np.ndarray, points: list[tuple[float, float]]) -> list[tuple[float, float]]:
    values = np.asarray(points, dtype=np.float64).reshape(-1, 1, 2)
    mapped = cv2.perspectiveTransform(values, matrix).reshape(-1, 2)
    return [(float(x), float(y)) for x, y in mapped]


def homography_jacobian(matrix: np.ndarray, point: tuple[float, float]) -> np.ndarray:
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
        ], dtype=np.float64,
    )


def get_cloth_range(hsv: np.ndarray, sample_points: list[tuple[int, int]]) -> tuple[int, int, int]:
    histogram = np.zeros(60, dtype=np.int32)
    samples: list[tuple[int, int, int]] = []
    height, width = hsv.shape[:2]
    for x, y in sample_points:
        if not (0 <= x < width and 0 <= y < height):
            continue
        hue, saturation, value = (int(v) for v in hsv[y, x])
        if saturation > 20 and value > 35:
            histogram[hue // 3] += 1
            samples.append((hue, saturation, value))
    hue = int(np.argmax(histogram)) * 3 + 1
    cloth = [item for item in samples if min(abs(item[0] - hue), 180 - abs(item[0] - hue)) <= HUE_BAND]
    if len(cloth) < 20:
        return hue, 60, 60
    median_saturation = float(np.median([item[1] for item in cloth]))
    median_value = float(np.median([item[2] for item in cloth]))
    return (
        hue,
        int(np.clip(median_saturation * 0.45, 8, 60)),
        int(np.clip(median_value * 0.45, 15, 60)),
    )


def make_cloth_mask(
    image: np.ndarray,
    sample_points: list[tuple[int, int]],
    pocket_centres: list[tuple[float, float]],
    pocket_jacobians: list[np.ndarray] | None = None,
    morphology_size: int = MORPHOLOGY_KERNEL,
    roi_polygon: np.ndarray | None = None,
) -> tuple[np.ndarray, dict[str, int]]:
    hsv = cv2.cvtColor(image, cv2.COLOR_BGR2HSV)
    cloth_hue, saturation_floor, value_floor = get_cloth_range(hsv, sample_points)
    hue = hsv[:, :, 0].astype(np.int16)
    saturation = hsv[:, :, 1].astype(np.int16)
    value = hsv[:, :, 2].astype(np.int16)
    hue_delta = np.abs(hue - cloth_hue)
    hue_delta = np.minimum(hue_delta, 180 - hue_delta)
    saturation_ceiling = int(np.clip(saturation_floor / 0.45 * 1.8, 90, 255))
    normal_cloth = (
        (hue_delta <= HUE_BAND)
        & (saturation >= saturation_floor)
        & (saturation <= saturation_ceiling)
        & (value >= value_floor)
    )
    neutral_ceiling = int(np.clip(value_floor / 0.45 * 0.9, 45, 120))
    neutral_cloth = (saturation <= 45) & (value >= value_floor) & (value <= neutral_ceiling)
    mask = np.where(normal_cloth | neutral_cloth, 255, 0).astype(np.uint8)

    kernel_size = max(3, int(round(morphology_size)) | 1)
    kernel = cv2.getStructuringElement(cv2.MORPH_RECT, (kernel_size, kernel_size))
    mask = cv2.morphologyEx(mask, cv2.MORPH_CLOSE, kernel)
    mask = cv2.morphologyEx(mask, cv2.MORPH_OPEN, kernel)
    if roi_polygon is not None:
        roi = np.zeros_like(mask)
        cv2.fillConvexPoly(roi, roi_polygon.astype(np.int32), 255)
        cv2.bitwise_and(mask, roi, dst=mask)

    for index, centre in enumerate(pocket_centres):
        x, y = int(round(centre[0])), int(round(centre[1]))
        if pocket_jacobians is None:
            cv2.circle(mask, (x, y), int(round(POCKET_MASK_RADIUS_RECTIFIED)), 255, -1)
        else:
            left, singular_values, _ = np.linalg.svd(pocket_jacobians[index])
            axes = tuple(max(1, int(round(POCKET_MASK_RADIUS_RECTIFIED * value))) for value in singular_values)
            angle = math.degrees(math.atan2(left[1, 0], left[0, 0]))
            cv2.ellipse(mask, (x, y), axes, angle, 0, 360, 255, -1)
    return mask, {"clothHue": cloth_hue, "saturationFloor": saturation_floor, "valueFloor": value_floor,
                  "neutralCeiling": neutral_ceiling, "morphologyKernel": kernel_size}


def greedy_matches(
    candidates: list[dict[str, float]],
    labels: list[dict[str, Any]],
    threshold: float,
) -> list[dict[str, Any]]:
    pairs = []
    for candidate_index, candidate in enumerate(candidates):
        for label_index, label in enumerate(labels):
            dx = candidate["rectifiedX"] - label["rectifiedX"]
            dy = candidate["rectifiedY"] - label["rectifiedY"]
            error = math.hypot(dx, dy)
            if error <= threshold:
                pairs.append((error, candidate_index, label_index, dx, dy))
    pairs.sort()
    used_candidates: set[int] = set()
    used_labels: set[int] = set()
    matches = []
    for error, candidate_index, label_index, dx, dy in pairs:
        if candidate_index in used_candidates or label_index in used_labels:
            continue
        used_candidates.add(candidate_index)
        used_labels.add(label_index)
        matches.append({
            "candidateIndex": candidate_index,
            "labelId": labels[label_index]["id"],
            "labelColour": labels[label_index]["colour"],
            "centreErrorRectifiedPx": round(error, 3),
            "centreErrorBallDiameters": round(error / (BALL_RADIUS_RECTIFIED * 2), 4),
            "dxRectifiedPx": round(dx, 3),
            "dyRectifiedPx": round(dy, 3),
        })
    return matches


def candidates_for_threshold(
    mask: np.ndarray,
    matrix_to_rectified: np.ndarray,
    mode: str,
    threshold_rectified: float,
    raw_radius_scale: float,
    raw_radius_range: tuple[int, int],
) -> list[dict[str, float]]:
    height, width = mask.shape[:2]
    if mode == "raw-perspective":
        min_radius, max_radius = raw_radius_range
        min_distance = max(8.0, HOUGH_SEPARATION_RECTIFIED * raw_radius_scale)
        threshold = threshold_rectified * raw_radius_scale
    else:
        min_radius, max_radius = RECTIFIED_RADIUS_RANGE
        min_distance = HOUGH_SEPARATION_RECTIFIED
        threshold = threshold_rectified
    detected = cv2.HoughCircles(
        mask, cv2.HOUGH_GRADIENT, dp=HOUGH_DP, minDist=min_distance,
        param1=HOUGH_EDGE_THRESHOLD, param2=threshold,
        minRadius=min_radius, maxRadius=max_radius,
    )
    if detected is None:
        return []
    points = [(float(row[0]), float(row[1])) for row in detected[0]]
    projected = transform_points(matrix_to_rectified, points) if mode == "raw-perspective" else points
    results = []
    for index, (circle, rectified_centre) in enumerate(zip(detected[0], projected)):
        rx, ry = rectified_centre
        # Keep proposals whose projected centres lie on the calibrated table.
        if not (-BALL_RADIUS_RECTIFIED <= rx <= RECTIFIED_WIDTH + BALL_RADIUS_RECTIFIED
                and -BALL_RADIUS_RECTIFIED <= ry <= 2 * RECTIFIED_WIDTH + BALL_RADIUS_RECTIFIED):
            continue
        results.append({
            "candidateIndex": index, "x": round(float(circle[0]), 3), "y": round(float(circle[1]), 3),
            "radius": round(float(circle[2]), 3),
            "rectifiedX": round(rx, 3), "rectifiedY": round(ry, 3),
        })
    return results


def score(candidates: list[dict[str, float]], labels: list[dict[str, Any]], match_radius: float) -> dict[str, Any]:
    matches = greedy_matches(candidates, labels, match_radius)
    errors = [item["centreErrorRectifiedPx"] for item in matches]
    return {
        "candidateCount": len(candidates), "labelCount": len(labels), "matchedCount": len(matches),
        "recall": round(len(matches) / len(labels), 4) if labels else None,
        "unmatchedCandidateCount": len(candidates) - len(matches),
        "unmatchedLabelIds": sorted({label["id"] for label in labels} - {item["labelId"] for item in matches}),
        "medianMatchedCentreErrorRectifiedPx": round(float(np.median(errors)), 3) if errors else None,
        "maximumMatchedCentreErrorRectifiedPx": round(max(errors), 3) if errors else None,
        "matches": matches,
    }


def draw_overlay(
    image: np.ndarray,
    labels: list[dict[str, Any]],
    candidates: list[dict[str, float]],
    mode: str,
    output: Path,
) -> None:
    rgb = cv2.cvtColor(image, cv2.COLOR_BGR2RGB)
    canvas = Image.fromarray(rgb)
    draw = ImageDraw.Draw(canvas)
    try:
        font = ImageFont.truetype("arial.ttf", 18)
    except OSError:
        font = ImageFont.load_default()
    draw.text((12, 10), f"{mode}: cyan = draft labels; magenta = Hough proposals", font=font,
              fill=(255, 255, 255), stroke_width=2, stroke_fill=(0, 0, 0))
    if mode == "raw-perspective":
        label_points = [(float(row["xPx"]), float(row["yPx"])) for row in labels]
        candidate_points = [(row["x"], row["y"]) for row in candidates]
    else:
        label_points = [(float(row["rectifiedX"]), float(row["rectifiedY"])) for row in labels]
        candidate_points = [(row["rectifiedX"], row["rectifiedY"]) for row in candidates]
    for x, y in label_points:
        draw.line((x - 8, y, x + 8, y), fill=(0, 255, 255), width=3)
        draw.line((x, y - 8, x, y + 8), fill=(0, 255, 255), width=3)
    if mode == "raw-perspective":
        radii = [max(5, row["radius"]) for row in candidates]
    else:
        radii = [max(5, row["radius"]) for row in candidates]
    for (x, y), radius in zip(candidate_points, radii):
        draw.ellipse((x - radius, y - radius, x + radius, y + radius), outline=(255, 0, 255), width=3)
        draw.ellipse((x - 2, y - 2, x + 2, y + 2), fill=(255, 0, 255))
    output.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(output)


def load_case(name: str, image_path: Path, receipt_path: Path, labels_path: Path) -> dict[str, Any]:
    for path in (image_path, receipt_path, labels_path):
        if not path.is_file():
            raise FileNotFoundError(path)
    receipt = json.loads(receipt_path.read_text(encoding="utf-8-sig"))
    labels_document = json.loads(labels_path.read_text(encoding="utf-8-sig"))
    image = cv2.imread(str(image_path), cv2.IMREAD_COLOR)
    if image is None:
        raise RuntimeError(f"Could not read image: {image_path}")
    height, width = image.shape[:2]
    source_hash = sha256(image_path)
    indexed = receipt.get("indexedFrame", receipt)
    receipt_hash = indexed.get("pixelSha256") or receipt.get("sha256")
    label_hash = labels_document.get("source", {}).get("sha256")
    if source_hash.lower() != str(receipt_hash).lower():
        raise RuntimeError(f"{name}: image SHA-256 does not match the frame receipt")
    if source_hash.lower() != str(label_hash).lower():
        raise RuntimeError(f"{name}: image SHA-256 does not match the centre labels")
    label_source = labels_document["source"]
    if (label_source.get("width"), label_source.get("height")) != (width, height):
        raise RuntimeError(f"{name}: labelled dimensions differ from source pixels")

    corners = receipt.get("calibrationCorners")
    if corners is None:
        corners = [{"x": point[0], "y": point[1]} for point in labels_document["calibrationCornersNormalized"]]
    source_points = np.array([[point["x"] * width, point["y"] * height] for point in corners], dtype=np.float32)
    target_points = np.array([[RECTIFIED_WIDTH, 0], [RECTIFIED_WIDTH, RECTIFIED_WIDTH * 2],
                              [0, RECTIFIED_WIDTH * 2], [0, 0]], dtype=np.float32)
    source_to_rectified = cv2.getPerspectiveTransform(source_points, target_points).astype(np.float64)
    rectified_to_source = np.linalg.inv(source_to_rectified)
    rectified = cv2.warpPerspective(image, source_to_rectified,
                                    (RECTIFIED_WIDTH, RECTIFIED_WIDTH * 2), flags=cv2.INTER_LINEAR)

    balls = labels_document["balls"]
    label_source_points = [(float(ball.get("xPx", ball.get("x"))),
                            float(ball.get("yPx", ball.get("y")))) for ball in balls]
    label_rectified_points = transform_points(source_to_rectified, label_source_points)
    label_rows = []
    for ball, source_point, rect_point in zip(balls, label_source_points, label_rectified_points):
        label_rows.append({
            "id": str(ball.get("id", f"ball-{len(label_rows)+1}")),
            "colour": str(ball.get("colour", "unknown")),
            "xPx": source_point[0], "yPx": source_point[1],
            "rectifiedX": rect_point[0], "rectifiedY": rect_point[1],
            "uncertaintyPx": ball.get("uncertaintyPx"),
        })

    raw_sample_points = []
    rect_height = RECTIFIED_WIDTH * 2
    rect_samples = [(x, y) for y in range(rect_height // 10, rect_height * 9 // 10, 10)
                    for x in range(RECTIFIED_WIDTH // 10, RECTIFIED_WIDTH * 9 // 10, 10)]
    for x, y in transform_points(rectified_to_source, [(float(x), float(y)) for x, y in rect_samples]):
        raw_sample_points.append((int(round(x)), int(round(y))))
    rect_sample_points = [(x, y) for y in range(rect_height // 10, rect_height * 9 // 10, 10)
                          for x in range(RECTIFIED_WIDTH // 10, RECTIFIED_WIDTH * 9 // 10, 10)]

    rectified_pockets = [(0, 0), (RECTIFIED_WIDTH, 0), (0, RECTIFIED_WIDTH),
                         (RECTIFIED_WIDTH, RECTIFIED_WIDTH), (0, rect_height), (RECTIFIED_WIDTH, rect_height)]
    raw_pockets = transform_points(rectified_to_source,
                                   [(float(x), float(y)) for x, y in rectified_pockets])
    raw_pocket_jacobians = [homography_jacobian(rectified_to_source, point)
                            for point in rectified_pockets]
    singular = []
    for y in np.linspace(50, rect_height - 50, 15):
        for x in np.linspace(50, RECTIFIED_WIDTH - 50, 9):
            values = np.linalg.svd(homography_jacobian(rectified_to_source, (float(x), float(y))),
                                   compute_uv=False)
            singular.extend(float(value) for value in values)
    raw_radius_values = [BALL_RADIUS_RECTIFIED * value for value in singular]
    raw_radius_range = (max(5, int(math.floor(min(raw_radius_values) * 0.85))),
                        max(6, int(math.ceil(max(raw_radius_values) * 1.15))))
    raw_radius_scale = float(np.median(raw_radius_values)) / BALL_RADIUS_RECTIFIED
    if raw_radius_range[0] >= raw_radius_range[1]:
        raise RuntimeError(f"{name}: invalid projected radius range {raw_radius_range}")

    raw_mask, raw_profile = make_cloth_mask(image, raw_sample_points, raw_pockets,
                                            raw_pocket_jacobians,
                                            max(3, int(round(MORPHOLOGY_KERNEL * raw_radius_scale)) | 1),
                                            source_points)
    rect_pocket_jacobians = [np.eye(2, dtype=np.float64) for _ in rectified_pockets]
    rect_mask, rect_profile = make_cloth_mask(rectified, rect_sample_points, rectified_pockets,
                                              rect_pocket_jacobians, MORPHOLOGY_KERNEL)
    return {
        "name": name, "image": image, "rectified": rectified, "rawMask": raw_mask,
        "rectifiedMask": rect_mask, "sourceToRectified": source_to_rectified,
        "labels": label_rows, "rawRadiusRange": raw_radius_range,
        "rawRadiusScale": raw_radius_scale,
        "profiles": {"raw": raw_profile, "rectified": rect_profile},
        "manifest": {
            "image": {"path": str(image_path), "sha256": source_hash},
            "receipt": {"path": str(receipt_path), "sha256": sha256(receipt_path)},
            "labels": {"path": str(labels_path), "sha256": sha256(labels_path),
                       "status": labels_document.get("status", "unspecified")},
            "frameId": indexed.get("frameId"),
            "timeline": indexed.get("scope", {}).get("timeline") or indexed.get("sourceTimeline"),
            "sourceTimeUs": indexed.get("sourceTimeUs"),
        },
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--case", action="append", nargs=4, metavar=("NAME", "IMAGE", "RECEIPT", "LABELS"),
                        required=True, help="Repeatable exact-frame input bundle")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--votes", default="9,12,15,18,21,24",
                        help="Rectified Hough centre thresholds; raw thresholds are scaled by local projected radius")
    parser.add_argument("--match-radius", type=float, default=BALL_RADIUS_RECTIFIED,
                        help="One-to-one centre matching radius in rectified pixels")
    args = parser.parse_args()
    thresholds = [float(value.strip()) for value in args.votes.split(",") if value.strip()]
    if not thresholds or any(value <= 0 for value in thresholds):
        parser.error("--votes must be a comma-separated list of positive thresholds")
    if args.match_radius <= 0:
        parser.error("--match-radius must be positive")

    args.output.mkdir(parents=True, exist_ok=True)
    reports = []
    with (args.output / "candidates.csv").open("w", newline="", encoding="utf-8") as csv_file:
        writer = csv.DictWriter(csv_file, fieldnames=["case", "mode", "rectifiedVotes", "scaledVotes",
            "candidateIndex", "x", "y", "radius", "rectifiedX", "rectifiedY"])
        writer.writeheader()
        for name, image_arg, receipt_arg, labels_arg in args.case:
            case = load_case(name, Path(image_arg), Path(receipt_arg), Path(labels_arg))
            mode_summaries = {}
            selected_overlays: dict[str, tuple[np.ndarray, list[dict[str, Any]]]] = {}
            preferred_threshold = min(thresholds, key=lambda value: abs(value - 15.0))
            for mode, image, mask, matrix in [
                ("raw-perspective", case["image"], case["rawMask"], case["sourceToRectified"]),
                ("rectified", case["rectified"], case["rectifiedMask"], np.eye(3, dtype=np.float64)),
            ]:
                threshold_rows = []
                for rectified_votes in thresholds:
                    raw_votes = rectified_votes * case["rawRadiusScale"] if mode == "raw-perspective" else rectified_votes
                    candidates = candidates_for_threshold(mask, matrix, mode, rectified_votes,
                                                          case["rawRadiusScale"], case["rawRadiusRange"])
                    metrics = score(candidates, case["labels"], args.match_radius)
                    threshold_rows.append({
                        "rectifiedReferenceVotes": rectified_votes,
                        "actualHoughThreshold": round(raw_votes, 3),
                        "parameters": {
                            "dp": HOUGH_DP,
                            "minDistance": round(HOUGH_SEPARATION_RECTIFIED * case["rawRadiusScale"]
                                                  if mode == "raw-perspective" else HOUGH_SEPARATION_RECTIFIED, 3),
                            "minRadius": case["rawRadiusRange"][0] if mode == "raw-perspective" else RECTIFIED_RADIUS_RANGE[0],
                            "maxRadius": case["rawRadiusRange"][1] if mode == "raw-perspective" else RECTIFIED_RADIUS_RANGE[1],
                        },
                        **metrics,
                        "candidates": candidates,
                    })
                    for candidate in candidates:
                        writer.writerow({"case": name, "mode": mode, "rectifiedVotes": rectified_votes,
                            "scaledVotes": round(raw_votes, 3), **candidate})
                    if rectified_votes == preferred_threshold:
                        selected_overlays[mode] = (image, candidates)
                mode_summaries[mode] = threshold_rows
            for mode, (image, candidates) in selected_overlays.items():
                draw_overlay(image, case["labels"], candidates, mode,
                             args.output / name / f"{mode}-votes-{preferred_threshold:g}.png")
            reports.append({
                "case": name, "inputManifest": case["manifest"],
                "labelCount": len(case["labels"]), "labels": case["labels"],
                "labelMatchingRadiusRectifiedPx": args.match_radius,
                "estimatedRawCircleRadiusRangePx": list(case["rawRadiusRange"]),
                "medianRawRadiusScaleRelativeToRectified": round(case["rawRadiusScale"], 5),
                "clothProfiles": case["profiles"],
                "thresholdSweep": mode_summaries,
            })

    summary = {
        "schemaVersion": 1,
        "purpose": "diagnostic Hough proposal comparison only; not the production detector or acceptance evidence",
        "method": {
            "candidateGenerator": "OpenCV HOUGH_GRADIENT on a cloth mask approximating VisionAnalyzer.ClothMask",
            "rawRadiusModel": "global min/max envelope from calibration inverse-homography Jacobian sampled over the table, with 15% margin",
            "rawPocketMask": "rectified circular pocket masks projected into raw perspective as local ellipses",
            "rawCentreMapping": "saved source-to-rectified homography",
            "matching": "greedy one-to-one geometry-only centre match within the configured rectified-pixel radius",
            "limitations": [
                "This comparator runs only a Hough proposal stage; it does not recreate the production colour-body, packed-cluster, dark-body, classifier, boundary-refiner or uncertainty stages.",
                "Raw Hough uses one global radius envelope and median-scaled separation; it does not model a position-varying ellipse per proposal.",
                "Labels are provisional visual estimates, and frame 1365 has no defensible black centre.",
                "Threshold sweeps include the labelled frames and are diagnostic; do not interpret the best threshold as held-out performance.",
                "A gain here would justify a native or better-calibrated proposal A/B, not a production detector change.",
            ],
        },
        "opencvVersion": cv2.__version__,
        "ballRadiusRectifiedPx": BALL_RADIUS_RECTIFIED,
        "rectifiedHoughDefaults": {"dp": HOUGH_DP, "edgeThreshold": HOUGH_EDGE_THRESHOLD,
                                   "separation": HOUGH_SEPARATION_RECTIFIED,
                                   "minimumRadius": RECTIFIED_RADIUS_RANGE[0],
                                   "maximumRadius": RECTIFIED_RADIUS_RANGE[1]},
        "cases": reports,
    }
    (args.output / "summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"output": str(args.output), "caseCount": len(reports),
                      "opencvVersion": cv2.__version__}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
