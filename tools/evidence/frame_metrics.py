#!/usr/bin/env python3
"""Photometric measurements for every unique receipt-bound camera frame.

For each frame: rectify with the receipt's own calibration corners (same corner
order as VisionAnalyzer.Warp), then measure the cloth's brightness, colour and
spatial uniformity. This turns "lighting condition" from a verbal label into
numbers taken from evidence already on disk. Resumable.
Outputs: work/evidence-audit/frame-metrics.csv and small rectified JPEGs in
work/evidence-audit/rectified/<sha12>.jpg (400x800).
"""
import csv, json, os, sys, time
import cv2, numpy as np
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
A = os.path.join(ROOT, "work", "evidence-audit"); OUT = os.path.join(A, "frame-metrics.csv")
os.makedirs(os.path.join(A, "rectified"), exist_ok=True)
W, H = 400, 800
F = ["sha256", "pixel", "capturedAt", "timeline", "operationId", "frameId", "clothHue", "clothSat", "clothV_p50", "clothV_p05", "clothV_p95",
     "clothB", "clothG", "clothR", "rOverB", "tileV_min", "tileV_max", "tileRatio", "longAxisGradient", "shortAxisGradient",
     "clippedPct", "darkPct", "frameLuma", "clothFraction"]

def rectify(img, corners):
    h, w = img.shape[:2]
    src = np.float32([[x * w, y * h] for x, y in corners])
    dst = np.float32([[W, 0], [W, H], [0, H], [0, 0]])
    return cv2.warpPerspective(img, cv2.getPerspectiveTransform(src, dst), (W, H))

def measure(t):
    hsv = cv2.cvtColor(t, cv2.COLOR_BGR2HSV); inner = hsv[40:-40, 40:-40]
    hh = inner[..., 0][(inner[..., 1] > 40) & (inner[..., 2] > 25)]
    if hh.size < 1000: return None
    hue = int(np.bincount(hh, minlength=180).argmax())
    d = np.abs(hsv[..., 0].astype(int) - hue); d = np.minimum(d, 180 - d)
    cloth = (d <= 10) & (hsv[..., 1] > 40) & (hsv[..., 2] > 25)
    cloth[:20] = cloth[-20:] = False; cloth[:, :20] = cloth[:, -20:] = False
    if cloth.sum() < 1000: return None
    v = hsv[..., 2][cloth]; bgr = np.median(t[cloth], axis=0)
    tiles = []
    for r in range(8):
        for c in range(4):
            m = cloth[r * 100:(r + 1) * 100, c * 100:(c + 1) * 100]
            if m.sum() > 500: tiles.append((r, c, float(np.median(hsv[r * 100:(r + 1) * 100, c * 100:(c + 1) * 100, 2][m]))))
    if not tiles: return None
    tv = np.array([x[2] for x in tiles]); rows = {}; cols = {}
    for r, c, x in tiles: rows.setdefault(r, []).append(x); cols.setdefault(c, []).append(x)
    rm = [np.mean(rows[k]) for k in sorted(rows)]; cm = [np.mean(cols[k]) for k in sorted(cols)]
    return dict(clothHue=hue, clothSat=int(np.median(hsv[..., 1][cloth])), clothV_p50=int(np.percentile(v, 50)),
                clothV_p05=int(np.percentile(v, 5)), clothV_p95=int(np.percentile(v, 95)),
                clothB=int(bgr[0]), clothG=int(bgr[1]), clothR=int(bgr[2]), rOverB="%.3f" % (bgr[2] / max(1, bgr[0])),
                tileV_min=int(tv.min()), tileV_max=int(tv.max()), tileRatio="%.2f" % (tv.max() / max(1, tv.min())),
                longAxisGradient="%.2f" % (rm[-1] / max(1, rm[0])), shortAxisGradient="%.2f" % (cm[-1] / max(1, cm[0])),
                clippedPct="%.2f" % (100 * (hsv[..., 2] >= 250).mean()), darkPct="%.2f" % (100 * (hsv[..., 2] < 40).mean()),
                clothFraction="%.2f" % cloth.mean())

def main(budget):
    seen = {}
    for l in open(os.path.join(A, "receipts.jsonl")):
        r = json.loads(l)
        if r["pixelExists"] and len(r["corners"]) == 4 and r["sha256"]: seen.setdefault(r["sha256"], r)
    done = set()
    if os.path.exists(OUT): done = {r["sha256"] for r in csv.DictReader(open(OUT))}
    new = not os.path.exists(OUT); start = time.time(); n = 0
    with open(OUT, "a", newline="") as f:
        w = csv.DictWriter(f, fieldnames=F)
        if new: w.writeheader()
        for sha, r in seen.items():
            if sha in done: continue
            if time.time() - start > budget: print("added", n, "MORE"); return
            img = cv2.imread(os.path.join(ROOT, r["pixel"]))
            if img is None: continue
            t = rectify(img, r["corners"]); m = measure(t) or {}
            cv2.imwrite(os.path.join(A, "rectified", sha[:12] + ".jpg"), t, [cv2.IMWRITE_JPEG_QUALITY, 85])
            row = dict(sha256=sha, pixel=r["pixel"], capturedAt=r["capturedAt"], timeline=r["timeline"], operationId=r["operationId"],
                       frameId=r["frameId"], frameLuma="%.1f" % cv2.cvtColor(img, cv2.COLOR_BGR2GRAY).mean(), **m)
            w.writerow(row); n += 1
    print("added", n, "finished")
main(float(sys.argv[1]) if len(sys.argv) > 1 else 1e9)
