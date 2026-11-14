#!/usr/bin/env python3
"""T4 prototype: change between settled states (docs/claude/t4-settled-change-experiment.md).

Does a brightness-compensated difference between two settled frames of the same
fixed-camera timeline show "a ball arrived / left here", including at cushions
and pocket jaws, while staying quiet when nothing moved?

Stages (each resumable, each fits a 180 s shell call; run repeatedly until it
prints "finished"):
  scan     decode each clip at 5 fps, rectify (receipt corners, 800x1600 raster
           plus a 40 px margin so cushions and jaws are inside), store per-frame
           tile-gain-compensated motion fraction  -> t4/motion/<id>.npz
  plan     settled segments (motion below threshold for >= 1.2 s), shot pairs
           (end of one settled segment vs start of the next) and control pairs
           (start vs end of one long settled segment, or across a code62
           motionless clip)                       -> t4/pairs.csv
  extract  median of up to 5 rectified full-resolution frames per window
                                                  -> t4/frames/<pair>_{a,b}.png
  diff     change maps (variant grey: per-tile gain removed on log grey;
           variant chroma: per-tile gain removed on each log channel, max over
           channels), threshold, morphology, ball-sized regions with area and
           circularity, arrival/departure by cloth-likeness, review sheets
                                                  -> t4/regions.csv, t4/sheets/
  stills   the same change maps on the 1 November staged black/cue still bursts
           (black present / absent at a cushion and two pocket jaws, reviewed centres)
                                                  -> t4/stills-<variant>.csv, t4/stills-<variant>/
Usage: python3 tools/evidence/prototypes/settled_change.py {scan|plan|extract|diff|stills} [--variant grey|chroma]
Corners: VisionAnalyzer.Warp order, corner0->(W,0), 1->(W,H), 2->(0,H), 3->(0,0).
Nothing here touches src/ or the camera.
"""
import csv, os, subprocess, sys, time, json
import numpy as np, cv2

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
A = os.path.join(ROOT, "work", "evidence-audit"); T4 = os.path.join(A, "t4")
W, H, M = 800, 1600, 40              # raster and margin (px)
RW, RH = W + 2 * M, H + 2 * M
R_BALL = 0.027 * W                   # 21.6 px
BALL_AREA = np.pi * R_BALL ** 2      # ~1466 px
FPS = 5
# Receipt corners. 58e37e: 29 Oct 12:07 (code43 receipt, nearest same-day calibration;
# a15778 from 00:13 differs by < 4 px). 27a809: 31 Oct (code70), used for code62.
CORNERS = {
    "2026-10-29": [[0.03864583174387614, 0.14129628781919126], [0.8844791412353515, 0.0712962962962963], [0.8903125127156576, 0.911296307599103], [0.0503124992052714, 0.8542592366536458]],
    "2026-10-31": [[0.029895832141240437, 0.14129628781919126], [0.880104128519694, 0.06351851710566768], [0.8873957951863607, 0.9061110885054977], [0.04010416666666667, 0.8775925812897859]],
}
MOTION_SETTLED = 0.0025   # fraction of table pixels changed between frames 0.4 s apart (one rolling ball ~0.002)
MERGE_GAP = 2             # frames: settled runs split by <= 0.4 s of motion are one segment
SETTLE_MIN_S = 1.2

def homography(day, scale=1.0, iw=1920, ih=1080):
    src = np.float32(np.array(CORNERS[day]) * [iw, ih])
    dst = np.float32([[M + W, M], [M + W, M + H], [M, M + H], [M, M]]) * scale
    return cv2.getPerspectiveTransform(src, dst)

def clips():
    out = []
    for r in csv.DictReader(open(os.path.join(A, "video-change-signals.csv"))):
        p = r["path"]
        day = None
        if "20261029" in p or "code52-poststop" in p: day = "2026-10-29"
        if "code62" in p: day = "2026-10-31"
        grp = next((g for g in ("code44", "code45", "code47", "preroll", "code52", "code57", "code62") if g in p), None)
        if day and grp: out.append(dict(path=p, day=day, id=grp + "_" + os.path.basename(p).replace(".mp4", "").replace("code62-", "")[:12]))
    return out

