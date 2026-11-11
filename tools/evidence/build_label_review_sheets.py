#!/usr/bin/env python3
"""T9: build label-review sheets from EXISTING frames and clips (no camera, no staging).

Sheet 1 (frames): about 40 unlabelled, non-Holdout frames from the T1 manifest
(tests/fixtures/detection-corpus/manifest.json), chosen for dim light, black or
cue at cushions and jaws (especially table (1.48, 0.067)), racks and empty-jaw
controls. Each gets a review image (rectified table, 0.25 grid, numbered DRAFT
centres from the result stored in the capture receipt, plus zoomed jaw/cushion
crops) and a draft label document in the project's sequence-label format
(schema 2, table-2x1) with "reviewed": false.

Sheet 2 (clips): the 29 October real-play clips scanned by T4. Per clip, a
timeline strip (1 s thumbnails, the T4 motion trace, DRAFT settled / moving /
shot intervals) and, for each T4 shot pair, a before/after panel with DRAFT
change regions (T4 grey v3) as candidate rest/departure positions.

Drafts are NOT truth: index_labels.py only reads files named labels.json with
reviewed true, and skips work/evidence-audit/ entirely. These drafts are named
*.labels-draft.json. tools/evidence/apply_label_review.py turns a reply from
Richard into reviewed labels.

Usage (repo root):
    python3 tools/evidence/build_label_review_sheets.py select
    python3 tools/evidence/build_label_review_sheets.py sheet1     [--force]
    python3 tools/evidence/build_label_review_sheets.py sheet2     [--force]   (repeat until "finished")
Outputs: work/evidence-audit/t9/sheet1/, work/evidence-audit/t9/sheet2/ (git-ignored).
Never opens Holdout rows (role holdout-excluded, split Holdout, or "holdout" in a path).
"""
import csv, importlib.util, json, math, os, subprocess, sys, time
import numpy as np, cv2

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
MANIFEST = os.path.join(ROOT, "tests", "fixtures", "detection-corpus", "manifest.json")
T9 = os.path.join(ROOT, "work", "evidence-audit", "t9")
S1 = os.path.join(T9, "sheet1"); S2 = os.path.join(T9, "sheet2")
T4 = os.path.join(ROOT, "work", "evidence-audit", "t4")
HOT = (1.48, 0.067)            # every reviewed black miss sits here (T1 README)
POCKETS = [(0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1)]
POCKET_NAMES = ["TL", "TM", "TR", "BL", "BM", "BR"]
R_BALL = 0.027                 # ball radius, table units
TARGET = 40
EXCLUDE_PATH = ("gamma", "ffmpeg", "brightmatch", "transition", "lightmix", "hand-2s", "obstruction-positive")
COL = {"red": (40, 40, 255), "yellow": (0, 230, 255), "cue": (255, 255, 255), "black": (255, 255, 0), "unknown": (200, 120, 255)}
FONT = cv2.FONT_HERSHEY_SIMPLEX

def rel(p): return os.path.relpath(p, ROOT).replace(os.sep, "/")
def is_holdout(r): return r.get("role") == "holdout-excluded" or r.get("split") == "Holdout" or any("holdout" in (p or "").lower() for p in [r.get("path")] + list(r.get("otherPaths") or []))
def zone(x, y):
    if min(math.hypot(x - a, y - b) for a, b in POCKETS) < 0.11: return "pocket"
    if min(x, 2 - x, y, 1 - y) < 0.075: return "cushion"
    return "open"
def stratum(v): return "unknown" if v is None else "dim" if v < 80 else "mid" if v < 110 else "bright"

def text(img, s, org, scale=0.6, col=(255, 255, 255), th=1, box=True):
    """text on a dark box (this OpenCV build's font widens with thickness, so no outline trick)."""
    if box:
        (tw, tht), bl = cv2.getTextSize(s, FONT, scale, th)
        x, y = org
        sub = img[max(0, y - tht - 3):min(img.shape[0], y + bl + 1), max(0, x - 2):min(img.shape[1], x + tw + 2)]
        if sub.size: sub[:] = (sub * 0.35).astype(np.uint8)
    cv2.putText(img, s, org, FONT, scale, col, th, cv2.LINE_AA)

