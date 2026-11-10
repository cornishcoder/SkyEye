#!/usr/bin/env python3
"""Still-table stability of the production detector, from receipts only.

Each capture operation is a 2-3.5 s burst of a table the user declared still.
(1) Frame-level: does TableTracker.SameLayout (same count, same kinds, centres
within 0.015) hold between consecutive frames?  (2) Ball-level: cluster the
detections of a burst into physical balls and report which balls are
intermittent, by kind and by position zone, and how often a kind label flips.
(3) What a per-ball vote would give: balls seen in at least half the frames.
"""
import json, os, collections, math, csv
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
A = os.path.join(ROOT, "work", "evidence-audit")
POCKETS = [(0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1)]
def zone(x, y):
    if min(math.hypot(x - a, y - b) for a, b in POCKETS) < .11: return "pocket"
    return "cushion" if min(x, 2 - x, y, 1 - y) < .075 else "open"
def same(a, b, tol=.015):
    if len(a) != len(b): return False
    rem = list(b)
    for k, x, y in a:
        c = [q for q in rem if q[0] == k]
        if not c: return False
        m = min(c, key=lambda q: math.hypot(q[1] - x, q[2] - y))
        if math.hypot(m[1] - x, m[2] - y) > tol: return False
        rem.remove(m)
    return True
R = {}
for l in open(os.path.join(A, "receipts.jsonl")):
    r = json.loads(l)
    if r["hasVision"] and r["sha256"]: R.setdefault(r["sha256"], r)
ops = collections.defaultdict(list)
for r in R.values(): ops[r["operationId"] or r["timeline"]].append(r)
bursts = [sorted(v, key=lambda r: r["sourceTimeUs"] or 0) for v in ops.values() if len(v) >= 3]
pairs = ok = 0; unstable = 0; T = collections.Counter(); jitter = []; voteStable = 0; rows = []
for v in bursts:
    p = [(same(a["balls"], b["balls"])) for a, b in zip(v, v[1:])]; pairs += len(p); ok += sum(p); unstable += not all(p)
    clusters = []
    for i, r in enumerate(v):
        for k, x, y in r["balls"]:
            c = next((c for c in clusters if math.hypot(c["x"] - x, c["y"] - y) < .03), None)
            if c is None: c = dict(x=x, y=y, seen={}, pts=[]); clusters.append(c)
            c["seen"][i] = k; c["pts"].append((x, y))
    inter = 0
    for c in clusters:
        z = zone(c["x"], c["y"]); kinds = collections.Counter(c["seen"].values()); k = kinds.most_common(1)[0][0]
        T["balls"] += 1; T["balls", z] += 1
        if len(c["seen"]) < len(v): T["intermittent"] += 1; T["intermittent", z] += 1; T["intermittentKind", k] += 1; inter += 1
        if len(kinds) > 1: T["kindFlip"] += 1; T["kindFlip", "/".join(sorted(kinds))] += 1
        if len(c["pts"]) > 1:
            mx = sum(p[0] for p in c["pts"]) / len(c["pts"]); my = sum(p[1] for p in c["pts"]) / len(c["pts"])
            jitter.append(max(math.hypot(p[0] - mx, p[1] - my) for p in c["pts"]) / .054)
    voted = [c for c in clusters if len(c["seen"]) * 2 >= len(v)]
    voteStable += all(abs(sum(1 for c in voted if i in c["seen"]) - len(voted)) <= 1 for i in range(len(v)))
    rows.append(dict(capturedAt=v[0]["capturedAt"], frames=len(v), counts=" ".join(str(r["nBalls"]) for r in v), sameLayoutPairs="%d/%d" % (sum(p), len(p)), physicalBalls=len(clusters), intermittent=inter, firstPixel=v[0]["pixel"]))
jitter.sort()
print("bursts (>=3 frames, declared still):", len(bursts), " consecutive pairs:", pairs)
print("frame-level SameLayout holds: %d/%d pairs; bursts with any mismatch: %d/%d" % (ok, pairs, unstable, len(bursts)))
print("ball-level: %d tracked balls, %d intermittent (%.1f%%)" % (T["balls"], T["intermittent"], 100 * T["intermittent"] / T["balls"]))
for z in ("open", "cushion", "pocket"): print("  zone %-8s balls %4d intermittent %3d (%.1f%%)" % (z, T["balls", z], T["intermittent", z], 100 * T["intermittent", z] / max(1, T["balls", z])))
print("  intermittent by majority kind:", {k[1]: v for k, v in T.items() if isinstance(k, tuple) and k[0] == "intermittentKind"})
print("  balls whose kind label flips within a burst: %d" % T["kindFlip"], {k[1]: v for k, v in T.items() if isinstance(k, tuple) and k[0] == "kindFlip"})
print("  centre jitter of persistent balls (ball diameters): median %.3f p95 %.3f max %.3f" % (jitter[len(jitter) // 2], jitter[int(.95 * len(jitter))], jitter[-1]))
print("  bursts where every frame is within one ball of the per-ball majority vote: %d/%d" % (voteStable, len(bursts)))
with open(os.path.join(A, "burst-stability.csv"), "w", newline="") as f:
    w = csv.DictWriter(f, fieldnames=list(rows[0])); w.writeheader(); w.writerows(sorted(rows, key=lambda r: r["capturedAt"] or ""))
