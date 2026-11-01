#!/usr/bin/env python3
"""Create read-only, source-bound visual crops for reviewing O09 ball radii."""

from __future__ import annotations

import argparse
import csv
import hashlib
import json
import math
from pathlib import Path
from typing import Any

from PIL import Image, ImageDraw, ImageFont


def read_json(path: Path) -> Any:
    with path.open("r", encoding="utf-8-sig") as stream:
        return json.load(stream)


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def font(size: int) -> ImageFont.ImageFont:
    for candidate in (r"C:\Windows\Fonts\arial.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"):
        try:
            return ImageFont.truetype(candidate, size=size)
        except OSError:
            pass
    return ImageFont.load_default()


def draw_crosshair(draw: ImageDraw.ImageDraw, x: int, y: int, *, scale: int, color: tuple[int, int, int]) -> None:
    # A compact point marker leaves the visible rim unobscured for radius review.
    arm = max(2, 3 * scale)
    draw.line((x - arm, y, x + arm, y), fill=(0, 0, 0), width=max(2, scale + 1))
    draw.line((x, y - arm, x, y + arm), fill=(0, 0, 0), width=max(2, scale + 1))
    draw.line((x - arm, y, x + arm, y), fill=color, width=max(1, scale))
    draw.line((x, y - arm, x, y + arm), fill=color, width=max(1, scale))
    dot = max(1, scale)
    draw.ellipse((x - dot, y - dot, x + dot, y + dot), fill=color, outline=(0, 0, 0))


