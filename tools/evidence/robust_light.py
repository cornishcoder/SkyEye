#!/usr/bin/env python3
"""Hue-independent lighting measurements from the rectified thumbnails.

frame_metrics.py finds the cloth by its dominant hue, which fails exactly when
warm or dim light desaturates the blue-grey cloth. These medians over the inner
playing area need no cloth model (balls cover only a few per cent of it).
Writes work/evidence-audit/robust-light.csv.
"""
import csv, os, cv2, numpy as np
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
A = os.path.join(ROOT, "work", "evidence-audit")
rows = []
for name in sorted(os.listdir(os.path.join(A, "rectified"))):
    t = cv2.imread(os.path.join(A, "rectified", name))
    if t is None: continue
    inner = t[60:-60, 50:-50]; hsv = cv2.cvtColor(inner, cv2.COLOR_BGR2HSV); lab = cv2.cvtColor(inner, cv2.COLOR_BGR2LAB)
    b, g, r = [float(np.median(inner[..., i])) for i in range(3)]
    q = [float(np.median(x[..., 2])) for half in np.array_split(hsv, 4, axis=0) for x in np.array_split(half, 2, axis=1)]
    rows.append(dict(sha12=name[:12], tableV=int(np.median(hsv[..., 2])), tableS=int(np.median(hsv[..., 1])), tableHue=int(np.median(hsv[..., 0])),
                     labA=int(np.median(lab[..., 1])) - 128, labB=int(np.median(lab[..., 2])) - 128, rOverB="%.3f" % (r / max(1, b)),
                     tileMin=int(min(q)), tileMax=int(max(q)), unevenness="%.2f" % (max(q) / max(1, min(q)))))
with open(os.path.join(A, "robust-light.csv"), "w", newline="") as f:
    w = csv.DictWriter(f, fieldnames=list(rows[0])); w.writeheader(); w.writerows(rows)
print("frames", len(rows))
