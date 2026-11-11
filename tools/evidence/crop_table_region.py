"""Warp manifest frames to a landscape table image (table x 0..2 left to right, y 0..1 top to bottom),
crop a region, mark labelled balls and detector output, and tile the crops into one sheet.
Usage: python -I tools/evidence/crop_table_region.py MANIFEST DETECTIONS_JSONL OUT.png NAME[,NAME...] x0 x1 y0 y1"""
import json, sys
import cv2, numpy as np

manifest, dets_path, out, names, x0, x1, y0, y1 = sys.argv[1:9]
names = names.split(","); x0, x1, y0, y1 = map(float, (x0, x1, y0, y1))
W = 600; H = 2 * W
dets = {}
for line in open(dets_path, encoding="utf-8"):
    d = json.loads(line); dets[d["sha256"]] = d
rows = [r for r in json.load(open(manifest, encoding="utf-8"))["frames"]
        if r["role"] in ("positive", "empty-control") and any(str(n) in names for n in r.get("frameIds", []) + r.get("names", []))]
tiles = []
for r in rows:
    img = cv2.imread(r["path"]); h, w = img.shape[:2]
    src = np.float32([[c[0] * w, c[1] * h] for c in r["calibration"]["corners"]])
    # Same corner order as VisionAnalyzer's portrait warp, rotated to landscape (col = table x * W, row = table y * W).
    dst = np.float32([[0, 0], [H, 0], [H, W], [0, W]])
    table = cv2.warpPerspective(img, cv2.getPerspectiveTransform(src, dst), (H, W))
    def px(x, y): return int(round(x * W)), int(round(y * W))
    for b in r["labels"]["balls"]:
        if b["visible"]:
            cv2.circle(table, px(b["x"], b["y"]), 18, (0, 255, 0), 1)
            cv2.putText(table, b["kind"][0].upper(), (px(b["x"], b["y"])[0] + 14, px(b["x"], b["y"])[1] - 14), cv2.FONT_HERSHEY_SIMPLEX, .45, (0, 255, 0), 1)
    for k, x, y in dets.get(r["sha256"], {}).get("balls", []):
        cv2.drawMarker(table, px(x, y), (0, 0, 255), cv2.MARKER_TILTED_CROSS, 10, 1)
    crop = table[int(y0 * W):int(y1 * W), int(x0 * W):int(x1 * W)]
    crop = cv2.resize(crop, None, fx=3, fy=3, interpolation=cv2.INTER_NEAREST)
    label = ",".join(str(n) for n in r.get("frameIds", [])) or r["path"]
    cv2.putText(crop, label, (6, 22), cv2.FONT_HERSHEY_SIMPLEX, .7, (255, 255, 255), 2)
    tiles.append(crop)
sheet = np.hstack(tiles) if tiles else np.zeros((10, 10, 3), np.uint8)
cv2.imwrite(out, sheet)
print("wrote", out, sheet.shape, "frames", len(tiles))
