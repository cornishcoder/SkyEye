"""Generate deterministic, approximate pool-table staging diagrams for O09 Development.

These are staging aids, not camera images, calibration targets, labels, or scale drawings.
Requires Pillow. Run with: python generate_references.py
"""

from __future__ import annotations

import hashlib
import json
import textwrap
from pathlib import Path

from PIL import Image, ImageDraw, ImageFont


OUT = Path(__file__).resolve().parent
ROOT = OUT.parents[2]
PLAN = ROOT / "docs/evidence/live-detect-o09-development-split-plan-20261101.json"
PLAN_SHA = hashlib.sha256(PLAN.read_bytes()).hexdigest()

W, H = 1600, 1080
TABLE = (120, 150, 1480, 830)
TW, TH = TABLE[2] - TABLE[0], TABLE[3] - TABLE[1]
BALL_R = 19
COLORS = {
    "R": (214, 70, 83),
    "Y": (247, 194, 54),
    "B": (19, 23, 27),
    "C": (244, 245, 238),
}
INK = (29, 42, 48)


def font(size: int, bold: bool = False):
    candidates = [
        Path("C:/Windows/Fonts/arialbd.ttf" if bold else "C:/Windows/Fonts/arial.ttf"),
        Path("C:/Windows/Fonts/segoeuib.ttf" if bold else "C:/Windows/Fonts/segoeui.ttf"),
    ]
    for candidate in candidates:
        if candidate.exists():
            return ImageFont.truetype(str(candidate), size)
    return ImageFont.load_default()


F_TITLE = font(34, True)
F_SUB = font(21)
F_BALL = font(15, True)
F_FOOT = font(19)
F_SMALL = font(16)


def point(x: float, y: float) -> tuple[int, int]:
    return (round(TABLE[0] + x * TW), round(TABLE[1] + y * TH))


def rack_balls(marking: str) -> list[dict]:
    """A fifteen-ball triangle, black in the middle row, plus cue at the other end."""
    pattern = ["R", "Y"] * 7
    if marking == "B":
        pattern.reverse()
    red_index = yellow_index = pattern_index = 0
    rack: list[dict] = []
    row_x, x_step, y_step = 0.38, 0.023, 0.053
    for row in range(5):
        for col in range(row + 1):
            x = row_x + row * x_step
            y = 0.50 + (col - row / 2) * y_step
            if row == 2 and col == 1:
                label = "B"
            else:
                kind = pattern[pattern_index]
                pattern_index += 1
                if kind == "R":
                    red_index += 1
                    label = f"R{red_index}"
                else:
                    yellow_index += 1
                    label = f"Y{yellow_index}"
            rack.append({"id": label, "x": x, "y": y})
    rack.append({"id": "C", "x": 0.77, "y": 0.50})
    assert sum(b["id"].startswith("R") for b in rack) == 7
    assert sum(b["id"].startswith("Y") for b in rack) == 7
    return rack


def spread(red_xy: list[tuple[float, float]], yellow_xy: list[tuple[float, float]], black=(0.53, 0.50), cue=(0.77, 0.50)) -> list[dict]:
    balls = [{"id": f"R{i}", "x": x, "y": y} for i, (x, y) in enumerate(red_xy, 1)]
    balls += [{"id": f"Y{i}", "x": x, "y": y} for i, (x, y) in enumerate(yellow_xy, 1)]
    if black is not None:
        balls.append({"id": "B", "x": black[0], "y": black[1]})
    if cue is not None:
        balls.append({"id": "C", "x": cue[0], "y": cue[1]})
    assert sum(b["id"].startswith("R") for b in balls) == 7
    assert sum(b["id"].startswith("Y") for b in balls) == 7
    return balls


NORMAL_TRAIN_L03 = spread(
    [(0.22, .16), (.48, .13), (.76, .18), (.31, .40), (.83, .42), (.47, .69), (.68, .79)],
    [(.16, .32), (.37, .28), (.58, .29), (.74, .34), (.26, .60), (.61, .61), (.88, .66)],
    black=(.065, .88), cue=(.078, .80),
)
WARM_VALIDATION_L03 = spread(
    [(.19, .18), (.46, .14), (.73, .23), (.28, .43), (.58, .46), (.82, .56), (.44, .76)],
    [(.18, .69), (.35, .27), (.55, .26), (.77, .33), (.69, .67), (.88, .73), (.39, .56)],
    black=(.065, .88), cue=(.078, .80),
)
WARM_TRAIN_L03 = spread(
    [(.20, .19), (.49, .15), (.78, .21), (.33, .40), (.85, .44), (.55, .72), (.37, .79)],
    [(.16, .35), (.40, .30), (.65, .29), (.75, .56), (.25, .62), (.65, .82), (.90, .34)],
    black=(.065, .88), cue=(.078, .80),
)

