#!/usr/bin/env python3
"""Contact sheets: one representative frame per capture operation, in capture order.

Each tile shows the raw camera frame, capture time (UTC), evidence folder,
measured cloth brightness (V) and red/blue ratio, and, where the receipt holds
one, the production detector's result (ball count by kind; U = uncertain).
Writes work/evidence-audit/sheets/operations-NN.jpg and sheet-index.csv.
"""
import csv, json, os, collections
import cv2, numpy as np
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
A = os.path.join(ROOT, "work", "evidence-audit"); S = os.path.join(A, "sheets"); os.makedirs(S, exist_ok=True)
M = {r["sha256"]: r for r in csv.DictReader(open(os.path.join(A, "frame-metrics.csv")))}
seen = {}
for l in open(os.path.join(A, "receipts.jsonl")):
    r = json.loads(l)
    if not (r["pixelExists"] and r["sha256"]): continue
    old = seen.get(r["sha256"])
    if old is None or (r["hasVision"] and not old["hasVision"]): seen[r["sha256"]] = r
ops = collections.defaultdict(list)
for r in seen.values(): ops[r["operationId"] or r["timeline"] or r["pixel"]].append(r)
reps = []
for k, v in ops.items():
    v.sort(key=lambda r: (r["sourceTimeUs"] or 0))
    reps.append((v[len(v) // 2], len(v)))
reps.sort(key=lambda x: x[0]["capturedAt"] or "")
TW, TH, CAP, COLS, ROWS = 480, 270, 44, 4, 5
idx = []
for s in range(0, len(reps), COLS * ROWS):
    sheet = np.full((ROWS * (TH + CAP), COLS * TW, 3), 24, np.uint8)
    for i, (r, n) in enumerate(reps[s:s + COLS * ROWS]):
        img = cv2.imread(os.path.join(ROOT, r["pixel"]))
        if img is None: continue
        y, x = (i // COLS) * (TH + CAP), (i % COLS) * TW
        sheet[y:y + TH, x:x + TW] = cv2.resize(img, (TW, TH), interpolation=cv2.INTER_AREA)
        m = M.get(r["sha256"], {}); num = s + i + 1
        k = r["kinds"]; det = ("det %d (r%d y%d b%d c%d ?%d)%s" % (r["nBalls"], k.get("red", 0), k.get("yellow", 0), k.get("black", 0), k.get("cue", 0), k.get("unknown", 0), " U" if r["uncertain"] else " ok")) if r["hasVision"] else "no detector result"
        folder = r["pixel"].split("/")[3 if r["pixel"].startswith("work/production") else 2][:34]
        l1 = "#%d %s x%d %s" % (num, (r["capturedAt"] or "?")[5:16].replace("T", " "), n, folder)
        l2 = "V%s R/B%s grad%s  %s" % (m.get("clothV_p50", "?"), m.get("rOverB", "?"), m.get("tileRatio", "?"), det)
        cv2.putText(sheet, l1, (x + 4, y + TH + 16), cv2.FONT_HERSHEY_SIMPLEX, .42, (255, 255, 255), 1, cv2.LINE_AA)
        cv2.putText(sheet, l2, (x + 4, y + TH + 36), cv2.FONT_HERSHEY_SIMPLEX, .42, (120, 220, 255), 1, cv2.LINE_AA)
        idx.append(dict(tile=num, sheet="operations-%02d.jpg" % (s // (COLS * ROWS) + 1), pixel=r["pixel"], capturedAt=r["capturedAt"], framesInOperation=n, sha256=r["sha256"]))
    cv2.imwrite(os.path.join(S, "operations-%02d.jpg" % (s // (COLS * ROWS) + 1)), sheet, [cv2.IMWRITE_JPEG_QUALITY, 82])
with open(os.path.join(S, "sheet-index.csv"), "w", newline="") as f:
    w = csv.DictWriter(f, fieldnames=list(idx[0])); w.writeheader(); w.writerows(idx)
print("operations", len(reps), "sheets", (len(reps) + COLS * ROWS - 1) // (COLS * ROWS))
