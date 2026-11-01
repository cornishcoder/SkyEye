#!/usr/bin/env python3
"""Append user-reviewed staged controls to a versioned O01/O09 Development corpus.

This keeps the existing O01 manifest/labels/split/export path. It copies the
prior reviewed-radius derivative, binds each added image to its exact saved
capture receipt and centre-review record, adds only visually approved
apparent source-image radii, and writes a new corpus version. It never writes
to the source corpus or Holdout data.
"""

from __future__ import annotations

import argparse
import copy
import hashlib
import json
import math
import shutil
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
EXPECTED_FRAME_IDS = {2831, 542, 295, 288, 959, 562, 1691}
GROUPS = {
    "mid-cushion": "live-detect-20261101-mid-cushion-controls",
    "opposite-cue-positive": "live-detect-20261101-opposite-cue-controls",
    "opposite-cue-absent": "live-detect-20261101-opposite-cue-controls",
    "opposite-cue-restaged": "live-detect-20261101-opposite-cue-controls",
}
LAYOUTS = {
    2831: "mid-cushion-black-present-cue-present",
    542: "mid-cushion-black-absent-cue-present",
    295: "mid-cushion-black-present-cue-absent",
    288: "mid-cushion-black-present-cue-present",
    959: "opposite-cue-black-present-cue-present",
    562: "opposite-cue-black-present-cue-absent",
    1691: "opposite-cue-black-present-cue-present",
}
LIGHTING_NOTES = {
    2831: "display-referred image condition mismatches the later mid-cushion controls; exclude from matched-light claims",
    542: "four-patch source-image comparison supports a match within the later mid-cushion sequence; physical illumination is not established",
    295: "four-patch source-image comparison supports a match within the later mid-cushion sequence; physical illumination is not established",
    288: "four-patch source-image comparison supports a match within the later mid-cushion sequence; physical illumination is not established",
    959: "cross-run comparison to cue-absent frame 562 is photometrically unmatched; exclude from paired-light claims",
    562: "cross-run comparison to cue-present frame 959 is photometrically unmatched; exclude from paired-light claims",
    1691: "lighting stratum and cross-run match are unverified",
}


def read_json(path: Path) -> Any:
    return json.loads(path.read_text(encoding="utf-8-sig"))