def lift(img, V):
    """display-only brightening for dim frames (gamma 0.55); stated on the sheet."""
    if V is None or V >= 80: return img, False
    lut = np.array([255 * (i / 255.0) ** 0.55 for i in range(256)], np.uint8)
    return cv2.LUT(img, lut), True

# ---------------------------------------------------------------- selection
def features(r):
    sr = r.get("storedResult") or {}
    balls = sr.get("balls") if sr.get("hasVision") else None
    if not balls: return None
    V = (r.get("light") or {}).get("tableV")
    blk = [b for b in balls if b[0] == "black"]; cue = [b for b in balls if b[0] == "cue"]
    cx = sum(b[1] for b in balls) / len(balls); cy = sum(b[2] for b in balls) / len(balls)
    rack = len(balls) >= 12 and sum(math.hypot(b[1] - cx, b[2] - cy) < 0.25 for b in balls) >= 10
    near_hot = any(math.hypot(b[1] - HOT[0], b[2] - HOT[1]) < 0.12 for b in balls)
    d = os.path.dirname(r["path"])
    burst = os.path.basename(d) if len(os.path.basename(d)) != 32 else os.path.basename(os.path.dirname(d)) + "/" + os.path.basename(d)
    edge = lambda b: zone(b[1], b[2]) != "open"
    tags = []
    if V is not None and V < 80: tags.append("dim")
    if near_hot or "mid-cushion" in r["path"] or "opposite-cue" in r["path"]: tags.append("hotspot")
    if blk and edge(blk[0]): tags.append("black-edge")
    if cue and edge(cue[0]): tags.append("cue-edge")
    if not blk: tags.append("black-not-detected")
    if not cue: tags.append("cue-not-detected")
    if rack: tags.append("rack")
    if "absent" in r["path"].lower(): tags.append("deliberately-absent")
    if sr.get("uncertain"): tags.append("stored-uncertain")
    return dict(V=V, n=len(balls), tags=tags, dir=d, burst=burst)

def category(f):
    t = f["tags"]
    if "deliberately-absent" in t: return "empty-control"
    if "hotspot" in t: return "hotspot-1.48"
    if "rack" in t: return "rack"
    if "dim" in t and ({"black-edge", "cue-edge", "black-not-detected"} & set(t)): return "dim-cushion-jaw"
    if "dim" in t: return "dim-other"
    if "black-edge" in t or "cue-edge" in t: return "bright-black-cue-edge"
    return None

QUOTA = {"dim-cushion-jaw": 21, "hotspot-1.48": 7, "rack": 10, "empty-control": 4, "dim-other": 4, "bright-black-cue-edge": 4}
PER_DIR = {"dim-cushion-jaw": 2, "hotspot-1.48": 1, "rack": 2, "empty-control": 1, "dim-other": 1, "bright-black-cue-edge": 1}

def stage_select():
    m = json.load(open(MANIFEST, encoding="utf-8"))
    cands = []
    for r in m["frames"]:
        if r.get("role") != "unlabelled-reference" or not r.get("pixelPresent") or is_holdout(r): continue
        if not (r.get("calibration") or {}).get("corners") or not r.get("path"): continue
        if any(x in r["path"] for x in EXCLUDE_PATH): continue
        f = features(r)
        if not f: continue
        c = category(f)
        if c: cands.append((c, r, f))
    # darkest first; inside a category prefer frames where the detector reported no black
    cands.sort(key=lambda t: (t[2]["V"] if t[2]["V"] is not None else 999, "black-not-detected" not in t[2]["tags"]))
    chosen, per_dir, per_cat, layouts = [], {}, {}, set()
    for c, r, f in cands:
        if per_cat.get(c, 0) >= QUOTA[c] or per_dir.get(f["dir"], 0) >= PER_DIR[c]: continue
        lay = (f["dir"], f["n"], tuple(sorted(t for t in f["tags"] if t != "stored-uncertain")))
        if lay in layouts: continue            # second frame of a burst only if it differs
        layouts.add(lay); per_dir[f["dir"]] = per_dir.get(f["dir"], 0) + 1; per_cat[c] = per_cat.get(c, 0) + 1
        chosen.append((c, r, f))
    order = list(QUOTA)
    chosen.sort(key=lambda t: (order.index(t[0]), t[2]["V"] or 999))
    os.makedirs(S1, exist_ok=True)
    rows = []
    for i, (c, r, f) in enumerate(chosen, 1):
        fid = r["frameIds"][0] if r.get("frameIds") else "na"
        sr = r["storedResult"]
        rows.append(dict(item="F%02d" % i, frameId=fid, category=c, tableV=f["V"], stratum=stratum(f["V"]), split=r.get("split"),
                         nDraft=f["n"], tags=";".join(f["tags"]), path=r["path"], sha1=r.get("sha1"), sha256=r.get("sha256"),
                         sampleId=(r.get("sampleIds") or [""])[0] or "t9-%s-%s" % ((r.get("sha1") or "")[:12], fid),
                         receipt=sr.get("receipt"), configuration=sr.get("configuration"), capturedAt=sr.get("capturedAt"),
                         calibrationId=r["calibration"].get("calibrationId"), image="", draft=""))
    with open(os.path.join(S1, "selection.csv"), "w", newline="", encoding="utf-8") as fh:
        w = csv.DictWriter(fh, fieldnames=list(rows[0])); w.writeheader(); w.writerows(rows)
    print(len(rows), "frames selected:", {k: per_cat.get(k, 0) for k in QUOTA})
    for x in rows: print(x["item"], x["frameId"], x["category"], x["tableV"], x["nDraft"], x["tags"], x["path"][-70:])

