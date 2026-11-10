#!/usr/bin/env python3
"""Diagnostic prototype (not production code): cloth-relative disc detector.

Purpose: test one hypothesis on existing frames only. If balls are found by their
contrast *relative to the surrounding cloth* (a ratio, so exposure cancels) with
the known ball radius, do the misses that persist in production (black and cue
at cushions and pocket jaws, dim light) go away?

Method: rectify -> local cloth estimate by large median filter -> per-pixel
log-ratio to that estimate -> matched filter with a disc of the calibrated ball
radius -> peaks with non-maximum suppression. Colour is decided from the ball's
own pixels relative to the local cloth. No absolute brightness thresholds.
Usage: disc_contrast_detector.py [--all] [--out FILE]   (default: labelled frames)
"""
import json, os, sys, time, math
import cv2, numpy as np
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
A = os.path.join(ROOT, "work", "evidence-audit")
W, H = 400, 800; R = 0.027 * W
POCKETS = [(0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1)]

def rectify(img, corners):
    h, w = img.shape[:2]
    src = np.float32([[x * w, y * h] for x, y in corners]); dst = np.float32([[W, 0], [W, H], [0, H], [0, 0]])
    return cv2.warpPerspective(img, cv2.getPerspectiveTransform(src, dst), (W, H), flags=cv2.INTER_AREA)

def disc(r):
    k = int(math.ceil(r)); y, x = np.mgrid[-k:k + 1, -k:k + 1]; m = (x * x + y * y <= r * r).astype(np.float32)
    return m / m.sum()

def detect(img, corners, thr=0.20):
    t = rectify(img, corners)
    small = cv2.resize(t, (W // 2, H // 2), interpolation=cv2.INTER_AREA)
    bg = cv2.resize(cv2.medianBlur(small, 31), (W, H), interpolation=cv2.INTER_LINEAR).astype(np.float32)
    f = t.astype(np.float32)
    lr = np.log(f + 12) - np.log(bg + 12)                       # per-channel log ratio to local cloth
    mag = np.sqrt((lr ** 2).sum(axis=2))
    score = cv2.filter2D(mag, -1, disc(R * .85)) - cv2.filter2D(mag, -1, disc(R * 1.9)) * .5
    # peaks
    dil = cv2.dilate(score, np.ones((int(R * 1.6) | 1, int(R * 1.6) | 1), np.uint8))
    ys, xs = np.where((score >= dil - 1e-6) & (score > thr))
    cand = sorted(((float(score[y, x]), x, y) for y, x in zip(ys, xs)), reverse=True)
    out = []
    hsv = cv2.cvtColor(t, cv2.COLOR_BGR2HSV)
    for s, x, y in cand:
        tx, ty = y / W, 1 - x / W
        if min(math.hypot(tx - px, ty - py) for px, py in POCKETS) < 0.036: continue      # centre inside the pocket hole
        if any(math.hypot(x - ox, y - oy) < R * 1.55 for _, ox, oy, _ in out): continue
        yy, xx = np.mgrid[max(0, y - int(R)):min(H, y + int(R) + 1), max(0, x - int(R)):min(W, x + int(R) + 1)]
        inside = (xx - x) ** 2 + (yy - y) ** 2 <= (R * .75) ** 2
        px = f[yy[inside], xx[inside]]; b = bg[yy[inside], xx[inside]]; hv = hsv[yy[inside], xx[inside]].astype(np.float32)
        lum = px @ np.float32([.114, .587, .299]); blum = b @ np.float32([.114, .587, .299])
        rel = float(np.median(lum) / max(1.0, np.median(blum)))                   # brightness relative to cloth
        sat = float(np.median(hv[:, 1])); bsat = float(np.median(cv2.cvtColor(np.uint8(b.reshape(-1, 1, 3)), cv2.COLOR_BGR2HSV)[:, 0, 1]))
        strong = hv[hv[:, 1] > max(90, bsat + 40)]
        if len(strong) > .35 * len(hv):
            hue = float(np.median(strong[:, 0])) * 2
            kind = "yellow" if 20 <= hue <= 75 else "red" if hue < 20 or hue > 320 else "unknown"
        elif rel < .62: kind = "black"
        elif rel > 1.25: kind = "cue"
        else: kind = "unknown"
        out.append((kind, x, y, s))
    return [[k, round(y / W, 4), round(1 - x / W, 4), round(s, 3)] for k, x, y, s in out], t

if __name__ == "__main__":
    rec = {}
    for l in open(os.path.join(A, "receipts.jsonl")):
        r = json.loads(l)
        if r["pixelExists"] and r["sha256"] and len(r["corners"]) == 4: rec.setdefault(r["sha256"], r)
    want = set(rec) if "--all" in sys.argv else {json.loads(l)["sha256"] for l in open(os.path.join(A, "labels.jsonl"))}
    out = sys.argv[sys.argv.index("--out") + 1] if "--out" in sys.argv else os.path.join(A, "detections-disc-contrast.jsonl")
    done = {json.loads(l)["sha256"] for l in open(out)} if os.path.exists(out) and "--resume" in sys.argv else set()
    start = time.time(); n = 0; ms = []
    with open(out, "a" if done else "w") as f:
        for sha in sorted(want & set(rec)):
            if sha in done: continue
            if time.time() - start > 150: print("MORE"); break
            img = cv2.imread(os.path.join(ROOT, rec[sha]["pixel"]))
            if img is None: continue
            t0 = time.time(); balls, _ = detect(img, rec[sha]["corners"]); ms.append(1000 * (time.time() - t0))
            f.write(json.dumps(dict(sha256=sha, balls=balls)) + "\n"); n += 1
    print("frames", n, "median ms per frame", round(float(np.median(ms)), 1) if ms else None)