def write_json(path: Path, value: Any) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def sha256_bytes(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def sha256(path: Path) -> str:
    return sha256_bytes(path.read_bytes())


def relative_repo_path(value: str) -> Path:
    path = Path(value)
    return path if path.is_absolute() else ROOT / path


def require(condition: bool, message: str) -> None:
    if not condition:
        raise ValueError(message)


def mapped_point(matrix: list[float], x: float, y: float) -> tuple[float, float]:
    denominator = matrix[6] * x + matrix[7] * y + matrix[8]
    require(abs(denominator) > 1e-12, "Source-to-table transform has a zero denominator.")
    return ((matrix[0] * x + matrix[1] * y + matrix[2]) / denominator,
            (matrix[3] * x + matrix[4] * y + matrix[5]) / denominator)


def inverse3(matrix: list[float]) -> list[float]:
    a, b, c, d, e, f, g, h, i = matrix
    cofactors = [e * i - f * h, c * h - b * i, b * f - c * e,
                 f * g - d * i, a * i - c * g, c * d - a * f,
                 d * h - e * g, b * g - a * h, a * e - b * d]
    determinant = a * cofactors[0] + b * cofactors[3] + c * cofactors[6]
    require(abs(determinant) > 1e-12, "Source-to-table transform is singular.")
    return [value / determinant for value in cofactors]


def find_record_frame(record: dict[str, Any], frame_id: int) -> dict[str, Any]:
    rows = record.get("frames")
    require(isinstance(rows, list), f"Review record for frame {frame_id} has no frame list.")
    row = next((item for item in rows if int(item.get("frameId", -1)) == frame_id), None)
    require(row is not None, f"Review record does not contain frame {frame_id}.")
    require(record.get("holdout") is not True and record.get("split", "Development") == "Development",
            f"Frame {frame_id} review is not Development-only.")
    return row


def record_source_fields(row: dict[str, Any]) -> tuple[str, str, str, str]:
    nested = row.get("source") if isinstance(row.get("source"), dict) else {}
    image = nested.get("image") or row.get("sourceImage")
    image_hash = nested.get("pngSha256") or row.get("sourceImageSha256")
    receipt_path = nested.get("receipt") or row.get("sourceReceipt")
    receipt_hash = nested.get("receiptSha256") or row.get("sourceReceiptSha256")
    require(all(isinstance(value, str) and value for value in (image, image_hash, receipt_path, receipt_hash)),
            f"Review record frame {row.get('frameId')} lacks exact image/receipt bindings.")
    return image, image_hash.lower(), receipt_path, receipt_hash.lower()


def reviewed_centres(row: dict[str, Any]) -> dict[str, dict[str, Any]]:
    raw = row.get("labels")
    if raw is None:
        raw = row.get("reviewedLabels")
    output: dict[str, dict[str, Any]] = {}
    if isinstance(raw, list):
        for item in raw:
            centre = item.get("centreSourcePx", {})
            label_id = str(item.get("id", "")).removesuffix("?")
            if label_id and "x" in centre and "y" in centre:
                output[label_id] = {"x": float(centre["x"]), "y": float(centre["y"]),
                                    "visible": bool(item.get("visibleBall", not item.get("expectedLocationOnly", False)))}
    elif isinstance(raw, dict):
        for key, item in raw.items():
            if not isinstance(item, dict) or "x" not in item or "y" not in item:
                continue
            label_id = str(key).removesuffix("?")
            visible = item.get("visibleBall", item.get("visible", not item.get("expectedLocationOnlyInDraft", False)))
            output[label_id] = {"x": float(item["x"]), "y": float(item["y"]), "visible": bool(visible)}
    return output


def normalize_control_layout_tag(sequence: dict[str, Any]) -> str:
    sequence_id = sequence["id"]
    aliases = {
        "lower-left-black-present-cue-present-678-v1": "lower-left-black-present-cue-present",
        "lower-left-black-absent-cue-present-844-v1": "lower-left-black-absent-cue-present",
        "lower-right-black-present-cue-present-1049-v1": "lower-right-black-present-cue-present",
        "lower-right-black-absent-cue-present-461-v1": "lower-right-black-absent-cue-present",
        "lower-right-black-present-cue-absent-477-v1": "lower-right-black-present-cue-absent",
        "lower-right-black-cue-restored-375-v1": "lower-right-black-present-cue-present",
        "upper-right-black-cue-present-697-v1": "upper-right-black-present-cue-present",
        "upper-right-black-absent-cue-present-229-v1": "upper-right-black-absent-cue-present",
        "upper-right-black-present-cue-absent-206-v1": "upper-right-black-present-cue-absent",
        "upper-right-black-cue-restored-190-v1": "upper-right-black-present-cue-present",
    }
    require(sequence_id in aliases, f"No explicit target-state tag mapping for existing sequence {sequence_id}.")
    return "control-layout:" + aliases[sequence_id]


def create(args: argparse.Namespace) -> dict[str, Any]:
    source_root = args.source_corpus.resolve()
    source_input = args.source_review_input.resolve()
    guide_path = args.radius_guides.resolve()
    output = args.output.resolve()
    require(source_root.is_dir(), f"Source O01 corpus does not exist: {source_root}")
    require(source_input.is_dir(), f"Reviewed source input does not exist: {source_input}")
    require(guide_path.is_file(), f"Approved guide manifest does not exist: {guide_path}")
    require(not output.exists(), f"Output already exists; choose a new versioned destination: {output}")
    response = " ".join(args.reviewer_response.lower().split())
    require(response in {"looks correct", "all look correct"}, "Reviewer response must record the affirmative guide review.")

    base_manifest_path = source_root / "manifest.json"
    base_manifest_bytes = base_manifest_path.read_bytes()
    manifest = read_json(base_manifest_path)
    transform_path = source_root / "source-to-table-transform.json"
    transform = read_json(transform_path)
    source_index_path = source_input / "source-review-input-manifest.json"
    source_data_root = source_input / "source-input" if (source_input / "source-input").is_dir() else source_input
    source_index = read_json(source_index_path)
    guide_manifest = read_json(guide_path)
    source_frames = {int(row["frameId"]): row for row in source_index["frames"]}
    guide_frames = {int(row["frameId"]): row for row in guide_manifest["frames"]}
    require(set(source_frames) == EXPECTED_FRAME_IDS, "Reviewed source input does not contain the expected seven frames.")
    require(set(guide_frames) == EXPECTED_FRAME_IDS, "Guide manifest does not contain the expected seven frames.")

    existing_ids = {int(sequence["id"].rsplit("-", 1)[-1].split("-v")[0])
                    for sequence in manifest["sequences"] if sequence.get("id", "").rsplit("-", 1)[-1].split("-v")[0].isdigit()}
    require(not (existing_ids & EXPECTED_FRAME_IDS), "An expansion frame already exists in the source corpus.")

    base_transform = next((frame for frame in transform["frames"] if frame.get("tableCalibrationMatrix")), None)
    require(base_transform is not None, "Source-to-table transform has no verified matrix to reuse.")
    matrix = [float(value) for value in base_transform["tableCalibrationMatrix"]]
    inverse = inverse3(matrix)
    targets = [(0.0, 0.0), (2.0, 0.0), (2.0, 1.0), (0.0, 1.0)]
    base_corners = base_transform["cornerCoordinatesNormalized"]
    for point, target in zip(base_corners, targets, strict=True):
        mapped = mapped_point(matrix, float(point["x"]), float(point["y"]))
        require(math.dist(mapped, target) < 1e-7, "Existing TableCalibration matrix does not map its recorded corners.")

    # Copy the prior O01 derivative byte-for-byte, omitting only its old split
    # lock. The new manifest requires a fresh lock bound to the expanded data.
    shutil.copytree(source_root, output, ignore=shutil.ignore_patterns("split-lock.json"))
    before_source_hashes: dict[str, str] = {}
    added_sequences: list[dict[str, Any]] = []
    added_transform_rows: list[dict[str, Any]] = []
    provenance_rows: list[dict[str, Any]] = []
    added_visible = 0
    added_absent = 0

    for frame_id in sorted(EXPECTED_FRAME_IDS):
        source = source_frames[frame_id]
        guide = guide_frames[frame_id]
        original_image = relative_repo_path(source["sourceImageOriginal"])
        input_image = source_data_root / source["sourceImage"]
        source_label_path = source_data_root / source["labelsFile"]
        review_record_path = relative_repo_path(source["sourceReviewRecord"])
        receipt_source = original_image.with_suffix(".json")
        for path in (original_image, input_image, source_label_path, review_record_path, receipt_source):
            require(path.is_file(), f"Required provenance file is missing: {path}")
            before_source_hashes[str(path)] = sha256(path)
        image_hash = sha256(original_image)
        source_label_hash = sha256(source_label_path)
        require(image_hash == source["sourceSha256"].lower() == sha256(input_image),
                f"Frame {frame_id} image does not match source/review-input hashes.")
        require(source_label_hash == source["labelsSha256"].lower(),
                f"Frame {frame_id} source label hash does not match its manifest.")
        require(source.get("sourceImageOriginalSha256", image_hash).lower() == image_hash,
                f"Frame {frame_id} original image hash changed.")

        receipt_bytes = receipt_source.read_bytes()
        receipt = json.loads(receipt_bytes.decode("utf-8-sig"))
        indexed = receipt.get("indexedFrame", receipt)
        scope = indexed.get("scope", {})
        require(indexed.get("frameId") == frame_id, f"Frame {frame_id} receipt has a different frame ID.")
        require(indexed.get("pixelSha256", "").lower() == image_hash,
                f"Frame {frame_id} receipt does not bind its exact source image.")
        require(indexed.get("width") == source["width"] and indexed.get("height") == source["height"],
                f"Frame {frame_id} receipt dimensions differ from the review input.")
        require(scope.get("source") == "mounted-camera" and scope.get("timeline") and scope.get("calibration"),
                f"Frame {frame_id} receipt lacks mounted-camera timeline/calibration provenance.")

        record = read_json(review_record_path)
        review_frame = find_record_frame(record, frame_id)
        record_image, record_image_hash, record_receipt_path, record_receipt_hash = record_source_fields(review_frame)
        require(record_image.replace("\\", "/") == source["sourceImageOriginal"].replace("\\", "/"),
                f"Frame {frame_id} review record refers to a different image path.")
        require(record_image_hash == image_hash, f"Frame {frame_id} review record image hash differs.")
        require(relative_repo_path(record_receipt_path).resolve() == receipt_source.resolve(),
                f"Frame {frame_id} review record refers to a different receipt path.")
        receipt_hash = sha256(receipt_source)
        require(record_receipt_hash == receipt_hash, f"Frame {frame_id} review record receipt hash differs.")

        source_labels = read_json(source_label_path)
        require(source_labels.get("schemaVersion") == 2 and len(source_labels.get("frames", [])) == 1,
                f"Frame {frame_id} source labels are not a single reviewed frame.")
        source_label_frame = source_labels["frames"][0]
        require(source_label_frame.get("pixelSha256", "").lower() == image_hash,
                f"Frame {frame_id} labels do not bind the source image.")
        balls = source_label_frame.get("balls", [])
        ball_by_id = {ball["id"]: ball for ball in balls}
        require(len(ball_by_id) == len(balls), f"Frame {frame_id} has duplicate label IDs.")
        point_by_id = {point["id"]: point for point in source.get("points", [])}
        require(set(point_by_id).issubset(set(ball_by_id)), f"Frame {frame_id} centre manifest contains IDs absent from labels.")
        reviewed_by_id = reviewed_centres(review_frame)
        require(set(reviewed_by_id) == set(ball_by_id),
                f"Frame {frame_id} user review and source label ID sets differ.")
        for ball_id, ball in ball_by_id.items():
            point = point_by_id.get(ball_id)
            review_centre = reviewed_by_id[ball_id]
            x_px = float(ball["x"]) * int(source["width"])
            y_px = float(ball["y"]) * int(source["height"])
            require(math.hypot(x_px - review_centre["x"], y_px - review_centre["y"]) <= 1e-6,
                    f"Frame {frame_id}/{ball_id} O01 draft centre differs from the frame-specific user review.")
            require((ball["visibility"] == "Visible") == review_centre["visible"],
                    f"Frame {frame_id}/{ball_id} visibility differs from the user review.")
            if point is not None:
                require(abs(float(point["sourceXpx"]) - x_px) < 1e-6
                        and abs(float(point["sourceYpx"]) - y_px) < 1e-6,
                        f"Frame {frame_id}/{ball_id} source input centre differs from the reviewed centre.")
        guide_point_by_id = {point["id"]: point for point in guide.get("points", [])}
        require(len(guide_point_by_id) == len(guide.get("points", [])), f"Frame {frame_id} has duplicate guide IDs.")
        require(guide.get("sourceSha256", "").lower() == image_hash
                and guide.get("labelsSha256", "").lower() == source_label_hash,
                f"Frame {frame_id} guide is not bound to its exact image/centre labels.")

        output_dir = output / str(frame_id)
        output_dir.mkdir(parents=True)
        image_name = f"{frame_id}.png"
        shutil.copyfile(original_image, output_dir / image_name)
        shutil.copyfile(receipt_source, output_dir / f"{frame_id}.source-receipt.json")

        output_balls = []
        mapping_rows = []
        for ball_id, ball in ball_by_id.items():
            point = point_by_id.get(ball_id)
            if point is None:
                point = {"id": ball_id, "colour": ball["colour"],
                         "sourceXpx": reviewed_by_id[ball_id]["x"],
                         "sourceYpx": reviewed_by_id[ball_id]["y"],
                         "status": "user-reviewed-expected-empty-location"}
            require(point.get("colour") == ball.get("colour"), f"Frame {frame_id}/{ball_id} class differs.")
            x_px = float(point["sourceXpx"])
            y_px = float(point["sourceYpx"])
            width = int(source["width"])
            height = int(source["height"])
            x = float(ball["x"])
            y = float(ball["y"])
            require(abs(x * width - x_px) < 1e-6 and abs(y * height - y_px) < 1e-6,
                    f"Frame {frame_id}/{ball_id} normalized label differs from the reviewed source-pixel centre.")
            visibility = ball["visibility"]
            row = {"id": ball_id, "colour": ball["colour"], "x": x, "y": y, "visibility": visibility}
            mapped = mapped_point(matrix, x, y)
            back = mapped_point(inverse, *mapped)
            round_trip_px = math.hypot((back[0] - x) * width, (back[1] - y) * height)
            require(round_trip_px <= 1e-6, f"Frame {frame_id}/{ball_id} transform round-trip exceeds 1e-6 source px.")
            mapping_rows.append({"id": ball_id, "colour": ball["colour"], "visibility": visibility,
                                 "sourceXpx": x_px, "sourceYpx": y_px, "normalizedX": x, "normalizedY": y,
                                 "tableX": mapped[0], "tableY": mapped[1], "roundTripErrorPx": round_trip_px})
            if visibility == "Visible":
                guide_point = guide_point_by_id.get(ball_id)
                require(guide_point is not None and guide_point.get("status") == "provisional-guide-only",
                        f"Frame {frame_id}/{ball_id} has no provisional visible-rim guide.")
                require(guide_point.get("touchesSearchBoundary") is False,
                        f"Frame {frame_id}/{ball_id} guide touched its search boundary.")
                require(abs(float(guide_point["sourceXpx"]) - x_px) < 1e-6
                        and abs(float(guide_point["sourceYpx"]) - y_px) < 1e-6,
                        f"Frame {frame_id}/{ball_id} guide centre differs from the reviewed point.")
                radius = float(guide_point["radiusSourcePx"])
                require(radius > 0, f"Frame {frame_id}/{ball_id} reviewed apparent radius is not positive.")
                row["radiusX"] = radius / width
                row["radiusY"] = radius / height
                added_visible += 1
            else:
                require(visibility in {"Absent", "Occluded", "Uncertain"},
                        f"Frame {frame_id}/{ball_id} has unsupported visibility {visibility}.")
                require(ball_id not in guide_point_by_id,
                        f"Frame {frame_id}/{ball_id} absent/uncertain point has a radius guide.")
                added_absent += 1
            output_balls.append(row)

        require(set(guide_point_by_id) == {ball["id"] for ball in balls if ball["visibility"] == "Visible"},
                f"Frame {frame_id} guide coverage differs from visible label coverage.")
        sample_id = str(indexed["sampleId"])
        require(sample_id and sample_id.endswith(f"-{frame_id}"),
                f"Frame {frame_id} receipt sample ID is not operation/frame bound.")
        sequence_id = f"o09-expansion-{frame_id}-v2"
        calibration = str(scope["calibration"])
        frame_tags = ["user-reviewed-centres", "user-approved-apparent-source-image-radii"]
        if frame_id == 2831:
            frame_tags.append("lighting-mismatch-excluded-from-paired-claims")
        if frame_id in {959, 562}:
            frame_tags.append("cross-run-photometric-pair-unmatched")
        labels_out = {
            "schemaVersion": 2,
            "sequenceId": sequence_id,
            "annotator": "User-reviewed centres and user-approved apparent source-image radius guides",
            "calibrationVersion": calibration,
            "reviewed": True,
            "frames": [{"sampleId": sample_id, "pixelSha256": image_hash, "coordinateSpace": "image-normalized",
                        "balls": output_balls,
                        "notes": ("Centres are approximate user-reviewed source-image points; no numeric matching tolerance was set. "
                                  "The user visually approved the displayed cyan rim guides. Radii are apparent source-image radii, "
                                  "not physical cloth-plane radii. Absent locations are expected points only; chalk is excluded."),
                        "tags": frame_tags}],
            "intervals": [],
        }
        write_json(output_dir / "labels.json", labels_out)

        index_row = {
            "sampleId": sample_id,
            "frameId": frame_id,
            "sourceTimeUs": indexed.get("sourceTimeUs"),
            "scope": scope,
            "pixelFile": f"{frame_id}/{image_name}",
            "pixelSha256": image_hash,
            "encoding": indexed.get("encoding", "Png"),
            "width": width,
            "height": height,
            "embeddedFrameId": bool(indexed.get("embeddedFrameId", False)),
        }
        (output_dir / "frames.jsonl").write_text(json.dumps(index_row, separators=(",", ":")) + "\n", encoding="utf-8")

        control = str(source["control"])
        setup_group = GROUPS.get(control)
        require(setup_group is not None, f"Frame {frame_id} control group has no Development setup-group assignment.")
        target_layout = LAYOUTS[frame_id]
        tags = ["live-detect", "development", "o09-dev-expansion", f"control:{control}",
                f"setup-group:{setup_group}", f"control-layout:{target_layout}",
                f"ball-state:black:{'present' if any(b['id']=='B' and b['visibility']=='Visible' for b in balls) else 'absent'}",
                f"ball-state:cue:{'present' if any(b['id']=='C' and b['visibility']=='Visible' for b in balls) else 'absent'}"]
        if frame_id == 2831:
            tags.append("lighting:unmatched")
        if frame_id in {959, 562, 1691}:
            tags.append("lighting:stratum-unverified")
        source_file = f"{frame_id}/{image_name}"
        sequence = {
            "id": sequence_id,
            "session": str(scope["timeline"]),
            "kind": "Real",
            "split": "Development",
            "purpose": "StillRegression",
            "source": {"id": str(scope["source"]),
                       "origin": f"Exact lossless user-reviewed Development frame {frame_id}; receipt-bound staged {control} control.",
                       "file": source_file, "sha256": image_hash, "width": width, "height": height,
                       "clock": "RecordingMicroseconds", "calibrationVersion": calibration, "nominalFps": None},
            "indexFile": f"{frame_id}/frames.jsonl",
            "labelsFile": f"{frame_id}/labels.json",
            "notes": (f"One user-reviewed anchor from setup group {setup_group}; {LIGHTING_NOTES[frame_id]}. "
                      "Development only. Spatial screening/training data; no detector acceptance, numeric centre tolerance, "
                      "or physical radius claim."),
            "tags": tags,
        }
        added_sequences.append(sequence)

        corners = receipt.get("calibrationCorners")
        require(isinstance(corners, list) and len(corners) == 4,
                f"Frame {frame_id} receipt lacks four calibration corners.")
        new_corners = [{"x": float(corner["x"]), "y": float(corner["y"])} for corner in corners]
        require(all(abs(new[axis] - old[axis]) <= 1e-12
                    for new, old in zip(new_corners, base_corners, strict=True) for axis in ("x", "y")),
                f"Frame {frame_id} corners do not match the frozen TableCalibration matrix source.")
        corner_order = ["top-left", "top-right", "bottom-right", "bottom-left"]
        added_transform_rows.append({
            "frameId": frame_id, "sampleId": sample_id, "pixelSha256": image_hash,
            "receiptPath": str(receipt_source.relative_to(ROOT)).replace("\\", "/"),
            "receiptSha256": receipt_hash, "calibrationVersion": calibration,
            "cornerOrder": corner_order, "cornerCoordinatesNormalized": new_corners,
            "tableCalibrationMatrix": matrix,
            "sourcePointToTableMapping": mapping_rows,
            "transformProvenance": "Reused existing SkyEye.Core.TableCalibration matrix because all four receipt corners match exactly; calibration identity is preserved separately and camera pose identity is not inferred.",
        })
        provenance_rows.append({
            "frameId": frame_id, "sampleId": sample_id, "session": scope["timeline"],
            "sourceTimeUs": indexed.get("sourceTimeUs"), "pixelSha256": image_hash,
            "sourceReceiptPath": str(receipt_source.relative_to(ROOT)).replace("\\", "/"),
            "sourceReceiptSha256": receipt_hash, "reviewRecordPath": str(review_record_path.relative_to(ROOT)).replace("\\", "/"),
            "reviewRecordSha256": sha256(review_record_path), "sourceLabelPath": str(source_label_path.relative_to(ROOT)).replace("\\", "/"),
            "sourceLabelSha256": source_label_hash, "guideFrameId": guide["frameId"],
            "guideSheetPath": str((guide_path.parent / guide["guideSheet"]).relative_to(ROOT)).replace("\\", "/"),
            "guideSheetSha256": sha256(guide_path.parent / guide["guideSheet"]),
            "guidePoints": sum(point.get("radiusSourcePx") is not None for point in guide["points"]),
            "setupGroup": setup_group, "controlLayout": target_layout, "lightingNote": LIGHTING_NOTES[frame_id],
        })

    # Confirm the operation has not changed any user-reviewed source inputs.
    unchanged = sum(1 for path, digest in before_source_hashes.items() if sha256(Path(path)) == digest)
    require(unchanged == len(before_source_hashes), "One or more source review files changed during corpus creation.")

    # Label the prior control states explicitly for coverage reporting without
    # changing any pixel, label, receipt, split, or setup-group assignment.
    for sequence in manifest["sequences"]:
        sequence.setdefault("tags", []).append(normalize_control_layout_tag(sequence))
    manifest["id"] = "live-detect-20261101-black-cue-development-v4-o09-reviewed-radii-v2"
    manifest["sequences"].extend(added_sequences)
    write_json(output / "manifest.json", manifest)

    transform["schemaVersion"] = max(2, int(transform.get("schemaVersion", 1)))
    transform["frames"].extend(added_transform_rows)
    transform["reviewedExpansion"] = {
        "userResponse": args.reviewer_response,
        "reviewDate": args.review_date,
        "sourceReviewInputSha256": sha256(source_index_path),
        "radiusGuideManifestSha256": sha256(guide_path),
        "matrixReuseBasis": "The 28 receipt ordered corners in source/base and expansion frames are numerically identical; TableCalibration matrix retained from the already verified base transform.",
        "physicalRadiusClaim": False,
    }
    write_json(output / "source-to-table-transform.json", transform)

    old_contract = read_json(source_root / "acceptance-contract.json")
    old_contract_bytes = (source_root / "acceptance-contract.json").read_bytes()
    new_contract = copy.deepcopy(old_contract)
    new_contract["contractId"] = "live-detect-phone-acceptance-v2-dev-expansion"
    new_contract["status"] = "criteria-unchanged-refrozen-before-next-candidate"
    new_contract["versioning"]["supersedes"] = old_contract["contractId"]
    new_contract["versioning"]["sourceContractSha256"] = sha256_bytes(old_contract_bytes)
    new_contract["versioning"]["criteriaChanged"] = False
    new_contract["versioning"]["developmentCorpusId"] = manifest["id"]
    new_contract["versioning"]["holdoutExposure"] = "None; all added samples remain Development and Holdout is unopened."
    write_json(output / "acceptance-contract.json", new_contract)

    review_record = {
        "schemaVersion": 1,
        "evidenceKind": "user-review-of-source-image-radius-guides",
        "reviewer": "user in Codex chat",
        "reviewResponse": args.reviewer_response,
        "reviewDate": args.review_date,
        "scope": "Seven displayed cyan guide sheets for frames 2831, 542, 295, 288, 959, 562, and 1691; visible rims only. Absent B?/C? points and chalk received no radius.",
        "interpretation": "Accepted apparent source-image radii for this O09 Development derivative only. Not numeric radius accuracy, physical cloth-plane radius, or detector acceptance.",
        "sourceReviewInputManifestSha256": sha256(source_index_path),
        "guideManifestPath": str(guide_path.relative_to(ROOT)).replace("\\", "/"),
        "guideManifestSha256": sha256(guide_path),
        "frames": provenance_rows,
    }
    write_json(output / "additional-controls-radius-review-record.json", review_record)

    labels_total = sum(len(read_json(output / sequence["labelsFile"])["frames"][0]["balls"])
                       for sequence in manifest["sequences"])
    visible_total = sum(sum(ball["visibility"] == "Visible"
                            for ball in read_json(output / sequence["labelsFile"])["frames"][0]["balls"])
                        for sequence in manifest["sequences"])
    radii_total = sum(sum(ball["visibility"] == "Visible" and "radiusX" in ball and "radiusY" in ball
                          for ball in read_json(output / sequence["labelsFile"])["frames"][0]["balls"])
                      for sequence in manifest["sequences"])
    absent_total = labels_total - visible_total
    layout_ids = sorted({tag for sequence in manifest["sequences"] for tag in sequence.get("tags", [])
                          if tag.startswith("control-layout:")})
    setup_groups = sorted({tag.removeprefix("setup-group:") if tag.startswith("setup-group:") else tag
                           for sequence in manifest["sequences"] for tag in sequence.get("tags", [])
                           if tag.startswith("setup-group:") or tag.startswith("matched-setup-group-")})
    coverage = {
        "schemaVersion": 1,
        "corpusId": manifest["id"],
        "baseCorpusId": read_json(base_manifest_path)["id"],
        "baseManifestSha256": sha256_bytes(base_manifest_bytes),
        "newManifestSha256": sha256(output / "manifest.json"),
        "sequenceCount": len(manifest["sequences"]),
        "addedSequenceCount": len(added_sequences),
        "uniqueSourceSessions": len({sequence["session"] for sequence in manifest["sequences"]}),
        "setupGroupCount": len(setup_groups),
        "setupGroups": setup_groups,
        "reviewedTargetStateIds": layout_ids,
        "reviewedTargetStateCount": len(layout_ids),
        "labelRows": labels_total,
        "visibleBallLabels": visible_total,
        "visibleApparentRadii": radii_total,
        "expectedAbsentLocations": absent_total,
        "holdoutFrames": sum(sequence["split"] == "Holdout" for sequence in manifest["sequences"]),
        "lightingStrata": 0,
        "notes": [
            "Target-state IDs describe black/cue position/presence controls, not 13 independently varied full-table layouts.",
            "Most red/yellow positions remain from the same small arrangement family; rack/spread diversity is still inadequate for acceptance.",
            "No measured lighting stratum is assigned. Frame 2831 is explicitly unmatched; opposite-cue present/absent runs are photometrically unmatched.",
            "All additions are Development-only; no Holdout sample was copied or opened.",
        ],
    }
    write_json(output / "coverage-report.json", coverage)
    provenance = {
        "schemaVersion": 1,
        "corpusId": manifest["id"],
        "generatorPath": "scripts/Create-O09ReviewedControlExpansion.py",
        "generatorSha256": sha256(Path(__file__)),
        "sourceReviewInputManifestPath": str(source_index_path.relative_to(ROOT)).replace("\\", "/"),
        "sourceReviewInputManifestSha256": sha256(source_index_path),
        "radiusGuideManifestPath": str(guide_path.relative_to(ROOT)).replace("\\", "/"),
        "radiusGuideManifestSha256": sha256(guide_path),
        "reviewRecordPath": "additional-controls-radius-review-record.json",
        "reviewRecordSha256": sha256(output / "additional-controls-radius-review-record.json"),
        "sourceFileCount": len(before_source_hashes),
        "sourceFilesUnchanged": unchanged,
        "frames": provenance_rows,
    }
    write_json(output / "expansion-provenance.json", provenance)
    return {**coverage, "sourceFilesCheckedUnchanged": unchanged,
            "sourceFileCount": len(before_source_hashes), "output": str(output)}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-corpus", type=Path, required=True)
    parser.add_argument("--source-review-input", type=Path, required=True)
    parser.add_argument("--radius-guides", type=Path, required=True)
    parser.add_argument("--reviewer-response", required=True)
    parser.add_argument("--review-date", required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    try:
        result = create(args)
        print(json.dumps(result, indent=2))
        return 0
    except (OSError, ValueError, KeyError, TypeError, json.JSONDecodeError) as exc:
        parser.error(str(exc))
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
