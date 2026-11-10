#!/usr/bin/env python3
"""Rectified-table sheets with the production detector's own result drawn on top.

One frame per capture operation that has an embedded detector result, ordered by
table brightness. Circles: detector balls (red/yellow/white=cue/black drawn
grey/magenta=unknown). Nothing is drawn where the detector reported nothing, so
missed balls are visible as bare balls. Captions: tile number, capture time,
median table V and S, detector count, U = uncertain.
Writes work/evidence-audit/sheets/detector-NN.jpg and detector-index.csv.
"""
import csv, json, os, collections, sys
import cv2, numpy as np
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
A = os.path.join(ROOT, "work", "evidence-audit"); S = os.path.join(A, "sheets")
L = {r["sha12"]: r for r in csv.DictReader(open(os.path.join(A, "robust-light.csv")))}
only_fail = "--failures" in sys.argv
seen = {}
for l in open(os.path.join(A, "receipts.jsonl")):
    r = json.loads(l)
    if r["pixelExists"] and r["sha256"] and r["hasVision"] and len(r["corners"]) == 4: seen.setdefault(r["sha256"], r)
ops = collections.defaultdict(list)
for r in seen.values(): ops[r["operationId"] or r["timeline"]].append(r)
reps = []
for v in ops.values():
    v.sort(key=lambda r: (r["nBalls"], r["sourceTimeUs"] or 0))
    r = v[0]  # the worst frame of the burst
    if only_fail and r["nBalls"] >= 16 and not r["kinds"].get("unknown"): continue
    reps.append(r)
reps.sort(key=lambda r: int(L.get(r["sha256"][:12], {}).get("tableV", 0)))
W, H, CAP, COLS, ROWS = 320, 640, 40, 6, 2
COL = {"red": (40, 40, 255), "yellow": (0, 220, 255), "cue": (255, 255, 255), "black": (160, 160, 160), "unknown": (255, 0, 255)}
idx = []
for s in range(0, len(reps), COLS * ROWS):
    sheet = np.full((ROWS * (H + CAP), COLS * W, 3), 24, np.uint8)
    for i, r in enumerate(reps[s:s + COLS * ROWS]):
        img = cv2.imread(os.path.join(ROOT, r["pixel"]))
        if img is None: continue
        h, w = img.shape[:2]
        src = np.float32([[x * w, y * h] for x, y in r["corners"]]); dst = np.float32([[W, 0], [W, H], [0, H], [0, 0]])
        t = cv2.warpPerspective(img, cv2.getPerspectiveTransform(src, dst), (W, H), flags=cv2.INTER_AREA)
        for kind, x, y in r["balls"]:
            cv2.circle(t, (int(W * (1 - y)), int(W * x)), int(W * .027) + 3, COL.get(kind, (255, 0, 255)), 1, cv2.LINE_AA)
        yy, xx = (i // COLS) * (H + CAP), (i % COLS) * W
        sheet[yy:yy + H, xx:xx + W] = t
        l = L.get(r["sha256"][:12], {}); k = r["kinds"]; n = s + i + 1
        cv2.putText(sheet, "D%d %s V%s S%s" % (n, (r["capturedAt"] or "?")[5:16].replace("T", " "), l.get("tableV"), l.get("tableS")), (xx + 3, yy + H + 15), cv2.FONT_HERSHEY_SIMPLEX, .42, (255, 255, 255), 1, cv2.LINE_AA)
        cv2.putText(sheet, "det %d r%d y%d b%d c%d ?%d%s" % (r["nBalls"], k.get("red", 0), k.get("yellow", 0), k.get("black", 0), k.get("cue", 0), k.get("unknown", 0), " U" if r["uncertain"] else " ok"), (xx + 3, yy + H + 33), cv2.FONT_HERSHEY_SIMPLEX, .42, (120, 220, 255), 1, cv2.LINE_AA)
        idx.append(dict(tile="D%d" % n, pixel=r["pixel"], receipt=r["receipt"], capturedAt=r["capturedAt"], tableV=l.get("tableV"), tableS=l.get("tableS"), detected=r["nBalls"], kinds=json.dumps(k), uncertain=r["uncertain"], warnings=" | ".join(r["warnings"] or [])))
    cv2.imwrite(os.path.join(S, "detector-%s%02d.jpg" % ("fail-" if only_fail else "", s // (COLS * ROWS) + 1)), sheet, [cv2.IMWRITE_JPEG_QUALITY, 85])
with open(os.path.join(S, "detector-%sindex.csv" % ("fail-" if only_fail else "")), "w", newline="") as f:
    w = csv.DictWriter(f, fieldnames=list(idx[0])); w.writeheader(); w.writerows(idx)
print("tiles", len(reps))
