import os
import json
import math
from pathlib import Path
from PIL import Image, ImageDraw
import cv2
import numpy as np

REPO = Path(r"c:\code\SkyEye")
SCRATCH = Path(r"C:\Users\richardkeast\.gemini\antigravity\brain\84dc0d15-24fb-4a73-9ecf-d2adee6f9b98\scratch")
SHEETS_DIR = SCRATCH / "contact_sheets"
SHEETS_DIR.mkdir(parents=True, exist_ok=True)

with open(SCRATCH / "evidence_inventory_all_images.json", "r", encoding="utf-8") as f:
    all_images = json.load(f)

with open(SCRATCH / "evidence_inventory_summary.json", "r", encoding="utf-8") as f:
    summary = json.load(f)

proj_images = [r for r in all_images if not r["path"].startswith("work/python-reference/")]
by_path = {r["path"]: r for r in proj_images}

def find_image(substrings, kind=None):
    for r in proj_images:
        p = r["path"]
        if kind and r.get("kind") != kind:
            continue
        if all(s.lower() in p.lower() for s in substrings):
            return p
    # fallback without kind filter
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

# Build Sheet 3: Lighting Strata & Within-Sequence Camera AE/AWB Drift
lighting_specs = [
    ("1. Daylight Bright (Holdout, luma=134)", ["code66-lighting-holdout", "daylight-01.png"]),
    ("2. All-Lights On (Holdout, luma=130)", ["code66-lighting-holdout", "all-01.png"]),
    ("3. Lights-Off Ambient (Holdout, luma=96)", ["code66-lighting-holdout", "off-01.png"]),
    ("4. Mid-Light Device Check (Frame 250, luma=61)", ["o08-current-device-check", "luma-gamma16/250.png"]),
    ("5. Ultra-Dark Failure (Frame 3496, luma=28)", ["code30-live-detect-frame-3496.png"]),
    ("6. Shadowed Dark Rack (Fixture, luma=78)", ["pixel-packed-rack-shadowed-dark.png"]),
    ("7. Warm-Dim Pool Light (Frame 266, luma=68)", ["warm-batch-20261102", "266.png"]),
    ("8. Normal Pool Room (Layout01 667, luma=91)", ["667", ".png"]),
    ("9. AE Drift A1: Frame 2831 (Bright +Cue, luma=107.8)", ["2831", ".png"]),
    ("10. AE Drift A2: Frame 542 (Same Light -Cue, luma=97.6)", ["542", ".png"]),
    ("11. AWB Drift B1: Frame 678 (Black+Cue, cloth B=188)", ["678", ".png"]),
    ("12. AWB Drift B2: Frame 455 (Black-Only, cloth B=197)", ["frame978-development-v1", "455.png"]),
]

lighting_tiles = []
for label, subs in lighting_specs:
    rel_p = find_image(subs, kind="raw_table_frame")
    if not rel_p:
        print("WARNING Sheet 3 missing:", label, subs)
        im = None
        rel_p = "NOT FOUND"
        stats_str = "missing"
    else:
        im = Image.open(REPO / rel_p).convert("RGB")
        arr = np.array(im)
        h, w, _ = arr.shape
        patch = arr[int(h*0.42):int(h*0.58), int(w*0.25):int(w*0.35)]
        mr, mg, mb = arr.mean(axis=(0,1))
        pr, pg, pb = patch.mean(axis=(0,1))
        stats_str = f"Global RGB=({mr:.1f},{mg:.1f},{mb:.1f}) | ClothPatch RGB=({pr:.1f},{pg:.1f},{pb:.1f})"
    short_p = rel_p.replace("work/production/evidence/", "w/p/e/").replace("work/", "w/")
    lighting_tiles.append(draw_tile(im, [label, short_p, stats_str], target_w=460, target_h=258))

sheet3 = assemble_grid(lighting_tiles, cols=3, title="SkyEye Forensic Audit - Sheet 3: Lighting Strata & Uncontrolled Camera AE/AWB Drift Across Captures")
sheet3.save(SHEETS_DIR / "sheet_03_lighting_strata_and_ae_awb_drift.png")
print("Saved Sheet 3")

