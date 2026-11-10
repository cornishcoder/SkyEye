from pathlib import Path
from PIL import Image, ImageDraw
import numpy as np
import math
import json

REPO = Path(r"c:\code\SkyEye")
SCRATCH = Path(r"C:\Users\richardkeast\.gemini\antigravity\brain\84dc0d15-24fb-4a73-9ecf-d2adee6f9b98\scratch")
SHEETS_DIR = SCRATCH / "contact_sheets"

with open(SCRATCH / "evidence_inventory_all_images.json", "r", encoding="utf-8") as f:
    all_images = json.load(f)
proj_images = [r for r in all_images if not r["path"].startswith("work/python-reference/")]
by_path = {r["path"]: r for r in proj_images}

def find_image(substrings, kind=None):
    for r in proj_images:
        p = r["path"]
        if kind and r.get("kind") != kind:
            continue
        if all(s.lower() in p.lower() for s in substrings):
            return p
    for r in proj_images:
        p = r["path"]
        if all(s.lower() in p.lower() for s in substrings):
            return p
    return None

def draw_tile(img_pil, title_lines, target_w=460, target_h=258, border_color=(70, 70, 70)):
    header_h = 20 * len(title_lines) + 8
    canvas = Image.new("RGB", (target_w, target_h + header_h), (20, 22, 26))
    draw = ImageDraw.Draw(canvas)
    for idx, line in enumerate(title_lines):
        draw.text((6, 4 + idx * 19), str(line)[:70], fill=(235, 235, 235))
    if img_pil is not None:
        iw, ih = img_pil.size
        scale = min((target_w - 4) / max(1, iw), (target_h - 4) / max(1, ih))
        nw, nh = max(1, int(iw * scale)), max(1, int(ih * scale))
        resized = img_pil.resize((nw, nh), Image.Resampling.LANCZOS)
        ox = (target_w - nw) // 2
        oy = header_h + (target_h - nh) // 2
        canvas.paste(resized, (ox, oy))
    else:
        draw.text((20, header_h + 40), "IMAGE NOT FOUND", fill=(255, 80, 80))
    draw.rectangle([0, 0, target_w - 1, target_h + header_h - 1], outline=border_color, width=1)
    return canvas

def assemble_grid(tiles, cols, title):
    tw, th = tiles[0].size
    rows = math.ceil(len(tiles) / cols)
    banner_h = 44
    sheet = Image.new("RGB", (cols * tw, banner_h + rows * th), (12, 14, 18))
    draw = ImageDraw.Draw(sheet)
    draw.text((12, 12), title, fill=(255, 220, 120))
    for idx, t in enumerate(tiles):
        r = idx // cols
        c = idx % cols
        sheet.paste(t, (c * tw, banner_h + r * th))
    return sheet

def make_zoom_crop_px(rel_p, box_px, label, sublabel):
    p = REPO / rel_p
    if not p.exists():
        return draw_tile(None, [label, sublabel, f"MISSING: {rel_p}"])
    im = Image.open(p).convert("RGB")
    x0, y0, x1, y1 = box_px
    crop = im.crop((x0, y0, x1, y1))
    arr = np.array(crop)
    mr, mg, mb = arr.mean(axis=(0,1))
    return draw_tile(crop, [label, sublabel, f"Crop px ({x0},{y0})-({x1},{y1}) | Mean RGB=({mr:.1f},{mg:.1f},{mb:.1f})"], target_w=460, target_h=258)

p_1691 = "work/o09/transfer-new-controls-pixel-baseline-replay-20261102/inputs/1691/1691.png"
p_2083 = "work/o08/live-detect-black-pair-v1/corpus-v4/190/190.png"
p_2831 = "work/o09/transfer-new-controls-pixel-baseline-replay-20261102/inputs/2831/2831.png"
p_1274 = find_image(["normal-train-layout-03", "1274.png"]) or find_image(["/1274", ".png"], kind="raw_table_frame")
p_1049 = "work/o08/live-detect-black-pair-v1/corpus-v4/1049/1049.png"
p_266  = find_image(["warm-batch-20261102", "266.png"], kind="raw_table_frame")

tiles_6 = [
    make_zoom_crop_px(p_1691, (930, 60, 1330, 280), "1. Zoom: Frame 1691 Top-Cushion Black+Cue Pair", "Black ball w/ specular spot + adjacent bright cue ball on top rail"),
    make_zoom_crop_px(p_2083, (1480, 50, 1860, 265), "2. Zoom: Frame 2083 Upper-Right Pocket Black+Cue", "Black in pocket throat + bright cue ball -> RejectExterior (>40)"),
    make_zoom_crop_px(p_1274, (60, 680, 420, 960), "3. Zoom: Layout03 Frame 1274 Lower-Left Cue+Black", "Cue on left cushion (y=0 in rectified 800x1600) + Black in pocket"),
    make_zoom_crop_px(p_2831, (1080, 60, 1480, 280), "4. Zoom: Frame 2831 Mid-Cushion Black+Cue (Bright)", "Bright glare + cue ball -> Black boundary support 17/32 (< 21/32)"),
    make_zoom_crop_px(p_1049, (960, 740, 1820, 1015), "5. Zoom: Frame 1049 Lower-Right Cushion Y3 & Black+Cue", "3D ball height + oblique view projects Y3 radially outward by 26.8px"),
    make_zoom_crop_px(p_266,  (420, 380, 1520, 780), "6. Zoom: Warm-Dim Frame 266 Cue & Rack", "Warm light: cloth R=58.3 > B=52.1; black & cue missed by baseline"),
]

overlay_specs_6 = [
    ("7. Repo Diagnostic: Pocket-Cushion Manual Centres (1365)", ["manual-centres-frame-1365-draft.png"]),
    ("8. Repo Diagnostic: Raw Perspective Hough Votes (Daylight)", ["raw-perspective-hough-probe", "daylight/raw-perspective-votes-15.png"]),
    ("9. Repo Diagnostic: Raw Perspective Local Scale (Dim)", ["dim-control-raw-local-scale-0.35.png"]),
    ("10. Repo Diagnostic: O09 Reviewed Spatial Overlay", ["o09", "overlay", ".png"]),
    ("11. Repo Diagnostic: Pocket-Warp Coverage Audit", ["pocket-warp-coverage-audit", ".png"]),
    ("12. Repo Diagnostic: Companion Live Detect Screenshot", ["baseline-final-pixel.png"]),
]

for label, subs in overlay_specs_6:
    rel_p = find_image(subs)
    if rel_p:
        im = Image.open(REPO / rel_p).convert("RGB")
        meta = by_path.get(rel_p, {})
        short_p = rel_p.replace("work/production/evidence/", "w/p/e/").replace("work/", "w/")
        tiles_6.append(draw_tile(im, [label, short_p, f"kind={meta.get('kind')} | {meta.get('width')}x{meta.get('height')}"], target_w=460, target_h=258))
    else:
        tiles_6.append(draw_tile(None, [label, "NOT FOUND", ""]))

sheet6 = assemble_grid(tiles_6, cols=3, title="SkyEye Forensic Audit - Sheet 6: Original-Resolution Failure Crops & Historical Diagnostic Overlays")
sheet6.save(SHEETS_DIR / "sheet_06_failure_mechanisms_homography_inventory_verifier.png")
print("Updated Sheet 6")
