#!/usr/bin/env python3
"""Score a frozen O09 transfer checkpoint on newly reviewed Development frames.

This is a bounded source-image probe. It performs no training, augmentation,
threshold tuning, Holdout access, or production integration.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import statistics
import time
from collections import Counter
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
TARGET_IDS = {2831, 542, 295, 288, 959, 562, 1691}
CLASS_NAMES = {0: "cue", 1: "black", 2: "red", 3: "yellow", 4: "unknown"}
CONFIDENCE = 0.001
NMS_IOU = 0.7
MATCH_IOU = 0.5
MAX_DETECTIONS = 100
IMAGE_SIZE = 640


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def box_iou(a: tuple[float, float, float, float], b: tuple[float, float, float, float]) -> float:
    left, top, right, bottom = max(a[0], b[0]), max(a[1], b[1]), min(a[2], b[2]), min(a[3], b[3])
    intersection = max(0.0, right - left) * max(0.0, bottom - top)
    area_a = max(0.0, a[2] - a[0]) * max(0.0, a[3] - a[1])
    area_b = max(0.0, b[2] - b[0]) * max(0.0, b[3] - b[1])
    union = area_a + area_b - intersection
    return intersection / union if union > 0 else 0.0


def percentile_nearest_rank(values: list[float], p: float) -> float | None:
    if not values:
        return None
    ordered = sorted(values)
    rank = max(1, math.ceil(p * len(ordered)))
    return ordered[rank - 1]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--corpus-root", type=Path, default=ROOT / "work/o09/radius-reviewed-corpus-v2-verified")
    parser.add_argument("--checkpoint", type=Path,
                        default=ROOT / "work/o09/training-baseline-v1/runs/transfer-n640-e20-seed20261101/weights/best.pt")
    parser.add_argument("--training-summary", type=Path, default=ROOT / "work/o09/training-transfer-v1/training-summary.json")
    parser.add_argument("--training-export", type=Path, default=ROOT / "work/o09/yolo-reviewed-radii-v1/o09-export.json")
    parser.add_argument("--output", type=Path, default=ROOT / "work/o09/transfer-new-controls-probe-v1/report.json")
    args = parser.parse_args()
    corpus_root = args.corpus_root.resolve()
    checkpoint = args.checkpoint.resolve()
    summary_path = args.training_summary.resolve()
    training_export_path = args.training_export.resolve()
    output_path = args.output.resolve()
    if output_path.exists():
        parser.error(f"Output exists; choose a new evidence path: {output_path}")
    for path in (corpus_root / "manifest.json", checkpoint, summary_path, training_export_path):
        if not path.is_file():
            parser.error(f"Required file is missing: {path}")

    manifest = read_json(corpus_root / "manifest.json")
    training_summary = read_json(summary_path)
    checkpoint_sha = sha256(checkpoint)
    expected_checkpoint_sha = str(training_summary.get("bestWeightsSha256", "")).lower()
    if not expected_checkpoint_sha or checkpoint_sha.lower() != expected_checkpoint_sha:
        parser.error("Checkpoint hash differs from the retained transfer training summary.")
    if training_summary.get("corpusId") != "live-detect-20261101-black-cue-development-v4-o09-reviewed-radii-v1":
        parser.error("Retained transfer checkpoint does not bind the v1 Development corpus.")
    training_export_bytes = training_export_path.read_bytes()
    expected_export_sha = str(training_summary.get("exportReportSha256", "")).lower()
    if not expected_export_sha or hashlib.sha256(training_export_bytes).hexdigest().lower() != expected_export_sha:
        parser.error("V1 training export hash differs from the retained transfer summary.")
    training_export = json.loads(training_export_bytes)

    sequences = {sequence["id"]: sequence for sequence in manifest["sequences"]}
    selected: list[dict[str, Any]] = []
    for sequence in manifest["sequences"]:
        try:
            frame_id = int(sequence["id"].removeprefix("o09-expansion-").removesuffix("-v2"))
        except ValueError:
            continue
        if frame_id in TARGET_IDS:
            if sequence["split"] != "Development":
                parser.error(f"Frame {frame_id} is not Development-only.")
            labels_doc = read_json(corpus_root / sequence["labelsFile"])
            if len(labels_doc.get("frames", [])) != 1:
                parser.error(f"Frame {frame_id} does not have exactly one reviewed label frame.")
            index_lines = (corpus_root / sequence["indexFile"]).read_text(encoding="utf-8-sig").splitlines()
            if len(index_lines) != 1:
                parser.error(f"Frame {frame_id} does not have exactly one source index row.")
            index_row = json.loads(index_lines[0])
            labels = labels_doc["frames"][0]
            if labels.get("sampleId") != index_row.get("sampleId"):
                parser.error(f"Frame {frame_id} index/label sample IDs differ.")
            image_path = corpus_root / sequence["source"]["file"]
            pixel_sha = sha256(image_path)
            if pixel_sha.lower() != index_row.get("pixelSha256", "").lower() or pixel_sha.lower() != labels.get("pixelSha256", "").lower():
                parser.error(f"Frame {frame_id} source, index and label pixel hashes differ.")
            selected.append({"frameId": frame_id, "sequence": sequence, "labels": labels,
                             "index": index_row, "imagePath": image_path, "pixelSha256": pixel_sha})
    selected.sort(key=lambda row: row["frameId"])
    if {row["frameId"] for row in selected} != TARGET_IDS:
        parser.error("Corpus does not contain exactly the seven expected new controls.")

    # Require the frozen checkpoint's original training data to predate and
    # exclude all seven expansion frames.
    training_samples = {sample["sampleId"] for sample in training_export.get("samples", [])
                        if sample.get("split") == "train"}
    if not training_samples:
        parser.error("V1 training export does not enumerate training sample IDs.")
    selected_samples = {row["index"]["sampleId"] for row in selected}
    if training_samples & selected_samples:
        parser.error("At least one probe sample appears in the checkpoint's recorded training sample IDs.")

    from ultralytics import YOLO
    import torch
    import ultralytics

    model = YOLO(str(checkpoint), task="detect", verbose=False)
    model_names = {int(key): value for key, value in model.names.items()} if isinstance(model.names, dict) else dict(enumerate(model.names))
    if any(model_names.get(index) != name for index, name in CLASS_NAMES.items()):
        parser.error(f"Checkpoint class order differs from the frozen five-class map: {model_names}")

    per_frame: list[dict[str, Any]] = []
    total_tp = total_fp = total_fn = 0
    all_center_errors: list[float] = []
    absent_black_proposals = 0
    absent_cue_proposals = 0
    class_counts: Counter[str] = Counter()

    for row in selected:
        frame_id = row["frameId"]
        labels = row["labels"]
        sequence = row["sequence"]
        width = int(sequence["source"]["width"])
        height = int(sequence["source"]["height"])
        truth: list[dict[str, Any]] = []
        absent: set[str] = set()
        for ball in labels["balls"]:
            if ball["visibility"] != "Visible":
                absent.add(ball["colour"])
                continue
            if "radiusX" not in ball or "radiusY" not in ball:
                parser.error(f"Frame {frame_id}/{ball['id']} lacks the reviewed apparent radius.")
            centre = (float(ball["x"]) * width, float(ball["y"]) * height)
            rx = float(ball["radiusX"]) * width
            ry = float(ball["radiusY"]) * height
            truth.append({"class": next(index for index, name in CLASS_NAMES.items() if name == ball["colour"]),
                          "colour": ball["colour"], "id": ball["id"], "centre": centre,
                          "box": (max(0.0, centre[0] - rx), max(0.0, centre[1] - ry),
                                  min(float(width), centre[0] + rx), min(float(height), centre[1] + ry))})

        result = model.predict(source=str(row["imagePath"]), imgsz=IMAGE_SIZE, conf=CONFIDENCE,
                               iou=NMS_IOU, max_det=MAX_DETECTIONS, device="cpu", verbose=False, save=False)[0]
        if tuple(result.orig_shape) != (height, width):
            parser.error(f"Frame {frame_id} inference dimensions changed: {result.orig_shape}.")
        predictions: list[dict[str, Any]] = []
        if result.boxes is not None:
            for box in result.boxes:
                box_values = tuple(float(value) for value in box.xyxy[0].cpu().tolist())
                predictions.append({"class": int(box.cls[0].cpu()), "confidence": float(box.conf[0].cpu()),
                                    "box": box_values})
        predictions.sort(key=lambda prediction: prediction["confidence"], reverse=True)

        unmatched = set(range(len(truth)))
        tp = fp = 0
        errors: list[float] = []
        frame_class_counts: Counter[str] = Counter()
        for prediction in predictions:
            candidates = [(box_iou(prediction["box"], truth[index]["box"]), index)
                          for index in unmatched if truth[index]["class"] == prediction["class"]]
            overlap, match = max(candidates, default=(0.0, -1))
            if match >= 0 and overlap >= MATCH_IOU:
                unmatched.remove(match)
                tp += 1
                px = (prediction["box"][0] + prediction["box"][2]) / 2.0
                py = (prediction["box"][1] + prediction["box"][3]) / 2.0
                error = math.hypot(px - truth[match]["centre"][0], py - truth[match]["centre"][1])
                errors.append(error)
                class_counts[truth[match]["colour"]] += 1
                frame_class_counts[truth[match]["colour"]] += 1
            else:
                fp += 1
        fn = len(unmatched)
        total_tp += tp
        total_fp += fp
        total_fn += fn
        all_center_errors.extend(errors)
        absent_black = "black" in absent
        absent_cue = "cue" in absent
        black_predictions = sum(prediction["class"] == 1 for prediction in predictions) if absent_black else 0
        cue_predictions = sum(prediction["class"] == 0 for prediction in predictions) if absent_cue else 0
        absent_black_proposals += black_predictions
        absent_cue_proposals += cue_predictions
        predicted_counts = Counter(CLASS_NAMES[prediction["class"]] for prediction in predictions)
        gt_counts = Counter(item["colour"] for item in truth)
        per_frame.append({
            "frameId": frame_id, "sampleId": row["index"]["sampleId"],
            "sourceTimeUs": row["index"]["sourceTimeUs"], "timeline": row["index"]["scope"]["timeline"],
            "pixelSha256": row["pixelSha256"], "groundTruth": len(truth), "predictionsAt001": len(predictions),
            "groundTruthByClass": dict(sorted(gt_counts.items())), "predictionsByClass": dict(sorted(predicted_counts.items())),
            "tp": tp, "fp": fp, "fn": fn, "matchedClassCounts": dict(sorted(frame_class_counts.items())),
            "completeLayout": tp == len(truth) and fp == 0,
            "absentBlackLabel": absent_black, "blackPredictionsWhenAbsent": black_predictions,
            "absentCueLabel": absent_cue, "cuePredictionsWhenAbsent": cue_predictions,
            "matchedCentreErrorSourcePx": errors,
        })

    gt_total = total_tp + total_fn
    report = {
        "schemaVersion": 1,
        "target": "O09-TRANSFER-NEW-CONTROLS-PROBE",
        "evidenceScope": "Out-of-training Development probe only; not Holdout, a frozen candidate comparison, physical acceptance, or a pixel/cloth-plane accuracy claim.",
        "testedCommit": "d914dfc",
        "corpusId": manifest["id"],
        "manifestSha256": sha256(corpus_root / "manifest.json"),
        "checkpointPath": str(checkpoint.relative_to(ROOT)).replace("\\", "/"),
        "checkpointSha256": checkpoint_sha,
        "trainingSummarySha256": sha256(summary_path),
        "trainingExportSha256": sha256(training_export_path),
        "trainingCorpusId": training_summary.get("corpusId"),
        "trainingSampleOverlap": len(training_samples & selected_samples),
        "inference": {"imgsz": IMAGE_SIZE, "confidence": CONFIDENCE, "nmsIou": NMS_IOU,
                      "maxDetections": MAX_DETECTIONS, "device": "cpu", "warmup": "none; timing not evaluated"},
        "matching": {"classAwareGreedyIou": MATCH_IOU, "radii": "user-approved apparent source-image radii from the seven frames"},
        "runtime": {"python": __import__("platform").python_version(), "ultralytics": ultralytics.__version__,
                    "torch": torch.__version__, "cudaAvailable": torch.cuda.is_available(),
                    "timingReported": False},
        "aggregate": {
            "frames": len(per_frame), "groundTruth": gt_total, "predictions": total_tp + total_fp,
            "truePositives": total_tp, "falsePositives": total_fp, "falseNegatives": total_fn,
            "precisionAtIou50": total_tp / (total_tp + total_fp) if total_tp + total_fp else 0.0,
            "recallAtIou50": total_tp / gt_total if gt_total else 0.0,
            "correctCompleteLayouts": sum(frame["completeLayout"] for frame in per_frame),
            "matchedCentreErrorSourcePx": {"count": len(all_center_errors),
                "median": statistics.median(all_center_errors) if all_center_errors else None,
                "p95NearestRank": percentile_nearest_rank(all_center_errors, 0.95),
                "max": max(all_center_errors) if all_center_errors else None},
            "matchedClassCounts": dict(sorted(class_counts.items())),
            "blackPredictionsInBlackAbsentFrames": absent_black_proposals,
            "cuePredictionsInCueAbsentFrames": absent_cue_proposals,
        },
        "perFrame": per_frame,
        "limitations": [
            "All seven inputs are Development controls from two related setup groups; these are correlated samples.",
            "The new O01 export assigns these images to training, but this checkpoint was trained only on the prior v1 corpus and has zero sample overlap.",
            "Frame2831 has a photometric mismatch; frames959/562 are a photometrically unmatched present/absent pair. Do not make matched-light or causal claims.",
            "Apparent radii and reviewed centres are visual labels without a numeric matching tolerance; source-pixel matches are descriptive only.",
            "This uses CPU still-image inference and reports no Pixel latency, resource, D1, D2, or event/clip result.",
        ],
    }
    write_json(output_path, report)
    print(json.dumps({"output": str(output_path), **report["aggregate"]}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
