#!/usr/bin/env python3
"""Inventory every image/video/raw-pixel evidence file in the repository.

Read-only with respect to evidence. Writes work/evidence-audit/inventory.csv
(resumable: already-listed paths are skipped). Records size, mtime, SHA-1,
dimensions and a 64-bit difference hash for images so exact and near
duplicates can be grouped without opening every file by hand.
"""
import csv, hashlib, os, subprocess, sys, time, json
from PIL import Image
Image.MAX_IMAGE_PIXELS = None
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "work", "evidence-audit", "inventory.csv")
IMG = {".png", ".jpg", ".jpeg", ".webp", ".bmp"}
VID = {".mp4", ".ts", ".h264", ".mkv", ".mov", ".m3u8"}
RAW = {".bgr", ".argb", ".gray"}
SKIP_DIRS = {".git", "bin", "obj", "__pycache__", "android-sdk-11", "python-reference", "evidence-audit"}
FIELDS = ["path", "kind", "bytes", "mtime_utc", "sha1", "width", "height", "dhash", "mean_luma", "note"]

def dhash(im):
    g = im.convert("L").resize((9, 8), Image.BILINEAR)
    px = list(g.tobytes()); bits = 0
    for r in range(8):
        for c in range(8):
            bits = (bits << 1) | (px[r * 9 + c] > px[r * 9 + c + 1])
    return "%016x" % bits, sum(px) / len(px)

def probe(path):
    try:
        o = subprocess.run(["ffprobe", "-v", "error", "-select_streams", "v:0", "-show_entries",
                            "stream=width,height,nb_frames,avg_frame_rate,duration", "-of", "json", path],
                           capture_output=True, text=True, timeout=30).stdout
        s = json.loads(o)["streams"][0]
        return s.get("width", ""), s.get("height", ""), "frames=%s fps=%s dur=%s" % (s.get("nb_frames"), s.get("avg_frame_rate"), s.get("duration"))
    except Exception as e:
        return "", "", "ffprobe-failed: %s" % type(e).__name__

def main(budget):
    done = set()
    if os.path.exists(OUT):
        with open(OUT, newline="", encoding="utf-8") as f:
            done = {r["path"] for r in csv.DictReader(f)}
    new = not os.path.exists(OUT)
    start = time.time(); n = 0; finished = True
    with open(OUT, "a", newline="", encoding="utf-8") as f:
        w = csv.DictWriter(f, fieldnames=FIELDS)
        if new: w.writeheader()
        for d, dirs, files in os.walk(ROOT):
            dirs[:] = sorted(x for x in dirs if x not in SKIP_DIRS)
            for name in sorted(files):
                ext = os.path.splitext(name)[1].lower()
                if ext not in IMG | VID | RAW: continue
                full = os.path.join(d, name); rel = os.path.relpath(full, ROOT).replace(os.sep, "/")
                if rel in done: continue
                if time.time() - start > budget: finished = False; break
                st = os.stat(full)
                row = dict(path=rel, bytes=st.st_size, mtime_utc=time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime(st.st_mtime)),
                           kind="image" if ext in IMG else "video" if ext in VID else "rawpixels", width="", height="", dhash="", mean_luma="", note="")
                h = hashlib.sha1()
                with open(full, "rb") as fh:
                    for chunk in iter(lambda: fh.read(1 << 20), b""): h.update(chunk)
                row["sha1"] = h.hexdigest()
                if ext in IMG:
                    try:
                        with Image.open(full) as im:
                            row["width"], row["height"] = im.size
                            im.draft("L", (320, 320))
                            row["dhash"], ml = dhash(im); row["mean_luma"] = "%.1f" % ml
                    except Exception as e:
                        row["note"] = "unreadable: %s" % type(e).__name__
                elif ext in VID and ext != ".m3u8":
                    row["width"], row["height"], row["note"] = probe(full)
                w.writerow(row); n += 1
            if not finished: break
    print("added", n, "finished" if finished else "MORE")

if __name__ == "__main__":
    main(float(sys.argv[1]) if len(sys.argv) > 1 else 1e9)
