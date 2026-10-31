#!/usr/bin/env python3
"""Offline audit of pocket-arc candidates against already accepted balls.

This is diagnostic bookkeeping only. It reads existing replay CSV/proof outputs,
hash-checks their exact source frames, and evaluates hypothetical geometric
vetoes. It does not run or modify the detector.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
from collections import Counter, defaultdict
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
DEFAULT_POSITIVE_CSV = ROOT / "work/production/evidence/code70-daylight-pocket-capture-20261031/pocket-arc-holdout/seed75fullarc/positive-probes.csv"
DEFAULT_NEGATIVE_CSV = ROOT / "work/production/evidence/code70-daylight-pocket-capture-20261031/pocket-arc-holdout/seed75fullarc/negative-probes.csv"
DEFAULT_POSITIVE_FRAMES = ROOT / "work/production/evidence/code70-daylight-pocket-capture-20261031/exact-replay-input"
DEFAULT_NEGATIVE_FRAMES = ROOT / "work/production/evidence/code65-source-frame-check-20261031/exact-replay-input"
DEFAULT_OUTPUT = ROOT / "docs/evidence/pocket-arc-neighbour-overlap-audit-20261031"
EXPECTED_RADIUS_PX = 27.0
THRESHOLD_MULTIPLES = (1.5, 1.75, 1.85, 2.0)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def truth(value: str | None) -> bool:
    return (value or "").strip().lower() == "true"


def number(value: Any, *, default: float | None = None) -> float | None:
    if value is None or value == "":
        return default
    parsed = float(value)
    if not math.isfinite(parsed):
        raise ValueError(f"Non-finite numeric value: {value!r}")
    return parsed


def read_csv(path: Path) -> list[dict[str, str]]:
    with path.open("r", encoding="utf-8-sig", newline="") as stream:
        return list(csv.DictReader(stream))


def load_proof(path_text: str, cache: dict[Path, dict[str, Any]]) -> dict[str, Any]:
    path = Path(path_text)
    if not path.is_absolute():
        path = ROOT / path
    path = path.resolve()
    if path not in cache:
        cache[path] = json.loads(path.read_text(encoding="utf-8"))
    return cache[path]


def get_candidate_probe(sample: dict[str, Any], rank: int) -> dict[str, Any]:
    probes = sample["width1000"]["darkMaskTrace"]["rankedSparseBlackPocketArcProbes"]
    matches = [probe for probe in probes if int(probe["candidateRank"]) == rank]
    if len(matches) != 1:
        raise ValueError(f"Expected one probe rank {rank}, found {len(matches)}")
    return matches[0]


def accepted_circles(sample: dict[str, Any]) -> list[dict[str, Any]]:
    # `analysis.circles` stores normalized table coordinates. Convert back to
    # the width-1000 portrait grid used by the probe: x=1000*(1-tableY),
    # y=1000*tableX. These are final accepted detections, not proposals.
    circles = sample["width1000"]["analysis"]["circles"]
    result = []
    for circle in circles:
        x = 1000.0 * (1.0 - float(circle["y"]))
        y = 1000.0 * float(circle["x"])
        radius = 1000.0 * float(circle["radius"])
        result.append({"kind": circle["kind"], "x": x, "y": y, "radius": radius})
    return result


def load_set(csv_path: Path, frame_root: Path, label: str, proof_cache: dict[Path, dict[str, Any]]) -> tuple[list[dict[str, Any]], dict[str, Any]]:
    rows = read_csv(csv_path)
    source_hashes: dict[str, str] = {str(csv_path.relative_to(ROOT)): sha256(csv_path)}
    source_frames: dict[str, dict[str, str]] = {}
    proof_by_run: dict[str, dict[str, Any]] = {}
    sample_by_run_frame: dict[tuple[str, str], dict[str, Any]] = {}
    audits: list[dict[str, Any]] = []

    for row in rows:
        run_id = row["RunId"]
        if run_id not in proof_by_run:
            proof = load_proof(row["ReportPath"], proof_cache)
            proof_by_run[run_id] = proof
            report_path = Path(row["ReportPath"]).resolve()
            source_hashes[str(report_path.relative_to(ROOT))] = sha256(report_path)
        frame_id = row["FrameId"]
        key = (run_id, frame_id)
        if key not in sample_by_run_frame:
            matches = [sample for sample in proof_by_run[run_id]["samples"] if str(sample["frameId"]) == frame_id]
            if len(matches) != 1:
                raise ValueError(f"{label} {run_id} frame {frame_id}: expected one proof sample, got {len(matches)}")
            sample = matches[0]
            if sample["pixelSha256"].lower() != row["PixelSha256"].lower():
                raise ValueError(f"{label} {run_id} frame {frame_id}: CSV/proof pixel hash mismatch")
            frame_path = frame_root / f"{frame_id}.png"
            frame_hash = sha256(frame_path)
            if frame_hash.lower() != row["PixelSha256"].lower():
                raise ValueError(f"{label} {run_id} frame {frame_id}: source PNG hash mismatch")
            receipt_path = frame_root / f"{frame_id}.json"
            receipt_hash = sha256(receipt_path)
            receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
            if receipt["indexedFrame"]["pixelSha256"].lower() != frame_hash.lower():
                raise ValueError(f"{label} {run_id} frame {frame_id}: receipt/source PNG hash mismatch")
            source_frames[frame_id] = {"pngSha256": frame_hash, "receiptSha256": receipt_hash}
            sample_by_run_frame[key] = sample
        sample = sample_by_run_frame[key]

        # Keep only model candidates that pass the saved experimental arc rule
        # and existing colour classifier. The frame-positive label is applied
        # below using the draft interior point with a one-ball-radius tolerance.
        if not truth(row["DiagnosticArcChecksPass"]) or row["ClassifierKind"].lower() != "black":
            continue
        rank = int(row["CandidateRank"])
        probe = get_candidate_probe(sample, rank)
        if not probe["diagnosticArcChecksPass"] or probe["classifier"]["evidence"]["kind"] != "black":
            raise ValueError(f"{label} frame {frame_id} rank {rank}: CSV/proof candidate decision mismatch")
        x = float(probe["arcFitCentre"]["x"])
        y = float(probe["arcFitCentre"]["y"])
        candidate_radius = float(probe["arcFitRadius"])
        balls = accepted_circles(sample)
        neighbours = []
        for ball in balls:
            distance = math.hypot(x - ball["x"], y - ball["y"])
            clearance = distance - candidate_radius - ball["radius"]
            neighbours.append((distance, clearance, ball))
        nearest = min(neighbours, key=lambda item: item[0]) if neighbours else None
        closest_surface = min(neighbours, key=lambda item: item[1]) if neighbours else None
        label_distance = number(row["FitDistanceFromLabel"])
        audits.append({
            "case": label,
            "frameId": frame_id,
            "runId": run_id,
            "candidateRank": rank,
            "candidateX": x,
            "candidateY": y,
            "candidateRadius": candidate_radius,
            "expectedBallRadius": EXPECTED_RADIUS_PX,
            "fitDistanceFromDraftLabel": label_distance,
            "truePositiveCandidate": label == "positive" and label_distance is not None and label_distance <= EXPECTED_RADIUS_PX,
            "supportedVisibleSectors": int(row["SupportedVisibleSectorCount"]),
            "visibleSectors": int(row["VisibleSectorCount"]),
            "unexplainedVisibleSectors": int(row["VisibleSectorCount"]) - int(row["SupportedVisibleSectorCount"]),
            "nearestBallKind": nearest[2]["kind"] if nearest else None,
            "nearestBallX": nearest[2]["x"] if nearest else None,
            "nearestBallY": nearest[2]["y"] if nearest else None,
            "nearestBallRadius": nearest[2]["radius"] if nearest else None,
            "nearestBallCentreDistance": nearest[0] if nearest else None,
            "nearestBallCircleClearance": nearest[1] if nearest else None,
            "minimumCircleClearance": closest_surface[1] if closest_surface else None,
            "minimumClearanceBallKind": closest_surface[2]["kind"] if closest_surface else None,
            "minimumClearanceBallCentreDistance": closest_surface[0] if closest_surface else None,
            "overlapVetoRejects": closest_surface is not None and closest_surface[1] < 0,
            **{
                f"rejectAt{str(multiple).replace('.', '_')}R": nearest is not None and nearest[0] < multiple * EXPECTED_RADIUS_PX
                for multiple in THRESHOLD_MULTIPLES
            },
            "sourcePixelSha256": row["PixelSha256"].lower(),
        })

    # Save full frame and proof inventories in the report for auditability.
    for run_id, proof in proof_by_run.items():
        # CSV/proof hashes were checked for every included frame above.
        pass
    inventory = {
        "csvPath": str(csv_path.relative_to(ROOT)),
        "csvSha256": source_hashes[str(csv_path.relative_to(ROOT))],
        "sourceFrames": dict(sorted(source_frames.items(), key=lambda pair: int(pair[0]))),
        "proofReports": {
            str(Path(next(row["ReportPath"] for row in rows if row["RunId"] == run_id)).resolve().relative_to(ROOT)):
            source_hashes[str(Path(next(row["ReportPath"] for row in rows if row["RunId"] == run_id)).resolve().relative_to(ROOT))]
            for run_id in sorted(proof_by_run)
        },
        "inputRows": len(rows),
        "passingBlackArcCandidates": len(audits),
    }
    return audits, inventory


def summarize(audits: list[dict[str, Any]], label: str) -> dict[str, Any]:
    subset = [row for row in audits if row["case"] == label]
    frames = sorted({row["frameId"] for row in subset}, key=int)
    true_candidates = [row for row in subset if row["truePositiveCandidate"]]
    tp_frames = sorted({row["frameId"] for row in true_candidates}, key=int)
    result: dict[str, Any] = {
        "case": label,
        "frameCount": len(frames),
        "candidateCount": len(subset),
        "framesWithPassingTargetCandidateBeforeVeto": len(tp_frames),
        "passingTargetCandidatesBeforeVeto": len(true_candidates),
    }
    for multiple in THRESHOLD_MULTIPLES:
        field = f"rejectAt{str(multiple).replace('.', '_')}R"
        kept_tp = [row for row in true_candidates if not row[field]]
        kept_tp_frames = sorted({row["frameId"] for row in kept_tp}, key=int)
        retained_false = [row for row in subset if not row[field]] if label == "negative" else []
        retained_false_frames = sorted({row["frameId"] for row in retained_false}, key=int)
        result[f"centerDistance{multiple}R"] = {
            "positiveTargetCandidatesRetained": len(kept_tp),
            "positiveFramesRetained": len(kept_tp_frames),
            "negativeFalseCandidatesRetained": len(retained_false),
            "negativeFramesWithAnyFalseCandidateRetained": len(retained_false_frames),
        }
    if label == "negative":
        for field, name in (("overlapVetoRejects", "circleOverlap"),):
            retained = [row for row in subset if not row[field]]
            result[name] = {
                "falseCandidatesRetained": len(retained),
                "framesWithAnyFalseCandidateRetained": len({row["frameId"] for row in retained}),
                "falseCandidatesRejected": len(subset) - len(retained),
            }
    else:
        retained_tp = [row for row in true_candidates if not row["overlapVetoRejects"]]
        result["circleOverlap"] = {
            "positiveTargetCandidatesRetained": len(retained_tp),
            "positiveFramesRetained": len({row["frameId"] for row in retained_tp}),
        }
    result["perFrame"] = []
    for frame in frames:
        frame_rows = [row for row in subset if row["frameId"] == frame]
        frame_tp = [row for row in frame_rows if row["truePositiveCandidate"]]
        frame_result = {
            "frameId": frame,
            "passingCandidates": len(frame_rows),
            "targetCandidates": len(frame_tp),
            "closestTargetDistance": min((row["fitDistanceFromDraftLabel"] for row in frame_rows if row["fitDistanceFromDraftLabel"] is not None), default=None),
        }
        for multiple in THRESHOLD_MULTIPLES:
            field = f"rejectAt{str(multiple).replace('.', '_')}R"
            if label == "positive":
                frame_result[f"targetSurvives{multiple}R"] = any(not row[field] for row in frame_tp)
            else:
                frame_result[f"falseCandidatesRetainedAt{multiple}R"] = sum(not row[field] for row in frame_rows)
        if label == "positive":
            frame_result["targetSurvivesCircleOverlapVeto"] = any(not row["overlapVetoRejects"] for row in frame_tp)
        else:
            frame_result["falseCandidatesRetainedAfterCircleOverlapVeto"] = sum(not row["overlapVetoRejects"] for row in frame_rows)
        result["perFrame"].append(frame_result)
    return result


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--positive-csv", type=Path, default=DEFAULT_POSITIVE_CSV)
    parser.add_argument("--negative-csv", type=Path, default=DEFAULT_NEGATIVE_CSV)
    parser.add_argument("--positive-frames", type=Path, default=DEFAULT_POSITIVE_FRAMES)
    parser.add_argument("--negative-frames", type=Path, default=DEFAULT_NEGATIVE_FRAMES)
    parser.add_argument("--output", type=Path, default=DEFAULT_OUTPUT)
    args = parser.parse_args()
    positive_csv = args.positive_csv.resolve()
    negative_csv = args.negative_csv.resolve()
    positive_frames = args.positive_frames.resolve()
    negative_frames = args.negative_frames.resolve()
    output = args.output.resolve()
    proof_cache: dict[Path, dict[str, Any]] = {}
    positives, positive_inventory = load_set(positive_csv, positive_frames, "positive", proof_cache)
    negatives, negative_inventory = load_set(negative_csv, negative_frames, "negative", proof_cache)
    all_rows = positives + negatives
    output.mkdir(parents=True, exist_ok=True)
    with (output / "candidate-neighbour-audit.csv").open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=list(all_rows[0].keys()))
        writer.writeheader()
        writer.writerows(all_rows)
    summary = {
        "schemaVersion": 1,
        "tool": "Audit-PocketArcNeighbourOverlap",
        "diagnosticOnly": True,
        "expectedRadiusPx": EXPECTED_RADIUS_PX,
        "truePositiveDefinition": "positive-set black-classified arc candidate passing the saved diagnostic rule and within one expected ball radius (27px) of the draft interior point",
        "negativeDefinition": "every black-classified arc candidate passing the saved diagnostic rule in the black-absent control is a false candidate",
        "thresholdSemantics": "reject when candidate centre is closer than the listed multiple of the expected 27px radius to any already accepted ball centre",
        "overlapSemantics": "reject when fitted candidate radius plus already accepted circle radius exceeds their centre separation; no extra tolerance",
        "inputs": {"positive": positive_inventory, "negative": negative_inventory},
        "positive": summarize(all_rows, "positive"),
        "negative": summarize(all_rows, "negative"),
        "caveats": [
            "Positive and negative frames are from separate capture timelines and different lighting conditions; they are not matched-light or session-separated acceptance data.",
            "Frames are repeated views of fixed layouts, not independent trials.",
            "Positive draft labels are interior reference points, not reviewed ball centres; one-radius proximity is a diagnostic matching tolerance.",
            "The arc candidates and their sector tests are diagnostic outputs, not detector results or game-state assertions.",
            "Neighbour circles come from the unchanged width-1000 analyzer output whose configuration does not match the capture configuration.",
            "The overlap rule uses the fitted partial-arc radius, which may be biased low when most of a ball is occluded.",
        ],
    }
    (output / "summary.json").write_text(json.dumps(summary, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({
        "output": str(output),
        "positive": {key: value for key, value in summary["positive"].items() if key != "perFrame"},
        "negative": {key: value for key, value in summary["negative"].items() if key != "perFrame"},
        "positivePerFrame": summary["positive"]["perFrame"],
    }, indent=2))


if __name__ == "__main__":
    main()
