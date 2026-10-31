#!/usr/bin/env python3
"""Compare SkyEye's colour-body proposal mask before and after perspective warp.

The raw branch thresholds decoded camera pixels first, then nearest-neighbour
warps that binary mask to the canonical analysis grid. The baseline warps BGR
pixels first and applies the same HSV mask afterward. Both then use the current
AddColourBodies component size, shape, edge and pocket gates. This is an offline
proposal diagnostic, not the complete detector or table-state acceptance path.
"""

from __future__ import annotations

import argparse
import csv
import importlib.util
import json
import math
from pathlib import Path
import sys
from typing import Any

import cv2
import numpy as np
from PIL import Image, ImageDraw


WIDTH = 1000
HEIGHT = 2000
MIN_RADIUS = 18.0
MAX_RADIUS = 35.0
EDGE_MARGIN = 23.0
POCKET_RADIUS = 48.0
MATCH_RADIUS = 27.0
POCKETS = [(0.0, 0.0), (WIDTH, 0.0), (0.0, WIDTH), (WIDTH, WIDTH),
           (0.0, HEIGHT), (WIDTH, HEIGHT)]


def load_hough_helpers():
    path = Path(__file__).with_name("Compare-RawPerspectiveHoughProposals.py")
    spec = importlib.util.spec_from_file_location("skyeye_raw_hough_helpers", path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Could not load shared frame/label helpers from {path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def production_colour_body_mask(bgr: np.ndarray) -> np.ndarray:
    hsv = cv2.cvtColor(bgr, cv2.COLOR_BGR2HSV)
    hue, saturation, value = cv2.split(hsv)
    mask = (saturation >= 160) & (value >= 40)
    # VisionAnalyzer.AddColourBodies removes green/blue hues from this broad mask.
    mask &= ~((hue > 37) & (hue < 150))
    return np.where(mask, 255, 0).astype(np.uint8)


def component_proposals(mask: np.ndarray) -> tuple[list[dict[str, Any]], dict[str, int]]:
    count, _, stats, centres = cv2.connectedComponentsWithStats(mask, connectivity=8)
    decisions: dict[str, int] = {"components": max(0, count - 1)}
    proposals: list[dict[str, Any]] = []
    for component_id in range(1, count):
        x = int(stats[component_id, cv2.CC_STAT_LEFT])
        y = int(stats[component_id, cv2.CC_STAT_TOP])
        width = int(stats[component_id, cv2.CC_STAT_WIDTH])
        height = int(stats[component_id, cv2.CC_STAT_HEIGHT])
        area = int(stats[component_id, cv2.CC_STAT_AREA])
        radius = (width + height) / 4.0
        aspect = width / height if height else math.inf
        fill = area / (width * height) if width and height else 0.0
        row = {
            "componentId": component_id, "rectifiedX": float(centres[component_id, 0]),
            "rectifiedY": float(centres[component_id, 1]), "radius": radius,
            "bboxX": x, "bboxY": y, "bboxWidth": width, "bboxHeight": height,
            "pixelArea": area, "aspect": aspect, "fill": fill,
        }
        reason = None
        if radius < MIN_RADIUS or radius > MAX_RADIUS:
            reason = "rejected-radius"
        elif aspect < 0.6 or aspect > 1.6:
            reason = "rejected-aspect"
        elif fill < 0.55:
            reason = "rejected-fill"
        elif (row["rectifiedX"] < EDGE_MARGIN or row["rectifiedY"] < EDGE_MARGIN
              or row["rectifiedX"] > WIDTH - EDGE_MARGIN
              or row["rectifiedY"] > HEIGHT - EDGE_MARGIN):
            reason = "rejected-margin"
        elif any(math.hypot(row["rectifiedX"] - px, row["rectifiedY"] - py) < POCKET_RADIUS
                 for px, py in POCKETS):
            reason = "rejected-pocket-exclusion"
        row["decision"] = reason or "proposal"
        decisions[row["decision"]] = decisions.get(row["decision"], 0) + 1
        if reason is None:
            proposals.append(row)
    return proposals, decisions


def one_to_one_score(candidates: list[dict[str, Any]], labels: list[dict[str, Any]]) -> dict[str, Any]:
    relevant = [label for label in labels if label["colour"].lower() in {"red", "yellow"}]
    pairs = []
    for ci, candidate in enumerate(candidates):
        for li, label in enumerate(relevant):
            error = math.hypot(candidate["rectifiedX"] - label["rectifiedX"],
                               candidate["rectifiedY"] - label["rectifiedY"])
            if error <= MATCH_RADIUS:
                pairs.append((error, ci, li))
    pairs.sort()
    used_candidates: set[int] = set()
    used_labels: set[int] = set()
    matches = []
    for error, ci, li in pairs:
        if ci in used_candidates or li in used_labels:
            continue
        used_candidates.add(ci)
        used_labels.add(li)
        matches.append({"candidateIndex": ci, "labelId": relevant[li]["id"],
                        "labelColour": relevant[li]["colour"],
                        "centreErrorRectifiedPx": round(error, 3),
                        "centreErrorBallDiameters": round(error / 54.0, 4)})
    errors = [match["centreErrorRectifiedPx"] for match in matches]
    return {
        "labelCount": len(relevant), "matchedCount": len(matches),
        "recall": round(len(matches) / len(relevant), 4) if relevant else None,
        "candidateCount": len(candidates), "unmatchedCandidateCount": len(candidates) - len(matches),
        "unmatchedLabelIds": sorted({label["id"] for label in relevant}
                                     - {match["labelId"] for match in matches}),
        "medianMatchedCentreErrorRectifiedPx": round(float(np.median(errors)), 3) if errors else None,
        "matches": matches,
    }


def draw_overlay(image: np.ndarray, labels: list[dict[str, Any]], candidates: list[dict[str, Any]],
                 mode: str, inverse: np.ndarray, output: Path) -> None:
    canvas = Image.fromarray(cv2.cvtColor(image, cv2.COLOR_BGR2RGB))
    draw = ImageDraw.Draw(canvas)
    draw.text((10, 10), f"{mode}: cyan=draft centres; magenta=colour-body proposals", fill="white",
              stroke_width=2, stroke_fill="black")
    raw_space = mode == "raw-mask-then-warp"
    for label in labels:
        if label["colour"].lower() not in {"red", "yellow"}:
            continue
        x = float(label["xPx"] if raw_space else label["rectifiedX"])
        y = float(label["yPx"] if raw_space else label["rectifiedY"])
        draw.line((x - 8, y, x + 8, y), fill=(0, 255, 255), width=3)
        draw.line((x, y - 8, x, y + 8), fill=(0, 255, 255), width=3)
    for candidate in candidates:
        cx, cy = float(candidate["rectifiedX"]), float(candidate["rectifiedY"])
        radius = max(3.0, float(candidate["radius"]))
        if raw_space:
            rect_points = [(cx + radius * math.cos(i * math.tau / 48),
                            cy + radius * math.sin(i * math.tau / 48)) for i in range(48)]
            boundary = cv2.perspectiveTransform(np.asarray(rect_points, dtype=np.float32).reshape(-1, 1, 2),
                                                inverse).reshape(-1, 2)
            poly = [(float(x), float(y)) for x, y in boundary]
            draw.line(poly + [poly[0]], fill=(255, 0, 255), width=3)
            x, y = hough_helpers.transform_points(inverse, [(cx, cy)])[0]
        else:
            x, y = cx, cy
            draw.ellipse((x - radius, y - radius, x + radius, y + radius),
                         outline=(255, 0, 255), width=3)
        draw.ellipse((x - 2, y - 2, x + 2, y + 2), fill=(255, 0, 255))
    output.parent.mkdir(parents=True, exist_ok=True)
    canvas.save(output)


sys.dont_write_bytecode = True
hough_helpers = load_hough_helpers()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--case", action="append", nargs=4,
                        metavar=("NAME", "IMAGE", "RECEIPT", "LABELS"), required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    reports = []
    csv_fields = ["case", "mode", "componentId", "rectifiedX", "rectifiedY", "radius",
                  "bboxX", "bboxY", "bboxWidth", "bboxHeight", "pixelArea", "aspect", "fill", "decision"]
    with (args.output / "candidates.csv").open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=csv_fields)
        writer.writeheader()
        for name, image_arg, receipt_arg, labels_arg in args.case:
            case = hough_helpers.load_case(name, Path(image_arg), Path(receipt_arg), Path(labels_arg))
            raw_mask = production_colour_body_mask(case["image"])
            raw_mask_rectified = cv2.warpPerspective(raw_mask, case["sourceToRectified"], (WIDTH, HEIGHT),
                                                       flags=cv2.INTER_NEAREST)
            rectified_mask = production_colour_body_mask(case["rectified"])
            mode_results = {}
            for mode, mask, image, inverse in (
                ("raw-mask-then-warp", raw_mask_rectified, case["image"],
                 np.linalg.inv(case["sourceToRectified"])),
                ("warp-bgr-then-mask", rectified_mask, case["rectified"], np.eye(3, dtype=np.float64)),
            ):
                candidates, stage_counts = component_proposals(mask)
                metrics = one_to_one_score(candidates, case["labels"])
                for candidate in candidates:
                    writer.writerow({"case": name, "mode": mode, **candidate})
                draw_overlay(image, case["labels"], candidates, mode, inverse,
                             args.output / name / f"{mode}.png")
                mode_results[mode] = {"stageCounts": stage_counts, **metrics,
                                      "candidates": candidates}
            reports.append({"case": name, "inputManifest": case["manifest"],
                            "proposalStage": "VisionAnalyzer.AddColourBodies HSV connected components",
                            "rawMaskWarp": "threshold source BGR-decoded pixels, then nearest-neighbour warp mask",
                            "rectifiedMask": "linear BGR warp, then threshold decoded HSV pixels",
                            "modes": mode_results})

    summary = {
        "schemaVersion": 1,
        "purpose": "offline raw-versus-rectified colour-body proposal ablation; not full detector or acceptance evidence",
        "opencvVersion": cv2.__version__,
        "productionSettingsCopiedFrom": "VisionSettings defaults and VisionAnalyzer.AddColourBodies",
        "parameters": {"hueKept": "0..37 and 150..179", "minimumSaturation": 160,
                       "minimumValue": 40, "minimumRadius": MIN_RADIUS,
                       "maximumRadius": MAX_RADIUS, "aspectRange": [0.6, 1.6],
                       "minimumFill": 0.55, "edgeMargin": EDGE_MARGIN,
                       "pocketExclusionRadius": POCKET_RADIUS, "matchRadius": MATCH_RADIUS},
        "limitations": [
            "The raw branch thresholds raw decoded RGB-derived HSV, then maps the binary mask with nearest-neighbour interpolation; this is a proposal-source ablation, not a complete raw-coordinate detector.",
            "Both branches apply component geometry and fixed pocket/edge gates on the canonical 1000x2000 grid; no Hough, packed-cluster splitting, black/cue recovery, classification, duplicate-against-other-stage, boundary-refiner or uncertainty logic is run.",
            "Labels are provisional visual estimates and all cases must be reviewed before scoring; they are not held-out gold labels.",
            "Per-frame thresholding and greedy centre matching are diagnostic only; use session-separated reviewed holdouts before any production decision.",
        ],
        "cases": reports,
    }
    (args.output / "summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"output": str(args.output), "caseCount": len(reports),
                      "opencvVersion": cv2.__version__}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