WARM_TRAIN_L02 = spread(
    [(.18, .18), (.39, .19), (.64, .16), (.81, .29), (.25, .48), (.51, .56), (.76, .74)],
    [(.12, .70), (.34, .72), (.55, .56), (.65, .37), (.87, .61), (.93, .17), (.43, .40)],
    black=(.49, .82), cue=(.71, .49),
)
WARM_VALIDATION_L02 = spread(
    [(.08, .10), (.32, .19), (.60, .17), (.82, .21), (.19, .52), (.43, .62), (.80, .77)],
    [(.19, .82), (.39, .27), (.57, .32), (.77, .39), (.31, .72), (.59, .59), (.60, .59)],
    black=(.73, .72), cue=(.39, .47),
)
# R7 is projected behind Y6; their small diagram overlap cues a camera-view
# occlusion. The table balls themselves must touch, not physically overlap.
WARM_VALIDATION_L02[6]["x"], WARM_VALIDATION_L02[6]["y"] = (.58, .60)  # R7
WARM_VALIDATION_L02[6]["callout"] = (-62, -36)
WARM_VALIDATION_L02[12]["x"], WARM_VALIDATION_L02[12]["y"] = (.59, .60)  # Y6
WARM_VALIDATION_L02[13]["x"], WARM_VALIDATION_L02[13]["y"] = (.69, .55)  # Y7

WARM_TRAIN_L01 = rack_balls("B")
NORMAL_TRAIN_L01 = rack_balls("A")

WARM_VALIDATION_L01 = spread(
    [(.19, .21), (.42, .18), (.69, .16), (.84, .32), (.24, .52), (.60, .59), (.77, .77)],
    [(.16, .76), (.37, .33), (.57, .28), (.72, .42), (.37, .68), (.87, .61), (.52, .81)],
    black=None, cue=(.77, .51),
)


def l03_specs(role: str, layout: list[dict]) -> list[dict]:
    states = [
        ("both-present", "Both present — start state", "16 balls: 7 red, 7 yellow, black B and cue C.", "Anchor: establish both visible centres."),
        ("black-absent", "Black absent", "15 balls: 7 red, 7 yellow and cue C; black is removed.", "Remove only B. Leave C and all 14 other object balls fixed."),
        ("cue-absent", "Cue absent", "15 balls: 7 red, 7 yellow and black B; cue is removed.", "Restore only B and remove only C. Keep every other object ball fixed."),
        ("both-restored", "Both restored", "16 balls: 7 red, 7 yellow, black B and cue C.", "Restore only C. Keep B, all other balls, camera and light fixed."),
    ]
    result = []
    for slug, title, count, instruction in states:
        if slug == "black-absent":
            balls = [b.copy() for b in layout if b["id"] != "B"]
            absent = [("B?", .065, .88)]
        elif slug == "cue-absent":
            balls = [b.copy() for b in layout if b["id"] != "C"]
            absent = [("C?", .078, .80)]
        else:
            balls = [b.copy() for b in layout]
            absent = []
        result.append({
            "filename": f"{role}-layout03-{slug}-reference.png",
            "title": f"{role.replace('-', ' ').title()} Layout03 — {title}",
            "subtitle": "Matched-control sequence · keep lighting, camera, chalk and every unlisted ball fixed",
            "balls": balls, "absent": absent,
            "count": count, "instruction": instruction,
            "setupGroupId": f"o09-v3-{role}-layout-03",
            "layoutId": f"{role}-layout-03",
            "stratum": "warm-dim-playable" if role.startswith("warm-") else "normal-pool-room",
            "role": "train" if role.endswith("train") else "validation",
        })
    return result


