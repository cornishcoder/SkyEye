#!/usr/bin/env python3
"""Render a conservative, provenance-checked PocketArc diagnostic contact sheet.

The renderer annotates saved width-1000 dark-mask overlays only. It does not
change detector results or imply that a diagnostic candidate is a detection.
Requires Pillow (PIL).
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import sys
from pathlib import Path
from typing import Any, NoReturn

from PIL import Image, ImageDraw, ImageFont


def fail(message: str) -> NoReturn:
    raise ValueError(message)


def read_json(path: Path) -> dict[str, Any]:
    try:
        value = json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        fail(f"Cannot read valid JSON from {path}: {exc}")
    if not isinstance(value, dict):
        fail(f"Expected a JSON object in {path}.")
    return value


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    try:
        with path.open("rb") as stream:
            for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                digest.update(chunk)
    except OSError as exc:
        fail(f"Cannot hash {path}: {exc}")
    return digest.hexdigest()


def number(value: Any, context: str) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        fail(f"{context} must be numeric; got {value!r}.")
    value = float(value)
    if not math.isfinite(value):
        fail(f"{context} must be finite.")
    return value


def point(value: Any, context: str) -> tuple[float, float]:
    if not isinstance(value, dict):
        fail(f"{context} must be an object with x/y coordinates.")
    return number(value.get("x"), f"{context}.x"), number(value.get("y"), f"{context}.y")


def safe_font(size: int, bold: bool = False) -> ImageFont.ImageFont:
    candidates = [
        Path(r"C:\Windows\Fonts\arialbd.ttf" if bold else r"C:\Windows\Fonts\arial.ttf"),
        Path("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf" if bold else "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"),
    ]
    for candidate in candidates:
        try:
            return ImageFont.truetype(str(candidate), size=size)
        except OSError:
            continue
    return ImageFont.load_default()


def get_candidate_audits(analysis: dict[str, Any], trace: dict[str, Any], frame_id: str) -> list[dict[str, Any]]:
    audits = analysis.get("candidateAudits")
    if not isinstance(audits, list):
        fail(f"Frame {frame_id}: analysis.candidateAudits is missing.")
    rows = [
        row for row in audits
        if isinstance(row, dict)
        and row.get("pass") == "initial"
        and row.get("stage") == "sparse-black-add"
        and row.get("kind") == "black"
    ]

    expected = trace.get("expectedAttemptedSeedCount")
    recorded = trace.get("auditedProductionSeedCount")
    if isinstance(expected, bool) or not isinstance(expected, int) or expected < 0:
        fail(f"Frame {frame_id}: expectedAttemptedSeedCount is unavailable.")
    if isinstance(recorded, bool) or not isinstance(recorded, int) or recorded != expected:
        fail(f"Frame {frame_id}: trace candidate audit prefix is incomplete.")
    if len(rows) != expected:
        fail(f"Frame {frame_id}: sparse-black audit rows ({len(rows)}) do not match expected count ({expected}).")

    indexes: list[int] = []
    for row in rows:
        index = row.get("index")
        if isinstance(index, bool) or not isinstance(index, int) or index < 0:
            fail(f"Frame {frame_id}: candidate audit has an invalid rank index.")
        indexes.append(index)
        point(row.get("proposalCentre"), f"Frame {frame_id} candidate rank {index + 1} proposalCentre")
        has_fit_centre = row.get("fittedCentre") is not None
        has_fit_radius = row.get("fittedRadius") is not None
        if has_fit_centre != has_fit_radius:
            fail(f"Frame {frame_id}: candidate rank {index + 1} has incomplete fitted-circle evidence.")
        if has_fit_centre:
            point(row.get("fittedCentre"), f"Frame {frame_id} candidate rank {index + 1} fittedCentre")
            number(row.get("fittedRadius"), f"Frame {frame_id} candidate rank {index + 1} fittedRadius")
        decision = row.get("decision")
        if not isinstance(decision, str) or not (decision.startswith("added-") or decision.startswith("rejected-")):
            fail(f"Frame {frame_id}: candidate rank {index + 1} has no explicit accepted/rejected decision.")
        if row.get("boundarySupport") is not None:
            number(row.get("boundarySupport"), f"Frame {frame_id} candidate rank {index + 1} boundarySupport")
    if sorted(indexes) != list(range(expected)):
        fail(f"Frame {frame_id}: sparse-black candidate ranks are not a complete zero-based prefix.")
    return rows


def resolve_source_input_directory(run_dir: Path, manifest: dict[str, Any], requested: Path | None) -> Path:
    if requested is not None:
        return requested.resolve()

    source_frames = manifest.get("sourceFrames")
    if not isinstance(source_frames, list) or not source_frames:
        fail("Run manifest has no source frames for exact input verification.")
    candidates: set[Path] = set()
    candidates.add((run_dir.parent.parent / "exact-replay-input").resolve())
    evidence_root = next((parent for parent in run_dir.parents if parent.name.lower() == "evidence"), None)
    if evidence_root is not None:
        try:
            candidates.update(path.resolve() for path in evidence_root.rglob("exact-replay-input") if path.is_dir())
        except OSError as exc:
            fail(f"Cannot search evidence folders for exact replay inputs: {exc}")

    matching: list[Path] = []
    for candidate_dir in sorted(candidates):
        if not candidate_dir.is_dir():
            continue
        matches_all = True
        for source in source_frames:
            if not isinstance(source, dict):
                fail("Run manifest contains an invalid source frame record.")
            pixel_file = source.get("pixelFile")
            expected_hash = source.get("sha256")
            if (not isinstance(pixel_file, str) or Path(pixel_file).name != pixel_file
                    or not isinstance(expected_hash, str) or len(expected_hash) != 64):
                fail("Run manifest contains an invalid source pixel reference or hash.")
            pixel_path = candidate_dir / pixel_file
            if not pixel_path.is_file() or sha256(pixel_path) != expected_hash.lower():
                matches_all = False
                break
        if matches_all:
            matching.append(candidate_dir)

    if not matching:
        fail("No exact-replay-input directory contains every source frame with the manifest SHA-256; supply --source-input-directory.")
    if len(matching) > 1:
        fail(f"Multiple exact-replay-input directories match all source hashes; supply --source-input-directory: {matching}")
    return matching[0]


def verify_and_prepare(run_dir: Path, source_dir: Path) -> tuple[dict[str, Any], list[dict[str, Any]]]:
    if not run_dir.is_dir():
        fail(f"Replay run directory not found: {run_dir}")
    if not source_dir.is_dir():
        fail(f"Source replay input directory not found: {source_dir}")

    manifest_path = run_dir / "manifest.json"
    manifest = read_json(manifest_path)
    if manifest.get("mode") != "pocket-arc-probe-only" or manifest.get("pocketArcProbeOnly") is not True:
        fail("Run manifest is not for a PocketArcProbeOnly replay.")

    report_name = manifest.get("report")
    if not isinstance(report_name, str) or Path(report_name).name != report_name:
        fail("Run manifest report path is missing or unsafe.")
    report_path = run_dir / report_name
    if not report_path.is_file():
        fail(f"Replay report is missing: {report_path}")
    expected_report_hash = manifest.get("reportSha256")
    if not isinstance(expected_report_hash, str) or sha256(report_path) != expected_report_hash.lower():
        fail("Replay report hash does not match the run manifest.")

    report = read_json(report_path)
    if report.get("mode") != "pocket-arc-probe-only" or report.get("pocketArcProbeOnly") is not True:
        fail("Replay report does not identify itself as PocketArcProbeOnly.")

    label_source = manifest.get("pocketArcLabelSourcePixel")
    if not isinstance(label_source, dict):
        fail("Run manifest has no explicit PocketArc label source point.")
    label_source_xy = point(label_source, "manifest.pocketArcLabelSourcePixel")

    source_frames = manifest.get("sourceFrames")
    samples = report.get("samples")
    artifacts = manifest.get("pocketArcTraceArtifacts")
    if not isinstance(source_frames, list) or not isinstance(samples, list) or not isinstance(artifacts, list):
        fail("Run is missing source frame, sample, or PocketArc artifact lists.")

    source_by_id: dict[str, dict[str, Any]] = {}
    for source in source_frames:
        if not isinstance(source, dict):
            fail("Run manifest contains an invalid source frame record.")
        frame_id = str(source.get("frameId"))
        if frame_id in source_by_id:
            fail(f"Duplicate source frame id {frame_id} in run manifest.")
        pixel_hash = source.get("sha256")
        if not isinstance(pixel_hash, str) or len(pixel_hash) != 64:
            fail(f"Frame {frame_id}: source pixel hash is missing or malformed.")
        for ref_name in ("pixelFile", "receipt"):
            reference = source.get(ref_name)
            if not isinstance(reference, str) or Path(reference).name != reference:
                fail(f"Frame {frame_id}: source {ref_name} reference is missing or unsafe.")
        source_by_id[frame_id] = source

    source_evidence: dict[str, dict[str, Any]] = {}
    for frame_id, source in source_by_id.items():
        pixel_path = source_dir / source["pixelFile"]
        receipt_path = source_dir / source["receipt"]
        if not pixel_path.is_file():
            fail(f"Frame {frame_id}: source pixel file is missing: {pixel_path}")
        if not receipt_path.is_file():
            fail(f"Frame {frame_id}: source receipt is missing: {receipt_path}")
        source_pixel_hash = sha256(pixel_path)
        if source_pixel_hash != source["sha256"].lower():
            fail(f"Frame {frame_id}: source pixel file hash does not match the manifest.")
        receipt = read_json(receipt_path)
        indexed = receipt.get("indexedFrame")
        if not isinstance(indexed, dict):
            fail(f"Frame {frame_id}: source receipt has no indexedFrame record.")
        if str(indexed.get("frameId")) != frame_id or indexed.get("pixelFile") != source["pixelFile"]:
            fail(f"Frame {frame_id}: source receipt frame id or pixel file reference does not match.")
        if str(indexed.get("pixelSha256", "")).lower() != source_pixel_hash:
            fail(f"Frame {frame_id}: source receipt pixel hash does not match the source file.")
        width = indexed.get("width")
        height = indexed.get("height")
        if isinstance(width, bool) or not isinstance(width, int) or width <= 0:
            fail(f"Frame {frame_id}: source receipt width is invalid.")
        if isinstance(height, bool) or not isinstance(height, int) or height <= 0:
            fail(f"Frame {frame_id}: source receipt height is invalid.")
        try:
            with Image.open(pixel_path) as source_image:
                if source_image.size != (width, height):
                    fail(f"Frame {frame_id}: source PNG size {source_image.size} does not match its receipt.")
                source_image.verify()
        except OSError as exc:
            fail(f"Frame {frame_id}: cannot decode source pixel PNG: {exc}")
        source_evidence[frame_id] = {
            "pixel_path": pixel_path,
            "receipt_path": receipt_path,
            "receipt_sha256": sha256(receipt_path),
            "dimensions": {"width": width, "height": height},
        }

    artifact_by_key: dict[tuple[str, int, str], dict[str, Any]] = {}
    for artifact in artifacts:
        if not isinstance(artifact, dict):
            fail("Run manifest contains an invalid PocketArc artifact record.")
        file_name = artifact.get("file")
        if not isinstance(file_name, str) or Path(file_name).name != file_name:
            fail("Run manifest contains an unsafe PocketArc artifact name.")
        key = (str(artifact.get("frameId")), int(artifact.get("configuredWidth", 0)), file_name)
        if key in artifact_by_key:
            fail(f"Duplicate PocketArc artifact record for frame {key[0]} and file {key[2]}.")
        artifact_by_key[key] = artifact

    prepared: list[dict[str, Any]] = []
    seen_samples: set[str] = set()
    for sample in samples:
        if not isinstance(sample, dict):
            fail("Replay report contains an invalid sample record.")
        frame_id = str(sample.get("frameId"))
        if frame_id in seen_samples:
            fail(f"Duplicate replay sample frame id {frame_id}.")
        seen_samples.add(frame_id)
        if frame_id not in source_by_id:
            fail(f"Frame {frame_id}: no matching source frame record in the manifest.")
        source_hash = source_by_id[frame_id]["sha256"].lower()
        sample_hash = sample.get("pixelSha256")
        if not isinstance(sample_hash, str) or sample_hash.lower() != source_hash:
            fail(f"Frame {frame_id}: report pixel hash does not match the manifest source hash.")
        if sample.get("detectionRequested") is not True:
            fail(f"Frame {frame_id}: detection was not requested; refusing a partial diagnostic render.")

        width = sample.get("width1000")
        if not isinstance(width, dict):
            fail(f"Frame {frame_id}: width-1000 replay block is missing.")
        analysis = width.get("analysis")
        trace = width.get("darkMaskTrace")
        if not isinstance(analysis, dict) or not isinstance(trace, dict):
            fail(f"Frame {frame_id}: analysis or dark-mask trace is missing.")
        if str(trace.get("frameId")) != frame_id or trace.get("analysisWidth") != 1000:
            fail(f"Frame {frame_id}: dark-mask trace frame id or width does not match.")

        label = trace.get("manualBlackLabel")
        if not isinstance(label, dict) or not isinstance(label.get("sourcePixel"), dict):
            fail(f"Frame {frame_id}: explicit manual label evidence is missing.")
        if point(label["sourcePixel"], f"Frame {frame_id} trace source label") != label_source_xy:
            fail(f"Frame {frame_id}: trace label source point differs from the manifest label.")
        label_analysis_xy = point(label.get("analysisPixel"), f"Frame {frame_id} transformed label")

        expected_radius = number(trace.get("labelledBallRegion", {}).get("expectedRadius"),
                                 f"Frame {frame_id} expected analysis radius")
        if expected_radius <= 0:
            fail(f"Frame {frame_id}: expected ball radius must be positive.")
        max_distance = 3.0 * expected_radius

        arc_report = trace.get("manualCentreProbe", {}).get("pocketAwarePartialArcExperiment")
        if not isinstance(arc_report, dict) or arc_report.get("diagnosticOnly") is not True:
            fail(f"Frame {frame_id}: pocket-aware arc diagnostic is missing or not marked diagnostic-only.")
        arc_candidate = arc_report.get("candidate")
        if arc_candidate is not None:
            if not isinstance(arc_candidate, dict):
                fail(f"Frame {frame_id}: arc candidate evidence is malformed.")
            point(arc_candidate.get("centre"), f"Frame {frame_id} arc candidate centre")
            if number(arc_candidate.get("radius"), f"Frame {frame_id} arc candidate radius") <= 0:
                fail(f"Frame {frame_id}: arc candidate radius must be positive.")

        candidate_rows = get_candidate_audits(analysis, trace, frame_id)
        ranked: list[tuple[float, int, dict[str, Any]]] = []
        for row in candidate_rows:
            proposal_xy = point(row["proposalCentre"], f"Frame {frame_id} proposal")
            distance = math.hypot(proposal_xy[0] - label_analysis_xy[0], proposal_xy[1] - label_analysis_xy[1])
            ranked.append((distance, row["index"], row))
        ranked.sort(key=lambda item: (item[0], item[1]))
        nearest_any = ranked[0] if ranked else None
        nearest_local = next((entry for entry in ranked if entry[0] <= max_distance), None)

        overlay_name = f"{frame_id}-1000-dark-mask-overlay.png"
        if overlay_name not in trace.get("overlays", []):
            fail(f"Frame {frame_id}: report does not reference expected overlay {overlay_name}.")
        artifact = artifact_by_key.get((frame_id, 1000, overlay_name))
        if artifact is None or artifact.get("file") != overlay_name:
            fail(f"Frame {frame_id}: manifest has no matching width-1000 overlay receipt.")
        overlay_path = run_dir / overlay_name
        if not overlay_path.is_file() or sha256(overlay_path) != str(artifact.get("sha256", "")).lower():
            fail(f"Frame {frame_id}: overlay is missing or its hash does not match the manifest.")
        try:
            with Image.open(overlay_path) as image:
                if image.size != (1000, 2000):
                    fail(f"Frame {frame_id}: overlay dimensions are {image.size}, expected 1000x2000.")
                if image.mode != "RGB":
                    fail(f"Frame {frame_id}: overlay mode is {image.mode}, expected RGB to preserve source pixels.")
                overlay = image.copy()
        except OSError as exc:
            fail(f"Frame {frame_id}: cannot decode overlay: {exc}")

        prepared.append({
            "frame_id": frame_id,
            "pixel_hash": sample_hash.lower(),
            "source_frame": source_by_id[frame_id],
            "source_evidence": source_evidence[frame_id],
            "overlay_name": overlay_name,
            "overlay_sha256": str(artifact["sha256"]).lower(),
            "manual_source_pixel": label["sourcePixel"],
            "overlay": overlay,
            "label": label_analysis_xy,
            "expected_radius": expected_radius,
            "max_distance": max_distance,
            "candidate": nearest_local,
            "nearest_any": nearest_any,
            "arc": arc_candidate,
            "arc_pass": arc_candidate is not None and arc_candidate.get("diagnosticArcChecksPass") is True,
            "arc_report": arc_report,
            "purpose": str(manifest.get("purpose", "")),
        })

    if seen_samples != set(source_by_id):
        missing = sorted(set(source_by_id) - seen_samples)
        extra = sorted(seen_samples - set(source_by_id))
        fail(f"Sample/source frame id sets do not match (missing={missing}, extra={extra}).")
    if not prepared:
        fail("Replay report has no frame samples to render.")
    return manifest, prepared


def outlined(draw: ImageDraw.ImageDraw, operation: str, coords: tuple[float, ...], color: tuple[int, int, int], width: int = 5) -> None:
    # A black halo keeps each marker visible on both balls and cloth.
    fn = getattr(draw, operation)
    fn(coords, outline=(0, 0, 0), width=width + 5)
    fn(coords, outline=color, width=width)


def cross(draw: ImageDraw.ImageDraw, x: float, y: float, color: tuple[int, int, int], radius: int = 18) -> None:
    draw.line((x - radius, y, x + radius, y), fill=(0, 0, 0), width=11)
    draw.line((x, y - radius, x, y + radius), fill=(0, 0, 0), width=11)
    draw.line((x - radius, y, x + radius, y), fill=color, width=5)
    draw.line((x, y - radius, x, y + radius), fill=color, width=5)


def wrap_text(draw: ImageDraw.ImageDraw, text: str, font: ImageFont.ImageFont, maximum_width: int) -> list[str]:
    words = text.split()
    if not words:
        return [""]
    lines: list[str] = []
    current = words[0]
    for word in words[1:]:
        proposed = current + " " + word
        if draw.textlength(proposed, font=font) <= maximum_width:
            current = proposed
        else:
            lines.append(current)
            current = word
    lines.append(current)
    return lines


def candidate_report(item: dict[str, Any], entry: tuple[float, int, dict[str, Any]] | None, within_window: bool) -> dict[str, Any] | None:
    if entry is None:
        return None
    distance, index, audit = entry
    decision = str(audit.get("decision", ""))
    return {
        "rank": index + 1,
        "distanceFromTransformedManualLabelPx": round(distance, 4),
        "within3R": within_window,
        "proposalCentre": audit.get("proposalCentre"),
        "proposalRadius": audit.get("proposalRadius"),
        "fittedCentre": audit.get("fittedCentre"),
        "fittedRadius": audit.get("fittedRadius"),
        "boundarySupport": audit.get("boundarySupport"),
        "decision": decision,
        "acceptedAtCandidateStage": True if decision in ("added-black-body", "added-strong-isolated-black") else False if decision.startswith("rejected-") else None,
        "classifiedKind": audit.get("classifiedKind"),
        "evidence": audit.get("evidence"),
        "semantics": "candidate audit only; never a detector result or table-state assertion",
    }


def frame_report(item: dict[str, Any]) -> dict[str, Any]:
    label_x, label_y = item["label"]
    nearest_any = item["nearest_any"]
    outside = nearest_any if item["candidate"] is None else None
    arc_report = item["arc_report"]
    return {
        "frameId": item["frame_id"],
        "sourceFrame": {
            "pixelFile": item["source_frame"]["pixelFile"],
            "receipt": item["source_frame"]["receipt"],
            "pixelSha256": item["pixel_hash"],
            "receiptSha256": item["source_evidence"]["receipt_sha256"],
            "dimensions": item["source_evidence"]["dimensions"],
        },
        "darkMaskOverlay": {
            "file": item["overlay_name"],
            "sha256": item["overlay_sha256"],
            "dimensions": {"width": 1000, "height": 2000},
            "mode": "RGB",
        },
        "manualBlackLabel": {
            "sourcePixel": item["manual_source_pixel"],
            "transformedAnalysisPixel": {"x": label_x, "y": label_y},
            "expectedAnalysisRadius": item["expected_radius"],
            "note": "Explicit evaluation label, not supplied to analyzer decisions.",
        },
        "sparseBlackAuditWindow": {
            "candidateStage": "initial/sparse-black-add/black",
            "distanceLimitMultipleOfRadius": 3,
            "distanceLimitPixels": item["max_distance"],
            "nearestProposalWithin3R": candidate_report(item, item["candidate"], True),
            "nearestProposalOutside3R": candidate_report(item, outside, False),
        },
        "pocketAwarePartialArcDiagnostic": {
            "diagnosticOnly": True,
            "candidate": arc_report.get("candidate"),
            "checksPassed": arc_report.get("candidate", {}).get("diagnosticArcChecksPass") is True
            if isinstance(arc_report.get("candidate"), dict) else False,
            "evidence": arc_report,
            "semantics": "diagnostic overlay only; not a detector result",
        },
    }


def draw_frame(item: dict[str, Any], purpose_short: str) -> Image.Image:
    base = item["overlay"].copy()
    draw = ImageDraw.Draw(base)
    label_x, label_y = item["label"]
    cross(draw, label_x, label_y, (255, 245, 0), radius=20)

    candidate = item["candidate"]
    if candidate is not None:
        distance, index, audit = candidate
        proposal_x, proposal_y = point(audit["proposalCentre"], "selected proposal")
        outlined(draw, "ellipse", (proposal_x - 13, proposal_y - 13, proposal_x + 13, proposal_y + 13),
                 (0, 255, 255), width=5)
        cross(draw, proposal_x, proposal_y, (0, 255, 255), radius=13)
        fit_centre = audit.get("fittedCentre")
        fit_radius = audit.get("fittedRadius")
        if isinstance(fit_centre, dict) and fit_radius is not None:
            fit_x, fit_y = point(fit_centre, "selected candidate fittedCentre")
            radius = number(fit_radius, "selected candidate fittedRadius")
            decision = str(audit.get("decision", ""))
            accepted_by_stage = decision in ("added-black-body", "added-strong-isolated-black")
            rejected_by_stage = decision.startswith("rejected-")
            fit_color = (60, 255, 80) if accepted_by_stage else (255, 95, 45) if rejected_by_stage else (245, 245, 245)
            outlined(draw, "ellipse", (fit_x - radius, fit_y - radius, fit_x + radius, fit_y + radius),
                     fit_color, width=6)
            cross(draw, fit_x, fit_y, fit_color, radius=12)

    arc = item["arc"]
    if isinstance(arc, dict):
        arc_x, arc_y = point(arc.get("centre"), "arc diagnostic centre")
        arc_radius = number(arc.get("radius"), "arc diagnostic radius")
        outlined(draw, "ellipse", (arc_x - arc_radius, arc_y - arc_radius, arc_x + arc_radius, arc_y + arc_radius),
                 (210, 90, 255), width=4)
        cross(draw, arc_x, arc_y, (210, 90, 255), radius=10)

    # Keep the verified overlay at 1:1 resolution. The added header/footer are
    # outside the image; only diagnostic markers touch its original pixels.
    tile_width = 1000
    image = base
    image_height = image.height
    header_height = 54
    header_font = safe_font(27, bold=True)
    body_font = safe_font(21)
    logical_lines: list[str] = [
        f"Source frame SHA-256: {item['pixel_hash']}  |  transformed label at ({label_x:.1f}, {label_y:.1f}) px",
    ]
    if candidate is None:
        nearest = item["nearest_any"]
        if nearest is None:
            logical_lines.append(f"No sparse-black proposal in the audited candidate prefix; local window = 3R ({item['max_distance']:.1f}px).")
        else:
            distance, index, _audit = nearest
            logical_lines.append(f"No proposal within 3R ({item['max_distance']:.1f}px); nearest is rank {index + 1} at {distance:.1f}px.")
    else:
        distance, index, audit = candidate
        support = audit.get("boundarySupport")
        support_text = "unavailable" if support is None else f"{round(float(support) * 32):.0f}/32"
        decision = str(audit.get("decision", "decision unavailable"))
        if audit.get("fittedCentre") is None:
            fit_text = f"no fitted circle; {decision}; boundarySupport={support_text}"
        else:
            fitted = audit["fittedCentre"]
            fit_text = (f"fitted centre ({fitted['x']:.1f}, {fitted['y']:.1f}) px, "
                        f"radius {audit['fittedRadius']:.1f} px; {decision}; boundarySupport={support_text}")
        proposal = audit["proposalCentre"]
        logical_lines.append(f"Nearest cap proposal rank {index + 1} at ({proposal['x']:.1f}, {proposal['y']:.1f}), {distance:.1f}px from label.")
        logical_lines.append(f"Candidate-stage audit only; not a detector result: {fit_text}.")
    if arc is None:
        logical_lines.append("Pocket-aware partial-arc diagnostic: no candidate.")
    else:
        arc_status = "PASS" if item["arc_pass"] else "FAIL"
        arc_xy = arc["centre"]
        logical_lines.append(f"Partial-arc diagnostic only: {arc_status} at ({arc_xy['x']:.1f}, {arc_xy['y']:.1f}); not a detector result.")
    legend = "Legend: yellow=transformed manual label; cyan=proposal; green=accepted candidate fit; orange=rejected candidate fit; purple=pocket-aware arc diagnostic only (not a detection)."
    logical_lines.append(legend)

    text_draw = ImageDraw.Draw(Image.new("RGB", (tile_width, 1)))
    lines: list[str] = []
    for line in logical_lines:
        lines.extend(wrap_text(text_draw, line, body_font, tile_width - 28))
    line_step = 29
    footer_height = 18 + line_step * len(lines)
    tile = Image.new("RGB", (tile_width, header_height + image_height + footer_height), (24, 27, 32))
    tile_draw = ImageDraw.Draw(tile)
    title = f"Frame {item['frame_id']} · {purpose_short}"
    tile_draw.text((14, 11), title, font=header_font, fill=(245, 245, 245))
    tile.paste(image, (0, header_height))

    line_y = header_height + image_height + 8
    for line in lines:
        tile_draw.text((14, line_y), line, font=body_font, fill=(240, 240, 240))
        line_y += line_step
    return tile


def render(run_dir: Path, output: Path, report_output: Path, source_dir: Path | None, selected_frames: set[str] | None, columns: int) -> list[dict[str, Any]]:
    if not run_dir.is_dir():
        fail(f"Replay run directory not found: {run_dir}")
    manifest_seed = read_json(run_dir / "manifest.json")
    source_dir = resolve_source_input_directory(run_dir, manifest_seed, source_dir)
    manifest, frames = verify_and_prepare(run_dir, source_dir)
    if selected_frames is not None:
        known = {item["frame_id"] for item in frames}
        unknown = selected_frames - known
        if unknown:
            fail(f"Requested frame ids are absent from the verified run: {sorted(unknown)}")
        frames = [item for item in frames if item["frame_id"] in selected_frames]
    if not frames:
        fail("No frames remain after selection.")

    report_name = manifest["report"]
    protected_inputs = {
        (run_dir / "manifest.json").resolve(),
        (run_dir / report_name).resolve(),
        *((run_dir / item["overlay_name"]).resolve() for item in frames),
        *(item["source_evidence"]["pixel_path"].resolve() for item in frames),
        *(item["source_evidence"]["receipt_path"].resolve() for item in frames),
    }
    if output.resolve() in protected_inputs or report_output.resolve() in protected_inputs:
        fail("Output path would overwrite verified input evidence.")
    if output.resolve() == report_output.resolve():
        fail("Contact sheet and JSON report must use different output paths.")

    purpose = str(manifest.get("purpose", ""))
    purpose_short = "positive/control"
    lowered = purpose.lower()
    if "empty-pocket" in lowered or "control" in lowered:
        purpose_short = "empty-pocket control"
    elif "positive" in lowered or "present" in lowered:
        purpose_short = "black-present"

    tile_width = 1000
    tiles = [draw_frame(item, purpose_short) for item in frames]
    columns = max(1, min(columns, len(tiles)))
    rows = math.ceil(len(tiles) / columns)
    tile_h = max(tile.height for tile in tiles)
    sheet = Image.new("RGB", (columns * tile_width, rows * tile_h), (12, 14, 18))
    for index, tile in enumerate(tiles):
        x = (index % columns) * tile_width
        y = (index // columns) * tile_h
        sheet.paste(tile, (x, y))

    output.parent.mkdir(parents=True, exist_ok=True)
    try:
        sheet.save(output, format="PNG", optimize=True)
    except OSError as exc:
        fail(f"Cannot save contact sheet {output}: {exc}")

    report_payload = {
        "schemaVersion": 1,
        "tool": "Render-PocketArcCandidateOverlay.py",
        "mode": manifest.get("mode"),
        "diagnosticOnly": True,
        "sourceRunDirectory": str(run_dir.resolve()),
        "sourceFrameDirectory": str(source_dir.resolve()),
        "sourceManifest": {"file": "manifest.json", "sha256": sha256(run_dir / "manifest.json")},
        "sourceReport": {"file": report_name, "sha256": str(manifest["reportSha256"]).lower()},
        "contactSheet": {
            "file": str(output.resolve()),
            "sha256": sha256(output),
            "pixelPreservation": "Each source overlay is placed at native 1000x2000 resolution. Only diagnostic markers are drawn over source pixels; title and text are outside the image area.",
        },
        "legend": {
            "yellowCross": "transformed manual black label",
            "cyanCircleAndCross": "nearest sparse-black-add proposal within 3R",
            "greenFitCircle": "candidate-stage fit accepted",
            "orangeFitCircle": "candidate-stage fit rejected",
            "purpleCircleAndCross": "pocket-aware partial-arc diagnostic candidate, not a detector result",
        },
        "frames": [frame_report(item) for item in frames],
    }
    report_output.parent.mkdir(parents=True, exist_ok=True)
    try:
        report_output.write_text(json.dumps(report_payload, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    except OSError as exc:
        fail(f"Cannot save provenance report {report_output}: {exc}")
    return frames


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--run-directory", required=True, type=Path, help="PocketArcProbeOnly run directory.")
    parser.add_argument("--source-input-directory", type=Path, help="Directory containing the exact replay PNGs and receipts; defaults to the run's sibling exact-replay-input directory.")
    parser.add_argument("--output", type=Path, help="Output PNG path; defaults to the run directory.")
    parser.add_argument("--report", type=Path, help="Output JSON provenance report; defaults to the run directory.")
    parser.add_argument("--frames", nargs="+", help="Optional frame ids to render after the full run is validated.")
    parser.add_argument("--columns", type=int, default=3, help="Contact-sheet columns (default: 3).")
    args = parser.parse_args()

    if args.columns < 1 or args.columns > 8:
        parser.error("--columns must be between 1 and 8")
    output = args.output or (args.run_directory / "pocket-arc-candidate-contact-sheet.png")
    report_output = args.report or (args.run_directory / "pocket-arc-candidate-report.json")
    try:
        selected = {str(value) for value in args.frames} if args.frames else None
        source_dir = args.source_input_directory.resolve() if args.source_input_directory is not None else None
        frames = render(args.run_directory.resolve(), output.resolve(), report_output.resolve(), source_dir, selected, args.columns)
    except ValueError as exc:
        print(f"ERROR: {exc}", file=sys.stderr)
        return 2
    print(f"Rendered {len(frames)} verified frame(s) to {output.resolve()}")
    print(f"Wrote provenance report to {report_output.resolve()}")
    for frame in frames:
        print(f"  frame={frame['frame_id']} sourceSha256={frame['pixel_hash']}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
