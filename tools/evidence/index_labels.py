#!/usr/bin/env python3
"""Collect human-reviewed ball-centre labels (corpus `labels.json` files).

Only label documents with `reviewed: true` are used; drafts and detector-derived
proposals are ignored so a model's output is never treated as ground truth.
Frames are de-duplicated by pixel SHA-256 (the newest corpus copy wins).
Writes work/evidence-audit/labels.jsonl. Read-only with respect to evidence.
"""
import json, os
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "work", "evidence-audit", "labels.jsonl")
SKIP = {".git", "bin", "obj", "__pycache__", "android-sdk-11", "python-reference", "evidence-audit", "railbird-pool-1.2.10", "node_modules"}
best = {}; docs = 0; unreviewed = 0
for top in ("work/o08", "work/o09", "work/production/evidence", "tests/fixtures", "docs/evidence"):
    for d, dirs, files in os.walk(os.path.join(ROOT, top)):
        dirs[:] = sorted(x for x in dirs if x not in SKIP)
        for name in files:
            if name != "labels.json": continue
            full = os.path.join(d, name)
            try: doc = json.load(open(full, encoding="utf-8-sig"))
            except Exception: continue
            if not isinstance(doc, dict) or not isinstance(doc.get("frames"), list): continue
            docs += 1
            if doc.get("reviewed") is not True: unreviewed += 1; continue
            for fr in doc["frames"]:
                sha = fr.get("pixelSha256")
                if not sha or "balls" not in fr: continue
                rec = dict(sha256=sha, sampleId=fr.get("sampleId"), space=fr.get("coordinateSpace"),
                           labels=os.path.relpath(full, ROOT).replace(os.sep, "/"), mtime=os.path.getmtime(full),
                           annotator=doc.get("annotator"), tags=fr.get("tags") or fr.get("conditions"),
                           balls=[dict(colour=b.get("colour"), x=b.get("x"), y=b.get("y"), vis=b.get("visibility"),
                                       rx=b.get("radiusX"), ry=b.get("radiusY")) for b in fr["balls"]])
                pngs = [f for f in os.listdir(d) if f.lower().endswith((".png", ".jpg"))]
                rec["pixelDirImages"] = [os.path.relpath(os.path.join(d, p), ROOT).replace(os.sep, "/") for p in pngs][:3]
                old = best.get(sha)
                score = (sum(b["rx"] is not None for b in rec["balls"]), rec["mtime"])
                if old is None or score > old[0]: best[sha] = (score, rec)
with open(OUT, "w", encoding="utf-8") as f:
    for _, rec in best.values(): f.write(json.dumps(rec) + "\n")
print("label documents", docs, "unreviewed skipped", unreviewed, "unique labelled frames", len(best))