# ---------------------------------------------------------------- sheet 1
def warp(img, corners, S, margin):
    h, w = img.shape[:2]
    src = np.float32(np.array(corners) * [w, h])
    m = margin * S
    dst = np.float32([[m, m], [m + 2 * S, m], [m + 2 * S, m + S], [m, m + S]])
    Hm = cv2.getPerspectiveTransform(src, dst)
    size = (int(round(2 * S + 2 * m)), int(round(S + 2 * m)))
    return cv2.warpPerspective(img, Hm, size, flags=cv2.INTER_CUBIC), m

def T2P(x, y, S, m): return int(round(m + x * S)), int(round(m + y * S))

def draw_grid(img, S, m, step=0.25, labels=True):
    for k in range(int(2 / step) + 1):
        x = k * step; px, _ = T2P(x, 0, S, m)
        cv2.line(img, (px, int(m)), (px, int(m + S)), (90, 90, 90) if k % 4 else (150, 150, 150), 1, cv2.LINE_AA)
        if labels: text(img, ("%g" % x), (px - 10, max(14, int(m) - 6)), 0.45, (220, 220, 220))
    for k in range(int(1 / step) + 1):
        y = k * step; _, py = T2P(0, y, S, m)
        cv2.line(img, (int(m), py), (int(m + 2 * S), py), (90, 90, 90) if k % 4 else (150, 150, 150), 1, cv2.LINE_AA)
        if labels: text(img, ("%g" % y), (2, py + 5), 0.45, (220, 220, 220))

def draw_marks(img, marks, S, m, th=1, fs=0.55, rscale=1.35):
    for mk in marks:
        px, py = T2P(mk["x"], mk["y"], S, m); col = COL.get(mk["colour"], COL["unknown"])
        r = int(round(R_BALL * S * rscale))
        cv2.circle(img, (px, py), r, (0, 0, 0), th + 2, cv2.LINE_AA)
        cv2.circle(img, (px, py), r, col, th, cv2.LINE_AA)
        text(img, str(mk["label"]), (px + int(r * 0.75), py - int(r * 0.75)), fs, col, 1 if fs < 0.7 else 2)

def crop_windows(marks):
    """pocket jaws, the (1.48, 0.067) spot, and any draft black/cue on an edge not already covered."""
    wins = [(POCKET_NAMES[i] + " jaw", p) for i, p in enumerate(POCKETS)] + [("spot 1.48,0.067", HOT)]
    for mk in marks:
        if mk["colour"] in ("black", "cue") and zone(mk["x"], mk["y"]) != "open":
            if all(abs(mk["x"] - c[0]) > 0.12 or abs(mk["y"] - c[1]) > 0.08 for _, c in wins):
                wins.append(("draft %s %s" % (mk["label"], mk["colour"]), (mk["x"], mk["y"])))
    return wins

