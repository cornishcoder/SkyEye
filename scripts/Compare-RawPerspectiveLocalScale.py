#!/usr/bin/env python3
"""Offline A/B of position-aware raw Hough ball-radius filtering.

The existing raw-perspective screen scales Hough parameters with one median
camera-to-table radius. This diagnostic filters its raw candidates using the
local projective scale at each candidate centre, then compares the filtered
proposals with the unfiltered raw set and the rectified Hough baseline.

It is exploratory only: it does not invoke production classification or
acceptance gates, and it does not change Live Detect behavior.
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

import numpy as np
from PIL import Image, ImageDraw, ImageFont


def load_probe_module():
    sys.dont_write_bytecode = True
    path = Path(__file__).with_name("Compare-RawPerspectiveHoughProposals.py")
    spec = importlib.util.spec_from_file_location("raw_perspective_hough_probe", path)
    if spec is None or spec.loader is None:
        raise RuntimeError(f"Could not load shared raw Hough probe: {path}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def local_projected_radius(module: Any, matrix: np.ndarray, x: float, y: float) -> tuple[float, float, float, float]:
    """Return geometric-mean radius, ellipse axes, and anisotropy in raw pixels."""
    forward = module.homography_jacobian(matrix, (x, y))
    inverse = np.linalg.inv(forward)
    axes = np.linalg.svd(inverse, compute_uv=False) * module.BALL_RADIUS_RECTIFIED
    major, minor = float(axes[0]), float(axes[1])
    return math.sqrt(major * minor), major, minor, major / minor if minor > 0 else float("inf")


def candidate_with_local_geometry(module: Any, case: dict[str, Any], candidate: dict[str, float]) -> dict[str, Any]:
    expected, major, minor, anisotropy = local_projected_radius(
        module, case["sourceToRectified"], candidate["x"], candidate["y"])
    row = dict(candidate)
    row.update({
        "expectedRawRadius": expected,
        "expectedRawMajorRadius": major,
        "expectedRawMinorRadius": minor,
        "localAnisotropy": anisotropy,
        "radiusRatio": candidate["radius"] / expected if expected > 0 else float("inf"),
    })
    return row


def within_tolerance(candidate: dict[str, Any], tolerance: float) -> bool:
    ratio = candidate["radiusRatio"]
    return (1.0 - tolerance) <= ratio <= (1.0 + tolerance)


def annotate_local_filter(path: Path, tolerance: float) -> None:
    image = Image.open(path).convert("RGB")
    draw = ImageDraw.Draw(image)
    try:
        font = ImageFont.truetype("arial.ttf", 18)
    except OSError:
        font = ImageFont.load_default()
    text = f"Local projected-radius filter: +/-{tolerance:.0%}"
    draw.text((12, 34), text, font=font, fill=(255, 255, 255), stroke_width=2, stroke_fill=(0, 0, 0))
    image.save(path)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--case", action="append", nargs=4,
                        metavar=("NAME", "IMAGE", "RECEIPT", "LABELS"), required=True,
                        help="Repeatable exact-frame input bundle")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--votes", default="9,12,15",
                        help="Rectified reference vote thresholds")
    parser.add_argument("--tolerances", default="0.20,0.35,0.50",
                        help="Relative local-radius tolerances to compare")
    parser.add_argument("--match-radius", type=float, default=27.0,
                        help="One-to-one centre matching radius in rectified pixels")
    args = parser.parse_args()

    module = load_probe_module()
    votes = [float(value.strip()) for value in args.votes.split(",") if value.strip()]
    tolerances = [float(value.strip()) for value in args.tolerances.split(",") if value.strip()]
    if not votes or any(value <= 0 for value in votes):
        parser.error("--votes must be comma-separated positive thresholds")
    if not tolerances or any(not 0 < value < 1 for value in tolerances):
        parser.error("--tolerances must contain values between 0 and 1")
    if args.match_radius <= 0:
        parser.error("--match-radius must be positive")

    args.output.mkdir(parents=True, exist_ok=True)
    reports = []
    candidate_path = args.output / "candidates.csv"
    columns = ["case", "votes", "candidateIndex", "rawX", "rawY", "rawRadius",
               "expectedRawRadius", "expectedRawMajorRadius", "expectedRawMinorRadius",
               "localAnisotropy", "radiusRatio", "rectifiedX", "rectifiedY"]
    with candidate_path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=columns)
        writer.writeheader()
        for name, image_arg, receipt_arg, labels_arg in args.case:
            case = module.load_case(name, Path(image_arg), Path(receipt_arg), Path(labels_arg))
            case_report: dict[str, Any] = {
                "case": name,
                "manifest": case["manifest"],
                "labelStatus": case["manifest"]["labels"]["status"],
                "rectifiedBaseline": [],
                "rawUnfiltered": [],
                "rawLocalRadiusFilters": [],
            }
            for vote in votes:
                raw_votes = vote * case["rawRadiusScale"]
                raw = module.candidates_for_threshold(
                    case["rawMask"], case["sourceToRectified"], "raw-perspective", vote,
                    case["rawRadiusScale"], case["rawRadiusRange"])
                raw = [candidate_with_local_geometry(module, case, candidate) for candidate in raw]
                rectified = module.candidates_for_threshold(
                    case["rectifiedMask"], np.eye(3, dtype=np.float64), "rectified", vote,
                    case["rawRadiusScale"], case["rawRadiusRange"])
                baseline_score = module.score(rectified, case["labels"], args.match_radius)
                raw_score = module.score(raw, case["labels"], args.match_radius)
                case_report["rectifiedBaseline"].append({"votes": vote, **baseline_score})
                case_report["rawUnfiltered"].append({
                    "votes": vote, "scaledRawVotes": raw_votes, **raw_score,
                    "medianLocalAnisotropy": round(float(np.median([r["localAnisotropy"] for r in raw])), 4) if raw else None,
                })
                for tolerance in tolerances:
                    filtered = [candidate for candidate in raw if within_tolerance(candidate, tolerance)]
                    filtered_score = module.score(filtered, case["labels"], args.match_radius)
                    case_report["rawLocalRadiusFilters"].append({
                        "votes": vote,
                        "relativeTolerance": tolerance,
                        "unfilteredCandidateCount": len(raw),
                        **filtered_score,
                    })
                for candidate in raw:
                    writer.writerow({
                        "case": name, "votes": vote,
                        "candidateIndex": candidate["candidateIndex"],
                        "rawX": candidate["x"], "rawY": candidate["y"],
                        "rawRadius": candidate["radius"],
                        "expectedRawRadius": round(candidate["expectedRawRadius"], 4),
                        "expectedRawMajorRadius": round(candidate["expectedRawMajorRadius"], 4),
                        "expectedRawMinorRadius": round(candidate["expectedRawMinorRadius"], 4),
                        "localAnisotropy": round(candidate["localAnisotropy"], 5),
                        "radiusRatio": round(candidate["radiusRatio"], 5),
                        "rectifiedX": candidate["rectifiedX"], "rectifiedY": candidate["rectifiedY"],
                    })

                if math.isclose(vote, 15.0, abs_tol=1e-6):
                    tolerance = min(tolerances, key=lambda value: abs(value - 0.35))
                    chosen = [candidate for candidate in raw if within_tolerance(candidate, tolerance)]
                    case_dir = args.output / name
                    module.draw_overlay(case["image"], case["labels"], chosen,
                                        "raw-perspective", case_dir / f"raw-local-scale-{tolerance:.2f}.png")
                    annotate_local_filter(case_dir / f"raw-local-scale-{tolerance:.2f}.png", tolerance)
                    module.draw_overlay(case["rectified"], case["labels"], rectified,
                                        "rectified", case_dir / "rectified-baseline.png")
            reports.append(case_report)

    summary = {
        "schemaVersion": 1,
        "purpose": "Diagnostic local projected-radius filter for raw-perspective Hough proposals.",
        "method": {
            "expectedRawRadius": "27 rectified pixels mapped locally through the inverse homography Jacobian; geometric-mean ellipse radius",
            "radiusFilter": "retain raw Hough proposals whose observed circle radius is within the requested relative tolerance of the local expected radius",
            "matching": "one-to-one geometry match in rectified coordinates; colour is not used",
            "limitations": [
                "The Hough candidate generator and cloth mask are approximate and do not invoke production detector stages.",
                "The local scale models a table-plane circle; ball-height parallax and non-circular raw projections are not corrected.",
                "A tolerance sweep on draft-labelled frames is exploratory, not held-out accuracy or a false-positive rate.",
                "Unmatched proposals are not confirmed false balls because labels are incomplete and the black centre is unresolved.",
                "This does not run the production classifier, boundary refiner, duplicate checks or uncertainty gates.",
            ],
        },
        "opencvVersion": module.cv2.__version__,
        "ballRadiusRectifiedPx": module.BALL_RADIUS_RECTIFIED,
        "votes": votes,
        "relativeRadiusTolerances": tolerances,
        "cases": reports,
    }
    (args.output / "summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"output": str(args.output), "caseCount": len(reports),
                      "candidateCsv": str(candidate_path)}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