SPECS = [
    {
        "filename": "normal-train-layout01-replacement-reference.png",
        "title": "Normal-train Layout01 — independent replacement",
        "subtitle": "New setup/session · full rack · normal-pool-room · marking orientation A",
        "balls": NORMAL_TRAIN_L01, "absent": [],
        "count": "16 balls: 7 red, 7 yellow, black and cue",
        "instruction": "Use a fresh independent setup. Turn each ball's visible mark to the agreed A orientation; label raw centres before detector output.",
        "setupGroupId": "o09-v3-normal-train-layout-01", "layoutId": "normal-train-layout-01",
        "stratum": "normal-pool-room", "role": "train",
    },
    {
        "filename": "warm-train-layout01-reference.png",
        "title": "Warm-train Layout01 — full rack",
        "subtitle": "warm-dim-playable · full rack · marking orientation distinct from normal-train A",
        "balls": WARM_TRAIN_L01, "absent": [],
        "count": "16 balls: 7 red, 7 yellow, black and cue",
        "instruction": "Keep the rack complete. Orient visible marks differently from normal/train A; this diagram shows colour/position, not mark angles.",
        "setupGroupId": "o09-v3-warm-train-layout-01", "layoutId": "warm-train-layout-01",
        "stratum": "warm-dim-playable", "role": "train",
    },
    {
        "filename": "warm-train-layout02-reference.png",
        "title": "Warm-train Layout02 — six-region spread",
        "subtitle": "warm-dim-playable · full 16-ball spread · all six pocket/cushion regions",
        "balls": WARM_TRAIN_L02, "absent": [],
        "count": "16 balls: 7 red, 7 yellow, black and cue",
        "instruction": "Keep every pocket mouth visible; R6/Y3 form a close pair, Y6 sits near the upper-right mouth, and chalk/shadow stay on the rail as negatives.",
        "setupGroupId": "o09-v3-warm-train-layout-02", "layoutId": "warm-train-layout-02",
        "stratum": "warm-dim-playable", "role": "train",
    },
    {
        "filename": "warm-validation-layout01-reference.png",
        "title": "Warm-validation Layout01 — black absent",
        "subtitle": "warm-dim-playable · distinct reduced spread · cue present",
        "balls": WARM_VALIDATION_L01, "absent": [("B?", .50, .49)],
        "count": "15 balls: 7 red, 7 yellow and cue; black absent",
        "instruction": "Do not place the black. Dashed B? shows only its expected empty point; leave all six pocket mouths open.",
        "setupGroupId": "o09-v3-warm-validation-layout-01", "layoutId": "warm-validation-layout-01",
        "stratum": "warm-dim-playable", "role": "validation",
    },
    {
        "filename": "warm-validation-layout02-reference.png",
        "title": "Warm-validation Layout02 — pocket and occlusion cases",
        "subtitle": "warm-dim-playable · full spread · near-mouth ball and touching/partly-hidden pair",
        "balls": WARM_VALIDATION_L02, "absent": [],
        "count": "16 balls: 7 red, 7 yellow, black and cue",
        "instruction": "R1 is near the upper-left mouth. Place R7/Y6 touching so the mounted view partly hides R7; do not physically overlap balls or label a covered centre.",
        "setupGroupId": "o09-v3-warm-validation-layout-02", "layoutId": "warm-validation-layout-02",
        "stratum": "warm-dim-playable", "role": "validation",
    },
]

SPECS += l03_specs("warm-train", WARM_TRAIN_L03)
SPECS += l03_specs("warm-validation", WARM_VALIDATION_L03)


def draw_wrapped(draw: ImageDraw.ImageDraw, text: str, xy: tuple[int, int], f, max_width: int, fill, leading: int = 28) -> int:
    words = text.split()
    lines: list[str] = []
    line = ""
    for word in words:
        candidate = (line + " " + word).strip()
        if draw.textbbox((0, 0), candidate, font=f)[2] <= max_width:
            line = candidate
        else:
            if line:
                lines.append(line)
            line = word
    if line:
        lines.append(line)
    x, y = xy
    for line in lines:
        draw.text((x, y), line, fill=fill, font=f)
        y += leading
    return y