def stage_sheet1(force=False):
    sel = list(csv.DictReader(open(os.path.join(S1, "selection.csv"), encoding="utf-8")))
    m = json.load(open(MANIFEST, encoding="utf-8")); bysha = {r.get("sha1"): r for r in m["frames"] if r.get("sha1")}
    os.makedirs(os.path.join(S1, "drafts"), exist_ok=True)
    out = []
    for s in sel:
        r = bysha[s["sha1"]]
        assert not is_holdout(r), "holdout row selected"
        balls = r["storedResult"]["balls"]
        marks = [dict(label=i + 1, colour=b[0], x=b[1], y=b[2]) for i, b in enumerate(sorted(balls, key=lambda b: (round(b[1], 1), b[2])))]
        img = cv2.imread(os.path.join(ROOT, r["path"]))
        img, lifted = lift(img, float(s["tableV"]) if s["tableV"] not in ("", None) else None)
        corners = r["calibration"]["corners"]
        name = "%s_%s" % (s["item"], s["frameId"])
        jpg = os.path.join(S1, name + ".jpg")
        if force or not os.path.exists(jpg):
            S, mg = 600, 0.08
            main, mpx = warp(img, corners, S, mg)
            draw_grid(main, S, mpx)
            draw_marks(main, marks, S, mpx, th=2, fs=0.6)
            # crops at higher scale
            SC, mc = 1500, 0.16
            big, bpx = warp(img, corners, SC, mc)
            crops = []
            for title, (cx, cy) in crop_windows(marks):
                wx, wy = 0.30, 0.22
                x0, y0 = T2P(cx - wx / 2, cy - wy / 2, SC, bpx); x1, y1 = T2P(cx + wx / 2, cy + wy / 2, SC, bpx)
                x0, y0 = max(0, x0), max(0, y0); x1, y1 = min(big.shape[1], x1), min(big.shape[0], y1)
                c = big[y0:y1, x0:x1].copy()
                # draw marks inside crop in crop coordinates
                for mk in marks:
                    px, py = T2P(mk["x"], mk["y"], SC, bpx); px -= x0; py -= y0
                    if 0 <= px < c.shape[1] and 0 <= py < c.shape[0]:
                        col = COL.get(mk["colour"], COL["unknown"]); rr = int(R_BALL * SC * 1.35)
                        cv2.circle(c, (px, py), rr, (0, 0, 0), 4, cv2.LINE_AA); cv2.circle(c, (px, py), rr, col, 2, cv2.LINE_AA)
                        text(c, str(mk["label"]), (px + int(rr * .75), py - int(rr * .75)), 0.8, col, 2)
                # tick marks every 0.05 table units along crop edges, labelled every 0.1
                for k in range(int(math.floor((cx - wx) * 20)), int(math.ceil((cx + wx) * 20)) + 1):
                    tx = k / 20; px, _ = T2P(tx, 0, SC, bpx); px -= x0
                    if 0 <= px < c.shape[1]:
                        cv2.line(c, (px, c.shape[0] - 1), (px, c.shape[0] - (12 if k % 2 == 0 else 6)), (255, 255, 255), 1)
                        if k % 2 == 0: text(c, "%.1f" % tx, (px - 14, c.shape[0] - 16), 0.45)
                for k in range(int(math.floor((cy - wy) * 20)), int(math.ceil((cy + wy) * 20)) + 1):
                    ty = k / 20; _, py = T2P(0, ty, SC, bpx); py -= y0
                    if 0 <= py < c.shape[0]:
                        cv2.line(c, (0, py), ((12 if k % 2 == 0 else 6), py), (255, 255, 255), 1)
                        if k % 2 == 0: text(c, "%.2f" % ty, (14, py + 5), 0.45)
                c = cv2.resize(c, (420, int(420 * c.shape[0] / c.shape[1])), interpolation=cv2.INTER_AREA)
                pad = np.zeros((310, 420, 3), np.uint8); pad[:min(310, c.shape[0]), :] = c[:310]
                text(pad, title, (6, 22), 0.6, (255, 255, 255), 1)
                cv2.rectangle(pad, (0, 0), (419, 309), (80, 80, 80), 1)
                crops.append(pad)
            W = main.shape[1]
            while len(crops) % 3: crops.append(np.zeros((310, 420, 3), np.uint8))
            rows = [np.hstack(crops[i:i + 3]) for i in range(0, len(crops), 3)]
            grid = np.vstack(rows); grid = cv2.resize(grid, (W, int(grid.shape[0] * W / grid.shape[1])), interpolation=cv2.INTER_AREA)
            head = np.full((96, W, 3), 32, np.uint8)
            text(head, "%s   frame %s   V %s (%s)   %s" % (s["item"], s["frameId"], s["tableV"], s["stratum"], s["category"]), (10, 30), 0.85, (255, 255, 255), 2)
            text(head, "DRAFT - NOT REVIEWED. Numbered circles = detector result stored at capture (%s, %s). %d drafts." % (s["configuration"] or "?", (s["capturedAt"] or "?")[:10], len(marks)), (10, 60), 0.55, (0, 200, 255))
            text(head, "Grid every 0.25 table units (x 0-2 across, y 0-1 down). Marker colours: red, yellow, cue WHITE, black CYAN, unknown VIOLET." + ("  Display brightened (gamma 0.55)." if lifted else ""), (10, 85), 0.5, (200, 200, 200))
            leg = np.full((34, W, 3), 32, np.uint8)
            text(leg, "  ".join("%d:%s" % (mk["label"], mk["colour"]) for mk in marks), (10, 23), 0.5, (230, 230, 230))
            cv2.imwrite(jpg, np.vstack([head, main, leg, grid]), [cv2.IMWRITE_JPEG_QUALITY, 90])
        draft = dict(schemaVersion=2, sequenceId="t9-sheet1-%s" % name, annotator="DRAFT, not human: detector result stored in capture receipt %s" % s["receipt"],
                     calibrationVersion=s["calibrationId"] or ("receipt-corners:" + (s["receipt"] or "")), reviewed=False, intervals=[],
                     frames=[dict(sampleId=s["sampleId"], pixelSha256=s["sha256"], coordinateSpace="table-2x1",
                                  balls=[dict(id="draft-%02d" % mk["label"], colour=mk["colour"], x=mk["x"], y=mk["y"], visibility="Visible") for mk in marks],
                                  notes="T9 sheet 1 item %s. Draft centres are the stored at-capture detector result (configuration %s, captured %s); never score as truth. Pixel %s." % (s["item"], s["configuration"], s["capturedAt"], s["path"]),
                                  tags=["t9-draft", s["stratum"], s["category"]] + ([s["split"]] if s["split"] and s["split"] != "unknown" else []))])
        dpath = os.path.join(S1, "drafts", name + ".labels-draft.json")
        json.dump(draft, open(dpath, "w", encoding="utf-8", newline="\n"), indent=2)
        s["image"] = rel(jpg); s["draft"] = rel(dpath); out.append(s)
        print(s["item"], "ok", flush=True)
    with open(os.path.join(S1, "index.csv"), "w", newline="", encoding="utf-8") as fh:
        w = csv.DictWriter(fh, fieldnames=list(out[0])); w.writeheader(); w.writerows(out)
    print("sheet1 finished", len(out))

