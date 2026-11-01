from __future__ import annotations

import hashlib
import json
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont, PngImagePlugin


REPO_ROOT = Path(__file__).resolve().parents[4]
SOURCE_ROOT = REPO_ROOT / "work/production/evidence/code65-source-frame-check-20261031/exact-replay-input"
RUN_ROOT = REPO_ROOT / "work/production/evidence/local-raw-proposal-gates-20261031/exact-profile-negative-replay-20261101/runs/rawclneg800-004647"
OUT = Path(__file__).resolve().parent

FRAME_ID = 1179
EXPECTED_PIXEL_SHA256 = "0ce1efbdfdb940d04c0021356a527ab5ce45312334aa8bf988eb84362fac7301"
EXPECTED_REPORT_SHA256 = "f19910e93a8b3a12eb38f18a1ede2362f267959e9655875f756c0d147d2099e7"
REPORT_NAME = "o08-replay-proof-rawclneg800-004647-rawproposals.json"

source_path = SOURCE_ROOT / f"{FRAME_ID}.png"
receipt_path = SOURCE_ROOT / f"{FRAME_ID}.json"
report_path = RUN_ROOT / REPORT_NAME
source_sha256 = hashlib.sha256(source_path.read_bytes()).hexdigest()
report_sha256 = hashlib.sha256(report_path.read_bytes()).hexdigest()
if source_sha256 != EXPECTED_PIXEL_SHA256:
    raise RuntimeError(f"Source PNG hash changed: {source_sha256}")
if report_sha256 != EXPECTED_REPORT_SHA256:
    raise RuntimeError(f"Replay report hash changed: {report_sha256}")

receipt = json.loads(receipt_path.read_text(encoding="utf-8-sig"))
report = json.loads(report_path.read_text(encoding="utf-8-sig"))
sample = next(item for item in report["samples"] if item["frameId"] == FRAME_ID)
proposal = next(item for item in sample["proposals"] if item["id"] == "ball_2")
if receipt["indexedFrame"]["pixelSha256"] != source_sha256:
    raise RuntimeError("Frame receipt does not bind to the source PNG")
if receipt["indexedFrame"]["sourceTimeUs"] != sample["sourceTimeUs"]:
    raise RuntimeError("Replay sample PTS does not match the source receipt")
if proposal["kind"] != "red" or proposal["source"] != "yolov11":
    raise RuntimeError("Expected the unreviewed YOLOv11 red proposal")

x = float(proposal["rawCentre"]["x"])
y = float(proposal["rawCentre"]["y"])
image = Image.open(source_path).convert("RGB")
left, top, right, bottom = 20, 90, 245, 315
crop = image.crop((left, top, right, bottom))
header_height = 38
canvas = Image.new("RGB", (crop.width, crop.height + header_height), (23, 23, 23))
canvas.paste(crop, (0, header_height))
draw = ImageDraw.Draw(canvas)
font = ImageFont.load_default()
draw.text((7, 3), "FRAME 1179 | UNREVIEWED RED HYPOTHESIS", font=font, fill=(255, 255, 255))
draw.text((7, 19), f"source pixel ({x:.1f}, {y:.1f}) | review aid only", font=font, fill=(235, 235, 235))

cx = round(x - left)
cy = round(y - top + header_height)
marker = (40, 255, 220)
draw.ellipse((cx - 29, cy - 29, cx + 29, cy + 29), outline=(0, 0, 0), width=7)
draw.ellipse((cx - 29, cy - 29, cx + 29, cy + 29), outline=marker, width=3)
draw.line((cx - 11, cy, cx + 11, cy), fill=(0, 0, 0), width=7)
draw.line((cx, cy - 11, cx, cy + 11), fill=(0, 0, 0), width=7)
draw.line((cx - 11, cy, cx + 11, cy), fill=marker, width=3)
draw.line((cx, cy - 11, cx, cy + 11), fill=marker, width=3)

scale = 4
review_image = canvas.resize((canvas.width * scale, canvas.height * scale), Image.Resampling.NEAREST)
metadata = PngImagePlugin.PngInfo()
metadata.add_text("review_status", "Visual review aid only; marker is an unreviewed model proposal, not ground truth.")
metadata.add_text("source_frame", str(FRAME_ID))
metadata.add_text("source_pixel_sha256", source_sha256)
metadata.add_text("replay_report_sha256", report_sha256)
metadata.add_text("proposal_id", str(proposal["id"]))
metadata.add_text("proposed_source_center", f"{x:.1f},{y:.1f}")
review_image.save(OUT / "frame1179-red-proposal-review.png", pnginfo=metadata, optimize=True)