# Build Sheet 4: Ball Arrangements, Control Groups & Obstructions
arrangement_specs = [
    ("1. Standard 15-Ball Rack + Cue (Fixture)", ["pixel-packed-rack-2026-10-25.jpg"]),
    ("2. 13-Ball Post-Shot Spread (Reviewed GT: 6R,5Y,1Blk,1Cue)", ["pixel-table-post-shot-spread-2026-10-27.png"]),
    ("3. Code65 16-Ball Spread (Frame 978, Mid-Cushion Black)", ["frame978-development-v1", "978.png"]),
    ("4. Lower-Left Pocket Black + Cue (Frame 288)", ["/288", ".png"]),
    ("5. Lower-Right Pocket Black + Cue (Frame 1049)", ["corpus-v4/1049/1049.png"]),
    ("6. Upper-Right Pocket Black + Cue (Frame 190)", ["corpus-v4/190/190.png"]),
    ("7. Upper-Right Dev Control: Black+Cue (Frame 2083)", ["/2083", ".png"]),
    ("8. Opposite-Side Cue Control: Black+Cue (Frame 1691)", ["/1691", ".png"]),
    ("9. Layout03 Four-State: Full 16 Balls (Frame 1274)", ["/1274", ".png"]),
    ("10. Layout03 Four-State: Minus Black 15 Balls (Frame 3360)", ["/3360", ".png"]),
    ("11. Layout03 Four-State: Minus Cue 15 Balls (Frame 957)", ["/957", ".png"]),
    ("12. Hand/Arm Obstruction Control (Frame 1184)", ["o08-hand-obstruction-pair", "1184.png"]),
]

arr_tiles = []
for label, subs in arrangement_specs:
    rel_p = find_image(subs, kind="raw_table_frame")
    if not rel_p:
        print("WARNING Sheet 4 missing:", label, subs)
        im = None
        rel_p = "NOT FOUND"
        meta_str = "missing"
    else:
        im = Image.open(REPO / rel_p).convert("RGB")
        meta = by_path.get(rel_p, {})
        meta_str = f"dim={meta.get('width')}x{meta.get('height')} | luma={meta.get('mean_luma')} | sha={meta.get('sha256','')[:10]}"
    short_p = rel_p.replace("work/production/evidence/", "w/p/e/").replace("work/", "w/")
    arr_tiles.append(draw_tile(im, [label, short_p, meta_str], target_w=460, target_h=258))

sheet4 = assemble_grid(arr_tiles, cols=3, title="SkyEye Forensic Audit - Sheet 4: Physical Ball Arrangements, 4-State Controls & Obstruction Cases")
sheet4.save(SHEETS_DIR / "sheet_04_ball_arrangements_and_controls.png")
print("Saved Sheet 4")

# Build Sheet 5 & Sheet 6: Zoomed Failure Mechanisms & Stage Masks/Overlays
# Let's inspect what overlay/mask files exist for 1691, 2083, 2831, 962, 1274, 3360, 957, 2263, 1365, 1049, 266
failure_specs_5 = [
    ("1. Frame 1691 Dark Mask Overlay (Black @ Pocket Lip)", ["1691", "dark-mask-overlay.png"]),
    ("2. Frame 1691 Distance Window (Black Seed Comp #133 > 64 Cap)", ["1691", "distance-window.png"]),
    ("3. Frame 2083 Upper-Right Black+Cue Overlay/Source", ["2083", ".png"]),
    ("4. Frame 2831 Dark Mask Overlay (Support 17/32 < 21/32 Gate)", ["2831", "dark-mask-overlay.png"]),
    ("5. Code70 Frame 1365 Dark Mask (Peak 7.77px < 8.0px Gate)", ["1365-1000-dark-mask-overlay.png"]),
    ("6. Code70 Frame 1365 Distance Window", ["1365-1000-distance-window.png"]),
    ("7. Frame 554 Daylight Pocket Dark Mask (Width 800)", ["554-800-dark-mask-overlay.png"]),
    ("8. Frame 554 Daylight Pocket Distance Window (Width 800)", ["554-800-distance-window.png"]),
    ("9. Layout03 Frame 1274 (Cue @ y=0 Top Cushion Miss)", ["1274", ".png"]),
    ("10. Layout03 Frame 3360 (Cue @ y=0 Top Cushion Miss)", ["3360", ".png"]),
    ("11. Layout03 Frame 382 (Cue @ y=0 Top Cushion Miss)", ["382", ".png"]),
    ("12. Warm-Dim Frame 266 Source (Cue & Black Missed)", ["warm-batch-20261102", "266.png"]),
]

tiles_5 = []
for label, subs in failure_specs_5:
    rel_p = find_image(subs)
    if not rel_p:
        print("WARNING Sheet 5 missing:", label, subs)
        im = None
        rel_p = "NOT FOUND"
        meta_str = "missing"
    else:
        im = Image.open(REPO / rel_p).convert("RGB")
        meta = by_path.get(rel_p, {})
        meta_str = f"kind={meta.get('kind')} | dim={meta.get('width')}x{meta.get('height')} | luma={meta.get('mean_luma')}"
    short_p = rel_p.replace("work/production/evidence/", "w/p/e/").replace("work/", "w/")
    tiles_5.append(draw_tile(im, [label, short_p, meta_str], target_w=460, target_h=258))

