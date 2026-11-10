#!/usr/bin/env python3
"""Index every frame receipt (JSON with an `indexedFrame` block) in the repo.

Each receipt binds a saved camera frame to its capture time, source timeline,
calibration corners and the production detector's result for that exact frame.
Writes work/evidence-audit/receipts.jsonl (one compact record per receipt).
Read-only with respect to evidence.
"""
import json, os, sys, collections
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
OUT = os.path.join(ROOT, "work", "evidence-audit", "receipts.jsonl")
SKIP = {".git", "bin", "obj", "__pycache__", "android-sdk-11", "python-reference", "evidence-audit", "railbird-pool-1.2.10"}
n = 0
with open(OUT, "w", encoding="utf-8") as out:
    for d, dirs, files in os.walk(ROOT):
        dirs[:] = sorted(x for x in dirs if x not in SKIP)
        for name in sorted(files):
            if not name.endswith(".json"): continue
            full = os.path.join(d, name)
            try:
                if os.path.getsize(full) > 8_000_000: continue
                with open(full, "rb") as f: head = f.read(400)
                if b'"indexedFrame"' not in head: continue
                r = json.load(open(full, encoding="utf-8-sig"))
            except Exception: continue
            fr = r.get("indexedFrame") or {}
            if not isinstance(fr, dict) or "pixelFile" not in fr: continue
            m = r.get("measurement") or {}; v = m.get("vision") or {}
            balls = v.get("balls") or []
            pix = os.path.join(d, fr["pixelFile"])
            rec = dict(receipt=os.path.relpath(full, ROOT).replace(os.sep, "/"),
                       pixel=os.path.relpath(pix, ROOT).replace(os.sep, "/"), pixelExists=os.path.exists(pix),
                       sha256=fr.get("pixelSha256"), frameId=fr.get("frameId"), sourceTimeUs=fr.get("sourceTimeUs"),
                       timeline=(fr.get("scope") or {}).get("timeline"), calibration=(fr.get("scope") or {}).get("calibration"),
                       w=fr.get("width"), h=fr.get("height"), capturedAt=r.get("capturedAt"), operationId=r.get("operationId"),
                       corners=[[c["x"], c["y"]] for c in (r.get("calibrationCorners") or [])],
                       configuration=r.get("configuration"), labelStatus=(r.get("labelStatus") or "")[:160],
                       algorithm=(json.loads(r["analyzerConfigurationIdentity"]).get("Algorithm") if r.get("analyzerConfigurationIdentity") else None),
                       hasVision=bool(v), kinds=dict(collections.Counter(b.get("kind") for b in balls)), nBalls=len(balls),
                       balls=[[b.get("kind"), round(b["position"]["x"], 4), round(b["position"]["y"], 4)] for b in balls if b.get("position")],
                       uncertain=v.get("uncertain"), layoutUncertain=v.get("layoutUncertain"), obstructed=v.get("obstructed"),
                       warnings=v.get("warnings"), colourGains=v.get("colourGains"), clothHue=v.get("clothHue"),
                       normalized=v.get("normalized"), elapsedMs=v.get("elapsedMs"),
                       motion=m.get("motion"), reason=m.get("reason"), illuminationChange=m.get("illuminationChange"))
            out.write(json.dumps(rec) + "\n"); n += 1
print("receipts", n)
