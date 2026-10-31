#!/usr/bin/env python3
"""Compare packed red/yellow colour-peak seeds from raw and rectified pixels.

Raw mode thresholds decoded camera pixels first and nearest-neighbour warps
the masks. Rectified mode linearly warps BGR pixels first, then thresholds.
Both run the production packed-colour distance-transform peak stage and its
radius, edge and pocket seed gates on the canonical analysis grid. Boundary
fitting and final acceptance are intentionally outside this proposal probe.
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
MINIMUM_PEAK_RADIUS = 12.0
MAXIMUM_RADIUS = 35.0
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


def packed_mask(bgr: np.ndarray, kind: str) -> np.ndarray:
    hsv = cv2.cvtColor(bgr, cv2.COLOR_BGR2HSV)
    hue, saturation, value = cv2.split(hsv)
    if kind == "red":
        hue_match = (hue <= 10) | (hue >= 170)
    elif kind == "yellow":
        hue_match = (hue >= 10) & (hue <= 40)
    else:
        raise ValueError(kind)
    return np.where(hue_match & (saturation >= 100) & (value >= 30), 255, 0).astype(np.uint8)


def peak_seeds(mask: np.ndarray, kind: str) -> tuple[list[dict[str, Any]], dict[str, int]]:
    distance = cv2.distanceTransform(mask, cv2.DIST_L2, 3)
    neighbourhood = cv2.getStructuringElement(cv2.MORPH_RECT, (17, 17))
    dilated = cv2.dilate(distance, neighbourhood)
    peaks = ((distance == dilated) & (distance > MINIMUM_PEAK_RADIUS)).astype(np.uint8) * 255
    count, _, stats, centres = cv2.connectedComponentsWithStats(peaks, connectivity=8)
    stage_counts: dict[str, int] = {"peakComponents": max(0, count - 1)}
    seeds = []
    for component_id in range(1, count):
        x = float(centres[component_id, 0])
        y = float(centres[component_id, 1])
        xi = max(0, min(mask.shape[1] - 1, int(round(x))))
        yi = max(0, min(mask.shape[0] - 1, int(round(y))))
        radius = float(distance[yi, xi])
        row = {"kind": kind, "componentId": component_id, "rectifiedX": x, "rectifiedY": y,
               "interiorRadius": radius, "peakPixels": int(stats[component_id, cv2.CC_STAT_AREA])}
        reason = None
        if x < EDGE_MARGIN or y < EDGE_MARGIN or x >= WIDTH - EDGE_MARGIN or y >= HEIGHT - EDGE_MARGIN:
            reason = "rejected-margin"
        elif any(math.hypot(x - px, y - py) < POCKET_RADIUS for px, py in POCKETS):
            reason = "rejected-pocket-exclusion"
        elif radius > MAXIMUM_RADIUS:
            reason = "rejected-peak-radius"
        row["decision"] = reason or "seed"
        stage_counts[row["decision"]] = stage_counts.get(row["decision"], 0) + 1
        if reason is None:
            seeds.append(row)
    return seeds, stage_counts


def match_seeds(seeds: list[dict[str, Any]], labels: list[dict[str, Any]], kind: str) -> dict[str, Any]:
    relevant = [label for label in labels if label["colour"].lower() == kind]
    pairs = []
    for si, seed in enumerate(seeds):
        for li, label in enumerate(relevant):
            error = math.hypot(seed["rectifiedX"] - label["rectifiedX"],
                               seed["rectifiedY"] - label["rectifiedY"])
            if error <= MATCH_RADIUS:
                pairs.append((error, si, li))
    pairs.sort()
    used_seeds: set[int] = set()
    used_labels: set[int] = set()
    matches = []
    for error, si, li in pairs:
        if si in used_seeds or li in used_labels:
            continue
        used_seeds.add(si)
        used_labels.add(li)
        matches.append({"seedIndex": si, "labelId": relevant[li]["id"],
                        "labelColour": relevant[li]["colour"],
                        "centreOffsetRectifiedPx": round(error, 3),
                        "centreOffsetBallDiameters": round(error / 54.0, 4)})
    errors = [row["centreOffsetRectifiedPx"] for row in matches]
    return {"labelCount": len(relevant), "matchedCount": len(matches),
            "recall": round(len(matches) / len(relevant), 4) if relevant else None,
            "seedCount": len(seeds), "unmatchedSeedCount": len(seeds) - len(matches),
            "unmatchedLabelIds": sorted({row["id"] for row in relevant}
                                         - {row["labelId"] for row in matches}),
            "medianMatchedOffsetRectifiedPx": round(float(np.median(errors)), 3) if errors else None,
            "matches": matches}


def draw_overlay(image: np.ndarray, labels: list[dict[str, Any]], seeds: list[dict[str, Any]],
                 raw_space: bool, inverse: np.ndarray, output: Path) -> None:
    canvas = Image.fromarray(cv2.cvtColor(image, cv2.COLOR_BGR2RGB))
    draw = ImageDraw.Draw(canvas)
    mode = "raw masks -> canonical peaks" if raw_space else "rectified pixels -> canonical peaks"
    draw.text((10, 10), f"{mode}: cyan=draft centres; magenta=colour peak seeds",
              fill="white", stroke_width=2, stroke_fill="black")
    for label in labels:
        if label["colour"].lower() not in {"red", "yellow"}:
            continue
        x = float(label["xPx"] if raw_space else label["rectifiedX"])
        y = float(label["yPx"] if raw_space else label["rectifiedY"])
        draw.line((x - 8, y, x + 8, y), fill=(0, 255, 255), width=3)
        draw.line((x, y - 8, x, y + 8), fill=(0, 255, 255), width=3)
    for seed in seeds:
        cx, cy = float(seed["rectifiedX"]), float(seed["rectifiedY"])
        radius = max(2.0, float(seed["interiorRadius"]))
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
    csv_fields = ["case", "mode", "kind", "componentId", "rectifiedX", "rectifiedY",
                  "interiorRadius", "peakPixels", "decision"]
    with (args.output / "seeds.csv").open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=csv_fields)
        writer.writeheader()
        for name, image_arg, receipt_arg, labels_arg in args.case:
            case = hough_helpers.load_case(name, Path(image_arg), Path(receipt_arg), Path(labels_arg))
            raw_masks = {kind: packed_mask(case["image"], kind) for kind in ("red", "yellow")}
            raw_grid = {kind: cv2.warpPerspective(mask, case["sourceToRectified"], (WIDTH, HEIGHT),
                                                  flags=cv2.INTER_NEAREST)
                        for kind, mask in raw_masks.items()}
            rectified_masks = {kind: packed_mask(case["rectified"], kind) for kind in ("red", "yellow")}
            modes = {}
            for mode, masks, image, raw_space in (
                ("raw-mask-then-warp", raw_grid, case["image"], True),
                ("warp-bgr-then-mask", rectified_masks, case["rectified"], False),
            ):
                by_kind = {}
                all_seeds = []
                for kind in ("red", "yellow"):
                    seeds, counts = peak_seeds(masks[kind], kind)
                    score = match_seeds(seeds, case["labels"], kind)
                    by_kind[kind] = {"stageCounts": counts, **score, "seeds": seeds}
                    all_seeds.extend(seeds)
                    for seed in seeds:
                        writer.writerow({"case": name, "mode": mode, **seed})
                draw_overlay(image, case["labels"], all_seeds, raw_space,
                             np.linalg.inv(case["sourceToRectified"]),
                             args.output / name / f"{mode}.png")
                modes[mode] = {"byKind": by_kind,
                               "totalSeeds": len(all_seeds),
                               "matchedTotal": sum(row["matchedCount"] for row in by_kind.values())}
            reports.append({"case": name, "inputManifest": case["manifest"], "modes": modes})

    summary = {
        "schemaVersion": 1,
        "purpose": "offline raw-versus-rectified packed-colour peak proposal screen; not full detector or acceptance evidence",
        "opencvVersion": cv2.__version__,
        "proposalStageCopiedFrom": "PackedBallClusterRecovery.FindColourPeakBodies",
        "parameters": {"redHueRangesInclusive": [[0, 10], [170, 179]],
                       "yellowHueRangeInclusive": [10, 40], "minimumSaturation": 100,
                       "minimumValue": 30, "distanceTransform": "DIST_L2 maskSize=3",
                       "peakDilation": "17x17 rectangular", "minimumPeakRadius": MINIMUM_PEAK_RADIUS,
                       "maximumRadius": MAXIMUM_RADIUS, "edgeMargin": EDGE_MARGIN,
                       "pocketExclusionRadius": POCKET_RADIUS, "matchRadius": MATCH_RADIUS},
        "limitations": [
            "This compares colour masks and distance-transform seeds only; it does not run outer-boundary fits, colour classification, dense-window selection, deduplication, or uncertainty acceptance.",
            "Raw masks are thresholded from decoded camera pixels and nearest-neighbour warped before the production seed operation; this does not implement a native raw-coordinate detector.",
            "Labels are provisional visual estimates, and frames from different sessions are not a held-out matched set.",
            "A seed match is not a detected ball; the existing production fit and classification gates remain authoritative.",
        ],
        "cases": reports,
    }
    (args.output / "summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"output": str(args.output), "caseCount": len(reports),
                      "opencvVersion": cv2.__version__}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
