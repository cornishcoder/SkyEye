#!/usr/bin/env python3
"""Create a separate O09 image-normalized corpus from explicitly reviewed radius guides."""

from __future__ import annotations

import argparse
import hashlib
import json
import shutil
from pathlib import Path
from typing import Any


def read_json(path: Path) -> Any:
    with path.open("r", encoding="utf-8-sig") as stream:
        return json.load(stream)


def write_json(path: Path, value: Any) -> None:
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-corpus", type=Path, required=True)
    parser.add_argument("--review-pack", type=Path, required=True)
    parser.add_argument("--radius-guides", type=Path, required=True)
    parser.add_argument("--reviewer-response", required=True)
    parser.add_argument("--review-date", required=True, help="ISO date for the user review")
    parser.add_argument("--output", type=Path, required=True, help="New derivative directory; existing directories are refused")
    args = parser.parse_args()

    source_root = args.source_corpus.resolve()
    pack_root = args.review_pack.resolve()
    guides_path = args.radius_guides.resolve()
    output = args.output.resolve()
    if output.exists():
        raise SystemExit(f"Refusing to overwrite existing derivative: {output}")
    if args.reviewer_response.strip().casefold() != "all look correct":
        raise SystemExit("This import command requires explicit approval of every displayed guide")

    manifest_path = source_root / "manifest.json"
    transform_path = source_root / "source-to-table-transform.json"
    manifest = read_json(manifest_path)
    transform = read_json(transform_path)
    pack = read_json(pack_root / "review-pack.json")
    guides = read_json(guides_path)
    if guides.get("status") != "provisional-human-review-required" or guides.get("pointCount") != pack.get("visibleLabelCount"):
        raise SystemExit("Radius guide report is not the complete provisional set for this review pack")
    if pack.get("radiusValuesProposed") != 0:
        raise SystemExit("The original read-only review pack unexpectedly contains radius values")

    guide_by_sample = {frame["sampleId"]: frame for frame in guides.get("frames", [])}
    transform_by_sample = {frame["sampleId"]: frame for frame in transform.get("frames", [])}
    if len(guide_by_sample) != len(guides.get("frames", [])) or len(transform_by_sample) != len(transform.get("frames", [])):
        raise SystemExit("Duplicate frame IDs in guide or transform report")

    original_image_hashes: dict[Path, str] = {}
    original_label_hashes: dict[Path, str] = {}
    visible_radius_count = 0
    normalized_centre_count = 0
    absent_count = 0
    expected_sample_ids = {frame["sampleId"] for frame in pack["frames"]}
    if set(guide_by_sample) != expected_sample_ids or set(transform_by_sample) != expected_sample_ids:
        raise SystemExit("Frame set differs between source labels, review pack, transform and radius guides")

    def ignore_stale_lock(directory: str, names: list[str]) -> set[str]:
        return {"split-lock.json"} if Path(directory).resolve() == source_root and "split-lock.json" in names else set()

    shutil.copytree(source_root, output, ignore=ignore_stale_lock)
    for sequence in manifest.get("sequences", []):
        image_path = source_root / sequence["source"]["file"]
        labels_path = source_root / sequence["labelsFile"]
        image_hash, label_hash = sha256(image_path), sha256(labels_path)
        original_image_hashes[image_path] = image_hash
        original_label_hashes[labels_path] = label_hash
        if image_hash.lower() != sequence["source"]["sha256"].lower():
            raise SystemExit(f"Source image hash does not match manifest: {image_path}")

        labels_doc = read_json(labels_path)
        if not labels_doc.get("reviewed"):
            raise SystemExit(f"Unreviewed source labels cannot enter this derivative: {labels_path}")
        if len(labels_doc.get("frames", [])) != 1:
            raise SystemExit(f"Expected one reviewed frame label per source sequence: {sequence['id']}")
        label_frame = labels_doc["frames"][0]
        sample_id = label_frame["sampleId"]
        if label_frame.get("pixelSha256", "").lower() != image_hash.lower():
            raise SystemExit(f"Label image hash mismatch: {sample_id}")
        guide_frame = guide_by_sample[sample_id]
        transform_frame = transform_by_sample[sample_id]
        if guide_frame.get("sourceSha256", "").lower() != image_hash.lower() \
                or transform_frame.get("pixelSha256", "").lower() != image_hash.lower():
            raise SystemExit(f"Guide/transform does not bind to source image: {sample_id}")
        width, height = sequence["source"]["width"], sequence["source"]["height"]
        point_by_id = {point["id"]: point for point in transform_frame.get("sourcePointToTableMapping", [])}
        approved_by_id = {point["id"]: point for point in guide_frame.get("points", [])}
        if len(point_by_id) != len(transform_frame.get("sourcePointToTableMapping", [])) \
                or len(approved_by_id) != len(guide_frame.get("points", [])):
            raise SystemExit(f"Duplicate point IDs in transform or review guide: {sample_id}")

        normalized_balls = []
        for ball in label_frame.get("balls", []):
            point = point_by_id.get(ball["id"])
            if point is None or point.get("colour") != ball.get("colour") or point.get("visibility") != ball.get("visibility"):
                raise SystemExit(f"Missing or mismatched source transform point: {sample_id}/{ball['id']}")
            if abs(float(point["tableX"]) - float(ball["x"])) > 1e-6 \
                    or abs(float(point["tableY"]) - float(ball["y"])) > 1e-6:
                raise SystemExit(f"Reviewed table centre differs from source transform: {sample_id}/{ball['id']}")
            source_x, source_y = int(point["sourceXpx"]), int(point["sourceYpx"])
            if not (0 <= source_x < width and 0 <= source_y < height):
                raise SystemExit(f"Transformed source centre is outside image: {sample_id}/{ball['id']}")
            converted = {**ball, "x": source_x / width, "y": source_y / height}
            normalized_centre_count += 1
            if ball["visibility"] == "Visible":
                approved = approved_by_id.get(ball["id"])
                if approved is None or approved.get("status") != "provisional-guide-only":
                    raise SystemExit(f"No user-reviewed guide for visible ball: {sample_id}/{ball['id']}")
                radius_px = float(approved["radiusSourcePx"])
                if not 0 < radius_px <= min(width, height) / 2:
                    raise SystemExit(f"Invalid reviewed source radius: {sample_id}/{ball['id']}")
                converted["radiusX"] = radius_px / width
                converted["radiusY"] = radius_px / height
                visible_radius_count += 1
            else:
                converted.pop("radiusX", None)
                converted.pop("radiusY", None)
                if ball["visibility"] == "Absent":
                    absent_count += 1
            normalized_balls.append(converted)

        label_frame["coordinateSpace"] = "image-normalized"
        label_frame["balls"] = normalized_balls
        label_frame["notes"] = (label_frame.get("notes", "")
            + f" User reviewed all {len(guide_frame['points'])} visible source-image radius guides on {args.review_date}: "
              f"{args.reviewer_response.strip()}. Radius values are apparent source-image pixels normalized by width/height; "
              "they are not physical cloth-plane radii. Centres are receipt-bound source pixel mappings.")
        labels_doc["annotator"] = "User-reviewed centres and apparent source-image radius guides via Codex"
        output_labels_path = output / sequence["labelsFile"]
        write_json(output_labels_path, labels_doc)

    manifest["id"] = manifest["id"] + "-o09-reviewed-radii-v1"
    write_json(output / "manifest.json", manifest)
    approval = {
        "schemaVersion": 1,
        "reviewer": "user",
        "reviewDate": args.review_date,
        "response": args.reviewer_response.strip(),
        "scope": "All cyan apparent image-space radius guides in the hash-bound guide report",
        "guideReportSha256": sha256(guides_path),
        "reviewPackSha256": sha256(pack_root / "review-pack.json"),
        "sourceManifestSha256": sha256(manifest_path),
        "transformSha256": sha256(transform_path),
        "physicalRadiusClaim": False,
    }
    write_json(output / "o09-radius-review-approval.json", approval)
    derivative_report = {
        "schemaVersion": 1,
        "sourceCorpusId": read_json(manifest_path)["id"],
        "derivedCorpusId": manifest["id"],
        "frameCount": len(manifest.get("sequences", [])),
        "normalizedCentreCount": normalized_centre_count,
        "visibleRadiusCount": visible_radius_count,
        "absentLabelCountWithoutRadius": absent_count,
        "sourceCoordinateNormalization": "x/width,y/height",
        "radiusNormalization": "radiusX=sourceRadiusPx/imageWidth; radiusY=sourceRadiusPx/imageHeight",
        "radiusApproval": approval,
        "sourceImageHashes": {str(path.relative_to(source_root)): digest for path, digest in original_image_hashes.items()},
        "sourceLabelHashes": {str(path.relative_to(source_root)): digest for path, digest in original_label_hashes.items()},
    }
    write_json(output / "o09-radius-derivation-report.json", derivative_report)

    unchanged_source_images = sum(sha256(path) == before for path, before in original_image_hashes.items())
    unchanged_source_labels = sum(sha256(path) == before for path, before in original_label_hashes.items())
    copied_images_match = sum(sha256(output / path.relative_to(source_root)) == digest
                              for path, digest in original_image_hashes.items())
    if unchanged_source_images != len(original_image_hashes) or unchanged_source_labels != len(original_label_hashes):
        raise SystemExit("Source corpus changed during derivative creation")
    if copied_images_match != len(original_image_hashes):
        raise SystemExit("Derivative source image hashes differ from O08 source images")
    if visible_radius_count != pack.get("visibleLabelCount") or normalized_centre_count != visible_radius_count + absent_count:
        raise SystemExit("Derived label counts do not match reviewed source points")

    print(f"PASS O09-RADIUS-LABEL-ROUNDTRIP-APPROVED-GUIDES {visible_radius_count}/{pack['visibleLabelCount']}")
    print(f"PASS O09-RADIUS-LABEL-ROUNDTRIP-NORMALIZED-CENTRES {normalized_centre_count}/{normalized_centre_count}")
    print(f"PASS O09-RADIUS-LABEL-ROUNDTRIP-SOURCE-IMAGES {copied_images_match}/{len(original_image_hashes)} copied hashes preserved")
    print(f"PASS O09-RADIUS-LABEL-ROUNDTRIP-ORIGINAL-IMMUTABILITY {unchanged_source_images + unchanged_source_labels}/{len(original_image_hashes) + len(original_label_hashes)} source files unchanged")
    print(f"INFO O09 absent labels with coordinates and no radius: {absent_count}")
    print(f"Reviewed-radius O09 derivative: {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