# ---------------------------------------------------------------- sheet 2
def t4mod():
    spec = importlib.util.spec_from_file_location("settled_change", os.path.join(ROOT, "tools", "evidence", "prototypes", "settled_change.py"))
    mod = importlib.util.module_from_spec(spec); spec.loader.exec_module(mod); return mod

def draft_intervals(mot, fps, settled_thr, segs):
    n = len(mot); dur = n / fps; iv = []; t = 0.0
    for a, b in segs:
        ta, tb = a / fps, b / fps
        if ta > t + 1e-6:
            iv.append(("moving", t, ta))
            if t > 0: iv.append(("shot", t, min(ta, t + 0.4)))
        iv.append(("settled", ta, tb)); t = tb
    if t < dur - 1e-6:
        iv.append(("moving", t, dur))
        if t > 0: iv.append(("shot", t, min(dur, t + 0.4)))
    iv.sort(key=lambda x: (x[1], x[0] != "settled"))
    return [dict(label="I%d" % (i + 1), kind=k, fromS=round(a, 1), toS=round(b, 1)) for i, (k, a, b) in enumerate(iv)], dur

IVCOL = {"settled": (80, 170, 60), "moving": (0, 140, 255), "shot": (40, 40, 230)}

def thumbs(path, corners, every, S=150, mg=0.12):
    """decode at 1/every fps, rectified landscape thumbnails."""
    p = subprocess.Popen(["ffmpeg", "-v", "error", "-i", os.path.join(ROOT, path), "-vf", "fps=1/%g,scale=960:540" % every, "-f", "rawvideo", "-pix_fmt", "bgr24", "-"], stdout=subprocess.PIPE)
    n = 960 * 540 * 3; out = []
    while True:
        b = p.stdout.read(n)
        if len(b) < n: break
        im, m = warp(np.frombuffer(b, np.uint8).reshape(540, 960, 3), corners, S, mg)
        out.append(im)
    p.wait(); return out