def frames(path, Hm, size):
    p = subprocess.Popen(["ffmpeg", "-v", "error", "-i", os.path.join(ROOT, path), "-vf", "fps=%d" % FPS, "-f", "rawvideo", "-pix_fmt", "bgr24", "-"], stdout=subprocess.PIPE)
    n = 1920 * 1080 * 3; i = 0
    while True:
        b = p.stdout.read(n)
        if len(b) < n: break
        yield i, cv2.warpPerspective(np.frombuffer(b, np.uint8).reshape(1080, 1920, 3), Hm, size, flags=cv2.INTER_AREA if size[0] < RW else cv2.INTER_LINEAR)
        i += 1
    p.wait()

def tile_comp(la, lb, tile):
    """log-ratio minus a smooth per-tile median (gain) field."""
    lr = lb - la
    h, w = lr.shape[:2]; th, tw = h // tile, w // tile
    t = lr[:th * tile, :tw * tile].reshape(th, tile, tw, tile, *lr.shape[2:])
    med = np.median(t.transpose(0, 2, 1, 3, *range(4, t.ndim)).reshape(th, tw, tile * tile, *lr.shape[2:]), axis=2).astype(np.float32)
    field = cv2.resize(med, (w, h), interpolation=cv2.INTER_LINEAR)
    if field.ndim < lr.ndim: field = field[..., None]
    return lr - field.reshape(lr.shape)

def stage_scan():
    os.makedirs(os.path.join(T4, "motion2"), exist_ok=True); t0 = time.time()
    q = 0.25; size = (int(RW * q), int(RH * q))
    for c in clips():
        out = os.path.join(T4, "motion2", c["id"] + ".npz")
        if os.path.exists(out): continue
        if time.time() - t0 > 100: print("MORE"); return
        Hm = homography(c["day"], q)
        g = np.array([cv2.cvtColor(f, cv2.COLOR_BGR2GRAY) for _, f in frames(c["path"], Hm, size)], np.float32)
        lg = np.log(g + 8); lag = 2
        inner = np.zeros(g.shape[1:], bool); inner[int(M * q):-int(M * q), int(M * q):-int(M * q)] = True
        mot = np.zeros(len(g), np.float32); raw = np.zeros(len(g), np.float32)
        for i in range(lag, len(g)):
            r = tile_comp(lg[i - lag], lg[i], 16)
            # blur and open so that compression speckle in dark clips does not count as motion
            mm = (cv2.GaussianBlur(np.abs(r), (5, 5), 0) > .08).astype(np.uint8)
            mm = cv2.morphologyEx(mm, cv2.MORPH_OPEN, np.ones((3, 3), np.uint8))
            mot[i] = mm.astype(bool)[inner].mean(); raw[i] = (np.abs(g[i] - g[i - lag]) > 12)[inner].mean()
        mot[:lag] = mot[lag] if len(g) > lag else 0
        np.savez_compressed(out, motion=mot, raw=raw, luma=g.reshape(len(g), -1).mean(1))
        print(c["id"], len(g), "frames", "settled %.2f" % (mot < MOTION_SETTLED).mean(), flush=True)
    print("finished")

def segments(mot):
    s = mot < MOTION_SETTLED; segs = []; i = 0
    while i < len(s):
        if s[i]:
            j = i
            while j < len(s) and s[j]: j += 1
            if segs and i - segs[-1][1] <= MERGE_GAP and (j - i) / FPS < SETTLE_MIN_S: segs[-1] = (segs[-1][0], j)
            elif segs and i - segs[-1][1] <= MERGE_GAP: segs[-1] = (segs[-1][0], j)
            elif (j - i) / FPS >= SETTLE_MIN_S: segs.append((i, j))
            i = j
        else: i += 1
    return segs