def draw_spec(spec: dict) -> Image.Image:
    image = Image.new("RGB", (W, H), (247, 248, 246))
    draw = ImageDraw.Draw(image)
    draw.text((72, 35), spec["title"], fill=INK, font=F_TITLE)
    draw.text((74, 83), spec["subtitle"], fill=(85, 103, 110), font=F_SUB)

    x0, y0, x1, y1 = TABLE
    # Cushion and rail.
    draw.rounded_rectangle((x0 - 22, y0 - 22, x1 + 22, y1 + 22), radius=30, fill=(82, 61, 43))
    draw.rectangle((x0, y0, x1, y1), fill=(68, 119, 128))
    draw.rectangle((x0 + 17, y0 + 17, x1 - 17, y1 - 17), outline=(104, 149, 154), width=3)
    # Six open pocket mouths.
    pockets = [(x0, y0), ((x0 + x1) // 2, y0), (x1, y0), (x0, y1), ((x0 + x1) // 2, y1), (x1, y1)]
    for px, py in pockets:
        draw.ellipse((px - 36, py - 36, px + 36, py + 36), fill=(8, 12, 15))
    # Fixed chalk/shadow negative; these are not balls.
    chalk = (x0 + round(.66 * TW), y0 - 22)
    draw.ellipse((chalk[0] - 13, chalk[1] - 13, chalk[0] + 13, chalk[1] + 13), fill=(55, 182, 204), outline=(20, 66, 78), width=2)
    draw.text((chalk[0] + 22, chalk[1] - 12), "blue rail chalk · negative, not a ball", fill=(30, 54, 62), font=F_SMALL)
    draw.ellipse((x0 + round(.78 * TW), y0 + 38, x0 + round(.85 * TW), y0 + 61), fill=(54, 89, 94))
    draw.text((x0 + round(.78 * TW) - 95, y0 + 64), "rail-shadow negative", fill=(220, 231, 230), font=F_SMALL)

    # Draw rear balls before front balls for the one specified occlusion example.
    balls = sorted(spec["balls"], key=lambda b: 0 if b.get("id") == "R7" and spec["layoutId"] == "warm-validation-layout-02" else 1)
    for ball in balls:
        label = ball["id"]
        cx, cy = point(ball["x"], ball["y"])
        fill = COLORS[label[0]]
        outline = (244, 245, 240) if label[0] in "RY" else (104, 126, 130) if label == "C" else (228, 228, 226)
        draw.ellipse((cx - BALL_R, cy - BALL_R, cx + BALL_R, cy + BALL_R), fill=fill, outline=outline, width=3)
        if "callout" in ball:
            tx, ty = cx + ball["callout"][0], cy + ball["callout"][1]
            draw.line((cx - 8, cy - 7, tx + 18, ty + 8), fill=(250, 250, 245), width=2)
            draw.text((tx, ty), label + " (rear)", fill=(255, 255, 255), font=F_SMALL, stroke_width=2, stroke_fill=(37, 48, 54))
        else:
            bbox = draw.textbbox((0, 0), label, font=F_BALL)
            tw, th = bbox[2] - bbox[0], bbox[3] - bbox[1]
            ink = (25, 31, 34) if label[0] in "YC" else (250, 250, 248)
            draw.text((cx - tw / 2, cy - th / 2 - 2), label, fill=ink, font=F_BALL)

    for label, bx, by in spec["absent"]:
        cx, cy = point(bx, by)
        for i in range(24):
            import math
            a0, a1 = i * 15, i * 15 + 8
            draw.arc((cx - BALL_R, cy - BALL_R, cx + BALL_R, cy + BALL_R), a0, a1, fill=(225, 231, 231), width=3)
        draw.text((cx + 23, cy - 11), label + " · empty point only", fill=(248, 250, 248), font=F_SMALL, stroke_width=2, stroke_fill=(50, 78, 83))

    y = draw_wrapped(draw, spec["count"], (72, 874), font(22, True), 1450, (32, 48, 55), leading=31)
    y = draw_wrapped(draw, spec["instruction"], (72, y + 5), F_FOOT, 1450, (54, 74, 81), leading=28)
    y = draw_wrapped(draw, "Approximate staging schematic. Dashed B?/C? marks an expected empty location; do not place a ball there.", (72, y + 8), F_SMALL, 1450, (85, 103, 110), leading=24)
    draw_wrapped(draw, "Keep camera capture stopped while staging. Future capture requires a playable, recorded light condition and hands clear.", (72, y + 4), F_SMALL, 1450, (85, 103, 110), leading=24)
    return image


def main() -> None:
    if len(SPECS) != 13:
        raise RuntimeError(f"Expected 13 references, got {len(SPECS)}")
    split = json.loads(PLAN.read_text(encoding="utf-8"))
    planned = {g["layoutId"]: g for g in split["setupGroups"] if g["layoutId"]}
    rows = []
    images = []
    for spec in SPECS:
        layout = planned.get(spec["layoutId"])
        if layout is None or layout["setupGroupId"] != spec["setupGroupId"]:
            raise RuntimeError(f"Reference does not match frozen split plan: {spec['layoutId']}")
        if layout["lightingStratum"] != spec["stratum"] or layout["role"] != spec["role"]:
            raise RuntimeError(f"Role/lighting mismatch for {spec['layoutId']}")
        ids = [b["id"] for b in spec["balls"]]
        absent_ids = [b[0] for b in spec["absent"]]
        if len(ids) != len(set(ids)):
            raise RuntimeError(f"Duplicate ball ID in {spec['filename']}")
        if sum(i.startswith("R") for i in ids) != 7 or sum(i.startswith("Y") for i in ids) != 7:
            raise RuntimeError(f"Expected seven reds and seven yellows in {spec['filename']}")
        if ("B" in ids) == ("B?" in absent_ids) or ("C" in ids) == ("C?" in absent_ids):
            raise RuntimeError(f"Present/absent black or cue markers disagree in {spec['filename']}")
        if len(ids) not in (15, 16):
            raise RuntimeError(f"Unexpected ball count in {spec['filename']}: {len(ids)}")
        if len(ids) + len(absent_ids) != 16:
            raise RuntimeError(f"Visible balls plus expected-empty point must represent 16 object-ball positions in {spec['filename']}")
        if len(absent_ids) != len(set(absent_ids)) or any(i not in ("B?", "C?") for i in absent_ids):
            raise RuntimeError(f"Unexpected or duplicate absent marker in {spec['filename']}")
        img = draw_spec(spec)
        path = OUT / spec["filename"]
        img.save(path, format="PNG", optimize=True)
        digest = hashlib.sha256(path.read_bytes()).hexdigest()
        rows.append({
            "file": spec["filename"], "sha256": digest,
            "setupGroupId": spec["setupGroupId"], "layoutId": spec["layoutId"],
            "role": spec["role"], "lightingStratum": spec["stratum"],
            "count": spec["count"], "instruction": spec["instruction"],
            "absentPointIds": [b[0] for b in spec["absent"]],
            "visibleCounts": {"red": sum(i.startswith("R") for i in ids), "yellow": sum(i.startswith("Y") for i in ids),
                              "black": ids.count("B"), "cue": ids.count("C")},
        })
        images.append((img, spec["filename"], spec["title"]))

    for role, layout in (("warm-train", WARM_TRAIN_L03), ("warm-validation", WARM_VALIDATION_L03)):
        sequence = {spec["filename"].split("-layout03-")[1].split("-reference")[0]: spec
                    for spec in SPECS if spec["filename"].startswith(role + "-layout03-")}
        base = {b["id"]: (b["x"], b["y"]) for b in layout if b["id"] not in ("B", "C")}
        for state in sequence.values():
            actual = {b["id"]: (b["x"], b["y"]) for b in state["balls"] if b["id"] not in ("B", "C")}
            if actual != base:
                raise RuntimeError(f"Non-target balls moved during {role} Layout03 controls")

    columns, cell_w, cell_h, margin, gap = 3, 520, 350, 36, 22
    rows_n = (len(images) + columns - 1) // columns
    sheet = Image.new("RGB", (margin * 2 + columns * cell_w + (columns - 1) * gap,
                               105 + margin + rows_n * cell_h + (rows_n - 1) * gap), (238, 241, 239))
    sd = ImageDraw.Draw(sheet)
    sd.text((margin, 30), "O09 remaining Development staging references · 13 states", fill=INK, font=font(31, True))
    for i, (img, filename, title) in enumerate(images):
        row, col = divmod(i, columns)
        x, y = margin + col * (cell_w + gap), 105 + margin + row * (cell_h + gap)
        thumb = img.copy()
        thumb.thumbnail((cell_w, cell_h - 42), Image.Resampling.LANCZOS)
        sheet.paste(thumb, (x, y))
        sd.text((x, y + cell_h - 36), filename, fill=INK, font=font(14))
    contact = OUT / "remaining-staging-references-contact-sheet.png"
    sheet.save(contact, format="PNG", optimize=True)

    manifest = {
        "target": "O09-DEVELOPMENT-REMAINDER-REFERENCE-SHEETS",
        "corpusId": split["corpusId"], "splitPlanSha256": PLAN_SHA,
        "scope": "Thirteen approximate staging references: twelve warm-stratum staged states plus an independent normal-train Layout01 replacement. No capture or staging request.",
        "diagramSize": [W, H], "ballRadiusPixels": BALL_R,
        "pillowVersion": __import__("PIL").__version__,
        "generatorSha256": hashlib.sha256(Path(__file__).read_bytes()).hexdigest(), "references": rows,
        "contactSheet": {"file": contact.name, "sha256": hashlib.sha256(contact.read_bytes()).hexdigest()},
    }
    (OUT / "reference-manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"target": manifest["target"], "references": len(rows), "layoutGroups": len({r['setupGroupId'] for r in rows}),
                      "contactSheet": contact.name, "contactSheetSha256": manifest["contactSheet"]["sha256"]}, indent=2))


if __name__ == "__main__":
    main()
