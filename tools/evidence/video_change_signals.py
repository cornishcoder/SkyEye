#!/usr/bin/env python3
"""Does frame differencing confuse lighting change with motion? Measured on the
existing clips, in real capture order (10 fps, 320x180 grey, whole frame).

Per clip, between frames 0.3 s apart:
  raw   = fraction of pixels whose grey level changed by more than 12
  comp  = the same after removing each 20x20 tile's own gain change
          (log-ratio minus the tile's median log-ratio, threshold 0.10)
A global or smooth lighting change moves `raw` but not `comp`; a moving ball,
cue or arm moves both. Also records mean brightness over time so clips where
the light or exposure changed can be identified without anyone's notes.
Writes work/evidence-audit/video-change-signals.csv (resumable).
"""
import csv, os, subprocess, sys, time
import numpy as np
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
A = os.path.join(ROOT, "work", "evidence-audit"); OUT = os.path.join(A, "video-change-signals.csv")
W, H, FPS, LAG = 320, 180, 10, 3
F = ["path", "seconds", "lumaMin", "lumaMax", "lumaRatio", "maxStepRatio", "rawP50", "rawP95", "rawMax", "compP50", "compP95", "compMax", "rawOver2pct", "compOver2pct", "rawOnlyFrames"]
vids = {}
for r in csv.DictReader(open(os.path.join(A, "inventory.csv"))):
    if r["kind"] == "video" and r["path"].startswith("work/production/evidence") and r["width"] == "1920" and r["mtime_utc"] >= "2026-10-26": vids.setdefault(r["sha1"], r)
done = {r["path"] for r in csv.DictReader(open(OUT))} if os.path.exists(OUT) else set()
new = not os.path.exists(OUT); start = time.time()
with open(OUT, "a", newline="") as f:
    w = csv.DictWriter(f, fieldnames=F)
    if new: w.writeheader()
    for r in sorted(vids.values(), key=lambda r: r["mtime_utc"]):
        if r["path"] in done: continue
        if time.time() - start > 120: print("MORE"); sys.exit()
        raw = subprocess.run(["ffmpeg", "-v", "error", "-i", os.path.join(ROOT, r["path"]), "-vf", "fps=%d,scale=%d:%d" % (FPS, W, H), "-f", "rawvideo", "-pix_fmt", "gray", "-"], capture_output=True).stdout
        fr = np.frombuffer(raw, np.uint8).reshape(-1, H, W).astype(np.float32)
        if len(fr) <= LAG + 1: continue
        luma = fr.mean(axis=(1, 2)); a, b = fr[:-LAG], fr[LAG:]
        rawc = (np.abs(b - a) > 12).mean(axis=(1, 2))
        lr = np.log(b + 8) - np.log(a + 8)
        tiles = lr.reshape(len(lr), H // 20, 20, W // 20, 20)
        med = np.median(tiles.transpose(0, 1, 3, 2, 4).reshape(len(lr), H // 20, W // 20, 400), axis=3)
        comp = (np.abs(tiles - med[:, :, None, :, None]) > .10).mean(axis=(1, 2, 3, 4))
        step = np.maximum(luma[LAG:] / luma[:-LAG], luma[:-LAG] / luma[LAG:])
        w.writerow(dict(path=r["path"], seconds="%.1f" % (len(fr) / FPS), lumaMin="%.1f" % luma.min(), lumaMax="%.1f" % luma.max(), lumaRatio="%.2f" % (luma.max() / max(1, luma.min())),
                        maxStepRatio="%.2f" % step.max(), rawP50="%.4f" % np.percentile(rawc, 50), rawP95="%.4f" % np.percentile(rawc, 95), rawMax="%.4f" % rawc.max(),
                        compP50="%.4f" % np.percentile(comp, 50), compP95="%.4f" % np.percentile(comp, 95), compMax="%.4f" % comp.max(),
                        rawOver2pct="%.3f" % (rawc > .02).mean(), compOver2pct="%.3f" % (comp > .02).mean(), rawOnlyFrames="%.3f" % ((rawc > .02) & (comp < .005)).mean()))
        f.flush()
print("finished")
