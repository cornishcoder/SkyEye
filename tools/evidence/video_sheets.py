#!/usr/bin/env python3
"""One middle frame from every unique video, as contact sheets, to classify content.

Writes work/evidence-audit/sheets/videos-NN.jpg and videos-index.csv. Videos that
cannot be decoded are listed with reason `undecodable`. Resumable via cache.
"""
import csv, os, re, sys, time
import cv2, numpy as np
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
A = os.path.join(ROOT, "work", "evidence-audit"); S = os.path.join(A, "sheets"); C = os.path.join(A, "video-thumbs"); os.makedirs(C, exist_ok=True)
seen = {}
for r in csv.DictReader(open(os.path.join(A, "inventory.csv"))):
    if r["kind"] == "video" and not r["path"].endswith(".m3u8"): seen.setdefault(r["sha1"], r)
vids = sorted(seen.values(), key=lambda r: r["mtime_utc"]); start = time.time(); bad = []
for r in vids:
    out = os.path.join(C, r["sha1"][:12] + ".jpg")
    if os.path.exists(out) or os.path.exists(out + ".bad"): continue
    if time.time() - start > 140: print("MORE"); sys.exit()
    cap = cv2.VideoCapture(os.path.join(ROOT, r["path"])); n = int(cap.get(cv2.CAP_PROP_FRAME_COUNT) or 0)
    if n > 2: cap.set(cv2.CAP_PROP_POS_FRAMES, n // 2)
    ok, fr = cap.read(); cap.release()
    if not ok: open(out + ".bad", "w").close(); continue
    h, w = fr.shape[:2]; s = 240 / max(w, h * 16 / 9)
    cv2.imwrite(out, cv2.resize(fr, (max(1, int(w * s)), max(1, int(h * s))), interpolation=cv2.INTER_AREA))
TW, TH, CAP, COLS, ROWS = 240, 135, 30, 8, 8; idx = []
for s0 in range(0, len(vids), COLS * ROWS):
    sheet = np.full((ROWS * (TH + CAP), COLS * TW, 3), 24, np.uint8)
    for i, r in enumerate(vids[s0:s0 + COLS * ROWS]):
        y, x = (i // COLS) * (TH + CAP), (i % COLS) * TW; n = s0 + i + 1
        t = cv2.imread(os.path.join(C, r["sha1"][:12] + ".jpg")); status = "ok"
        if t is None: status = "undecodable"
        else: sheet[y:y + t.shape[0], x:x + t.shape[1]] = t[:TH, :TW]
        m = re.search(r"dur=([\d.]+)", r["note"]); d = float(m.group(1)) if m else 0
        cv2.putText(sheet, "V%d %s %.0fs %sx%s" % (n, r["mtime_utc"][5:16].replace("T", " "), d, r["width"], r["height"]), (x + 2, y + TH + 12), cv2.FONT_HERSHEY_SIMPLEX, .33, (255, 255, 255), 1, cv2.LINE_AA)
        cv2.putText(sheet, r["path"].split("/")[-2][:26] + "/" + r["path"].split("/")[-1][:14], (x + 2, y + TH + 25), cv2.FONT_HERSHEY_SIMPLEX, .3, (120, 220, 255), 1, cv2.LINE_AA)
        idx.append(dict(tile="V%d" % n, path=r["path"], seconds=d, width=r["width"], height=r["height"], mtime=r["mtime_utc"], status=status))
    cv2.imwrite(os.path.join(S, "videos-%02d.jpg" % (s0 // (COLS * ROWS) + 1)), sheet, [cv2.IMWRITE_JPEG_QUALITY, 80])
with open(os.path.join(S, "videos-index.csv"), "w", newline="") as f:
    w = csv.DictWriter(f, fieldnames=list(idx[0])); w.writeheader(); w.writerows(idx)
print("videos", len(vids), "undecodable", sum(i["status"] != "ok" for i in idx), "total seconds", round(sum(i["seconds"] for i in idx)))
