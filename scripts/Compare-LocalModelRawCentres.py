#!/usr/bin/env python3
"""Compare local-model raw centres with exact-frame draft labels.

This is an offline geometry diagnostic. It verifies that the model output,
labels and decoded source image refer to the same bytes, then performs a
greedy one-to-one same-colour match in raw image pixels. It does not treat the
model as ground truth or feed detections into SkyEye state.
"""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
from pathlib import Path
from typing import Any


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def kind(value: Any) -> str:
    return str(value or "unknown").strip().lower()


def load_case(name: str, model_path: Path, labels_path: Path, image_path: Path) -> dict[str, Any]:
    for path in (model_path, labels_path, image_path):
        if not path.is_file():
            raise FileNotFoundError(path)
    model = json.loads(model_path.read_text(encoding="utf-8-sig"))
    labels = json.loads(labels_path.read_text(encoding="utf-8-sig"))
    model_result = model.get("result") or {}
    predictions = model_result.get("balls") or []
    reference = labels.get("balls") or []
    image_hash = sha256(image_path)
    model_hash = model.get("sourceSha256")
    label_hash = (labels.get("source") or {}).get("sha256")
    if not model_hash or image_hash.lower() != str(model_hash).lower():
        raise ValueError(f"{name}: model output does not match the source image hash")
    if not label_hash or image_hash.lower() != str(label_hash).lower():
        raise ValueError(f"{name}: labels do not match the source image hash")
    image_size = model_result.get("image_size") or {}
    source = labels.get("source") or {}
    if (source.get("width"), source.get("height")) != (image_size.get("width"), image_size.get("height")):
        raise ValueError(f"{name}: image dimensions differ between model output and labels")
    return {
        "name": name, "model": model, "result": model_result, "labels": labels,
        "predictions": predictions, "reference": reference, "imageHash": image_hash,
        "imagePath": image_path, "modelPath": model_path, "labelsPath": labels_path,
    }