def point_rgb(colour: str) -> tuple[int, int, int]:
    return {
        "red": (255, 62, 90),
        "yellow": (255, 220, 35),
        "black": (250, 250, 250),
        "cue": (30, 240, 255),
        "unknown": (255, 255, 255),
    }.get(colour.lower(), (255, 255, 255))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--corpus-root", type=Path, required=True, help="O01 corpus-v4 directory")
    parser.add_argument("--transform", type=Path, required=True, help="Receipt-bound source-to-table-transform.json")
    parser.add_argument("--output", type=Path, required=True, help="New output directory; existing directories are refused")
    args = parser.parse_args()

    corpus_root = args.corpus_root.resolve()
    transform_path = args.transform.resolve()
    output = args.output.resolve()
    if output.exists():
        raise SystemExit(f"Refusing to overwrite existing output: {output}")
    manifest_path = corpus_root / "manifest.json"
    manifest = read_json(manifest_path)
    transform = read_json(transform_path)
    if transform.get("sourceCoordinateSpace", {}).get("normalization") != "x/width,y/height":
        raise SystemExit("Unsupported source coordinate normalization")

    transform_by_sample = {frame["sampleId"]: frame for frame in transform.get("frames", [])}
    if len(transform_by_sample) != len(transform.get("frames", [])):
        raise SystemExit("Transform contains duplicate sample IDs")

    # Record input hashes before generation; compare again afterwards.
    input_hashes: dict[Path, str] = {}
    entries: list[dict[str, Any]] = []
    template_rows: list[dict[str, Any]] = []
    frame_binding_count = 0
    visible_count = 0
    mapping_count = 0

    output.mkdir(parents=True)
    frame_output = output / "frames"
    frame_output.mkdir()

    for sequence in sorted(manifest.get("sequences", []), key=lambda item: item["source"]["file"]):
        image_path = (corpus_root / sequence["source"]["file"]).resolve()
        labels_path = (corpus_root / sequence["labelsFile"]).resolve()
        input_hashes[image_path] = sha256(image_path)
        input_hashes[labels_path] = sha256(labels_path)
        source_sha = input_hashes[image_path]
        labels_sha = input_hashes[labels_path]
        if source_sha.lower() != sequence["source"]["sha256"].lower():
            raise SystemExit(f"Image hash does not match manifest: {image_path}")

        labels_doc = read_json(labels_path)
        visible_frames = [frame for frame in labels_doc.get("frames", []) if frame.get("balls")]
        if len(visible_frames) != 1:
            raise SystemExit(f"Expected one reviewed label frame for {sequence['id']}")
        label_frame = visible_frames[0]
        sample_id = label_frame["sampleId"]
        if label_frame.get("pixelSha256", "").lower() != source_sha.lower():
            raise SystemExit(f"Label pixel hash does not match source image: {sample_id}")
        transform_frame = transform_by_sample.get(sample_id)
        if transform_frame is None:
            raise SystemExit(f"Missing transform frame for {sample_id}")
        if transform_frame.get("pixelSha256", "").lower() != source_sha.lower():
            raise SystemExit(f"Transform pixel hash does not match source image: {sample_id}")
        frame_binding_count += 1

        with Image.open(image_path) as decoded:
            source = decoded.convert("RGB")
        width, height = source.size
        if (width, height) != (sequence["source"]["width"], sequence["source"]["height"]):
            raise SystemExit(f"Decoded dimensions differ from manifest: {image_path}")

        mapping_rows = transform_frame.get("sourcePointToTableMapping", [])
        mapping_by_id = {point["id"]: point for point in mapping_rows}
        if len(mapping_by_id) != len(mapping_rows):
            raise SystemExit(f"Duplicate mapped IDs in {sample_id}")
        points: list[dict[str, Any]] = []
        for ball in label_frame.get("balls", []):
            if ball.get("visibility") != "Visible":
                continue
            point = mapping_by_id.get(ball["id"])
            if point is None:
                raise SystemExit(f"Visible label has no source point: {sample_id}/{ball['id']}")
            if point.get("visibility") != "Visible" or point.get("colour") != ball.get("colour"):
                raise SystemExit(f"Label/source-point identity mismatch: {sample_id}/{ball['id']}")
            if abs(float(point["tableX"]) - float(ball["x"])) > 1e-6 or abs(float(point["tableY"]) - float(ball["y"])) > 1e-6:
                raise SystemExit(f"Label/source-point transform mismatch: {sample_id}/{ball['id']}")
            x, y = int(point["sourceXpx"]), int(point["sourceYpx"])
            if not (0 <= x < width and 0 <= y < height):
                raise SystemExit(f"Mapped centre is outside the source image: {sample_id}/{ball['id']}")
            points.append({
                "id": ball["id"], "colour": ball["colour"], "sourceXpx": x, "sourceYpx": y,
                "tableX": ball["x"], "tableY": ball["y"],
            })
            template_rows.append({
                "frameId": transform_frame["frameId"], "sampleId": sample_id,
                "sourceSha256": source_sha, "id": ball["id"], "colour": ball["colour"],
                "sourceXpx": x, "sourceYpx": y, "radiusXpx": "", "radiusYpx": "",
                "uncertaintyXpx": "", "uncertaintyYpx": "", "reviewStatus": "pending", "reviewNotes": "",
            })
            mapping_count += 1

        visible_count += len(points)
        frame_id = transform_frame["frameId"]
        overview = source.copy()
        overview_draw = ImageDraw.Draw(overview)
        for point in points:
            x, y = point["sourceXpx"], point["sourceYpx"]
            color = point_rgb(point["colour"])
            draw_crosshair(overview_draw, x, y, scale=1, color=color)
            label_text = point["id"]
            box = overview_draw.textbbox((x + 8, y + 7), label_text, font=font(18), stroke_width=2)
            overview_draw.rounded_rectangle((box[0] - 4, box[1] - 2, box[2] + 4, box[3] + 2), radius=3,
                                            fill=(0, 0, 0), outline=color, width=2)
            overview_draw.text((x + 8, y + 7), label_text, fill=(255, 255, 255), font=font(18), stroke_width=0)
        overview_path = frame_output / f"{frame_id}-overview.png"
        overview.save(overview_path, format="PNG", optimize=True)

        crop_size, magnification = 112, 3
        tile_image_size = crop_size * magnification
        columns, cell_height = 5, tile_image_size + 38
        rows = math.ceil(len(points) / columns)
        sheet = Image.new("RGB", (columns * (tile_image_size + 20), rows * cell_height), (34, 38, 44))
        sheet_draw = ImageDraw.Draw(sheet)
        for index, point in enumerate(points):
            column, row = index % columns, index // columns
            x0, y0 = column * (tile_image_size + 20) + 10, row * cell_height + 32
            cx, cy = point["sourceXpx"], point["sourceYpx"]
            left, top = cx - crop_size // 2, cy - crop_size // 2
            crop = Image.new("RGB", (crop_size, crop_size), (24, 27, 31))
            region = source.crop((left, top, left + crop_size, top + crop_size))
            crop.paste(region, (max(0, -left), max(0, -top)))
            enlarged = crop.resize((tile_image_size, tile_image_size), Image.Resampling.LANCZOS)
            draw_crosshair(ImageDraw.Draw(enlarged), (cx - left) * magnification,
                           (cy - top) * magnification, scale=1, color=point_rgb(point["colour"]))
            sheet.paste(enlarged, (x0, y0))
            caption = f"{point['id']}  ({cx}, {cy}) px"
            sheet_draw.text((x0, row * cell_height + 7), caption, fill=(245, 245, 245), font=font(20))
        crops_path = frame_output / f"{frame_id}-centre-crops.png"
        sheet.save(crops_path, format="PNG", optimize=True)

        entries.append({
            "frameId": frame_id, "sampleId": sample_id, "sequenceId": sequence["id"],
            "sourceImage": sequence["source"]["file"], "sourceSha256": source_sha,
            "labelsFile": sequence["labelsFile"], "labelsSha256": labels_sha,
            "transformFile": str(transform_path), "calibrationVersion": transform_frame.get("calibrationVersion"),
            "width": width, "height": height, "visibleBallCount": len(points),
            "overview": f"frames/{overview_path.name}", "centreCropSheet": f"frames/{crops_path.name}",
            "points": points,
        })

    template_path = output / "radius-review-template.csv"
    fieldnames = ["frameId", "sampleId", "sourceSha256", "id", "colour", "sourceXpx", "sourceYpx",
                  "radiusXpx", "radiusYpx", "uncertaintyXpx", "uncertaintyYpx", "reviewStatus", "reviewNotes"]
    with template_path.open("w", encoding="utf-8", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=fieldnames)
        writer.writeheader()
        writer.writerows(template_rows)

    # Inputs are read-only: re-hash every image and label file before reporting pass.
    unchanged_count = 0
    for path, before_hash in input_hashes.items():
        if sha256(path) != before_hash:
            raise SystemExit(f"Input changed during pack generation: {path}")
        unchanged_count += 1

    index = {
        "schemaVersion": 1,
        "purpose": "Human visual review of source-image ball radii; this pack does not propose or write radius labels.",
        "corpusManifest": str(manifest_path), "corpusId": manifest.get("id"),
        "transformFile": str(transform_path), "frameCount": frame_binding_count,
        "visibleLabelCount": visible_count, "mappedCentreCount": mapping_count,
        "inputFilesUnchanged": unchanged_count, "radiusValuesProposed": 0,
        "frames": entries,
    }
    index_path = output / "review-pack.json"
    index_path.write_text(json.dumps(index, indent=2) + "\n", encoding="utf-8")
    readme = """# O09 source radius review pack

This pack is a read-only visual aid for reviewing ball radii. Each full-frame overview and enlarged crop sheet is derived from an exact source image whose SHA-256 is recorded in `review-pack.json`. The compact crosshair marks the previously reviewed centre, mapped through the saved source-to-table transform. Crop sheets show the source rim without a radius guide, detector overlay, or fitted contour.

`radius-review-template.csv` contains one row per visible labelled ball. Radius and uncertainty columns are intentionally blank. Do not copy values into O01 labels unless a reviewer explicitly supplies and approves them. These images are for apparent image-space radii only; they do not establish physical cloth-plane radius or remove sphere-height/parallax uncertainty.
"""
    (output / "README.md").write_text(readme, encoding="utf-8")

    if frame_binding_count != len(manifest.get("sequences", [])):
        raise SystemExit("Frame binding count does not cover every manifest sequence")
    if visible_count != mapping_count:
        raise SystemExit("Visible label/mapped centre counts differ")
    print(f"PASS O09-RADIUS-LABEL-PACK-FRAME-BINDING {frame_binding_count}/{len(manifest.get('sequences', []))}")
    print(f"PASS O09-RADIUS-LABEL-PACK-VISIBLE-CROP-COVERAGE {mapping_count}/{visible_count}")
    print(f"PASS O09-RADIUS-LABEL-PACK-INPUT-IMMUTABILITY {unchanged_count}/{len(input_hashes)} source image/label files unchanged")
    print(f"PASS O09-RADIUS-LABEL-PACK-NO-RADIUS-PROPOSALS 0 values written; template rows={len(template_rows)}")
    print(f"Review pack: {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
