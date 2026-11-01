#!/usr/bin/env python3
"""Create provisional source-image edge guides for human ball-radius review."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
from typing import Any

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont


PARAMETERS = {
    "method": "median radial gradient in source-image CIELAB channels",
    "cropSizeSourcePx": 112,
    "gaussianKernel": 5,
    "gaussianSigma": 1.0,
    "angleCount": 360,
    "radiusSearchSourcePx": [12, 31],
    "selectionStatistic": "maximum 35th-percentile gradient response across angles",
    "guideColour": "cyan",
    "status": "provisional-guide-only; not reviewed labels",
}


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
    try:
        return ImageFont.truetype(r"C:\Windows\Fonts\arial.ttf", size=size)
    except OSError:
        return ImageFont.load_default()


def centre_colour(colour: str) -> tuple[int, int, int]:
    return {
        "red": (255, 65, 90), "yellow": (255, 220, 35), "black": (250, 250, 250),
        "cue": (35, 240, 255), "unknown": (255, 255, 255),
    }.get(colour.lower(), (255, 255, 255))


def radial_gradient_proposal(crop_bgr: np.ndarray) -> dict[str, Any]:
    half = PARAMETERS["cropSizeSourcePx"] // 2
    lab = cv2.cvtColor(crop_bgr, cv2.COLOR_BGR2LAB)
    lab = cv2.GaussianBlur(lab, (PARAMETERS["gaussianKernel"], PARAMETERS["gaussianKernel"]),
                           PARAMETERS["gaussianSigma"]).astype(np.float32)
    gradients_x = np.stack([cv2.Sobel(lab[:, :, channel], cv2.CV_32F, 1, 0, ksize=3) for channel in range(3)])
    gradients_y = np.stack([cv2.Sobel(lab[:, :, channel], cv2.CV_32F, 0, 1, ksize=3) for channel in range(3)])
    magnitude = np.sqrt(np.sum(gradients_x * gradients_x + gradients_y * gradients_y, axis=0))
    theta = np.linspace(0, 2 * np.pi, PARAMETERS["angleCount"], endpoint=False, dtype=np.float32)
    scores: list[tuple[float, float, int]] = []
    lower, upper = PARAMETERS["radiusSearchSourcePx"]
    for radius in range(lower, upper + 1):
        map_x = (half + radius * np.cos(theta)).astype(np.float32).reshape(1, -1)
        map_y = (half + radius * np.sin(theta)).astype(np.float32).reshape(1, -1)
        ring = cv2.remap(magnitude, map_x, map_y, cv2.INTER_LINEAR).ravel()
        scores.append((float(np.quantile(ring, 0.35)), float(np.median(ring)), radius))
    # The radius is a proposal only. The score is retained for inspection and
    # no threshold converts it into a label or an acceptance result.
    best = max(scores, key=lambda item: (item[0], item[1], -item[2]))
    return {
        "radiusSourcePx": float(best[2]),
        "response35thPercentile": round(best[0], 3),
        "responseMedian": round(best[1], 3),
        "touchesSearchBoundary": best[2] in (lower, upper),
        "status": "provisional-guide-only",
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--corpus-root", type=Path, required=True)
    parser.add_argument("--review-pack", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True, help="New output directory; existing directories are refused")
    args = parser.parse_args()

    corpus_root = args.corpus_root.resolve()
    pack_root = args.review_pack.resolve()
    output = args.output.resolve()
    if output.exists():
        raise SystemExit(f"Refusing to overwrite existing output: {output}")
    pack = read_json(pack_root / "review-pack.json")
    if pack.get("radiusValuesProposed") != 0:
        raise SystemExit("Input review pack unexpectedly contains radius proposals")

    output.mkdir(parents=True)
    source_files: dict[Path, str] = {}
    label_files: dict[Path, str] = {}
    frame_reports: list[dict[str, Any]] = []
    total_points = 0
    boundary_search_count = 0

    for frame in pack.get("frames", []):
        source_path = (corpus_root / frame["sourceImage"]).resolve()
        labels_path = (corpus_root / frame["labelsFile"]).resolve()
        source_hash, labels_hash = sha256(source_path), sha256(labels_path)
        if source_hash.lower() != frame["sourceSha256"].lower():
            raise SystemExit(f"Source hash changed since review-pack creation: {source_path}")
        if labels_hash.lower() != frame["labelsSha256"].lower():
            raise SystemExit(f"Label hash changed since review-pack creation: {labels_path}")
        source_files[source_path] = source_hash
        label_files[labels_path] = labels_hash
        labels_doc = read_json(labels_path)
        if any("radiusX" in ball or "radiusY" in ball for label_frame in labels_doc.get("frames", [])
               for ball in label_frame.get("balls", [])):
            raise SystemExit(f"Input O01 labels already contain radii: {labels_path}")

        image_bgr = cv2.imread(str(source_path), cv2.IMREAD_COLOR)
        if image_bgr is None or (image_bgr.shape[1], image_bgr.shape[0]) != (frame["width"], frame["height"]):
            raise SystemExit(f"Could not decode expected source dimensions: {source_path}")
        points: list[dict[str, Any]] = []
        for point in frame["points"]:
            total_points += 1
            x, y = int(point["sourceXpx"]), int(point["sourceYpx"])
            half = PARAMETERS["cropSizeSourcePx"] // 2
            crop = image_bgr[y - half:y + half, x - half:x + half]
            if crop.shape[:2] != (2 * half, 2 * half):
                raise SystemExit(f"Centre crop is clipped: frame{frame['frameId']}/{point['id']}")
            proposal = radial_gradient_proposal(crop)
            boundary_search_count += int(proposal["touchesSearchBoundary"])
            points.append({**point, **proposal})

        columns, cell_width, cell_height = 5, 356, 374
        sheet = Image.new("RGB", (columns * cell_width, math.ceil(len(points) / columns) * cell_height), (34, 38, 44))
        sheet_draw = ImageDraw.Draw(sheet)
        for index, point in enumerate(points):
            col, row = index % columns, index // columns
            x0, y0 = col * cell_width + 10, row * cell_height + 32
            source_x, source_y = int(point["sourceXpx"]), int(point["sourceYpx"])
            half = PARAMETERS["cropSizeSourcePx"] // 2
            crop = image_bgr[source_y - half:source_y + half, source_x - half:source_x + half]
            crop_rgb = cv2.cvtColor(crop, cv2.COLOR_BGR2RGB)
            enlarged = Image.fromarray(crop_rgb).resize((336, 336), Image.Resampling.LANCZOS)
            draw = ImageDraw.Draw(enlarged)
            cx, cy = half * 3, half * 3
            radius = round(float(point["radiusSourcePx"]) * 3)
            draw.ellipse((cx - radius, cy - radius, cx + radius, cy + radius), outline=(0, 255, 255), width=3)
            color = centre_colour(point["colour"])
            draw.line((cx - 6, cy, cx + 6, cy), fill=(0, 0, 0), width=3)
            draw.line((cx, cy - 6, cx, cy + 6), fill=(0, 0, 0), width=3)
            draw.line((cx - 5, cy, cx + 5, cy), fill=color, width=1)
            draw.line((cx, cy - 5, cx, cy + 5), fill=color, width=1)
            sheet.paste(enlarged, (x0, y0))
            edge_flag = " !range" if point["touchesSearchBoundary"] else ""
            caption = f"{point['id']} r{radius / 3:.0f}px q35={point['response35thPercentile']:.0f}{edge_flag}"
            sheet_draw.text((x0, row * cell_height + 7), caption, fill=(245, 245, 245), font=font(18))

        guide_path = output / f"{frame['frameId']}-radius-guides.png"
        sheet.save(guide_path, format="PNG", optimize=True)
        frame_reports.append({
            "frameId": frame["frameId"], "sampleId": frame["sampleId"],
            "sourceSha256": source_hash, "labelsSha256": labels_hash,
            "guideSheet": guide_path.name, "points": points,
        })

    unchanged_images = sum(sha256(path) == before for path, before in source_files.items())
    unchanged_labels = sum(sha256(path) == before for path, before in label_files.items())
    if unchanged_images != len(source_files) or unchanged_labels != len(label_files):
        raise SystemExit("Input image/label immutability check failed")
    accounted = sum(len(frame["points"]) for frame in frame_reports)
    if accounted != total_points:
        raise SystemExit("Not every visible source point received a proposal record")

    report = {
        "schemaVersion": 1,
        "status": "provisional-human-review-required",
        "purpose": "Source-pixel edge guides only; these are not reviewed radii or O09 labels.",
        "methodParameters": PARAMETERS,
        "inputReviewPack": str((pack_root / "review-pack.json").resolve()),
        "frameCount": len(frame_reports), "pointCount": total_points,
        "provisionalGuideCount": total_points, "searchBoundaryGuideCount": boundary_search_count,
        "inputImageFilesUnchanged": unchanged_images, "inputLabelFilesUnchanged": unchanged_labels,
        "frames": frame_reports,
    }
    (output / "radius-guides.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    (output / "README.md").write_text(
        "# Provisional source-image radius guides\n\n"
        "Cyan circles show a radial CIELAB image-gradient proposal computed from each source-image crop. The cross remains the previously reviewed centre. `q35` is the 35th-percentile edge response around the ring; `!range` means the strongest response hit the searched radius boundary. These values are visual aids only. Do not copy them into O01 or use them for O09 export without human review.\n",
        encoding="utf-8")

    print(f"PASS O09-IMAGE-EDGE-RADIUS-GUIDES-INPUT-IMMUTABILITY {unchanged_images + unchanged_labels}/{len(source_files) + len(label_files)} source image/label files unchanged")
    print(f"PASS O09-IMAGE-EDGE-RADIUS-GUIDES-POINT-ACCOUNTING {accounted}/{total_points} visible labels have a proposal record")
    print(f"INFO O09-IMAGE-EDGE-RADIUS-GUIDES-SEARCH-BOUNDARY {boundary_search_count}/{total_points}; review those guides carefully")
    print("INFO O09-IMAGE-EDGE-RADIUS-GUIDES-ACCEPTANCE none; human review required")
    print(f"Review overlays: {output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