def compare_case(case: dict[str, Any], match_radius: float) -> tuple[dict[str, Any], list[dict[str, Any]]]:
    labels = case["reference"]
    predictions = case["predictions"]
    pairs: list[tuple[float, int, int, float, float]] = []
    for label_index, label in enumerate(labels):
        label_kind = kind(label.get("colour"))
        for prediction_index, prediction in enumerate(predictions):
            if label_kind != kind(prediction.get("type")):
                continue
            centre = prediction.get("center") or []
            if len(centre) != 2:
                continue
            dx = float(centre[0]) - float(label["xPx"])
            dy = float(centre[1]) - float(label["yPx"])
            pairs.append((math.hypot(dx, dy), label_index, prediction_index, dx, dy))
    pairs.sort()

    used_labels: set[int] = set()
    used_predictions: set[int] = set()
    matches: list[dict[str, Any]] = []
    for distance, label_index, prediction_index, dx, dy in pairs:
        if distance > match_radius:
            continue
        if label_index in used_labels or prediction_index in used_predictions:
            continue
        used_labels.add(label_index)
        used_predictions.add(prediction_index)
        label = labels[label_index]
        prediction = predictions[prediction_index]
        matches.append({
            "labelId": str(label.get("id", f"label-{label_index + 1}")),
            "colour": kind(label.get("colour")),
            "modelId": str(prediction.get("id", f"prediction-{prediction_index + 1}")),
            "modelSource": str(prediction.get("source", "unknown")),
            "confidence": prediction.get("conf"),
            "physicsAdjusted": bool(prediction.get("physics_adjusted", False)),
            "labelX": float(label["xPx"]), "labelY": float(label["yPx"]),
            "modelX": float(prediction["center"][0]), "modelY": float(prediction["center"][1]),
            "dx": round(dx, 3), "dy": round(dy, 3), "errorPx": round(distance, 3),
            "labelUncertaintyPx": label.get("uncertaintyPx"),
        })

    errors = [row["errorPx"] for row in matches]
    unmatched_predictions = [
        {"id": p.get("id"), "type": kind(p.get("type")), "source": p.get("source"),
         "centre": p.get("center"), "confidence": p.get("conf")}
        for index, p in enumerate(predictions) if index not in used_predictions
    ]
    unmatched_labels = [
        {"id": label.get("id"), "colour": kind(label.get("colour")),
         "centre": [label.get("xPx"), label.get("yPx")]}
        for index, label in enumerate(labels) if index not in used_labels
    ]
    counts: dict[str, int] = {}
    for prediction in predictions:
        label_kind = kind(prediction.get("type"))
        counts[label_kind] = counts.get(label_kind, 0) + 1

    summary = {
        "case": case["name"],
        "frameId": case["model"].get("frameId"),
        "condition": case["model"].get("condition"),
        "exactSourceHash": case["imageHash"],
        "sourceHashVerifiedForModelAndLabels": True,
        "labelStatus": case["labels"].get("status", "unspecified"),
        "imageSize": case["result"].get("image_size"),
        "modelLatencyMs": case["result"].get("latency_ms"),
        "modelPhysicsCorrectionCount": case["result"].get("physics_corrections"),
        "physicsAdjustedPredictionCount": sum(bool(p.get("physics_adjusted", False)) for p in predictions),
        "expectedLabelCount": len(labels),
        "predictionCount": len(predictions),
        "predictionCountsByKind": counts,
        "sameColourMatchesWithinRadius": len(matches),
        "matchRadiusRawPx": match_radius,
        "matchedWithin10px": sum(row["errorPx"] <= 10 for row in matches),
        "matchedWithin20px": sum(row["errorPx"] <= 20 for row in matches),
        "medianMatchedCentreErrorRawPx": round(sorted(errors)[(len(errors) - 1) // 2], 3) if errors else None,
        "maximumMatchedCentreErrorRawPx": max(errors) if errors else None,
        "unmatchedPredictions": unmatched_predictions,
        "unmatchedLabels": unmatched_labels,
        "matches": matches,
        "blackInteriorPointCheck": None,
        "inputFiles": {
            "image": str(case["imagePath"]), "model": str(case["modelPath"]),
            "labels": str(case["labelsPath"]),
        },
    }
    black_point = case["labels"].get("blackInteriorPoint")
    black_predictions = [p for p in predictions if kind(p.get("type")) == "black"]
    if black_point and black_predictions:
        centre = black_predictions[0].get("center") or []
        if len(centre) == 2:
            distance = math.hypot(float(centre[0]) - float(black_point["xPx"]),
                                  float(centre[1]) - float(black_point["yPx"]))
            summary["blackInteriorPointCheck"] = {
                "modelCentre": centre,
                "approximateInteriorPoint": [black_point["xPx"], black_point["yPx"]],
                "distancePx": round(distance, 3),
                "warning": "The reference is an approximate interior point, not a reviewed ball centre.",
            }
    return summary, matches


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--case", action="append", nargs=4,
                        metavar=("NAME", "MODEL_JSON", "LABELS_JSON", "IMAGE"), required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--match-radius", type=float, default=27.0,
                        help="One ball radius in raw source pixels")
    args = parser.parse_args()
    if args.match_radius <= 0:
        parser.error("--match-radius must be positive")
    cases = [load_case(name, Path(model), Path(labels), Path(image))
             for name, model, labels, image in args.case]
    summaries, all_matches = [], []
    for case in cases:
        summary, matches = compare_case(case, args.match_radius)
        summaries.append(summary)
        for row in matches:
            all_matches.append({"case": case["name"], **row})
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({
        "schemaVersion": 1,
        "purpose": "Exploratory same-colour raw-centre comparison of local model outputs against exact-frame draft labels.",
        "method": {
            "matching": "greedy one-to-one same-colour assignment by increasing Euclidean centre distance",
            "coordinateSpace": "raw source-image pixels",
            "radius": args.match_radius,
            "limitations": [
                "Manual labels are draft and do not constitute independent ground truth.",
                "The black location is compared only with an approximate interior point, not scored as a centre label.",
                "An unmatched prediction is not a confirmed false positive; inspect its source pixels and label coverage.",
                "This measures geometry only and does not run SkyEye classification, boundary or acceptance gates.",
            ],
        },
        "cases": summaries,
    }, indent=2) + "\n", encoding="utf-8")
    csv_path = args.output.with_suffix(".matches.csv")
    fields = ["case", "labelId", "colour", "modelId", "modelSource", "confidence",
              "physicsAdjusted", "labelX", "labelY", "modelX", "modelY", "dx", "dy",
              "errorPx", "labelUncertaintyPx"]
    with csv_path.open("w", newline="", encoding="utf-8") as stream:
        writer = csv.DictWriter(stream, fieldnames=fields)
        writer.writeheader()
        writer.writerows(all_matches)
    print(json.dumps({"output": str(args.output), "matchOutput": str(csv_path),
                      "caseCount": len(summaries)}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
