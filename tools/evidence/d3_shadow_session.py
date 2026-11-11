"""Summarise D3 settled-change shadow evidence for one Live Detect run in a pulled log.

    python -I tools/evidence/d3_shadow_session.py <live-detect dir> [runId] [--sheet out.jpg]

<live-detect dir> holds live-detect.jsonl and settled-change/ (as pulled from the phone). Without a runId the
latest run is used. Prints per-pass cost, states, every comparison with its regions and shadow proposals,
raster sidecars, run events and the reducer's own outcomes and table events. With --sheet, draws each saved
raster pair side by side with the comparison's regions (green ball-sized, grey rejected).
"""
import json, os, sys, collections

sheet = sys.argv[sys.argv.index("--sheet") + 1] if "--sheet" in sys.argv else None
args = [a for i, a in enumerate(sys.argv[1:], 1) if not a.startswith("--") and sys.argv[i - 1] != "--sheet"]
root = args[0]; run = args[1] if len(args) > 1 else None
records = [json.loads(l) for l in open(os.path.join(root, "live-detect.jsonl"), encoding="utf-8") if l.strip()]
if run is None: run = [r for r in records if r["kind"] == "frame"][-1]["runId"]
mine = [r for r in records if r["runId"] == run]
frames = [r["frame"] for r in mine if r["kind"] == "frame"]
sc = [f.get("settledChange") for f in frames]
print("run", run, "frames", len(frames), "with settledChange", sum(1 for s in sc if s))
ms = sorted(s["computeMs"] for s in sc if s)
q = lambda p: ms[min(len(ms) - 1, int(p * (len(ms) - 1)))]
print("pass computeMs: median %.1f p95 %.1f max %.1f" % (q(.5), q(.95), ms[-1]))
print("states", dict(collections.Counter(s["state"] for s in sc if s)))
print("unavailable", dict(collections.Counter(s["unavailableReason"] for s in sc if s and s.get("unavailableReason"))))
print("checkOutcome", dict(collections.Counter(f["checkOutcome"] for f in frames)))
for r in mine:
    if r["kind"] == "runEvent": print("runEvent", r["runEvent"]["status"], r["runEvent"]["message"][:110])
    if r["kind"] == "tableEvent": print("tableEvent", r["tableEvent"]["capturedAt"][11:19], r["tableEvent"]["type"])
pairs = []
for f, s in zip(frames, sc):
    if not s or not s.get("evidenceIsNew"): continue
    e = s["evidence"]; c = e.get("comparison")
    status = {0: "NoReference", 1: "Compared"}.get(e["status"], e["status"])
    print("\nevidence seq %s %s at %s  gates=%s" % (e["sequence"], status, f["capturedAt"][11:19], e["gateReasons"]))
    if c:
        print("  drift band %.4f bed %.4f  compare %.0f ms (pass %.0f ms)  regions %d" % (c["cushionBandDrift"], c["bedDrift"], c["computeMs"], s["computeMs"], len(c["regions"])))
        for g in c["regions"]:
            print("   (%.3f, %.3f) %-7s area %.2f inr %.2f out %.2f %s %s dir=%s dark %.2f->%.2f white %.2f->%.2f" % (
                g["tableX"], g["tableY"], g["zone"], g["areaBalls"], g["inscribedRadiusBalls"], g["outsideFraction"],
                "BALL" if g["ballSized"] else "    ", g.get("rejectReason") or "", ["arrival", "departure", "unclear"][g["direction"]] if isinstance(g["direction"], int) else g["direction"],
                g["darkBefore"], g["darkAfter"], g["whiteBefore"], g["whiteAfter"]))
    print("  chainIntact", s.get("chainIntact"), "chainLength", s.get("chainLength"))
    for p in s.get("proposals") or []: print("  proposal", p["kind"], p.get("colour"), "(%.3f, %.3f)" % (p["tableX"], p["tableY"]), "applied", p["applied"], p.get("detail") or "")
    for b in s.get("ballStates") or []: print("  ballState", b["colour"], b["state"])
    for x in s.get("rasters") or []: print("  raster", x["trigger"], x["relativePath"], x["byteLength"], "available", x["available"], x.get("skipReason") or "")
    if s.get("rasters") and c: pairs.append((e["sequence"], s["rasters"], c["regions"]))
if sheet and pairs:
    import cv2, numpy as np
    rows = []
    for seq, rasters, regions in pairs:
        imgs = [cv2.imread(os.path.join(root, x["relativePath"])) for x in rasters if x["available"]]
        for g in regions:
            col = (0, 255, 0) if g["ballSized"] else (160, 160, 160)
            cv2.circle(imgs[-1], (int(g["rasterX"]), int(g["rasterY"])), int(max(8, g["inscribedRadiusBalls"] * 10.8 * 1.6)), col, 2)
        row = np.hstack(imgs); cv2.putText(row, "seq %d: reference | current" % seq, (8, 24), cv2.FONT_HERSHEY_SIMPLEX, .7, (255, 255, 255), 2)
        rows.append(row)
    cv2.imwrite(sheet, np.vstack(rows), [cv2.IMWRITE_JPEG_QUALITY, 85]); print("\nwrote", sheet)
