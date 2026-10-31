#!/usr/bin/env python3
"""Compare BallBoundaryRefiner support on the same fitted geometry in raw pixels.

This diagnostic reuses the exact regular/arc candidate centres and sector
geometry from the saved 23-pair Code70 evidence export. It resamples the same
canonical sector points from (a) the existing warped BGR raster and (b) the
decoded source frame via the inverse calibration homography. It does not
redetect or refit candidates, and cannot establish detector accuracy.
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


WIDTH = 1000
HEIGHT = 2000
MIN_BACKGROUND_SAMPLES = 24
EDGE_THRESHOLD = 16.0
EXTERIOR_THRESHOLD = 40.0


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def map_point(matrix: np.ndarray, point: tuple[float, float]) -> tuple[float, float]:
    x, y = point
    d = matrix[2, 0] * x + matrix[2, 1] * y + matrix[2, 2]
    if abs(d) < 1e-12:
        raise ValueError("Homography mapped a point to infinity")
    return ((matrix[0, 0] * x + matrix[0, 1] * y + matrix[0, 2]) / d,
            (matrix[1, 0] * x + matrix[1, 1] * y + matrix[1, 2]) / d)


def rounded_bgr(image: np.ndarray, point: tuple[float, float]) -> list[int] | None:
    height, width = image.shape[:2]
    x, y = int(round(point[0])), int(round(point[1]))
    if not (0 <= x < width and 0 <= y < height):
        return None
    return [int(channel) for channel in image[y, x]]


def make_frame(image_path: Path, frame_id: int, expected_sha: str) -> tuple[np.ndarray, np.ndarray, np.ndarray, dict[str, Any]]:
    receipt_path = image_path.parent / f"{frame_id}.json"
    if not image_path.is_file() or not receipt_path.is_file():
        raise FileNotFoundError(f"Missing source image or receipt: {image_path}, {receipt_path}")
    actual_sha = sha256(image_path)
    if actual_sha.lower() != expected_sha.lower():
        raise RuntimeError(f"Source evidence SHA does not match image: {image_path}")
    receipt = json.loads(receipt_path.read_text(encoding="utf-8-sig"))
    indexed = receipt.get("indexedFrame", receipt)
    receipt_sha = indexed.get("pixelSha256") or receipt.get("sha256")
    if receipt_sha is not None and str(receipt_sha).lower() != actual_sha.lower():
        raise RuntimeError(f"Frame receipt SHA does not match image: {receipt_path}")
    source = cv2.imread(str(image_path), cv2.IMREAD_COLOR)
    if source is None:
        raise RuntimeError(f"Could not decode {image_path}")
    height, width = source.shape[:2]
    corners = receipt.get("calibrationCorners")
    if corners is None:
        raise RuntimeError(f"Frame receipt has no calibration corners: {receipt_path}")
    source_points = np.array([[float(p["x"]) * width, float(p["y"]) * height]
                              for p in corners], dtype=np.float32)
    target_points = np.array([[WIDTH, 0], [WIDTH, HEIGHT], [0, HEIGHT], [0, 0]], dtype=np.float32)
    source_to_rect = cv2.getPerspectiveTransform(source_points, target_points).astype(np.float64)
    rect_to_source = np.linalg.inv(source_to_rect)
    rectified = cv2.warpPerspective(source, source_to_rect, (WIDTH, HEIGHT), flags=cv2.INTER_LINEAR)
    manifest = {"image": str(image_path), "imageSha256": actual_sha,
                "receipt": str(receipt_path), "receiptSha256": sha256(receipt_path),
                "frameId": indexed.get("frameId"),
                "sourceTimeUs": indexed.get("sourceTimeUs"),
                "timeline": indexed.get("scope", {}).get("timeline") or indexed.get("sourceTimeline"),
                "calibration": indexed.get("scope", {}).get("calibration")}
    return source, rectified, rect_to_source, manifest


def support_on_image(image: np.ndarray, mapping: np.ndarray | None,
                     fit: dict[str, Any], proposal: dict[str, float],
                     expected_radius: float) -> dict[str, Any]:
    """Mirror BallBoundaryRefiner.Inspect's generic black/cue support predicate."""
    def sample(point: list[float] | tuple[float, float]) -> list[int] | None:
        mapped = tuple(point) if mapping is None else map_point(mapping, (float(point[0]), float(point[1])))
        return rounded_bgr(image, mapped)

    background_points = []
    for index in range(32):
        angle = index * math.pi / 16
        point = (proposal["x"] + math.cos(angle) * expected_radius * 1.7,
                 proposal["y"] + math.sin(angle) * expected_radius * 1.7)
        pixel = sample(point)
        if pixel is not None:
            background_points.append(pixel)
    if len(background_points) < MIN_BACKGROUND_SAMPLES:
        return {"backgroundSamples": len(background_points), "backgroundValid": False,
                "supportedSectorIndices": [], "sectors": []}
    background = []
    for channel in range(3):
        values = sorted(pixel[channel] for pixel in background_points)
        background.append(values[len(values) // 2])  # matches C# upper median for an even count

    rows = []
    support_indices = []
    for sector in fit["sectors"]:
        inner = sample(sector["inner"])
        outer = sample(sector["outer"])
        fine_inner = sample(sector["fineInner"])
        fine_outer = sample(sector["fineOuter"])
        if any(pixel is None for pixel in (inner, outer, fine_inner, fine_outer)):
            rows.append({"index": sector["index"], "sampleInBounds": False, "supported": False})
            continue
        edge = sum(abs(inner[k] - outer[k]) for k in range(3)) / 3.0
        exterior = sum(abs(outer[k] - background[k]) for k in range(3)) / 3.0
        fine = sum(abs(fine_inner[k] - fine_outer[k]) for k in range(3)) / 3.0
        exterior_rejected = exterior > EXTERIOR_THRESHOLD
        supported = not exterior_rejected and edge >= EDGE_THRESHOLD
        if supported:
            support_indices.append(int(sector["index"]))
        rows.append({"index": int(sector["index"]), "sampleInBounds": True,
                     "edgeContrast": round(edge, 3), "exteriorContrast": round(exterior, 3),
                     "fineEdgeContrast": round(fine, 3), "exteriorRejected": exterior_rejected,
                     "supported": supported})
    return {"backgroundSamples": len(background_points), "backgroundValid": True,
            "backgroundBgr": background, "supportedSectorIndices": support_indices,
            "supportedCount": len(support_indices), "sectors": rows}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    default_evidence = Path("work/production/evidence/code70-daylight-pocket-capture-20261031/pocket-arc-holdout/regular-sector-trace/source-pixel-support/sector-pixel-evidence.json")
    parser.add_argument("--evidence", type=Path, default=default_evidence,
                        help="Saved exact-pair sector evidence JSON")
    parser.add_argument("--output", type=Path,
                        default=Path("docs/evidence/raw-perspective-boundary-support-20261031"))
    args = parser.parse_args()
    evidence = json.loads(args.evidence.read_text(encoding="utf-8-sig"))
    frame_cache: dict[tuple[str, int], tuple[np.ndarray, np.ndarray, np.ndarray, dict[str, Any]]] = {}
    reports = []
    csv_rows = []
    for pair_index, pair in enumerate(evidence["pairs"]):
        image_path = Path(pair["sourceImagePath"])
        key = (str(image_path), int(pair["frameId"]))
        if key not in frame_cache:
            frame_cache[key] = make_frame(image_path, key[1], pair["pixelSha256"])
        source, rectified, rect_to_source, frame_manifest = frame_cache[key]
        proposal = pair["seedCentre"]
        expected_radius = float(pair["expectedBallRadiusPx"])
        pair_report = {"pairIndex": pair_index, "case": pair["case"],
                       "caseLabel": pair["caseLabel"], "frameId": pair["frameId"],
                       "sourceTimeUs": pair["sourceTimeUs"], "pixelSha256": pair["pixelSha256"],
                       "candidateRank": pair["candidateRank"], "seedCentre": proposal,
                       "seedRadius": pair.get("seedRadius"), "expectedRadius": expected_radius,
                       "sourceManifest": frame_manifest, "fits": {}}
        for fit_name in ("regular", "arc"):
            fit = pair["fits"][fit_name]
            saved_rect_indices = sorted(int(i) for i in fit["supportedSectorIndices"])
            visible_indices = sorted(int(i) for i in fit.get("visibleSectorIndices", range(32)))
            rect_result = support_on_image(rectified, None, fit, proposal, expected_radius)
            raw_result = support_on_image(source, rect_to_source, fit, proposal, expected_radius)
            recomputed_rect = sorted(rect_result["supportedSectorIndices"])
            # Saved arc support is already restricted to visible sectors; regular fits use all sectors.
            recomputed_selected = sorted(set(recomputed_rect) & set(visible_indices))
            rect_match = recomputed_selected == saved_rect_indices
            raw_selected = sorted(set(raw_result["supportedSectorIndices"]) & set(visible_indices))
            fit_row = {"centre": fit["centre"], "radius": fit["radius"],
                       "savedRectifiedSupportIndices": saved_rect_indices,
                       "visibleSectorIndices": visible_indices,
                       "rectifiedSupportIndicesRecomputed": recomputed_rect,
                       "rectifiedMatchesSavedSupport": rect_match,
                       "rawSupportIndices": raw_result["supportedSectorIndices"],
                       "rawSupportIndicesOnVisibleArc": raw_selected,
                       "rawSupportCount": len(raw_result["supportedSectorIndices"]),
                       "rawVisibleSupportCount": len(raw_selected),
                       "rectifiedSupportCount": len(saved_rect_indices),
                       "rawMinusRectifiedVisibleSupport": len(raw_selected) - len(saved_rect_indices),
                       "addedRawVisibleSectors": sorted(set(raw_selected) - set(saved_rect_indices)),
                       "lostRawVisibleSectors": sorted(set(saved_rect_indices) - set(raw_selected)),
                       "rawBackgroundSamples": raw_result["backgroundSamples"],
                       "rawBackgroundValid": raw_result["backgroundValid"],
                       "rawBackgroundBgr": raw_result.get("backgroundBgr"),
                       "rawSectors": raw_result["sectors"]}
            pair_report["fits"][fit_name] = fit_row
            csv_rows.append({"pairIndex": pair_index, "case": pair["case"],
                             "caseLabel": pair["caseLabel"], "frameId": pair["frameId"],
                             "candidateRank": pair["candidateRank"], "fit": fit_name,
                             "centreX": fit["centre"]["x"], "centreY": fit["centre"]["y"],
                             "radius": fit["radius"], "savedRectifiedSupport": len(saved_rect_indices),
                             "rawSupport": len(raw_result["supportedSectorIndices"]),
                             "savedRectifiedVisibleSupport": len(saved_rect_indices),
                             "rawVisibleSupport": len(raw_selected),
                             "rawMinusRectifiedVisibleSupport": len(raw_selected) - len(saved_rect_indices),
                             "rectifiedMatchesSavedSupport": rect_match,
                             "rawBackgroundSamples": raw_result["backgroundSamples"],
                             "rawBackgroundValid": raw_result["backgroundValid"]})
        reports.append(pair_report)

    args.output.mkdir(parents=True, exist_ok=True)
    csv_path = args.output / "raw-perspective-boundary-support.csv"
    fields = list(csv_rows[0].keys()) if csv_rows else []
    with csv_path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=fields)
        writer.writeheader()
        writer.writerows(csv_rows)
    json_path = args.output / "raw-perspective-boundary-support.json"
    summary = {
        "schemaVersion": 1,
        "diagnosticOnly": True,
        "purpose": "Compare the production generic BallBoundaryRefiner support predicate on fixed saved fits using raw source pixels versus the existing warped raster.",
        "inputEvidence": str(args.evidence),
        "inputEvidenceSha256": sha256(args.evidence),
        "coordinateMapping": "Canonical sector samples are mapped into decoded source pixels through the inverse receipt homography; this creates a projective perimeter, not a raw-space refit.",
        "supportPredicate": {"minimumBackgroundSamples": MIN_BACKGROUND_SAMPLES,
                             "exteriorContrastRejectGreaterThan": EXTERIOR_THRESHOLD,
                             "edgeContrastAtLeast": EDGE_THRESHOLD,
                             "fineEdgeUsedForScoreOnly": True,
                             "matchesGenericBallBoundaryRefinerInspect": "expectedKind=null; no colour-mask or dark-edge override"},
        "pairCount": len(reports), "fitCount": len(csv_rows),
        "rectifiedParityAllFits": all(row["rectifiedMatchesSavedSupport"] for row in csv_rows),
        "rawBackgroundValidAllFits": all(row["rawBackgroundValid"] for row in csv_rows),
        "groupSummary": {},
        "limitations": [
            "Fit proposals and centres were selected from the warped analysis path; this is a fixed-fit pixel comparison, not an independent raw detector.",
            "Source points at the pocket are not black ground-truth centres; raw support gains do not establish correct localization.",
            "Positive and negative source sets are unmatched in lighting and capture session; per-fit false-positive rates cannot be inferred.",
            "No fitting search, classifier, candidate ranking, obstruction handling, uncertainty gate, table-state reducer, or event/clip path is run.",
            "The source is decoded camera video pixels, not sensor RAW.",
        ],
        "pairs": reports,
    }
    for case in sorted({row["case"] for row in csv_rows}):
        group = [row for row in csv_rows if row["case"] == case]
        by_fit = {}
        for fit_name in ("regular", "arc"):
            values = [row["rawMinusRectifiedVisibleSupport"] for row in group if row["fit"] == fit_name]
            by_fit[fit_name] = {"count": len(values), "rawBetter": sum(v > 0 for v in values),
                                "equal": sum(v == 0 for v in values), "rawWorse": sum(v < 0 for v in values),
                                "medianRawMinusRectified": float(np.median(values)) if values else None}
        summary["groupSummary"][case] = by_fit
    json_path.write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"output": str(args.output), "pairCount": len(reports),
                      "fitCount": len(csv_rows), "rectifiedParityAllFits": summary["rectifiedParityAllFits"],
                      "rawBackgroundValidAllFits": summary["rawBackgroundValidAllFits"],
                      "groupSummary": summary["groupSummary"]}, indent=2))
    return 0 if summary["rectifiedParityAllFits"] else 2


if __name__ == "__main__":
    raise SystemExit(main())
