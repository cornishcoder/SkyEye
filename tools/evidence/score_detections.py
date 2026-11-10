#!/usr/bin/env python3
"""Score ball detections against human-reviewed labels, offline, with no device.

Truth: work/evidence-audit/labels.jsonl (reviewed labels only).
Detections: by default the production detector result embedded in each frame's
receipt at capture time (work/evidence-audit/receipts.jsonl). Pass
`--detections FILE.jsonl` (lines of {"sha256":..., "balls":[[kind,x,y],...]} in
table-2x1 coordinates) to score any candidate on exactly the same frames.

Matching: greedy nearest pairs within one ball radius (0.027 table units),
colour-agnostic, so position and colour errors are reported separately.
Strata: ball colour; ball position (pocket = centre within 0.11 of a pocket
point, cushion = within 0.075 of a rail, i.e. touching it; else open); brightness.
"""
import argparse, csv, json, os, collections, math
import numpy as np, cv2
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
A = os.path.join(ROOT, "work", "evidence-audit")
R = 0.027; POCKETS = [(0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1)]

def to_table(corners, pts):
    src = np.float32(corners); dst = np.float32([[0, 0], [2, 0], [2, 1], [0, 1]])
    Hm = cv2.getPerspectiveTransform(src, dst)
    return cv2.perspectiveTransform(np.float32(pts).reshape(-1, 1, 2), Hm).reshape(-1, 2)

def zone(x, y):
    if min(math.hypot(x - px, y - py) for px, py in POCKETS) < 0.11: return "pocket"
    if min(x, 2 - x, y, 1 - y) < 0.075: return "cushion"
    return "open"

def load():
    rec = {}
    for l in open(os.path.join(A, "receipts.jsonl")):
        r = json.loads(l)
        if not r["sha256"] or len(r["corners"]) != 4: continue
        old = rec.get(r["sha256"])
        if old is None or (r["hasVision"] and not old["hasVision"]): rec[r["sha256"]] = r
    labels = [json.loads(l) for l in open(os.path.join(A, "labels.jsonl"))]
    met = {r["sha12"]: r for r in csv.DictReader(open(os.path.join(A, "robust-light.csv")))} if os.path.exists(os.path.join(A, "robust-light.csv")) else {}
    return rec, labels, met

def score(dets, name, out=None, quiet=False):
    rec, labels, met = load()
    T = collections.Counter(); per = []; errs = []; missed = []; extras = []
    for lab in labels:
        r = rec.get(lab["sha256"])
        if r is None or lab["sha256"] not in dets: T["labelled frames without detections"] += 1; continue
        vis = [b for b in lab["balls"] if b["vis"] == "Visible"]
        if lab["space"] == "image-normalized": tp = to_table(r["corners"], [[b["x"], b["y"]] for b in vis])
        else: tp = np.float32([[b["x"], b["y"]] for b in vis])
        d = dets[lab["sha256"]]; pairs = sorted((math.hypot(tp[i][0] - b[1], tp[i][1] - b[2]), i, j) for i in range(len(vis)) for j, b in enumerate(d))
        ui, uj, m = set(), set(), []
        for dist, i, j in pairs:
            if dist > R: break
            if i in ui or j in uj: continue
            ui.add(i); uj.add(j); m.append((i, j, dist))
        v = met.get(lab["sha256"][:12], {}).get("tableV"); light = "unknown" if not v else "dim(V<80)" if int(v) < 80 else "mid(V80-110)" if int(v) < 110 else "bright(V>=110)"
        ok_colour = 0
        for i, j, dist in m:
            z = zone(*tp[i]); c = vis[i]["colour"]; good = d[j][0] == c; ok_colour += good
            for key in ("all", "colour:" + c, "zone:" + z, "light:" + light):
                T[key, "tp"] += 1; T[key, "colourOk"] += good
            errs.append(dist / (2 * R))
        for i in range(len(vis)):
            z = zone(*tp[i]); c = vis[i]["colour"]
            for key in ("all", "colour:" + c, "zone:" + z, "light:" + light): T[key, "gt"] += 1
            if i not in ui: missed.append((lab["sha256"][:12], c, z, round(float(tp[i][0]), 3), round(float(tp[i][1]), 3)))
        for j, b in enumerate(d):
            if j not in uj:
                T["all", "fp"] += 1; T["light:" + light, "fp"] += 1; extras.append((lab["sha256"][:12], b[0], round(b[1], 3), round(b[2], 3)))
        complete = len(m) == len(vis) == len(d) and ok_colour == len(vis)
        T["frames"] += 1; T["framesComplete"] += complete
        per.append(dict(sha12=lab["sha256"][:12], pixel=r["pixel"], light=light, gt=len(vis), det=len(d), matched=len(m), colourOk=ok_colour, complete=complete))
    res = dict(name=name, frames=T["frames"], framesComplete=T["framesComplete"], strata={}, missed=missed, extras=extras, perFrame=per)
    if errs:
        e = sorted(errs); res["centreErrorBallDiameters"] = dict(median=round(e[len(e) // 2], 3), p95=round(e[int(.95 * (len(e) - 1))], 3), max=round(e[-1], 3))
    for key in sorted({k[0] for k in T if isinstance(k, tuple)}):
        gt, tp, fp, co = T[key, "gt"], T[key, "tp"], T[key, "fp"], T[key, "colourOk"]
        res["strata"][key] = dict(gt=gt, tp=tp, recall=round(tp / gt, 3) if gt else None, colourAccuracy=round(co / tp, 3) if tp else None,
                                  **(dict(fp=fp, precision=round(tp / (tp + fp), 3) if tp + fp else None) if key == "all" or key.startswith("light:") else {}))
    if out: json.dump(res, open(out, "w"), indent=1)
    if not quiet:
        print("== %s: %d labelled frames scored, %d fully correct (count, positions and colours)" % (name, res["frames"], res["framesComplete"]))
        print("centre error (ball diameters):", res.get("centreErrorBallDiameters"))
        for k, s in res["strata"].items(): print("  %-22s" % k, s)
        print("missed:", collections.Counter((c, z) for _, c, z, _, _ in missed).most_common())
        print("extras:", collections.Counter(k for _, k, _, _ in extras).most_common())
    return res

if __name__ == "__main__":
    ap = argparse.ArgumentParser(); ap.add_argument("--detections"); ap.add_argument("--name", default="production-at-capture"); ap.add_argument("--out")
    a = ap.parse_args()
    if a.detections: dets = {json.loads(l)["sha256"]: json.loads(l)["balls"] for l in open(a.detections)}
    else: dets = {s: r["balls"] for s, r in load()[0].items() if r["hasVision"]}
    score(dets, a.name, a.out or os.path.join(A, "score-%s.json" % a.name))
