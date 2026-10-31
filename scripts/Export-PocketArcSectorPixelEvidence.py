#!/usr/bin/env python3
"""Export pixel-addressable boundary-sector evidence for exact matched fits.

Reads saved regular/arc replay reports and source frames only. It recreates the
1000x2000 calibrated perspective warp, validates the source PNG hashes and
frame receipts, then emits one row per model sector with warped coordinates,
sampled BGR values, contrast and support. Evaluation labels are copied as
references and never used to select candidates.

Requires Python packages cv2, numpy and Pillow.
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
from PIL import Image, ImageDraw


DEFAULT_EVIDENCE = Path(
    "work/production/evidence/code70-daylight-pocket-capture-20261031/pocket-arc-holdout"
)
TRACE_DIR = Path("regular-sector-trace")
OUT_DIR = Path("regular-sector-trace/source-pixel-support")
WIDTH = 1000
HEIGHT = 2000
DIRECTIONS = [(math.cos(i * math.pi / 16), math.sin(i * math.pi / 16)) for i in range(32)]


def read_json(path: Path) -> dict[str, Any]:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def json_default(value: Any) -> Any:
    if isinstance(value, np.generic):
        return value.item()
    raise TypeError(f"Cannot serialize {type(value).__name__}")


def sample_index(report: dict[str, Any]) -> dict[str, dict[str, Any]]:
    return {str(sample["frameId"]): sample for sample in report["samples"]}


def index_by_rank(rows: list[dict[str, Any]], key: str = "candidateRank") -> dict[str, dict[str, Any]]:
    return {str(row[key]): row for row in rows}


def source_label(sample: dict[str, Any]) -> dict[str, Any] | None:
    return sample.get("width1000", {}).get("darkMaskTrace", {}).get("manualBlackLabel")


def source_frame_paths(evidence: Path, frame: str, expected_hash: str) -> tuple[Path, Path]:
    # Positive code70 frames and empty-pocket controls are stored in adjacent
    # exact replay input bundles. Accept only a receipt that matches this row's
    # exact source hash; no live or regenerated image is considered.
    candidates = [
        evidence.parent / "exact-replay-input",
        evidence.parents[1] / "code65-source-frame-check-20261031" / "exact-replay-input",
    ]
    for directory in candidates:
        image_path, receipt_path = directory / f"{frame}.png", directory / f"{frame}.json"
        if image_path.is_file() and receipt_path.is_file():
            receipt = read_json(receipt_path)
            actual = hashlib.sha256(image_path.read_bytes()).hexdigest()
            if actual.lower() == expected_hash.lower() and receipt["indexedFrame"]["pixelSha256"].lower() == expected_hash.lower():
                return image_path, receipt_path
    raise FileNotFoundError(f"No saved exact source frame/receipt matches frame {frame} hash {expected_hash}")


def expected_background(warped: np.ndarray, seed: tuple[float, float], expected_radius: float) -> np.ndarray:
    xy = []
    for dx, dy in DIRECTIONS:
        x, y = round(seed[0] + dx * expected_radius * 1.7), round(seed[1] + dy * expected_radius * 1.7)
        if 0 <= x < WIDTH and 0 <= y < HEIGHT:
            xy.append((x, y))
    if len(xy) < 24:
        raise ValueError("Cannot reproduce generic black-sector background: fewer than 24 in-frame samples")
    # Match BallBoundaryRefiner: channel-wise sorted median, upper middle for even counts.
    return np.array([sorted(int(warped[y, x, c]) for x, y in xy)[len(xy) // 2] for c in range(3)], dtype=np.float64)


def sample_sector(warped: np.ndarray, proposal: tuple[float, float], expected_radius: float,
                  centre: tuple[float, float], radius: float, index: int) -> dict[str, Any]:
    dx, dy = DIRECTIONS[index]
    offset = max(2.0, expected_radius * .12)
    coords = {
        "inner": (centre[0] + dx * (radius - offset), centre[1] + dy * (radius - offset)),
        "outer": (centre[0] + dx * (radius + offset), centre[1] + dy * (radius + offset)),
        "fineInner": (centre[0] + dx * (radius - 1), centre[1] + dy * (radius - 1)),
        "fineOuter": (centre[0] + dx * (radius + 1), centre[1] + dy * (radius + 1)),
    }
    pixels: dict[str, list[int]] = {}
    for name, (x, y) in coords.items():
        ix, iy = round(x), round(y)
        if not (0 <= ix < WIDTH and 0 <= iy < HEIGHT):
            raise ValueError(f"Sector sample out of bounds at {name} ({x:.4f},{y:.4f})")
        pixels[name] = [int(v) for v in warped[iy, ix]]
    background = expected_background(warped, proposal, expected_radius)
    edge = sum(abs(a - b) for a, b in zip(pixels["inner"], pixels["outer"])) / 3.0
    exterior = sum(abs(a - b) for a, b in zip(pixels["outer"], background)) / 3.0
    fine_edge = sum(abs(a - b) for a, b in zip(pixels["fineInner"], pixels["fineOuter"])) / 3.0
    # Both traces request the generic black-boundary support rule. No color or
    # dark-transition override is enabled for the reported sector sets.
    exterior_rejected = exterior > 40.0
    supported = not exterior_rejected and edge >= 16.0
    return {
        "index": index,
        "angleDegrees": index * 11.25,
        "inner": coords["inner"], "outer": coords["outer"],
        "fineInner": coords["fineInner"], "fineOuter": coords["fineOuter"],
        "innerPixel": pixels["inner"], "outerPixel": pixels["outer"],
        "fineInnerPixel": pixels["fineInner"], "fineOuterPixel": pixels["fineOuter"],
        "backgroundPixel": [int(v) for v in background],
        "edgeContrast": edge, "exteriorContrast": exterior, "fineEdgeContrast": fine_edge,
        "exteriorRejected": exterior_rejected, "supported": supported,
    }


def warp_source(image_path: Path, receipt: dict[str, Any]) -> np.ndarray:
    digest = hashlib.sha256(image_path.read_bytes()).hexdigest()
    expected_hash = receipt["indexedFrame"]["pixelSha256"]
    if digest.lower() != expected_hash.lower():
        raise ValueError(f"Source SHA-256 mismatch for {image_path}: {digest} != {expected_hash}")
    image = cv2.imread(str(image_path), cv2.IMREAD_COLOR)
    if image is None:
        raise ValueError(f"Could not decode {image_path}")
    source_h, source_w = image.shape[:2]
    if (source_w, source_h) != (receipt["indexedFrame"]["width"], receipt["indexedFrame"]["height"]):
        raise ValueError(f"Frame shape differs from receipt: {image_path}")
    corners = receipt["calibrationCorners"]
    src = np.float32([[c["x"] * source_w, c["y"] * source_h] for c in corners])
    dst = np.float32([[WIDTH, 0], [WIDTH, HEIGHT], [0, HEIGHT], [0, 0]])
    transform = cv2.getPerspectiveTransform(src, dst)
    return cv2.warpPerspective(image, transform, (WIDTH, HEIGHT))


def get_rank_rows(sample: dict[str, Any], field: str) -> dict[str, dict[str, Any]]:
    rows = sample["width1000"]["darkMaskTrace"][field]
    return index_by_rank(rows)


def matched_pairs(evidence: Path) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    cases = []
    for case in ("positive", "negative"):
        comparison_path = evidence / TRACE_DIR / f"{case}-comparison.json"
        comparison = read_json(comparison_path)
        regular_report_path = Path(comparison["regularReportPath"])
        arc_report_path = Path(comparison["pocketArcReportPath"])
        if not regular_report_path.exists() or not arc_report_path.exists():
            raise FileNotFoundError(f"Comparison report path missing: {regular_report_path} / {arc_report_path}")
        regular_samples = sample_index(read_json(regular_report_path))
        arc_samples = sample_index(read_json(arc_report_path))
        csv_path = evidence / TRACE_DIR / f"{case}-comparison.csv"
        with csv_path.open(newline="", encoding="utf-8-sig") as stream:
            rows = list(csv.DictReader(stream))
        for row in rows:
            if row["RegularFitAvailable"] != "True" or row["PocketArcFitAvailable"] != "True":
                continue
            frame = row["FrameId"]
            rank = row["CandidateRank"]
            regular_sample = regular_samples[frame]
            arc_sample = arc_samples[frame]
            if regular_sample["pixelSha256"] != arc_sample["pixelSha256"]:
                raise ValueError(f"Report source hash mismatch for {case} frame {frame}")
            regular_rows = [a for a in regular_sample["width1000"]["analysis"]["candidateAudits"]
                            if a.get("stage") == "sparse-black-add" and a.get("pass") == "initial"]
            regular_candidates = index_by_rank(regular_rows, "index")
            arc_candidates = get_rank_rows(arc_sample, "rankedSparseBlackPocketArcProbes")
            regular_candidate, arc_candidate = regular_candidates[rank], arc_candidates[rank]
            if regular_candidate["proposalCentre"] != arc_candidate["seedCentre"] or abs(regular_candidate["proposalRadius"] - arc_candidate["seedRadius"]) > 1e-6:
                raise ValueError(f"Ranked seed mismatch at {case} frame {frame} rank {rank}")
            if not regular_candidate.get("fittedCentre"):
                raise ValueError(f"Regular fit missing at {case} frame {frame} rank {rank}")
            if not arc_candidate.get("arcFitAvailable") or not arc_candidate.get("arcFitCentre"):
                raise ValueError(f"Arc fit missing at {case} frame {frame} rank {rank}")
            image_path, receipt_path = source_frame_paths(evidence, frame, row["PixelSha256"])
            receipt = read_json(receipt_path)
            if receipt["indexedFrame"]["pixelSha256"] != row["PixelSha256"]:
                raise ValueError(f"Receipt hash differs from comparison at frame {frame}")
            if receipt["indexedFrame"]["sourceTimeUs"] != int(row["SourceTimeUs"]):
                raise ValueError(f"Receipt source timestamp differs at frame {frame}")
            cases.append({
                "case": case, "caseLabel": row["CaseLabel"], "frameId": int(frame),
                "sourceTimeUs": int(row["SourceTimeUs"]), "pixelSha256": row["PixelSha256"],
                "candidateRank": int(rank), "expectedBallRadiusPx": float(row["ExpectedBallRadiusPxForScalingOnly"]),
                "seedCentre": regular_candidate["proposalCentre"], "seedRadius": float(regular_candidate["proposalRadius"]),
                "regularFit": {"centre": regular_candidate["fittedCentre"], "radius": float(regular_candidate["fittedRadius"]),
                               "supportFraction": float(regular_candidate["boundarySupport"]),
                               "supportedSectorIndices": regular_candidate.get("supportedSectorIndices", [])},
                "arcFit": {"centre": arc_candidate["arcFitCentre"], "radius": float(arc_candidate["arcFitRadius"]),
                           "supportedSectorIndices": arc_candidate.get("supportedVisibleSectorIndices", []),
                           "visibleSectorIndices": arc_candidate.get("visibleSectorIndices", []),
                           "diagnosticChecksPass": bool(arc_candidate["diagnosticArcChecksPass"])},
                "centreDriftPx": float(row["CenterDriftPx"]),
                "evaluationReferences": {
                    "regularReport": source_label(regular_sample),
                    "pocketArcReport": source_label(arc_sample),
                    "status": "regular reference invalid for black localization; arc reference is only an approximate interior point, not a centre label",
                    "labelDistanceClaimsUsingRegularReference": "void",
                    "use": "review-only; not used to select pairs, candidates, or sectors",
                },
                "receipt": receipt,
                "sourceImagePath": str(image_path),
            })
    if len([x for x in cases if x["case"] == "positive"]) != 20 or len([x for x in cases if x["case"] == "negative"]) != 3:
        raise ValueError(f"Expected 20 positive and 3 negative matched pairs, found {len(cases)}")
    return cases, {"positive": 20, "negative": 3}


def emit_pair_evidence(evidence: Path, output: Path, pairs: list[dict[str, Any]]) -> tuple[list[dict[str, Any]], list[dict[str, Any]]]:
    flat: list[dict[str, Any]] = []
    enriched: list[dict[str, Any]] = []
    image_cache: dict[str, np.ndarray] = {}
    for pair in pairs:
        frame = str(pair["frameId"])
        if frame not in image_cache:
            image_cache[frame] = warp_source(Path(pair["sourceImagePath"]), pair["receipt"])
        warped = image_cache[frame]
        pair_out = {k: v for k, v in pair.items() if k != "receipt"}
        pair_out["fits"] = {}
        seed = (float(pair["seedCentre"]["x"]), float(pair["seedCentre"]["y"]))
        expected = pair["expectedBallRadiusPx"]
        for model in ("regular", "arc"):
            fit = pair[model + "Fit"]
            centre = (float(fit["centre"]["x"]), float(fit["centre"]["y"]))
            radius = fit["radius"]
            reported = set(int(x) for x in fit["supportedSectorIndices"])
            visible = set(int(x) for x in fit.get("visibleSectorIndices", range(32)))
            sectors = [sample_sector(warped, seed, expected, centre, radius, i) for i in range(32)]
            recomputed = {s["index"] for s in sectors if s["supported"] and s["index"] in visible}
            if recomputed != reported:
                missing = sorted(reported - recomputed)
                extra = sorted(recomputed - reported)
                raise ValueError(f"Recomputed pixel support mismatch {pair['case']} frame {frame} rank {pair['candidateRank']} {model}: missing={missing} extra={extra}")
            fit_detail = {k: v for k, v in fit.items() if k != "supportedSectorIndices"}
            fit_detail["centre"] = {"x": centre[0], "y": centre[1]}
            fit_detail["supportedSectorIndices"] = sorted(reported)
            fit_detail["visibleSectorIndices"] = sorted(visible)
            fit_detail["sectors"] = sectors
            pair_out["fits"][model] = fit_detail
            for sector in sectors:
                flat.append({
                    "case": pair["case"], "caseLabel": pair["caseLabel"], "frameId": frame,
                    "sourceTimeUs": pair["sourceTimeUs"], "pixelSha256": pair["pixelSha256"],
                    "candidateRank": pair["candidateRank"], "model": model,
                    "seedX": seed[0], "seedY": seed[1], "seedRadius": pair["seedRadius"],
                    "fitX": centre[0], "fitY": centre[1], "fitRadius": radius,
                    "centreDriftPx": pair["centreDriftPx"], "sectorIndex": sector["index"],
                    "angleDegrees": sector["angleDegrees"],
                    "innerX": sector["inner"][0], "innerY": sector["inner"][1],
                    "outerX": sector["outer"][0], "outerY": sector["outer"][1],
                    "fineInnerX": sector["fineInner"][0], "fineInnerY": sector["fineInner"][1],
                    "fineOuterX": sector["fineOuter"][0], "fineOuterY": sector["fineOuter"][1],
                    "innerBgr": ",".join(map(str, sector["innerPixel"])),
                    "outerBgr": ",".join(map(str, sector["outerPixel"])),
                    "fineInnerBgr": ",".join(map(str, sector["fineInnerPixel"])),
                    "fineOuterBgr": ",".join(map(str, sector["fineOuterPixel"])),
                    "backgroundBgr": ",".join(map(str, sector["backgroundPixel"])),
                    "edgeContrast": sector["edgeContrast"], "exteriorContrast": sector["exteriorContrast"],
                    "fineEdgeContrast": sector["fineEdgeContrast"],
                    "exteriorRejected": sector["exteriorRejected"],
                    "supportReported": sector["index"] in reported,
                    "visible": sector["index"] in visible,
                    "rawEdgeSupportRecomputed": sector["supported"],
                    "supportRecomputed": sector["supported"] and sector["index"] in visible,
                    "supportSource": "source-warped BGR pixels; exact generic BallBoundaryRefiner.Inspect rule",
                    "regularLabelSourcePixel": json.dumps((pair["evaluationReferences"]["regularReport"] or {}).get("sourcePixel"), separators=(",", ":")),
                    "regularLabelWarpedPixel": json.dumps((pair["evaluationReferences"]["regularReport"] or {}).get("analysisPixel"), separators=(",", ":")),
                    "arcLabelSourcePixel": json.dumps((pair["evaluationReferences"]["pocketArcReport"] or {}).get("sourcePixel"), separators=(",", ":")),
                    "arcLabelWarpedPixel": json.dumps((pair["evaluationReferences"]["pocketArcReport"] or {}).get("analysisPixel"), separators=(",", ":")),
                })
        enriched.append(pair_out)
    return flat, enriched


def contact_sheet(evidence: Path, output_path: Path, pairs: list[dict[str, Any]]) -> None:
    # Selections use fit geometry / duplicates only, never label proximity.
    sorted_positive = sorted((p for p in pairs if p["case"] == "positive"), key=lambda p: p["centreDriftPx"], reverse=True)
    sorted_negative = sorted((p for p in pairs if p["case"] == "negative"), key=lambda p: p["centreDriftPx"], reverse=True)
    pos_dup = next(p for p in pairs if p["frameId"] == 1365 and p["candidateRank"] == 2)
    neg_largest = sorted_negative[0]
    # Include one exact duplicate group from the negative holdout. These are arc
    # candidates without a regular fit, so the card documents their duplicate geometry only.
    duplicate = {"frameId": 978, "rankA": 23, "rankB": 29}
    panels: list[tuple[str, int, list[tuple[str, dict[str, Any]]], bool]] = [
        (f"Positive matched pair: frame {sorted_positive[0]['frameId']} rank {sorted_positive[0]['candidateRank']} (largest centre drift)", sorted_positive[0]["frameId"], [("regular", sorted_positive[0]["regularFit"]), ("arc", sorted_positive[0]["arcFit"])], False),
        (f"Positive duplicate neighbourhood: frame 1365 rank 2 plus arc duplicate rank 1", 1365, [("regular", pos_dup["regularFit"]), ("arc rank 2", pos_dup["arcFit"])], True),
        (f"Negative matched pair: frame {neg_largest['frameId']} rank {neg_largest['candidateRank']} (largest centre drift)", neg_largest["frameId"], [("regular", neg_largest["regularFit"]), ("arc", neg_largest["arcFit"])], False),
        ("Negative duplicate hypotheses: frame 978 ranks 23 and 29 (same arc centre)", 978, [("arc rank 23", None), ("arc rank 29", None)], True),
    ]
    cards = []
    for title, frame, fits, duplicate_card in panels:
        expected_hash = next((p["pixelSha256"] for p in pairs if p["frameId"] == frame), None)
        if expected_hash is None:
            # Non-matched duplicate-only frame: reuse the verified source hash
            # from its exact arc report sample.
            neg_report = read_json(Path(read_json(evidence / TRACE_DIR / "negative-comparison.json")["pocketArcReportPath"]))
            expected_hash = sample_index(neg_report)[str(frame)]["pixelSha256"]
        image_path, receipt_path = source_frame_paths(evidence, str(frame), expected_hash)
        receipt = read_json(receipt_path)
        warped = warp_source(image_path, receipt)
        centers = []
        for _, fit in fits:
            if fit is not None:
                centers.append((float(fit["centre"]["x"]), float(fit["centre"]["y"]), float(fit["radius"])))
        if duplicate_card and frame == 978:
            arc_sample = sample_index(read_json(Path(read_json(evidence / TRACE_DIR / "negative-comparison.json")["pocketArcReportPath"])))
            candidates = get_rank_rows(arc_sample[str(frame)], "rankedSparseBlackPocketArcProbes")
            for rank in (23, 29):
                c = candidates[str(rank)]
                centers.append((c["arcFitCentre"]["x"], c["arcFitCentre"]["y"], c["arcFitRadius"]))
        if not centers:
            raise ValueError("Contact sheet card has no fit geometry")
        margin = 75
        left = max(0, int(min(x-r for x, y, r in centers) - margin))
        right = min(WIDTH, int(max(x+r for x, y, r in centers) + margin))
        top = max(0, int(min(y-r for x, y, r in centers) - margin))
        bottom = min(HEIGHT, int(max(y+r for x, y, r in centers) + margin))
        crop = warped[top:bottom, left:right]
        rgb = cv2.cvtColor(crop, cv2.COLOR_BGR2RGB)
        card = Image.fromarray(rgb).resize((600, 614), Image.Resampling.BILINEAR)
        draw = ImageDraw.Draw(card)
        sx, sy = 600/(right-left), 614/(bottom-top)
        def xy(point: tuple[float, float]) -> tuple[int, int]:
            return (int((point[0]-left)*sx), int((point[1]-top)*sy))
        colors = {"regular": (30, 220, 255), "arc": (255, 145, 20), "arc rank 2": (255, 145, 20), "arc rank 23": (255, 145, 20), "arc rank 29": (220, 60, 255)}
        if duplicate_card and frame == 1365:
            arc_sample = sample_index(read_json(Path(read_json(evidence / TRACE_DIR / "positive-comparison.json")["pocketArcReportPath"])))
            c = get_rank_rows(arc_sample[str(frame)], "rankedSparseBlackPocketArcProbes")["1"]
            fits = fits + [("arc duplicate rank 1", {"centre": c["arcFitCentre"], "radius": c["arcFitRadius"], "supportedSectorIndices": c["supportedVisibleSectorIndices"]})]
        if duplicate_card and frame == 978:
            arc_sample = sample_index(read_json(Path(read_json(evidence / TRACE_DIR / "negative-comparison.json")["pocketArcReportPath"])))
            candidates = get_rank_rows(arc_sample[str(frame)], "rankedSparseBlackPocketArcProbes")
            fits = [(name, {"centre": candidates[str(rank)]["arcFitCentre"], "radius": candidates[str(rank)]["arcFitRadius"], "supportedSectorIndices": candidates[str(rank)]["supportedVisibleSectorIndices"]}) for name, rank in (("arc rank 23",23),("arc rank 29",29))]
        legend_lines = []
        for name, fit in fits:
            if fit is None: continue
            center = (float(fit["centre"]["x"]), float(fit["centre"]["y"]))
            radius = float(fit["radius"])
            color = colors.get(name, (255,145,20))
            legend_lines.append((name, center, radius, color))
            cx, cy = xy(center); rp = max(2, int(radius*sx))
            draw.ellipse((cx-rp, cy-rp, cx+rp, cy+rp), outline=color, width=2)
            draw.line((cx-5,cy,cx+5,cy), fill=color, width=2); draw.line((cx,cy-5,cx,cy+5), fill=color, width=2)
            supported = set(int(i) for i in fit.get("supportedSectorIndices", []))
            for i,(dx,dy) in enumerate(DIRECTIONS):
                # Display the actual radial evidence segment locations, scaled to this crop.
                inner = (center[0]+dx*(radius-3.24), center[1]+dy*(radius-3.24))
                outer = (center[0]+dx*(radius+3.24), center[1]+dy*(radius+3.24))
                a,b = xy(inner),xy(outer)
                sector_color = (60,235,90) if i in supported else (245,70,70)
                draw.line((a[0],a[1],b[0],b[1]), fill=sector_color, width=2)
                if i % 8 == 0:
                    draw.text((b[0]+2,b[1]-8), str(i), fill=(255,255,255), stroke_width=1, stroke_fill=(0,0,0))
        header = Image.new("RGB", (600, 78), (25,25,25)); header_draw = ImageDraw.Draw(header)
        header_draw.text((8,5), title, fill=(255,255,255))
        for i,(name,center,radius,color) in enumerate(legend_lines[:3]):
            header_draw.text((8,25+i*16), f"{name}: centre=({center[0]:.1f},{center[1]:.1f}), r={radius:.1f}", fill=color)
        combined = Image.new("RGB", (600, 692), (20,20,20)); combined.paste(header,(0,0)); combined.paste(card,(0,78))
        cards.append(combined)
    sheet = Image.new("RGB", (1200, 1434), (30,30,30))
    for i, card in enumerate(cards): sheet.paste(card, ((i%2)*600, (i//2)*692))
    footer = ImageDraw.Draw(sheet)
    footer.text((10, 1392), "Eval refs only: regular (1282,536) is empty cloth/invalid; arc (102,903) is approximate interior, not centre. No label-distance accuracy claim.", fill=(255,255,255))
    sheet.save(output_path)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--evidence-directory", type=Path, default=DEFAULT_EVIDENCE)
    parser.add_argument("--output-directory", type=Path)
    args = parser.parse_args()
    evidence = args.evidence_directory.resolve()
    output = args.output_directory.resolve() if args.output_directory else (evidence / OUT_DIR).resolve()
    output.mkdir(parents=True, exist_ok=True)
    pairs, expected_counts = matched_pairs(evidence)
    flat, enriched = emit_pair_evidence(evidence, output, pairs)
    csv_path = output / "sector-pixel-evidence.csv"
    with csv_path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(flat[0]))
        writer.writeheader(); writer.writerows(flat)
    json_path = output / "sector-pixel-evidence.json"
    json_path.write_text(json.dumps({
        "schemaVersion": 1,
        "tool": "Export-PocketArcSectorPixelEvidence",
        "diagnosticOnly": True,
        "coordinateSystem": {"width": WIDTH, "height": HEIGHT, "source": "calibrated perspective warp from exact receipt corners", "sectorZeroDegrees": "right", "positiveAngle": "clockwise in image coordinates"},
        "selection": "Only exact matched pairs already present in regular-sector-trace comparison CSVs; labels are copied as review references and never used for selection.",
        "sourceValidation": "Every source PNG SHA-256, receipt dimensions, report hash, source timestamp, and ranked seed coordinates/radius were checked before export.",
        "supportMethod": "Pixel evidence recomputed from source-warped BGR using BallBoundaryRefiner.Inspect's generic expectedKind=null support predicate; asserted equal to recorded regular and arc sector indices.",
        "pairCounts": expected_counts,
        "sampleRowCount": len(flat),
        "pairs": enriched,
        "limitations": ["The regular O08 evaluation reference at source pixel (1282,536) lands on empty cloth in the exact source frames and is invalid for black localization; all label-distance or accuracy claims using that point are void.", "The pocket-arc reference at source pixel (102,903) lies in the lower-left pocket ball cluster but is only an approximate interior point, not a centre label; do not infer centre error or accuracy from it.", "Evaluation references are not proposal inputs and must not be used to select or tune candidates.", "Arc support is visible-sector support; regular support is generic fit support. Pixel coordinates expose the actual sampled points, but model visibility rules remain distinct."],
    }, indent=2, default=json_default), encoding="utf-8")
    contact_path = output / "sector-pixel-contact-sheet.png"
    contact_sheet(evidence, contact_path, pairs)
    print(f"pairs={len(enriched)} rows={len(flat)} csv={csv_path} json={json_path} contact={contact_path}")


if __name__ == "__main__":
    main()