sheet5 = assemble_grid(tiles_5, cols=3, title="SkyEye Forensic Audit - Sheet 5: Failure Mechanisms Part 1 (Sparse Black Seed Cap, Distance Peak, Sector Support & Cushion Cue)")
sheet5.save(SHEETS_DIR / "sheet_05_failure_mechanisms_black_and_cue.png")
print("Saved Sheet 5")

# Sheet 6: Zoomed Original-Resolution Crops of Key Failure Sites + Overlays
# We create high-resolution zoomed crops directly from the original 1920x1080 frames to inspect pixel-level evidence!
def make_zoom_crop(rel_p, box_norm, label, sublabel):
    p = REPO / rel_p
    if not p.exists():
        return draw_tile(None, [label, sublabel, "MISSING"])
    im = Image.open(p).convert("RGB")
    w, h = im.size
    x0, y0, x1, y1 = int(box_norm[0]*w), int(box_norm[1]*h), int(box_norm[2]*w), int(box_norm[3]*h)
    crop = im.crop((x0, y0, x1, y1))
    arr = np.array(crop)
    mr, mg, mb = arr.mean(axis=(0,1))
    return draw_tile(crop, [label, sublabel, f"Crop ({x0},{y0})-({x1},{y1}) | Mean RGB=({mr:.1f},{mg:.1f},{mb:.1f})"], target_w=460, target_h=258)

p_1691 = find_image(["/1691", ".png"], kind="raw_table_frame")
p_2083 = find_image(["/2083", ".png"], kind="raw_table_frame")
p_1274 = find_image(["/1274", ".png"], kind="raw_table_frame")
p_957  = find_image(["/957", ".png"], kind="raw_table_frame")
p_1049 = find_image(["corpus-v4/1049/1049.png"], kind="raw_table_frame")
p_266  = find_image(["warm-batch-20261102", "266.png"], kind="raw_table_frame")

tiles_6 = [
    make_zoom_crop(p_1691, (0.68, 0.08, 0.90, 0.35), "1. Zoom: Frame 1691 Upper-Right Pocket Black", "Black ball on pocket jaw + specular spot + dark pocket shadow"),
    make_zoom_crop(p_2083, (0.68, 0.08, 0.90, 0.35), "2. Zoom: Frame 2083 Upper-Right Black + Cue", "Adjacent bright cue ball triggers RejectExterior (>40) on black sectors"),
    make_zoom_crop(p_1274, (0.12, 0.12, 0.34, 0.42), "3. Zoom: Layout03 Frame 1274 Left-Rail Cue Ball", "Cue ball touching top/left cushion; rail glare & shadow reject sectors"),
    make_zoom_crop(p_957,  (0.18, 0.15, 0.82, 0.85), "4. Zoom: Layout03 Frame 957 (Minus-Cue 15 Balls)", "1 ball misclassified as 8th yellow -> Inventory Cap demotes ALL 8 to Unknown!"),
    make_zoom_crop(p_1049, (0.50, 0.70, 0.88, 0.96), "5. Zoom: Frame 1049 Lower-Right Cushion Y3 & Black", "3D ball height + oblique angle projects Y3 radially outward by 26.8px"),
    make_zoom_crop(p_266,  (0.15, 0.15, 0.85, 0.85), "6. Zoom: Warm-Dim Frame 266 Table Interior", "Warm illumination shifts cloth/ball chromaticity; black & cue missed"),
]

# Add 6 existing diagnostic overlays / contact sheets from repo to Sheet 6
overlay_specs_6 = [
    ("7. Repo Diagnostic: Pocket-Cushion Manual Centres (1365)", ["manual-centres-frame-1365-draft.png"]),
    ("8. Repo Diagnostic: Raw Perspective Hough Votes (Daylight)", ["raw-perspective-hough-probe", "daylight/raw-perspective-votes-15.png"]),
    ("9. Repo Diagnostic: Raw Perspective Local Scale (Dim)", ["dim-control-raw-local-scale-0.35.png"]),
    ("10. Repo Diagnostic: O09 Reviewed Spatial Overlay", ["o09", "overlay", ".png"]),
    ("11. Repo Diagnostic: Rectified Table Verification Frame", ["rectified", ".png"]),
    ("12. Repo Diagnostic: Companion Live Detect Screenshot", ["ui_screenshot"]),
]

for label, subs in overlay_specs_6:
    if subs == ["ui_screenshot"]:
        rel_p = find_image(["screenshot"], kind="ui_screenshot") or find_image([".png"], kind="ui_screenshot")
    else:
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
print("Saved Sheet 6")
