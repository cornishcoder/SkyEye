#!/usr/bin/env python3
"""Render one requested, explicitly evaluation-only pocket-arc review example.

Uses the existing saved sector-pixel JSON and exact saved source frame. The
approximate pocket point is used only to choose this illustrative example; it
is explicitly not a ground-truth centre and supports no accuracy claim.
"""

from __future__ import annotations

import hashlib
import json
import math
from pathlib import Path
from typing import Any

import cv2
import numpy as np
from PIL import Image, ImageDraw, ImageFont


ROOT = Path("work/production/evidence/code70-daylight-pocket-capture-20261031")
EVIDENCE = ROOT / "pocket-arc-holdout/regular-sector-trace/source-pixel-support"
SOURCE = ROOT / "exact-replay-input/1365.png"
RECEIPT = ROOT / "exact-replay-input/1365.json"
OUT_IMAGE = EVIDENCE / "target-near-frame1365-rank19-review.png"
OUT_JSON = EVIDENCE / "target-near-frame1365-rank19-review.json"
WIDTH, HEIGHT = 1000, 2000


def load_font(size: int) -> ImageFont.ImageFont:
    path = Path("C:/Windows/Fonts/arial.ttf")
    return ImageFont.truetype(str(path), size) if path.exists() else ImageFont.load_default()


def transform_points(points: list[tuple[float, float]], matrix: np.ndarray) -> list[tuple[float, float]]:
    arr = np.float32(points).reshape((-1, 1, 2))
    mapped = cv2.perspectiveTransform(arr, matrix).reshape((-1, 2))
    return [(float(x), float(y)) for x, y in mapped]


def draw_cross(draw: ImageDraw.ImageDraw, point: tuple[float, float], color: tuple[int, int, int], radius: int = 8) -> None:
    x, y = map(round, point)
    draw.line((x-radius, y, x+radius, y), fill=color, width=3)
    draw.line((x, y-radius, x, y+radius), fill=color, width=3)


def draw_polyline_circle(draw: ImageDraw.ImageDraw, matrix: np.ndarray, centre: tuple[float, float],
                         radius: float, crop: tuple[int, int, int, int], scale: tuple[float, float],
                         color: tuple[int, int, int]) -> None:
    x, y = centre
    ring = [(x + radius*math.cos(i*2*math.pi/96), y + radius*math.sin(i*2*math.pi/96)) for i in range(96)]
    mapped = transform_points(ring, matrix)
    l, t, _, _ = crop; sx, sy = scale
    coords = [(round((px-l)*sx), round((py-t)*sy)) for px,py in mapped]
    draw.line(coords + [coords[0]], fill=color, width=3, joint="curve")