def stage_sheet2(force=False):
    mod = t4mod(); os.makedirs(os.path.join(S2, "drafts"), exist_ok=True)
    clips = [c for c in mod.clips() if c["day"] == "2026-10-29"]
    clips.sort(key=lambda c: c["id"])
    pairs = list(csv.DictReader(open(os.path.join(T4, "pairs.csv"))))
    regs = list(csv.DictReader(open(os.path.join(T4, "regions-grey.csv"))))
    t0 = time.time(); index = []
    for ci, c in enumerate(clips, 1):
        item = "C%02d" % ci; name = "%s_%s" % (item, c["id"])
        z = np.load(os.path.join(T4, "motion2", c["id"] + ".npz")); mot = z["motion"]
        segs = mod.segments(mot); ivs, dur = draft_intervals(mot, mod.FPS, mod.MOTION_SETTLED, segs)
        cps = [p for p in pairs if p["clip"] == c["path"] and p["kind"] == "shot"]
        rest = []
        for p in cps:
            sfx = p["pair"].rsplit("_", 1)[1]
            k = 0
            for r in regs:
                if r["pair"] != p["pair"] or r.get("ball_v3") != "True": continue
                k += 1
                cx, cy = float(r["cx"]), float(r["cy"])
                x = (cy - mod.M) / mod.W; y = (mod.M + mod.W - cx) / mod.W
                rest.append(dict(pair=sfx, label="R%d" % k, t4k=int(r["k"]), x=round(x, 3), y=round(y, 3), zone=r["zone"], cls=r["cls"],
                                 beforeS="%.1f-%.1f" % (int(p["a0"]) / mod.FPS, int(p["a1"]) / mod.FPS), afterS="%.1f-%.1f" % (int(p["b0"]) / mod.FPS, int(p["b1"]) / mod.FPS)))
        strip = os.path.join(S2, name + "_timeline.jpg")
        if force or not os.path.exists(strip):
            if time.time() - t0 > 120: print("MORE"); return
            th = thumbs(c["path"], mod.CORNERS[c["day"]], 1.0)
            tw = th[0].shape[1]; per = 5; W = tw * per
            # motion trace panel
            Hp = 190; pan = np.full((Hp, W, 3), 28, np.uint8)
            x0, x1, y0, y1 = 50, W - 15, 30, Hp - 35
            X = lambda t: int(x0 + (x1 - x0) * t / max(dur, 1e-6))
            for iv in ivs:
                if iv["kind"] == "shot": continue
                ov = pan.copy(); cv2.rectangle(ov, (X(iv["fromS"]), y0), (X(iv["toS"]), y1), IVCOL[iv["kind"]], -1)
                pan = cv2.addWeighted(ov, 0.35, pan, 0.65, 0)
            ymax = max(0.01, float(np.percentile(mot, 99)) * 1.1)
            Y = lambda v: int(y1 - (y1 - y0) * min(v, ymax) / ymax)
            pts = np.array([[X(i / mod.FPS), Y(float(v))] for i, v in enumerate(mot)], np.int32)
            cv2.polylines(pan, [pts], False, (255, 255, 255), 1, cv2.LINE_AA)
            cv2.line(pan, (x0, Y(mod.MOTION_SETTLED)), (x1, Y(mod.MOTION_SETTLED)), (0, 200, 255), 1)
            for iv in ivs:
                if iv["kind"] == "shot":
                    cv2.line(pan, (X(iv["fromS"]), y0 - 4), (X(iv["fromS"]), y1), IVCOL["shot"], 2)
                    text(pan, iv["label"] + " shot?", (X(iv["fromS"]) + 3, y0 + 14), 0.45, (120, 120, 255))
                else:
                    text(pan, "%s %s" % (iv["label"], iv["kind"]), (X(iv["fromS"]) + 3, y1 - 6), 0.45, (230, 230, 230))
            for s_ in range(0, int(dur) + 1):
                cv2.line(pan, (X(s_), y1), (X(s_), y1 + (8 if s_ % 5 == 0 else 4)), (200, 200, 200), 1)
                if s_ % 5 == 0: text(pan, "%ds" % s_, (X(s_) - 10, y1 + 24), 0.45)
            text(pan, "T4 motion (tile-compensated change, 0.4 s lag); orange line = settled threshold. DRAFT: green settled, orange moving, red line = shot start.", (8, 18), 0.45, (200, 200, 200))
            cells = []
            for i, im in enumerate(th):
                t = i * 1.0; kind = next((iv["kind"] for iv in ivs if iv["kind"] != "shot" and iv["fromS"] <= t < iv["toS"] + 1e-6), "moving")
                im = im.copy(); cv2.rectangle(im, (0, 0), (im.shape[1] - 1, im.shape[0] - 1), IVCOL[kind], 3)
                text(im, "%.0fs" % t, (6, 22), 0.6, (255, 255, 255), 2)
                cells.append(im)
            while len(cells) % per: cells.append(np.zeros_like(th[0]))
            grid = np.vstack([np.hstack(cells[i:i + per]) for i in range(0, len(cells), per)])
            head = np.full((70, W, 3), 32, np.uint8)
            text(head, "%s  %s   %.1f s   %s" % (item, c["id"], dur, c["path"]), (10, 28), 0.6, (255, 255, 255), 1)
            text(head, "DRAFT - NOT REVIEWED. Intervals from T4 motion trace. Thumbnails every 1 s (rectified, top cushion at top). Border colour = draft state.", (10, 56), 0.5, (0, 200, 255))
            ivl = np.full((30 + 22 * ((len(ivs) + 3) // 4), W, 3), 32, np.uint8)
            for i, iv in enumerate(ivs):
                text(ivl, "%s %s %.1f-%.1f s" % (iv["label"], iv["kind"], iv["fromS"], iv["toS"]), (10 + (i % 4) * (W // 4), 22 + 22 * (i // 4)), 0.5, (230, 230, 230))
            cv2.imwrite(strip, np.vstack([head, pan, ivl, grid]), [cv2.IMWRITE_JPEG_QUALITY, 85])
        # rest panels per shot pair
        restimgs = []
        for p in cps:
            sfx = p["pair"].rsplit("_", 1)[1]
            rp = os.path.join(S2, "%s_%s_rest.jpg" % (name, sfx)); restimgs.append(rel(rp))
            if not force and os.path.exists(rp): continue
            fa = cv2.imread(os.path.join(T4, "frames", p["pair"] + "_a.png")); fb = cv2.imread(os.path.join(T4, "frames", p["pair"] + "_b.png"))
            if fa is None or fb is None: continue
            A = cv2.rotate(fa, cv2.ROTATE_90_COUNTERCLOCKWISE); B = cv2.rotate(fb, cv2.ROTATE_90_COUNTERCLOCKWISE)
            sc = 0.75; S = mod.W * sc; mg = mod.M * sc
            A = cv2.resize(A, None, fx=sc, fy=sc, interpolation=cv2.INTER_AREA); B = cv2.resize(B, None, fx=sc, fy=sc, interpolation=cv2.INTER_AREA)
            mine = [r for r in rest if r["pair"] == sfx]
            for im, which in ((A, "BEFORE"), (B, "AFTER")):
                draw_grid(im, S, mg)
                draw_marks(im, [dict(label=r["label"], colour="unknown", x=r["x"], y=r["y"]) for r in mine], S, mg, th=2, fs=0.6)
                text(im, which, (int(mg) + 8, int(mg) + 30), 0.9, (255, 255, 255), 2)
            # zoomed before|after crops per region
            crops = []
            Sf = mod.W; mf = mod.M
            for r in mine:
                px, py = T2P(r["x"], r["y"], Sf, mf)
                pa = []
                for im in (cv2.rotate(fa, cv2.ROTATE_90_COUNTERCLOCKWISE), cv2.rotate(fb, cv2.ROTATE_90_COUNTERCLOCKWISE)):
                    h = 70; cc = im[max(0, py - h):py + h, max(0, px - h):px + h]
                    pa.append(cv2.resize(cc, (210, 210), interpolation=cv2.INTER_CUBIC))
                cell = np.hstack([pa[0], np.full((210, 4, 3), 255, np.uint8), pa[1]]); cell = np.vstack([np.full((26, cell.shape[1], 3), 32, np.uint8), cell])
                text(cell, "%s %s %s (%.2f,%.2f)  before | after" % (r["label"], r["cls"], r["zone"], r["x"], r["y"]), (4, 18), 0.45)
                crops.append(cell)
            W = A.shape[1]
            per = max(1, W // crops[0].shape[1]) if crops else 1
            while crops and len(crops) % per: crops.append(np.zeros_like(crops[0]))
            cgrid = np.vstack([np.hstack(crops[i:i + per]) for i in range(0, len(crops), per)]) if crops else np.zeros((0, W, 3), np.uint8)
            if cgrid.shape[1] < W: cgrid = np.hstack([cgrid, np.zeros((cgrid.shape[0], W - cgrid.shape[1], 3), np.uint8)])
            head = np.full((70, W, 3), 32, np.uint8)
            text(head, "%s %s  shot %s   before %s s | after %s s (median of up to 5 frames each)" % (item, c["id"], sfx, mine[0]["beforeS"] if mine else "%.1f" % (int(p["a1"]) / mod.FPS), mine[0]["afterS"] if mine else "%.1f" % (int(p["b0"]) / mod.FPS)), (10, 28), 0.6)
            text(head, "DRAFT - NOT REVIEWED. R# = T4 grey-v3 ball-sized change region (where a ball arrived or left). Colour unknown.", (10, 56), 0.5, (0, 200, 255))
            cv2.imwrite(rp, np.vstack([head, A, B, cgrid]), [cv2.IMWRITE_JPEG_QUALITY, 88])
        draft = dict(format="t9-clip-labels-draft v1", clip=c["path"], clipId=c["id"], item=item, timeDomain="ClipMicroseconds",
                     reviewed=False, annotator="DRAFT, not human: T4 motion2 settled segments and T4 grey-v3 change regions",
                     calibration="T4 substitute corners for 2026-10-29 (receipt 58e37e, code43); see docs/claude/t4-settled-change-experiment.md",
                     intervals=[dict(id=iv["label"], kind=iv["kind"], fromUs=int(iv["fromS"] * 1e6), toUs=int(iv["toS"] * 1e6), certainty="uncertain", ballIds=[], notes="draft") for iv in ivs],
                     restPositions=[dict(id="%s-%s" % (r["pair"], r["label"]), shot=r["pair"], coordinateSpace="table-2x1", x=r["x"], y=r["y"], colour="unknown",
                                         t4Class=r["cls"], zone=r["zone"], t4RegionK=r["t4k"], beforeS=r["beforeS"], afterS=r["afterS"]) for r in rest])
        dpath = os.path.join(S2, "drafts", name + ".labels-draft.json")
        json.dump(draft, open(dpath, "w", encoding="utf-8", newline="\n"), indent=2)
        index.append(dict(item=item, clipId=c["id"], seconds=round(dur, 1), nIntervals=len(ivs), nShotsDraft=sum(iv["kind"] == "shot" for iv in ivs),
                          t4ShotPairs=len(cps), nRestDraft=len(rest), timeline=rel(strip), restSheets=";".join(restimgs), draft=rel(dpath), clip=c["path"]))
        print(item, c["id"], "ok", flush=True)
    with open(os.path.join(S2, "index.csv"), "w", newline="", encoding="utf-8") as fh:
        w = csv.DictWriter(fh, fieldnames=list(index[0])); w.writeheader(); w.writerows(index)
    print("sheet2 finished", len(index))

if __name__ == "__main__":
    st = sys.argv[1] if len(sys.argv) > 1 else ""
    force = "--force" in sys.argv
    if st == "select": stage_select()
    elif st == "sheet1": stage_sheet1(force)
    elif st == "sheet2": stage_sheet2(force)
    else: print(__doc__)
