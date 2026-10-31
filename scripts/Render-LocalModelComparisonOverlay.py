#!/usr/bin/env python3
"""Render source-hash-verified overlays for the saved PoolTableModeller run.

The cyan evaluation marker is a post-hoc interior-point / empty-pocket locator,
not a centre label and never an inference input. This is a visual diagnostic,
not an accuracy score.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from datetime import datetime, timezone
from pathlib import Path
from typing import Any

from PIL import Image, ImageDraw, ImageFont


ROOT = Path(__file__).resolve().parents[1]
EVIDENCE_ROOT = (ROOT / "work" / "production" / "evidence").resolve()
DEFAULT_RUN = Path(
    "work/production/evidence/code70-daylight-pocket-capture-20261031/"
    "local-model-comparison-20261031-135351"
)
DEFAULT_FRAME_IDS = ("1736", "978", "1198")
EVALUATION_POINT = (102.0, 903.0)
KNOWN_SOURCE_DIRS = {
    "black-present": Path("code70-daylight-pocket-capture-20261031/exact-replay-input"),
    "empty-pocket": Path("code65-source-frame-check-20261031/exact-replay-input"),
}
CLASS_COLOURS = {
    "red": (255, 64, 64),
    "yellow": (255, 220, 48),
    "black": (245, 64, 255),
    "cue": (240, 248, 255),
}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def inside(path: Path, parent: Path) -> bool:
    try:
        path.resolve().relative_to(parent.resolve())
        return True
    except ValueError:
        return False


def load_font(size: int) -> ImageFont.ImageFont:
    candidates = (
        Path("C:/Windows/Fonts/arial.ttf"),
        Path("C:/Windows/Fonts/consola.ttf"),
    )
    for candidate in candidates:
        if candidate.is_file():
            try:
                return ImageFont.truetype(str(candidate), size=size)
            except OSError:
                pass
    return ImageFont.load_default(size=size)


def load_inputs(run_dir: Path, frame_id: str, run_manifest: dict[str, Any]) -> tuple[dict[str, Any], Path]:
    record_path = run_dir / f"{frame_id}.json"
    if not record_path.is_file():
        raise FileNotFoundError(f"Missing per-frame result: {record_path}")
    record = json.loads(record_path.read_text(encoding="utf-8"))
    if str(record.get("frameId")) != frame_id:
        raise ValueError(f"Frame id mismatch in {record_path}")
    result = record.get("result")
    if not isinstance(result, dict) or result.get("success") is not True:
        raise ValueError(f"Inference result is absent or unsuccessful for frame {frame_id}")
    balls = result.get("balls")
    if not isinstance(balls, list) or not balls:
        raise ValueError(f"Frame {frame_id} has no ball detections")

    manifest_rows = run_manifest.get("frames")
    if not isinstance(manifest_rows, list):
        raise ValueError("Run manifest has no frames array")
    metadata = next((row for row in manifest_rows if str(row.get("frameId")) == frame_id), None)
    if metadata is None:
        raise ValueError(f"Frame {frame_id} is absent from run manifest")
    if metadata.get("sourceSha256", "").lower() != record.get("sourceSha256", "").lower():
        raise ValueError(f"Manifest/result source-hash mismatch for frame {frame_id}")
    if metadata.get("sourceFile") != record.get("sourceFile"):
        raise ValueError(f"Manifest/result source filename mismatch for frame {frame_id}")

    condition = record.get("condition")
    source_dir = metadata.get("sourceDirectory") or record.get("sourceDirectory")
    if source_dir:
        relative_dir = Path(source_dir)
    else:
        relative_dir = KNOWN_SOURCE_DIRS.get(condition)
    if relative_dir is None:
        raise ValueError(f"No source directory mapping for condition {condition!r}")
    source_path = (EVIDENCE_ROOT / relative_dir / record["sourceFile"]).resolve()
    if not inside(source_path, EVIDENCE_ROOT):
        raise ValueError(f"Resolved input escapes evidence root: {source_path}")
    if not source_path.is_file():
        raise FileNotFoundError(f"Source image not found: {source_path}")
    actual_hash = sha256(source_path)
    if actual_hash.lower() != record.get("sourceSha256", "").lower():
        raise ValueError(f"Source SHA-256 mismatch for frame {frame_id}: {actual_hash}")

    expected_size = result.get("image_size")
    if not isinstance(expected_size, dict):
        raise ValueError(f"Inference image dimensions are missing for frame {frame_id}")
    with Image.open(source_path) as source:
        actual_size = source.size
        source.verify()
    if actual_size != (expected_size.get("width"), expected_size.get("height")):
        raise ValueError(f"Source/result dimensions differ for frame {frame_id}: {actual_size} vs {expected_size}")

    width, height = actual_size
    for ball in balls:
        center = ball.get("center")
        radius = ball.get("radius")
        if (not isinstance(center, list) or len(center) != 2
                or not all(isinstance(v, (int, float)) and math.isfinite(v) for v in center)
                or not isinstance(radius, (int, float)) or not math.isfinite(radius) or radius <= 0):
            raise ValueError(f"Invalid ball geometry in frame {frame_id}: {ball.get('id')}")
        if not (0 <= center[0] < width and 0 <= center[1] < height):
            raise ValueError(f"Out-of-image ball centre in frame {frame_id}: {center}")

    return record, source_path


def draw_overlay(record: dict[str, Any], source_path: Path) -> Image.Image:
    result = record["result"]
    image = Image.open(source_path).convert("RGB")
    width, height = image.size
    draw = ImageDraw.Draw(image)
    font = load_font(26)
    small_font = load_font(18)

    header_height = 94
    draw.rectangle((0, 0, width, header_height), fill=(13, 18, 28))
    condition = record.get("condition", "unknown")
    counts = result.get("counts", {})
    draw.text((20, 10),
              f"Frame {record['frameId']}  |  {condition}  |  "
              f"balls={result.get('balls_count')}  "
              f"R={counts.get('red')} Y={counts.get('yellow')} "
              f"8={counts.get('black')} cue={counts.get('cue')}",
              font=font, fill=(255, 255, 255))
    draw.text((20, 54),
              "Cyan cross = post-hoc interior point / empty-pocket locator; not a centre label or detector input",
              font=small_font, fill=(80, 235, 255))

    # The marker is intentionally a small cross only: the point is not a centre.
    px, py = EVALUATION_POINT
    marker_size = 11
    draw.line((px - marker_size, py, px + marker_size, py), fill=(20, 245, 255), width=3)
    draw.line((px, py - marker_size, px, py + marker_size), fill=(20, 245, 255), width=3)
    draw.ellipse((px - 3, py - 3, px + 3, py + 3), fill=(20, 245, 255))

    for ball in result["balls"]:
        cx, cy = float(ball["center"][0]), float(ball["center"][1])
        radius = float(ball["radius"])
        kind = str(ball.get("type", "unknown"))
        colour = CLASS_COLOURS.get(kind, (255, 255, 255))
        width_px = 4 if ball.get("physics_adjusted") else 3
        draw.ellipse((cx - radius, cy - radius, cx + radius, cy + radius), outline=colour, width=width_px)
        arm = 8
        draw.line((cx - arm, cy, cx + arm, cy), fill=colour, width=2)
        draw.line((cx, cy - arm, cx, cy + arm), fill=colour, width=2)
        source = str(ball.get("source", "unknown"))
        adjusted = "  [physics adjusted]" if ball.get("physics_adjusted") else ""
        confidence = float(ball.get("conf", 0.0))
        label = f"{kind} {confidence:.2f} {source}{adjusted}"
        left = min(width - 500, max(4, int(cx + radius + 7)))
        top = max(header_height + 3, min(height - 30, int(cy - 16)))
        bbox = draw.textbbox((left, top), label, font=small_font, stroke_width=2)
        draw.rectangle((bbox[0] - 3, bbox[1] - 2, bbox[2] + 3, bbox[3] + 2), fill=(10, 14, 22))
        draw.text((left, top), label, font=small_font, fill=colour, stroke_width=2, stroke_fill=(0, 0, 0))

    # Restore the header over detections that happen to overlap it.
    draw.rectangle((0, 0, width, header_height), fill=(13, 18, 28))
    draw.text((20, 10),
              f"Frame {record['frameId']}  |  {condition}  |  "
              f"balls={result.get('balls_count')}  "
              f"R={counts.get('red')} Y={counts.get('yellow')} "
              f"8={counts.get('black')} cue={counts.get('cue')}",
              font=font, fill=(255, 255, 255))
    draw.text((20, 54),
              "Cyan cross = post-hoc interior point / empty-pocket locator; not a centre label or detector input",
              font=small_font, fill=(80, 235, 255))
    return image


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run-dir", default=str(DEFAULT_RUN), help="saved model comparison run directory")
    parser.add_argument("--frames", nargs="+", default=list(DEFAULT_FRAME_IDS), help="frame IDs to render")
    args = parser.parse_args()

    run_dir = Path(args.run_dir)
    if not run_dir.is_absolute():
        run_dir = ROOT / run_dir
    run_dir = run_dir.resolve()
    if not inside(run_dir, EVIDENCE_ROOT) or not run_dir.is_dir():
        raise ValueError(f"Run directory must exist under {EVIDENCE_ROOT}: {run_dir}")
    run_manifest_path = run_dir / "manifest.json"
    if not run_manifest_path.is_file():
        raise FileNotFoundError(f"Missing run manifest: {run_manifest_path}")
    run_manifest = json.loads(run_manifest_path.read_text(encoding="utf-8"))

    output_dir = run_dir / "overlays"
    output_dir.mkdir(parents=True, exist_ok=True)
    outputs: list[dict[str, Any]] = []
    panels: list[tuple[str, Image.Image]] = []
    for frame_id in args.frames:
        record, source_path = load_inputs(run_dir, str(frame_id), run_manifest)
        overlay = draw_overlay(record, source_path)
        output_path = output_dir / f"local-model-{frame_id}.png"
        overlay.save(output_path, format="PNG", optimize=True)
        with Image.open(output_path) as check:
            if check.size != overlay.size or check.format != "PNG":
                raise ValueError(f"Rendered overlay failed reopen check: {output_path}")
        outputs.append({
            "frameId": str(frame_id),
            "condition": record.get("condition"),
            "sourcePath": str(source_path.relative_to(EVIDENCE_ROOT)),
            "sourceSha256": record["sourceSha256"],
            "resultJsonSha256": sha256(run_dir / f"{frame_id}.json"),
            "outputPath": str(output_path.relative_to(run_dir)),
            "outputSha256": sha256(output_path),
            "width": overlay.width,
            "height": overlay.height,
            "ballCount": len(record["result"]["balls"]),
            "manualPointSemantics": "post-hoc interior-point / empty-pocket locator; not a centre label",
        })
        panels.append((str(frame_id), overlay))

    if len(panels) > 1:
        panel_width = 640
        panel_height = 390
        sheet = Image.new("RGB", (panel_width * len(panels), panel_height), (18, 22, 30))
        sheet_draw = ImageDraw.Draw(sheet)
        title_font = load_font(22)
        for i, (frame_id, overlay) in enumerate(panels):
            resized = overlay.resize((panel_width, 360), Image.Resampling.LANCZOS)
            x = i * panel_width
            sheet.paste(resized, (x, 30))
            sheet_draw.rectangle((x, 0, x + panel_width - 1, 29), fill=(13, 18, 28))
            sheet_draw.text((x + 10, 4), f"Frame {frame_id}", font=title_font, fill=(255, 255, 255))
        contact_path = output_dir / "local-model-contact-sheet.png"
        sheet.save(contact_path, format="PNG", optimize=True)
        outputs.append({
            "frameId": None,
            "outputPath": str(contact_path.relative_to(run_dir)),
            "outputSha256": sha256(contact_path),
            "width": sheet.width,
            "height": sheet.height,
            "panels": [frame_id for frame_id, _ in panels],
        })

    overlay_manifest = {
        "tool": "Render-LocalModelComparisonOverlay.py",
        "schemaVersion": 1,
        "createdAtUtc": datetime.now(timezone.utc).isoformat(),
        "diagnosticOnly": True,
        "sourceImageChanges": False,
        "manualPoint": {
            "x": EVALUATION_POINT[0],
            "y": EVALUATION_POINT[1],
            "meaning": "post-hoc inspected interior point for positive frames / corresponding empty-pocket locator for controls; not a centre label and not inference input",
        },
        "outputs": outputs,
    }
    (output_dir / "overlay-manifest.json").write_text(
        json.dumps(overlay_manifest, indent=2) + "\n", encoding="utf-8"
    )
    print(json.dumps(overlay_manifest, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