def main() -> None:
    data = json.loads((EVIDENCE / "sector-pixel-evidence.json").read_text(encoding="utf-8-sig"))
    pair = next(p for p in data["pairs"] if p["frameId"] == 1365 and p["candidateRank"] == 19)
    regular = pair["fits"]["regular"]
    arc = pair["fits"]["arc"]
    if pair["case"] != "positive" or arc["diagnosticChecksPass"] is not True:
        raise ValueError("Requested review pair is no longer the expected positive arc-pass case")
    approximate = pair["evaluationReferences"]["pocketArcReport"]
    if not approximate:
        raise ValueError("The pocket-arc report has no evaluation reference for this review example")
    approx_xy = (float(approximate["analysisPixel"]["x"]), float(approximate["analysisPixel"]["y"]))
    regular_xy = (float(regular["centre"]["x"]), float(regular["centre"]["y"]))
    arc_xy = (float(arc["centre"]["x"]), float(arc["centre"]["y"]))
    # Use the same one-decimal report precision for the displayed descriptive
    # separations so the figure does not imply subpixel label precision.
    regular_point_distance = math.dist(tuple(round(v, 1) for v in regular_xy), tuple(round(v, 1) for v in approx_xy))
    arc_point_distance = math.dist(tuple(round(v, 1) for v in arc_xy), tuple(round(v, 1) for v in approx_xy))

    receipt = json.loads(RECEIPT.read_text(encoding="utf-8-sig"))
    source_hash = hashlib.sha256(SOURCE.read_bytes()).hexdigest()
    if source_hash != pair["pixelSha256"] or source_hash != receipt["indexedFrame"]["pixelSha256"]:
        raise ValueError("Saved source image does not match the selected report/receipt hash")
    raw = cv2.imread(str(SOURCE), cv2.IMREAD_COLOR)
    if raw is None or raw.shape[:2] != (1080, 1920):
        raise ValueError("Unexpected exact raw source image shape")
    corners = receipt["calibrationCorners"]
    src = np.float32([[c["x"]*1920, c["y"]*1080] for c in corners])
    dst = np.float32([[WIDTH, 0], [WIDTH, HEIGHT], [0, HEIGHT], [0, 0]])
    forward = cv2.getPerspectiveTransform(src, dst)
    inverse = np.linalg.inv(forward)
    warped = cv2.warpPerspective(raw, forward, (WIDTH, HEIGHT))
    if pair["sourceTimeUs"] != receipt["indexedFrame"]["sourceTimeUs"]:
        raise ValueError("Saved receipt timestamp differs from selected pair")

    # Fixed crops are around the requested lower-left pocket example only.
    raw_crop = (0, 500, 650, 1080)
    warp_crop = (0, 0, 180, 190)
    raw_region = cv2.cvtColor(raw[raw_crop[1]:raw_crop[3], raw_crop[0]:raw_crop[2]], cv2.COLOR_BGR2RGB)
    warp_region = cv2.cvtColor(warped[warp_crop[1]:warp_crop[3], warp_crop[0]:warp_crop[2]], cv2.COLOR_BGR2RGB)
    raw_panel = Image.fromarray(raw_region).resize((750, 670), Image.Resampling.LANCZOS)
    warp_crop_panel = Image.fromarray(warp_region).resize((630, 665), Image.Resampling.LANCZOS)
    warp_panel = Image.new("RGB", (750, 670), (20, 20, 20))
    warp_panel.paste(warp_crop_panel, (60, 2))
    raw_draw, warp_draw = ImageDraw.Draw(raw_panel), ImageDraw.Draw(warp_panel)
    raw_scale = (750/(raw_crop[2]-raw_crop[0]), 670/(raw_crop[3]-raw_crop[1]))
    warp_scale = (630/(warp_crop[2]-warp_crop[0]), 665/(warp_crop[3]-warp_crop[1]))
    colors = {"regular": (20, 210, 255), "arc": (255, 155, 20), "seed": (255, 230, 40), "approx": (245, 60, 220)}

    # Overlay the approximate point and seed in the calibrated warp panel.
    def warp_canvas(point: tuple[float,float]) -> tuple[int,int]:
        return (60 + round((point[0]-warp_crop[0])*warp_scale[0]), 2 + round((point[1]-warp_crop[1])*warp_scale[1]))
    approx_canvas = warp_canvas(approx_xy)
    warp_draw.ellipse((approx_canvas[0]-13,approx_canvas[1]-13,approx_canvas[0]+13,approx_canvas[1]+13), outline=colors["approx"], width=4)
    draw_cross(warp_draw, approx_canvas, colors["approx"], 10)
    seed_xy = (float(pair["seedCentre"]["x"]), float(pair["seedCentre"]["y"]))
    seed_canvas = warp_canvas(seed_xy)
    draw_cross(warp_draw, seed_canvas, colors["seed"], 10)
    raw_seed = transform_points([seed_xy], inverse)[0]
    raw_seed_canvas = ((raw_seed[0]-raw_crop[0])*raw_scale[0], (raw_seed[1]-raw_crop[1])*raw_scale[1])
    draw_cross(raw_draw, raw_seed_canvas, colors["seed"], 10)

    # Each fit is drawn from its saved centre/radius and saved supported set.
    for name, fit, color in (("regular", regular, colors["regular"]), ("arc", arc, colors["arc"])):
        centre = (float(fit["centre"]["x"]), float(fit["centre"]["y"]))
        radius = float(fit["radius"])
        supported = set(int(i) for i in fit["supportedSectorIndices"])
        sectors = {int(s["index"]): s for s in fit["sectors"]}
        centre_canvas = warp_canvas(centre)
        radius_x = round(radius*warp_scale[0]); radius_y = round(radius*warp_scale[1])
        warp_draw.ellipse((centre_canvas[0]-radius_x,centre_canvas[1]-radius_y,centre_canvas[0]+radius_x,centre_canvas[1]+radius_y), outline=color, width=3)
        draw_cross(warp_draw, centre_canvas, color, 8)
        for index in sorted(supported):
            sector = sectors[index]
            inner, outer = tuple(sector["inner"]), tuple(sector["outer"])
            a, b = warp_canvas(inner), warp_canvas(outer)
            warp_draw.line((a[0],a[1],b[0],b[1]), fill=color, width=4)
            warp_draw.ellipse((a[0]-3,a[1]-3,a[0]+3,a[1]+3), fill=color)
            warp_draw.ellipse((b[0]-3,b[1]-3,b[0]+3,b[1]+3), fill=color)
            if index % 4 == 0:
                warp_draw.text((b[0]+3,b[1]-14), str(index), fill=(255,255,255), font=load_font(13), stroke_width=2, stroke_fill=(0,0,0))

        # Project every saved supported radial segment and the fitted circle
        # back to the original camera image for the raw-source panel.
        draw_polyline_circle(raw_draw, inverse, centre, radius, raw_crop, raw_scale, color)
        raw_points = []
        for index in sorted(supported):
            sector = sectors[index]
            raw_points.extend([tuple(sector["inner"]), tuple(sector["outer"])])
        raw_mapped = transform_points(raw_points, inverse)
        l,t,_,_ = raw_crop; sx,sy = raw_scale
        for j in range(0,len(raw_mapped),2):
            a = (round((raw_mapped[j][0]-l)*sx), round((raw_mapped[j][1]-t)*sy))
            b = (round((raw_mapped[j+1][0]-l)*sx), round((raw_mapped[j+1][1]-t)*sy))
            raw_draw.line((a[0],a[1],b[0],b[1]), fill=color, width=3)
            raw_draw.ellipse((a[0]-3,a[1]-3,a[0]+3,a[1]+3), fill=color)
            raw_draw.ellipse((b[0]-3,b[1]-3,b[0]+3,b[1]+3), fill=color)
        raw_centre = transform_points([centre], inverse)[0]
        raw_cross = ((raw_centre[0]-raw_crop[0])*raw_scale[0], (raw_centre[1]-raw_crop[1])*raw_scale[1])
        draw_cross(raw_draw, raw_cross, color, 8)

    # Mark the source pixel reference in both raw and warped views.
    raw_label = (float(approximate["sourcePixel"]["x"]), float(approximate["sourcePixel"]["y"]))
    raw_label_canvas = ((raw_label[0]-raw_crop[0])*raw_scale[0], (raw_label[1]-raw_crop[1])*raw_scale[1])
    raw_draw.ellipse((raw_label_canvas[0]-13,raw_label_canvas[1]-13,raw_label_canvas[0]+13,raw_label_canvas[1]+13), outline=colors["approx"], width=4)
    draw_cross(raw_draw, raw_label_canvas, colors["approx"], 10)

    canvas = Image.new("RGB", (1560, 1020), (25,25,25))
    draw = ImageDraw.Draw(canvas)
    font_big, font = load_font(27), load_font(19)
    draw.text((20,15), "Evaluation only: frame 1365, full-sweep rank 19", font=font_big, fill=(255,255,255))
    draw.text((20,54), "Approximate pocket interior point used only to select this review example; it is not a ground-truth centre.", font=font, fill=colors["approx"])
    draw.text((20,82), "Cyan = regular supported sectors; orange = arc supported-visible sectors; yellow = seed; magenta = approximate point.", font=font, fill=(235,235,235))
    draw.text((20,122), "Raw camera crop", font=font_big, fill=(255,255,255))
    draw.text((800,122), "Calibrated 1000 x 2000 warp crop", font=font_big, fill=(255,255,255))
    canvas.paste(raw_panel, (20,165)); canvas.paste(warp_panel, (790,165))
    draw = ImageDraw.Draw(canvas)
    draw.text((20,850), f"Regular: centre ({regular_xy[0]:.1f},{regular_xy[1]:.1f}), r={regular['radius']:.2f}px, {len(regular['supportedSectorIndices'])}/32 sectors", font=font, fill=colors["regular"])
    draw.text((20,882), f"Arc: centre ({arc_xy[0]:.1f},{arc_xy[1]:.1f}), r={arc['radius']:.2f}px, {len(arc['supportedSectorIndices'])} supported-visible, checks pass", font=font, fill=colors["arc"])
    draw.text((20,914), f"Approx point ({approx_xy[0]:.1f},{approx_xy[1]:.1f}); descriptive point separations: regular {regular_point_distance:.1f}px, arc {arc_point_distance:.1f}px.", font=font, fill=(245,245,245))
    draw.text((20,946), "The point is approximate and not a centre label; these separations are not localization error or accuracy measurements.", font=font, fill=(255,220,220))
    draw.text((20,978), f"Seed ({seed_xy[0]:.1f},{seed_xy[1]:.1f}); source SHA-256 {source_hash}; timestamp {pair['sourceTimeUs']} us.", font=load_font(15), fill=(210,210,210))
    canvas.save(OUT_IMAGE)

    result: dict[str, Any] = {
        "schemaVersion": 1,
        "diagnosticOnly": True,
        "reviewSelection": "Selected using the requested approximate pocket point to illustrate one target-near full-sweep candidate; no candidate generation or fit was changed.",
        "groundTruth": False,
        "accuracyClaim": False,
        "source": {"frameId": 1365, "sourceTimeUs": pair["sourceTimeUs"], "pixelSha256": source_hash, "sourceImage": str(SOURCE)},
        "candidate": {"rank": 19, "seedCentre": pair["seedCentre"], "seedRadius": pair["seedRadius"], "expectedBallRadiusPx": pair["expectedBallRadiusPx"]},
        "approximateEvaluationPoint": {"sourcePixel": approximate["sourcePixel"], "warpedPixel": approximate["analysisPixel"], "role": "approximate interior point, not a ground-truth centre"},
        "regularFit": {"centre": regular["centre"], "radius": regular["radius"], "supportedSectorIndices": regular["supportedSectorIndices"], "pointSeparationPx": round(regular_point_distance, 1)},
        "arcFit": {"centre": arc["centre"], "radius": arc["radius"], "supportedVisibleSectorIndices": arc["supportedSectorIndices"], "diagnosticChecksPass": arc["diagnosticChecksPass"], "pointSeparationPx": round(arc_point_distance, 1)},
        "distanceInterpretation": "Descriptive distance to an approximate interior point only; not centre error or accuracy.",
        "artifacts": {"reviewOverlay": str(OUT_IMAGE), "inputEvidenceJson": str(EVIDENCE / "sector-pixel-evidence.json")},
    }
    OUT_JSON.write_text(json.dumps(result, indent=2), encoding="utf-8")
    print(f"image={OUT_IMAGE} json={OUT_JSON} regularPointSeparation={regular_point_distance:.2f}px arcPointSeparation={arc_point_distance:.2f}px")


if __name__ == "__main__":
    main()
