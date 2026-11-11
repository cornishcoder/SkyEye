"""D3 slice 2 (design 5.3 C): inputs and scoring for the live-cadence simulation on the T4 clips.

    python -I tools/evidence/d3_clips.py inputs work/d3/clips.json [--regions regions-grey-ballcentre.csv]
    python -I tools/evidence/d3_clips.py score work/d3/clips.json work/d3/clips-live.jsonl [work/d3/clips-score.json]

inputs: one entry per clip in work/evidence-audit/t4/pairs.csv with its corners (the T4 prototype's per-day
receipt corners), T4's settled windows (5 fps frame numbers as seconds) and T4's v3 ball-sized regions per
pair, converted to the half-scale raster (T4 raster coordinates / 2).

score: matches each T4 shot pair (end of settled segment A, start of segment B) to a live comparison whose
reference window ends near A's end and whose current window lies after the motion, then compares the
ball-sized regions. Control pairs (one long settled segment, or the motionless code62 clips) count live
comparisons inside them and any ball-sized region those produce.
"""
import csv, json, math, os, sys, collections

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
sys.path.insert(0, os.path.join(ROOT, "tools", "evidence", "prototypes"))
import settled_change as sc  # noqa: E402  (config only)

R_HALF = 0.027 * 400  # ball radius in the half-scale raster


def inputs(out, regions_file="regions-grey.csv"):
    regions = collections.defaultdict(list)
    for r in csv.DictReader(open(os.path.join(sc.T4, regions_file))):
        if r.get("k") in ("-1", "") or r.get("ball_v3") != "True": continue
        regions[r["pair"]].append(dict(x=float(r["cx"]) / 2, y=float(r["cy"]) / 2, zone=r["zone"], cls=r["cls"]))
    clips = collections.OrderedDict()
    for p in csv.DictReader(open(os.path.join(sc.T4, "pairs.csv"))):
        c = clips.setdefault(p["clip"], dict(path=p["clip"], day=p["day"], corners=sc.CORNERS[p["day"]], pairs=[]))
        c["pairs"].append(dict(pair=p["pair"], kind=p["kind"], aStartS=int(p["a0"]) / 5, aEndS=int(p["a1"]) / 5,
                               bStartS=int(p["b0"]) / 5, bEndS=int(p["b1"]) / 5, t4Regions=regions.get(p["pair"], [])))
    os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
    json.dump(dict(clips=list(clips.values())), open(out, "w"), indent=1)
    print("wrote", out, len(clips), "clips", sum(len(c["pairs"]) for c in clips.values()), "pairs")


def score(clips_path, live_path, out=None):
    clips = {c["path"]: c for c in json.load(open(clips_path))["clips"]}
    live = {}
    for line in open(live_path):
        d = json.loads(line); live[d["clip"]] = d
    rows, totals = [], collections.Counter()
    near = lambda g, t: math.hypot(g["RasterX"] - t["x"], g["RasterY"] - t["y"]) < 1.6 * R_HALF
    for path, c in clips.items():
        L = live.get(path)
        if L is None: totals["clips missing"] += 1; continue
        comps = [e for e in L["evidence"] if e["status"] == "Compared"]
        shots = [p for p in c["pairs"] if p["kind"] == "shot"]
        # A live comparison spans a T4 shot when its reference ends no earlier than 1.5 s before the shot's
        # settled segment A ends, and its current window starts after A ends and no later than 4 s after B
        # starts. A live machine can split one T4 motion span (a pause while aiming) or merge two (a short
        # middle segment that never settles at live cadence), so a shot may be covered by a chain of comparisons
        # and one comparison may cover several shots.
        def spans(e, p):
            r, cur = e["refWindowEndS"], e["curWindowStartS"]
            if not (r < p["bStartS"] + 1.0 and cur > p["aEndS"]): return False      # intervals overlap
            inside = r >= p["aEndS"] - 1.5 and cur <= p["bStartS"] + 4.0          # part of a chain within the shot
            contains = r <= p["aEndS"] + 1.5 and cur >= p["bStartS"] - 1.0        # the whole shot, possibly with others
            return inside or contains
        for p in shots:
            chain = [e for e in comps if spans(e, p)]
            ball = [g for e in chain if not e["gates"] for g in e["regions"] if g["BallSized"]]
            t4 = p["t4Regions"]; hit = sum(1 for t in t4 if any(near(g, t) for g in ball))
            totals["shots"] += 1; totals["shots covered"] += bool(chain); totals["t4 regions"] += len(t4); totals["t4 regions found"] += hit
            totals["covering comparisons gated"] += sum(1 for e in chain if e["gates"])
            rows.append(dict(pair=p["pair"], kind="shot", covered=bool(chain), comparisons=[e["sequence"] for e in chain],
                             t4=len(t4), found=hit))
        for e in comps:
            spanned = [p for p in shots if spans(e, p)]
            ball = [g for g in e["regions"] if g["BallSized"]]
            expected = [t for p in spanned for t in p["t4Regions"]]
            unexplained = [g for g in ball if not any(near(g, t) for t in expected)]
            totals["live comparisons"] += 1; totals["live comparisons gated"] += bool(e["gates"])
            if not spanned:
                ctl = [p["pair"] for p in c["pairs"] if p["kind"] != "shot" and p["aStartS"] - .5 <= e["refWindowStartS"] and e["curWindowEndS"] <= p["bEndS"] + .5]
                totals["comparisons spanning no T4 shot"] += 1
                if not e["gates"]: totals["ungated ball regions with no T4 shot"] += len(ball)
                rows.append(dict(clip=os.path.basename(path), sequence=e["sequence"], kind="no-shot", insideControl=ctl,
                                 refWindowEndS=e["refWindowEndS"], curWindowStartS=e["curWindowStartS"], gates=e["gates"], ballRegions=len(ball)))
            elif not e["gates"]:
                totals["ungated ball regions not at a T4 region"] += len(unexplained)
                if unexplained:
                    rows.append(dict(clip=os.path.basename(path), sequence=e["sequence"], kind="unexplained", shots=[p["pair"] for p in spanned],
                                     regions=[dict(x=g["RasterX"], y=g["RasterY"], zone=g["Zone"], direction=g["direction"]) for g in unexplained]))
        for p in c["pairs"]:
            if p["kind"] != "shot":
                inside = [e for e in comps if p["aStartS"] - .5 <= e["refWindowStartS"] and e["curWindowEndS"] <= p["bEndS"] + .5]
                totals["controls"] += 1; totals["comparisons inside controls"] += len(inside)
                totals["ungated ball regions inside controls"] += sum(1 for e in inside if not e["gates"] for g in e["regions"] if g["BallSized"])
        totals["passes"] += L["passes"]; totals["settled passes"] += L["settledPasses"]
    res = dict(totals=dict(totals), rows=rows)
    for k, v in totals.items(): print("%-40s %s" % (k, v))
    if out: json.dump(res, open(out, "w"), indent=1)
    return res


if __name__ == "__main__":
    if sys.argv[1] == "inputs": inputs(sys.argv[2], sys.argv[sys.argv.index("--regions") + 1] if "--regions" in sys.argv else "regions-grey.csv")
    else: score(sys.argv[2], sys.argv[3], sys.argv[4] if len(sys.argv) > 4 else None)
