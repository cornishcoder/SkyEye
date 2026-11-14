"""D3 criteria A input: the 33 staged still pairs from T4 (work/evidence-audit/t4/stills-grey.csv) as JSON
for `SkyEye.Vision.Desktop.Runner settled-change`: frame paths, receipt corners (normalised, VisionAnalyzer.Warp
order) and the reviewed black (B) and cue (C) source pixels. Reads the T4 prototype's config read-only.

    python -I tools/evidence/d3_stills_pairs.py work/d3/stills-pairs.json
"""
import csv, glob, json, os, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
sys.path.insert(0, os.path.join(ROOT, "tools", "evidence", "prototypes"))
import settled_change as sc  # noqa: E402  (config and receipt corners only)

out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(ROOT, "work", "d3", "stills-pairs.json")
rc = sc.receipt_corners(); pairs = []
for row in csv.DictReader(open(os.path.join(sc.T4, "stills-grey.csv"))):
    g = row["group"]; cfg = sc.STILLS[g]
    (sa, fa_id), (sb, fb_id) = [p.rsplit("-", 1) for p in row["pair"].split("_", 1)[1].split("_")]
    def find(st, fid):
        hits = glob.glob(os.path.join(ROOT, cfg["states"][st][0], "**", fid + ".png"), recursive=True)
        return os.path.relpath(hits[0], ROOT).replace(os.sep, "/")
    fa, fb = find(sa, fa_id), find(sb, fb_id)
    pairs.append(dict(pair=row["pair"], group=g, kind=row["kind"], expect=json.loads(row["expect"]),
                      before=fa, after=fb,
                      cornersBefore=rc.get(fa, sc.CORNERS["2026-10-31"]), cornersAfter=rc.get(fb, sc.CORNERS["2026-10-31"]),
                      receiptCorners=fa in rc and fb in rc,
                      black=list(cfg["B"]), cue=list(cfg["C"])))
os.makedirs(os.path.dirname(os.path.abspath(out)), exist_ok=True)
json.dump(dict(source="work/evidence-audit/t4/stills-grey.csv", pairs=pairs), open(out, "w"), indent=1)
print("wrote", out, len(pairs), "pairs")
