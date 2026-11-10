#!/usr/bin/env python3
"""Replay existing per-frame detector output through two settlement rules.

CURRENT: TableTracker semantics. A burst settles only if every consecutive
frame pair passes SameLayout (same count, same kinds, centres within 0.015).
PER-BALL: each physical ball is a track. A track is confirmed after appearing in
2 of the last 3 frames and dropped only after missing 2 of the last 3
(hysteresis). The layout is the confirmed set, and it is stable when that set
(kinds, centres within 0.015 of the track mean) is unchanged across the
burst's last 3 frames.
Input: work/evidence-audit/receipts.jsonl (no images, no device). The table
was declared still in every burst, so any change in the published layout is a
false change.
"""
import json, os, collections, math
ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
A = os.path.join(ROOT, "work", "evidence-audit")
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
cur_ok = new_ok = cur_changes = new_changes = 0
for v in bursts:
    pairs = [same(a["balls"], b["balls"]) for a, b in zip(v, v[1:])]
    cur_ok += all(pairs); cur_changes += pairs.count(False)
    tracks = []; published = []
    for i, r in enumerate(v):
        for t in tracks: t["hits"].append(0)
        for k, x, y in r["balls"]:
            t = min((t for t in tracks if t["kind"] == k and math.hypot(t["x"] - x, t["y"] - y) < .03), key=lambda t: math.hypot(t["x"] - x, t["y"] - y), default=None)
            if t is None: t = dict(kind=k, x=x, y=y, n=0, hits=[0] * (i + 1), confirmed=False); tracks.append(t)
            t["hits"][-1] = 1; t["n"] += 1; t["x"] += (x - t["x"]) / t["n"]; t["y"] += (y - t["y"]) / t["n"]
        for t in tracks:
            last = t["hits"][-3:]
            if not t["confirmed"] and sum(last) >= 2: t["confirmed"] = True
            elif t["confirmed"] and len(last) == 3 and sum(last) <= 1: t["confirmed"] = False
        published.append([(t["kind"], t["x"], t["y"]) for t in tracks if t["confirmed"]])
    tail = published[1:]  # first frame cannot confirm anything yet
    ch = sum(not same(a, b) for a, b in zip(tail, tail[1:]))
    new_changes += ch; new_ok += ch == 0 and len(tail) >= 2
print("still bursts:", len(bursts))
print("CURRENT  all-pairs SameLayout: settled %d/%d bursts; false layout changes %d" % (cur_ok, len(bursts), cur_changes))
print("PER-BALL 2-of-3 hysteresis:    stable  %d/%d bursts; false layout changes %d" % (new_ok, len(bursts), new_changes))