def stage_plan():
    rows = []
    for c in clips():
        z = np.load(os.path.join(T4, "motion2", c["id"] + ".npz")); mot = z["motion"]; segs = segments(mot)
        k = 5  # up to 5 frames (1 s) per window
        for n, ((a0, a1), (b0, b1)) in enumerate(zip(segs, segs[1:])):
            peak = float(mot[a1:b0].max()) if b0 > a1 else 0
            rows.append(dict(pair="%s_s%d" % (c["id"], n), clip=c["path"], day=c["day"], kind="shot" if "code62" not in c["path"] else "control-code62",
                             a0=max(a0, a1 - k), a1=a1, b0=b0, b1=min(b1, b0 + k), gap_s="%.1f" % ((b0 - a1) / FPS), peak="%.4f" % peak))
        for n, (s0, s1) in enumerate(segs):
            if (s1 - s0) >= 4 * k:   # long settled segment: first second vs last second
                rows.append(dict(pair="%s_c%d" % (c["id"], n), clip=c["path"], day=c["day"], kind="control-same-segment", a0=s0, a1=s0 + k, b0=s1 - k, b1=s1, gap_s="%.1f" % ((s1 - k - s0 - k) / FPS), peak="%.4f" % float(mot[s0:s1].max())))
        if "code62" in c["path"] and len(mot) > 4 * k:  # motionless clip: first vs last second regardless of segmentation
            rows.append(dict(pair="%s_m" % c["id"], clip=c["path"], day=c["day"], kind="control-code62", a0=0, a1=k, b0=len(mot) - k, b1=len(mot), gap_s="%.1f" % ((len(mot) - 2 * k) / FPS), peak="%.4f" % float(mot.max())))
        print(c["id"], "frames", len(mot), "segments", [(round(a / FPS, 1), round(b / FPS, 1)) for a, b in segs])
    rows = [r for r in rows if not (r["kind"] == "control-code62" and "_s" in r["pair"])] + []
    with open(os.path.join(T4, "pairs.csv"), "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys())); w.writeheader(); w.writerows(rows)
    print(len(rows), "pairs:", {k: sum(r["kind"] == k for r in rows) for k in set(r["kind"] for r in rows)})

def stage_extract():
    d = os.path.join(T4, "frames"); os.makedirs(d, exist_ok=True); t0 = time.time()
    pairs = list(csv.DictReader(open(os.path.join(T4, "pairs.csv"))))
    byclip = {}
    for p in pairs: byclip.setdefault(p["clip"], []).append(p)
    for clip, ps in byclip.items():
        if all(os.path.exists(os.path.join(d, p["pair"] + "_b.png")) for p in ps): continue
        if time.time() - t0 > 90: print("MORE"); return
        want = {}
        for p in ps:
            for s in "ab":
                for i in range(int(p[s + "0"]), int(p[s + "1"])): want.setdefault(i, []).append((p["pair"], s))
        acc = {}
        for i, f in frames(clip, homography(ps[0]["day"]), (RW, RH)):
            for key in want.get(i, []): acc.setdefault(key, []).append(f)
        for (pair, s), fs in acc.items():
            cv2.imwrite(os.path.join(d, "%s_%s.png" % (pair, s)), np.median(np.array(fs), axis=0).astype(np.uint8))
        print(os.path.basename(clip), len(acc), "windows", flush=True)
    print("finished")

def cloth_model(img):
    hsv = cv2.cvtColor(img, cv2.COLOR_BGR2HSV)
    c = hsv[M + 200:M + H - 200, M + 150:M + W - 150].reshape(-1, 3)
    return np.median(c, axis=0)

def clothlike(img, mask, ref):
    hsv = cv2.cvtColor(img, cv2.COLOR_BGR2HSV)[mask].astype(np.float32)
    dh = np.abs(hsv[:, 0] - ref[0]); dh = np.minimum(dh, 180 - dh)
    # cloth and cushion share hue; cushion is darker, so value is allowed to drop to 45 % of the bed
    ok = (dh < 12) & (np.abs(hsv[:, 1] - ref[1]) < 70) & (hsv[:, 2] > 0.45 * ref[2]) & (hsv[:, 2] < 1.4 * ref[2] + 20)
    return float(ok.mean()) if len(ok) else 0.0

def cloth_or_shadow(hsv, ref):
    """Boolean map: cloth or its cast shadow (matches C# SettledChangeMath.ClothOrShadow): cloth hue and
    saturation at 0.2-1.4 x cloth V; below S 40 on both pixel and cloth, saturation alone (grey cloth has
    no stable hue)."""
    h, s, v = hsv[..., 0].astype(np.float32), hsv[..., 1].astype(np.float32), hsv[..., 2].astype(np.float32)
    bright = (v > 0.2 * ref[2]) & (v < 1.4 * ref[2] + 20)
    dh = np.abs(h - ref[0]); dh = np.minimum(dh, 180 - dh)
    hue_ok = (dh < 12) & (np.abs(s - ref[1]) < 70)
    grey_ok = (s < 40) & (ref[1] < 40)
    return bright & (grey_ok | hue_ok)

def ball_centre(a, b, comp, ref_a, ref_b, minimum_fraction=0.2):
    """Region centre from pixels that are neither cloth nor shadow in a or b (C# BallCentre); None to keep
    the plain centroid."""
    ha, hb = cv2.cvtColor(a, cv2.COLOR_BGR2HSV), cv2.cvtColor(b, cv2.COLOR_BGR2HSV)
    msk = comp.astype(bool); ball = msk & ~(cloth_or_shadow(ha, ref_a) & cloth_or_shadow(hb, ref_b))
    n, total = int(ball.sum()), int(msk.sum())
    if n == 0 or n < minimum_fraction * total: return None
    ys, xs = np.nonzero(ball)
    return float(xs.mean()), float(ys.mean())

def stage_ballcentre(variant="grey"):
    """Regenerate the T4 reference regions with ball-only centres (D3 region-centre fix, 11 Nov). Writes
    regions-<variant>-ballcentre.csv beside the original; cx/cy are the ball centres, plain_cx/plain_cy the
    old centroids. The original regions-<variant>.csv is not touched."""
    out = os.path.join(T4, "regions-%s-ballcentre.csv" % variant); rows = []; moved = []
    for p in csv.DictReader(open(os.path.join(T4, "pairs.csv"))):
        fa = os.path.join(T4, "frames", p["pair"] + "_a.png"); fb = os.path.join(T4, "frames", p["pair"] + "_b.png")
        if not (os.path.exists(fa) and os.path.exists(fb)): continue
        a, b = cv2.imread(fa), cv2.imread(fb)
        d, thr = change_map(a, b, variant)
        d = cv2.GaussianBlur(d, (5, 5), 0)
        m = (d > thr).astype(np.uint8)
        m = cv2.morphologyEx(m, cv2.MORPH_OPEN, np.ones((5, 5), np.uint8))
        m = cv2.morphologyEx(m, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (9, 9)))
        n, lab, st, cen = cv2.connectedComponentsWithStats(m)
        _, _, regs = regions(a, b, variant)   # same components, same order (area filter applied inside)
        ref_a, ref_b = cloth_model(a), cloth_model(b)
        comps = [i for i in range(1, n) if st[i, 4] >= 0.30 * BALL_AREA]
        for k, (i, r) in enumerate(zip(comps, regs)):
            c = ball_centre(a, b, (lab == i), ref_a, ref_b)
            row = dict(pair=p["pair"], kind=p["kind"], k=k, **r); row["plain_cx"], row["plain_cy"] = r["cx"], r["cy"]
            if c is not None:
                row["cx"], row["cy"] = c; row["zone"] = zone(*c)
                if r["ball_v3"]: moved.append(np.hypot(c[0] - r["cx"], c[1] - r["cy"]))
            rows.append(row)
        if not regs: rows.append(dict(pair=p["pair"], kind=p["kind"], k=-1))
    keys = ["pair", "kind", "k", "cx", "cy", "plain_cx", "plain_cy", "area", "w", "h", "circ", "inr", "out_frac", "ballsized", "ball_v3", "zone", "cloth_a", "cloth_b", "cls", "peak", "dark_a", "dark_b"]
    with open(out, "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=keys); w.writeheader(); w.writerows(rows)
    print("wrote", out, len(rows), "rows; ball-sized centres moved: median %.1f px, max %.1f px (R %.1f)" % (np.median(moved), max(moved), R_BALL))

def zone(cx, cy):
    """table, cushion (centre within 4.4 radii of the raster edge) or jaw (within 5 radii of a pocket point)."""
    x, y = cx - M, cy - M
    pockets = [(0, 0), (W, 0), (0, H / 2), (W, H / 2), (0, H), (W, H)]
    # The receipt corners sit on the outer edge of the cushion (checked by eye on
    # t4/calib/*-c.jpg), so a ball touching the cushion nose has its centre about
    # 40 px (cushion) + 21.6 px (radius) inside the raster edge.
    if min(np.hypot(x - px, y - py) for px, py in pockets) < 5.0 * R_BALL: return "jaw"
    if min(x, W - x, y, H - y) < 4.4 * R_BALL: return "cushion"
    return "table"

def change_map(a, b, variant):
    if variant == "grey":
        la = np.log(cv2.cvtColor(a, cv2.COLOR_BGR2GRAY).astype(np.float32) + 8); lb = np.log(cv2.cvtColor(b, cv2.COLOR_BGR2GRAY).astype(np.float32) + 8)
        return np.abs(tile_comp(la, lb, 80)), 0.18
    la = np.log(a.astype(np.float32) + 8); lb = np.log(b.astype(np.float32) + 8)
    return np.abs(tile_comp(la, lb, 80)).max(axis=2), 0.22

LAST_BG = {}

def regions(a, b, variant):
    d, thr = change_map(a, b, variant)
    d = cv2.GaussianBlur(d, (5, 5), 0)
    # drift measures (lighting / shadow-geometry change): fraction of the cushion band
    # and of the open bed above threshold. Used to flag a pair as unreliable.
    band = np.zeros(d.shape, bool); band[M:M + H, M:M + W] = True
    inner = np.zeros(d.shape, bool); inner[M + 90:M + H - 90, M + 90:M + W - 90] = True
    band &= ~inner
    LAST_BG.update(cushion_frac=round(float((d[band] > thr).mean()), 4), bed_frac=round(float((d[inner] > thr).mean()), 4), bed_p99=round(float(np.percentile(d[inner], 99)), 3))
    m = (d > thr).astype(np.uint8)
    m = cv2.morphologyEx(m, cv2.MORPH_OPEN, np.ones((5, 5), np.uint8))
    m = cv2.morphologyEx(m, cv2.MORPH_CLOSE, cv2.getStructuringElement(cv2.MORPH_ELLIPSE, (9, 9)))
    n, lab, st, cen = cv2.connectedComponentsWithStats(m)
    ref = cloth_model(a); out = []
    for i in range(1, n):
        area = st[i, 4]
        if area < 0.30 * BALL_AREA: continue
        comp = (lab == i).astype(np.uint8)
        cs, _ = cv2.findContours(comp, cv2.RETR_EXTERNAL, cv2.CHAIN_APPROX_NONE)
        per = max(cv2.arcLength(cs[0], True), 1); circ = 4 * np.pi * area / per ** 2
        x, y, w, h = st[i, :4]
        # v1 (first run) used 0.3-2.6 ball areas, bbox <= 6.4 R and circularity >= 0.45; that
        # rejected real arrivals whose shadow tail stretches the region. v2: the largest
        # inscribed disc must be ball-like (0.6-1.45 R) -- thin shadow streaks and cue
        # shafts are narrower, a hand or arm is wider -- and the region at most 5 ball areas.
        sub = comp[max(0, y - 2):y + h + 2, max(0, x - 2):x + w + 2]
        inr = float(cv2.distanceTransform(sub, cv2.DIST_L2, 5).max())
        ballsized = 0.6 * R_BALL <= inr <= 1.45 * R_BALL and area <= 5 * BALL_AREA
        # v3 rail rule: a ball resting against a cushion or in a jaw stays inside the
        # outer cushion edge (the raster edge); a hand or forearm on the rail crosses it.
        ys, xs = np.nonzero(comp)
        out_frac = float(((xs < M - 4) | (xs > M + W + 4) | (ys < M - 4) | (ys > M + H + 4)).mean())
        ball_v3 = ballsized and out_frac <= 0.05
        msk = comp.astype(bool); ca, cb = clothlike(a, msk, ref), clothlike(b, msk, ref)
        va, vb = cv2.cvtColor(a, cv2.COLOR_BGR2HSV)[..., 2][msk], cv2.cvtColor(b, cv2.COLOR_BGR2HSV)[..., 2][msk]
        da, db = float((va < 0.35 * ref[2]).mean()), float((vb < 0.35 * ref[2]).mean())   # black-ball-dark pixels
        cls = "arrival" if ca - cb > 0.25 else "departure" if cb - ca > 0.25 else "unclear"
        out.append(dict(cx=float(cen[i][0]), cy=float(cen[i][1]), area=int(area), w=int(w), h=int(h), circ=round(circ, 2), ballsized=ballsized,
                        inr=round(inr, 1), out_frac=round(out_frac, 3), ball_v3=ball_v3, zone=zone(*cen[i]), cloth_a=round(ca, 2), cloth_b=round(cb, 2), cls=cls, peak=round(float(d[msk].max()), 2), dark_a=round(da, 2), dark_b=round(db, 2)))
    return d, thr, out

def sheet(pair, a, b, d, thr, regs, path):
    s = 0.5
    def sm(x): return cv2.resize(x, (int(RW * s), int(RH * s)), interpolation=cv2.INTER_AREA)
    dm = cv2.applyColorMap(np.clip(d / (2 * thr) * 255, 0, 255).astype(np.uint8), cv2.COLORMAP_INFERNO)
    pa, pb, pd = sm(a).copy(), sm(b).copy(), sm(dm).copy()
    crops = []
    for k, r in enumerate(regs):
        col = (0, 255, 0) if r["cls"] == "arrival" else (0, 0, 255) if r["cls"] == "departure" else (0, 255, 255)
        th = 2 if r["ballsized"] else 1
        for p in (pa, pb, pd):
            cv2.circle(p, (int(r["cx"] * s), int(r["cy"] * s)), int(max(r["w"], r["h"]) * s / 2 + 6), col, th)
            cv2.putText(p, str(k), (int(r["cx"] * s) + 14, int(r["cy"] * s) - 10), cv2.FONT_HERSHEY_SIMPLEX, .45, col, 1)
        if r["ballsized"] and len(crops) < 8:
            x0 = int(np.clip(r["cx"] - 60, 0, RW - 120)); y0 = int(np.clip(r["cy"] - 60, 0, RH - 120))
            c3 = [cv2.resize(z[y0:y0 + 120, x0:x0 + 120], (150, 150), interpolation=cv2.INTER_NEAREST) for z in (a, b, dm)]
            t = np.vstack(c3); cv2.putText(t, "%d %s %s" % (k, r["zone"][:3], r["cls"][:3]), (2, 14), cv2.FONT_HERSHEY_SIMPLEX, .42, (255, 255, 255), 1)
            crops.append(t)
    top = np.hstack([pa, pb, pd])
    cv2.putText(top, pair, (6, 18), cv2.FONT_HERSHEY_SIMPLEX, .55, (255, 255, 255), 2)
    if crops:
        row = np.hstack(crops); row = np.hstack([row, np.zeros((row.shape[0], top.shape[1] - row.shape[1], 3), np.uint8)]) if row.shape[1] < top.shape[1] else row[:, :top.shape[1]]
        top = np.vstack([top, row])
    cv2.imwrite(path, top, [cv2.IMWRITE_JPEG_QUALITY, 85])

def stage_diff(variant):
    d = os.path.join(T4, "sheets-" + variant); os.makedirs(d, exist_ok=True)
    out = os.path.join(T4, "regions-%s.csv" % variant); rows = []; prow = []
    for p in csv.DictReader(open(os.path.join(T4, "pairs.csv"))):
        fa = os.path.join(T4, "frames", p["pair"] + "_a.png"); fb = os.path.join(T4, "frames", p["pair"] + "_b.png")
        if not (os.path.exists(fa) and os.path.exists(fb)): continue
        a, b = cv2.imread(fa), cv2.imread(fb)
        dm, thr, regs = regions(a, b, variant)
        sheet(p["pair"] + " " + p["kind"] + " gap %ss" % p["gap_s"], a, b, dm, thr, regs, os.path.join(d, p["pair"] + ".jpg"))
        for k, r in enumerate(regs): rows.append(dict(pair=p["pair"], kind=p["kind"], k=k, **r))
        if not regs: rows.append(dict(pair=p["pair"], kind=p["kind"], k=-1))
        prow.append(dict(pair=p["pair"], kind=p["kind"], gap_s=p["gap_s"], n_v2=sum(r["ballsized"] for r in regs), n_v3=sum(r["ball_v3"] for r in regs), **LAST_BG))
    keys = ["pair", "kind", "k", "cx", "cy", "area", "w", "h", "circ", "inr", "out_frac", "ballsized", "ball_v3", "zone", "cloth_a", "cloth_b", "cls", "peak", "dark_a", "dark_b"]
    with open(out, "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=keys); w.writeheader(); w.writerows(rows)
    with open(os.path.join(T4, "pairs-%s.csv" % variant), "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(prow[0].keys())); w.writeheader(); w.writerows(prow)
    print("wrote", out, len(rows))

# ---------------------------------------------------------------------------
# stills: the 1 November staged black/cue controls (same camera, same
# calibration 00a2e8). Each state is a burst of still frames; the user staged
# "black absent", "cue absent" and "restored" states minutes apart. B and C are
# the user-reviewed source-pixel centres (docs/evidence/mid-cushion-user-reviewed-
# centres-20261101.json and the lower-right / upper-right / opposite-cue JSONs
# under work/o08/live-detect-black-pair-v1/physical-controls/).
PC = "work/o08/live-detect-black-pair-v1/physical-controls/"
STILLS = {
    "mid": dict(B=(1223, 154), C=(1168, 154), zoneB="cushion", states={
        "absent": (PC + "mid-cushion-absent", "C"), "cueabs": (PC + "mid-cushion-cue-absent", "B"),
        "restored": (PC + "mid-cushion-restored", "BC"), "present2831": (PC + "mid-cushion-present", "BC")}),
    "opp": dict(B=(1223, 151), C=(1279, 148), zoneB="cushion", states={
        "present": (PC + "mid-cushion-opposite-cue", "BC"), "cueabs": (PC + "cue-absent-opposite-cue", "B"),
        "restaged": (PC + "cue-present-opposite-cue-restaged-20261101", "BC")}),
    "lowright": dict(B=(1648, 928), C=(1578, 901), zoneB="jaw", states={
        "present": (PC + "lower-right-present", "BC"), "absent": (PC + "lower-right-absent", "C"),
        "cueabs": (PC + "lower-right-cue-absent", "B"), "restored": (PC + "lower-right-restored", "BC")}),
    "upright": dict(B=(1632.5, 126.5), C=(1573.5, 137.5), zoneB="jaw", states={
        "present": (PC + "upper-right-present", "BC"), "absent": (PC + "upper-right-absent", "C"),
        "cueabs": (PC + "upper-right-cue-absent", "B"), "restored": (PC + "upper-right-restored", "BC")}),
}

def receipt_corners():
    out = {}
    for l in open(os.path.join(A, "receipts.jsonl")):
        d = json.loads(l)
        if d.get("corners"): out[d["pixel"]] = d["corners"]
    return out

def stage_stills(variant):
    import glob
    rc = receipt_corners(); d = os.path.join(T4, "stills-" + variant); os.makedirs(d, exist_ok=True)
    rows, pairs_out = [], []
    for g, cfg in STILLS.items():
        frames_by_state = {}
        for st, (folder, present) in cfg["states"].items():
            fs = sorted([f for f in glob.glob(os.path.join(ROOT, folder, "**", "*.png"), recursive=True) if os.path.basename(f)[:-4].isdigit()], key=lambda f: int(os.path.basename(f)[:-4]))
            frames_by_state[st] = (fs, present)
        def rect(f):
            rel = os.path.relpath(f, ROOT).replace(os.sep, "/")
            co = rc.get(rel, CORNERS["2026-10-31"])
            CORNERS["_tmp"] = co; Hm = homography("_tmp")
            return cv2.warpPerspective(cv2.imread(f), Hm, (RW, RH), flags=cv2.INTER_LINEAR), Hm, rel in rc
        pairs = []
        for st, (fs, pr) in frames_by_state.items():
            if len(fs) >= 2: pairs.append((st, fs[0], st, fs[-1]))           # same state, first vs last burst frame
            if len(fs) >= 4: pairs.append((st, fs[1], st, fs[2]))
        sts = list(frames_by_state)
        for i in range(len(sts)):
            for j in range(i + 1, len(sts)):
                pairs.append((sts[i], frames_by_state[sts[i]][0][0], sts[j], frames_by_state[sts[j]][0][0]))
        for sa, fa, sb, fb in pairs:
            a, Ha, oka = rect(fa); b, Hb, okb = rect(fb)
            pa_, pb_ = frames_by_state[sa][1], frames_by_state[sb][1]
            expect = {o: ("arrival" if (o in pb_ and o not in pa_) else "departure") for o in "BC" if (o in pa_) != (o in pb_)}
            dm, thr, regs = regions(a, b, variant)
            name = "%s_%s-%s_%s-%s" % (g, sa, os.path.basename(fa)[:-4], sb, os.path.basename(fb)[:-4])
            pts = {o: cv2.perspectiveTransform(np.float32([[cfg[o]]]), Ha)[0, 0] for o in "BC"}
            hit = {}
            for o, (px, py) in pts.items():
                near = [k for k, r in enumerate(regs) if r["ball_v3"] and np.hypot(r["cx"] - px, r["cy"] - py) < 1.6 * R_BALL]
                anyn = [k for k, r in enumerate(regs) if np.hypot(r["cx"] - px, r["cy"] - py) < 2.5 * R_BALL]
                hit[o] = (near, anyn)
            matched = set(k for o in hit for k in hit[o][1])
            unexpected = [k for k, r in enumerate(regs) if r["ballsized"] and k not in matched]
            unexpected3 = [k for k, r in enumerate(regs) if r["ball_v3"] and k not in matched]
            for o in "BC": regs.append(dict(cx=float(pts[o][0]), cy=float(pts[o][1]), area=0, w=0, h=0, circ=0, ballsized=False, zone="ref", cloth_a=0, cloth_b=0, cls="ref-" + o, peak=0)) if False else None
            sheet(name + " expect " + (",".join("%s:%s" % kv for kv in expect.items()) or "none"), a, b, dm, thr, regs, os.path.join(d, name + ".jpg"))
            img = cv2.imread(os.path.join(d, name + ".jpg"))
            for o, (px, py) in pts.items():   # reviewed B/C positions as small white crosses on all three panels
                for off in (0, RW // 2, RW):
                    cv2.drawMarker(img, (int(px * .5) + off, int(py * .5)), (255, 255, 255), cv2.MARKER_TILTED_CROSS, 8, 1)
            cv2.imwrite(os.path.join(d, name + ".jpg"), img, [cv2.IMWRITE_JPEG_QUALITY, 85])
            row = dict(pair=name, group=g, kind="control-same-state" if sa == sb else "staged-change", expect=json.dumps(expect),
                       B_hit=bool(hit["B"][0]), C_hit=bool(hit["C"][0]), B_peak=max([regs[k]["peak"] for k in hit["B"][1]] or [0]), C_peak=max([regs[k]["peak"] for k in hit["C"][1]] or [0]),
                       B_cls="/".join(regs[k]["cls"] for k in hit["B"][0]), C_cls="/".join(regs[k]["cls"] for k in hit["C"][0]),
                       unexpected_ballsized=len(unexpected), unexpected_v3=len(unexpected3), **LAST_BG, all_ballsized=sum(r["ballsized"] for r in regs), receipt_corners=oka and okb)
            # local residual at B/C: 95th percentile of the change map inside the ball disc, for separability
            for o, (px, py) in pts.items():
                yy, xx = np.ogrid[:RH, :RW]; disc = (xx - px) ** 2 + (yy - py) ** 2 < (0.8 * R_BALL) ** 2
                row[o + "_p95"] = round(float(np.percentile(dm[disc], 95)), 3)
            rows.append(row); print(name, row["expect"], "B_hit", row["B_hit"], "C_hit", row["C_hit"], "B_p95", row["B_p95"], "C_p95", row["C_p95"], "unexp", len(unexpected), flush=True)
    with open(os.path.join(T4, "stills-%s.csv" % variant), "w", newline="") as f:
        w = csv.DictWriter(f, fieldnames=list(rows[0].keys())); w.writeheader(); w.writerows(rows)

if __name__ == "__main__":
    st = sys.argv[1] if len(sys.argv) > 1 else "scan"
    variant = sys.argv[sys.argv.index("--variant") + 1] if "--variant" in sys.argv else "grey"
    {"scan": stage_scan, "plan": stage_plan, "extract": stage_extract, "diff": lambda: stage_diff(variant), "stills": lambda: stage_stills(variant), "ballcentre": lambda: stage_ballcentre(variant)}[st]()
