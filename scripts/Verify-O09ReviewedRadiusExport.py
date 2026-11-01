#!/usr/bin/env python3
"""Verify O09 YOLO export coverage, image bindings, group splits and reviewed radii."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
from typing import Any


CLASS_ID = {"cue": 0, "black": 1, "red": 2, "yellow": 3, "unknown": 4}


def read_json(path: Path) -> Any:
    with path.open("r", encoding="utf-8-sig") as stream:
        return json.load(stream)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest().upper()


def inside(root: Path, relative: str) -> Path:
    path = (root / relative).resolve()
    if root.resolve() not in path.parents:
        raise ValueError(f"Path escapes root: {relative}")
    return path


def close(actual: float, expected: float) -> bool:
    return abs(actual - expected) <= 2e-8


def expected_yolo(ball: dict[str, Any]) -> tuple[int, float, float, float, float]:
    colour = ball["colour"]
    if colour not in CLASS_ID or ball.get("visibility") != "Visible":
        raise ValueError(f"Unsupported visible class/visibility: {ball}")
    x, y = float(ball["x"]), float(ball["y"])
    rx, ry = float(ball["radiusX"]), float(ball["radiusY"])
    left, right = max(0.0, x - rx), min(1.0, x + rx)
    top, bottom = max(0.0, y - ry), min(1.0, y + ry)
    return CLASS_ID[colour], (left + right) / 2, (top + bottom) / 2, right - left, bottom - top


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--corpus-root", type=Path, required=True)
    parser.add_argument("--manifest", type=Path, required=True)
    parser.add_argument("--export-dir", type=Path, required=True)
    args = parser.parse_args()

    corpus_root = args.corpus_root.resolve()
    manifest_path = args.manifest.resolve()
    export_root = args.export_dir.resolve()
    manifest = read_json(manifest_path)
    report = read_json(export_root / "o09-export.json")
    lock = read_json(corpus_root / "split-lock.json")
    if report.get("schemaVersion") != 2 or report.get("sourceSplit") != "Development":
        raise SystemExit("Unexpected O09 export schema or split")
    if report.get("manifestSha256", "").upper() != sha256(manifest_path):
        raise SystemExit("Export does not bind to the supplied manifest bytes")
    if lock.get("manifestSha256", "").upper() != sha256(manifest_path):
        raise SystemExit("Frozen split lock does not bind to the supplied manifest")

    sequences = {sequence["id"]: sequence for sequence in manifest["sequences"]}
    exported = report.get("samples", [])
    if len(exported) != len(sequences) or len({sample["sequenceId"] for sample in exported}) != len(exported):
        raise SystemExit("Export sample coverage is not one-to-one with corpus sequences")
    assignments = {item["session"]: item for item in report.get("sessionAssignments", [])}
    groups = {item["splitGroupId"]: item for item in report.get("splitGroups", [])}
    if len(groups) < 3 or {item["role"] for item in groups.values()} != {"train", "validation"}:
        raise SystemExit("Train/validation split groups are missing or incomplete")

    sample_count = image_hash_count = png_count = visible_box_count = radius_count = 0
    group_roles: dict[str, set[str]] = {}
    for sample in exported:
        sequence = sequences.get(sample["sequenceId"])
        if sequence is None or sequence["split"] != "Development" or sample["split"] not in ("train", "validation"):
            raise SystemExit("An O01 Holdout or unknown sequence entered the O09 Development export")
        assignment = assignments.get(sequence["session"])
        if assignment is None or assignment["role"] != sample["split"] or assignment["splitGroupId"] != sample["splitGroupId"]:
            raise SystemExit(f"Session split assignment differs from export: {sample['sequenceId']}")
        group = groups.get(sample["splitGroupId"])
        if group is None or group["role"] != assignment["role"]:
            raise SystemExit(f"Split group role mismatch: {sample['splitGroupId']}")
        for tag in sequence.get("tags", []):
            if tag.startswith("setup-group:"):
                group_roles.setdefault(tag[len("setup-group:"):], set()).add(group["role"])
            elif tag.startswith("matched-setup-group-"):
                group_roles.setdefault(tag, set()).add(group["role"])
        sample_count += 1

        index_path = inside(corpus_root, sequence["indexFile"])
        indexed = [json.loads(line) for line in index_path.read_text(encoding="utf-8-sig").splitlines() if line.strip()]
        frame = next((item for item in indexed if item["sampleId"] == sample["sampleId"]), None)
        if frame is None or frame["pixelSha256"].upper() != sample["pixelSha256"].upper():
            raise SystemExit(f"Export sample has no matching source-frame receipt: {sample['sampleId']}")
        image_path = inside(export_root, sample["imageFile"])
        source_path = inside(corpus_root, frame["pixelFile"])
        if sha256(image_path) != sample["pixelSha256"].upper() or sha256(source_path) != sample["pixelSha256"].upper():
            raise SystemExit(f"Export/source image hash mismatch: {sample['sampleId']}")
        if frame["encoding"] == "Png":
            if image_path.suffix.lower() != ".png" or image_path.read_bytes()[:8] != b"\x89PNG\r\n\x1a\n":
                raise SystemExit(f"PNG frame encoding/extension was not preserved: {sample['sampleId']}")
            png_count += 1
        elif frame["encoding"] == "Jpeg":
            if image_path.suffix.lower() not in (".jpg", ".jpeg") or image_path.read_bytes()[:2] != b"\xff\xd8":
                raise SystemExit(f"JPEG frame encoding/extension was not preserved: {sample['sampleId']}")
        else:
            raise SystemExit(f"Unexpected exported pixel encoding: {frame['encoding']}")
        image_hash_count += 1

        labels = read_json(inside(corpus_root, sequence["labelsFile"]))
        label_frame = next((item for item in labels["frames"] if item["sampleId"] == sample["sampleId"]), None)
        if label_frame is None or label_frame["coordinateSpace"] != "image-normalized":
            raise SystemExit(f"Missing image-normalized labels: {sample['sampleId']}")
        visible = [ball for ball in label_frame["balls"] if ball["visibility"] == "Visible"]
        label_path = inside(export_root, sample["labelFile"])
        lines = [line.split() for line in label_path.read_text(encoding="utf-8").splitlines() if line.strip()]
        if len(visible) != sample["visibleBoxes"] or len(lines) != len(visible):
            raise SystemExit(f"Visible-box denominator mismatch: {sample['sampleId']}")
        for ball, values in zip(visible, lines, strict=True):
            if ball.get("radiusX") is None or ball.get("radiusY") is None:
                raise SystemExit(f"Visible ball has no accepted X/Y radii: {sample['sampleId']}/{ball['id']}")
            expected = expected_yolo(ball)
            actual = (int(values[0]), *(float(value) for value in values[1:]))
            if len(values) != 5 or actual[0] != expected[0] or any(not close(a, e) for a, e in zip(actual[1:], expected[1:])):
                raise SystemExit(f"YOLO centre/extent differs from approved normalized label: {sample['sampleId']}/{ball['id']}")
            radius_count += 1
            visible_box_count += 1

    if any(len(roles) != 1 for roles in group_roles.values()):
        raise SystemExit("A setup/control group crosses train and validation")
    print(f"PASS O09-RADIUS-LABEL-ROUNDTRIP-SAMPLE-COVERAGE {sample_count}/{len(sequences)}")
    print(f"PASS O09-RADIUS-LABEL-ROUNDTRIP-IMAGE-HASHES {image_hash_count}/{len(exported)}; PNG preserved {png_count}/{len(exported)}")
    print(f"PASS O09-RADIUS-LABEL-ROUNDTRIP-BOXES {visible_box_count}/{radius_count} visible labels match normalized radii")
    print(f"PASS O09-RADIUS-LABEL-ROUNDTRIP-SETUP-GROUPS {len(group_roles)}/{len(group_roles)} remain in a single train/validation role")
    print(f"INFO O09 exported {len(groups)} split groups: " + ", ".join(f"{item['role']}={item['splitGroupId']}" for item in groups.values()))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
