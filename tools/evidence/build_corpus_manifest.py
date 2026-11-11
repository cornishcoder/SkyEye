#!/usr/bin/env python3
"""Build the single detection test-corpus manifest (plan thread T1).

Writes tests/fixtures/detection-corpus/manifest.json: one row per unique image
content hash (SHA-1 of the pixel file), with its path, calibration corners,
reviewed labels normalised to table-2x1 coordinates, light stratum, zone tags,
role, corpora of origin and split, plus the production result stored in the
frame's receipt at capture time (so a scorer needs nothing but the manifest).

Inputs (all read-only): work/evidence-audit/{inventory.csv, receipts.jsonl,
labels.jsonl, robust-light.csv}, the project split locks and v3 split plan,
four label sources that index_labels.py does not read (frame 266, Layout03
validation v2, the restaged opposite-cue review, the O01 still fixture), and
Argon's docs/argon/data/aggregated_regression_benchmark.json.

Holdout rule: any frame whose receipt path contains "holdout" is recorded by
id and hash only, with role "holdout-excluded". Its pixels are never opened,
its labels never read and no light value is carried for it.

Run from anywhere: python3 tools/evidence/build_corpus_manifest.py
"""
import csv, hashlib, json, math, os, collections
import numpy as np, cv2

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
A = os.path.join(ROOT, "work", "evidence-audit")
OUT = os.path.join(ROOT, "tests", "fixtures", "detection-corpus", "manifest.json")
POCKETS = [(0, 0), (1, 0), (2, 0), (0, 1), (1, 1), (2, 1)]
FULL = {"red": 7, "yellow": 7, "black": 1, "cue": 1}

SPLIT_LOCKS = [  # project split locks found under work/ (none is a Holdout lock)
    "work/o08/corpus-v1/split-lock.json",
    "work/o08/frame978-development-v1/corpus-v1/split-lock.json",
    "work/o08/live-detect-black-pair-v1/corpus-v2/split-lock.json",
    "work/o08/live-detect-black-pair-v1/corpus-v3/split-lock.json",
    "work/o08/live-detect-black-pair-v1/corpus-v4/split-lock.json",
    "work/o09/development-v3/reviews/layout03-corrected-black-score-refresh-20261102/split-lock.json",
    "work/o09/layout02-baseline-v1/corpus/split-lock.json",
    "work/o09/radius-reviewed-corpus-v2-verified/split-lock.json",
    "work/production/evidence/code64-static-pocket-lip-20261031/import-verification-current-receipt/split-lock-receiptbind-v1.json",
    "work/production/evidence/o08-packed-rack-investigation-20261028/centre-labelling-v1/split-lock.json",
    "work/production/evidence/o08-packed-rack-investigation-20261028/centre-labelling-v1/daylight-static-sequence/split-lock.json",
    "work/production/evidence/o08-packed-rack-investigation-20261028/pixel11-live-frame-code33/corpus-v2/split-lock.json",
    "work/production/evidence/shot-samples-20261027/pixel11-replay-142235-finalstate/exact-review-corpus-v1/split-lock.json",
    "tests/fixtures/observations/v1/split-lock.json",
]
SPLIT_PLAN = "docs/evidence/live-detect-o09-development-split-plan-20261101.json"
SHORT = {  # readable tags for the corpora that matter to the plan
    "live-detect-20261101-black-cue-development-v4": "project-corpus-v4",
    "live-detect-20261101-black-cue-development-v3": "project-corpus-v3-black-pair",
}
W266 = "work/o09/development-v3/warm-batch-20261102"
L03V = "work/o09/development-v3/reviews/layout03-corrected-black-score-refresh-20261102/score-corpus-v2"
RESTAGED = "work/o08/live-detect-black-pair-v1/physical-controls/cue-present-opposite-cue-restaged-20261101/review-v1/user-reviewed-centres-20261101.json"
O01 = "tests/fixtures/observations/v1/still-labels.json"
D1365 = "docs/evidence/pocket-cushion-centre-labels-20261031/positive/manual-labels-frame-1365-draft.json"
ARGON = "docs/argon/data/aggregated_regression_benchmark.json"
NAMED_CONTROLS = [461, 844, 542, 562, 206, 978]
NAMED_BLACK = [375, 678, 1365, 1691, 1726, 1764, 2831]
ARGON_LIGHT = {  # frames in Argon's lighting table (docs/argon/skyeye-detection-evidence.md section 4.1)
    "work/production/evidence/code66-lighting-holdout-20261031/input/daylight-01.png": "daylight-01",
    "work/production/evidence/code66-lighting-holdout-20261031/input/all-01.png": "all-01",
    "work/production/evidence/code66-lighting-holdout-20261031/input/off-01.png": "off-01",
    "work/production/evidence/o08-current-device-check-20261029/luma-gamma16/250.png": "250",
    "work/production/evidence/code30-live-detect-frame-3496.png": "3496",
    "work/o08/frame978-development-v1/physical-controls/cue-absent/21f8a006a157407da648ffebea8f4d96/455.png": "455",
}


def rel(p): return os.path.relpath(p, ROOT).replace(os.sep, "/")
def jl(p): return [json.loads(l) for l in open(os.path.join(ROOT, p), encoding="utf-8-sig") if l.strip()]
def jload(p): return json.load(open(os.path.join(ROOT, p), encoding="utf-8-sig"))
def is_holdout(path): return "holdout" in path.lower()


def file_hash(path, algo):
    h = hashlib.new(algo)
    with open(os.path.join(ROOT, path), "rb") as f:
        for b in iter(lambda: f.read(1 << 20), b""): h.update(b)
    return h.hexdigest()


def to_table(corners, pts):
    Hm = cv2.getPerspectiveTransform(np.float32(corners), np.float32([[0, 0], [2, 0], [2, 1], [0, 1]]))
    return cv2.perspectiveTransform(np.float32(pts).reshape(-1, 1, 2), Hm).reshape(-1, 2)


def zone(x, y):
    if min(math.hypot(x - px, y - py) for px, py in POCKETS) < 0.11: return "pocket"
    if min(x, 2 - x, y, 1 - y) < 0.075: return "cushion"
    return "open"


def stratum(v):
    if v is None: return "unknown"
    return "dim" if v < 80 else "mid" if v < 110 else "bright"


def main():
    inv = {r["path"]: r for r in csv.DictReader(open(os.path.join(A, "inventory.csv"), encoding="utf-8"))}
    light = {r["sha12"]: r for r in csv.DictReader(open(os.path.join(A, "robust-light.csv")))}
    receipts = jl("work/evidence-audit/receipts.jsonl")

    # Receipt selection identical to score_detections.load(): first usable receipt per
    # pixel SHA-256, replaced only by a later one that carries a vision result.
    by256 = collections.defaultdict(list); sel = {}
    for r in receipts:
        if not r["sha256"]: continue
        by256[r["sha256"]].append(r)
        if len(r["corners"]) != 4: continue
        old = sel.get(r["sha256"])
        if old is None or (r["hasVision"] and not old["hasVision"]): sel[r["sha256"]] = r

    # Split locks and plan.
    locks = collections.defaultdict(set); splits = collections.defaultdict(set); seqids = collections.defaultdict(set)
    for p in SPLIT_LOCKS:
        d = jload(p)
        for s in d.get("sequences", []):
            for h in s.get("pixelSha256") or []:
                h = h.lower(); locks[h].add(SHORT.get(d["corpusId"], d["corpusId"])); splits[h].add(s.get("split")); seqids[h].add(s.get("id"))
    plan = jload(SPLIT_PLAN); plan_sha = file_hash(SPLIT_PLAN, "sha256")
    groups = {g["layoutId"]: g for g in plan["setupGroups"] if g.get("layoutId")}

    rows = {}  # keyed by SHA-256 while building; re-keyed by SHA-1 at the end

    def row_for(h256, path=None):
        r = rows.get(h256)
        if r: return r
        recs = by256.get(h256, [])
        hold = any(is_holdout(x["receipt"]) or is_holdout(x["pixel"]) for x in recs) or (path is not None and is_holdout(path))
        r = dict(sha256=h256, holdout=hold, corpora=set(), names=[], labelSources=[], notes=[])
        rows[h256] = r
        return r

    # 1. Every receipt-bound frame becomes at least an unlabelled reference.
    for h in by256: row_for(h)

    # 2. Reviewed labels already indexed by this review (labels.jsonl).
    labels = {}
    for lab in jl("work/evidence-audit/labels.jsonl"):
        labels[lab["sha256"]] = dict(source=lab["labels"], format="corpus-labels.json (" + lab["space"] + ")", space=lab["space"],
                                     balls=[dict(kind=b["colour"], x=b["x"], y=b["y"], visible=b["vis"] == "Visible") for b in lab["balls"]],
                                     sampleId=lab["sampleId"], tags=lab.get("tags"), corpus="claude-review-labels", dirImages=lab.get("pixelDirImages") or [])
        row_for(lab["sha256"])["corpora"].add("claude-review-labels")

    extra = []  # (sha256, label record) from sources index_labels.py does not read

    # 2a. Warm frame 266: user-reviewed source-pixel centres; kinds from the score file.
    rc = jload(W266 + "/raw-review-frame266/reviewed-centres.json")
    kinds = {l["id"]: l["kind"] for l in jload(W266 + "/warm266-reviewed-spatial-score.json")["labels"]}
    extra.append((rc["sourceSha256"], dict(source=W266 + "/raw-review-frame266/reviewed-centres.json", format="reviewed-centres source-pixels (frame 266)",
                  space="source-px", balls=[dict(kind=kinds[p["id"]], x=p["sourceXpx"], y=p["sourceYpx"], visible=True, id=p["id"]) for p in rc["points"]],
                  sampleId="9a3c2da33ac44bb4a5b6458a3a44a2a7-266", tags=["warm-dim-playable", rc["status"]], corpus="o09-warm266-review")))
    # 2b. Layout03 normal-validation, black-corrected v2 (table-2x1, reviewed: true).
    for n in ("2083", "2263", "957", "962"):
        d = jload("%s/%s/%s.labels.json" % (L03V, n, n))
        if d.get("reviewed") is not True: continue
        for fr in d["frames"]:
            extra.append((fr["pixelSha256"].lower(), dict(source="%s/%s/%s.labels.json" % (L03V, n, n), format="corpus-labels.json v2 named <frame>.labels.json (table-2x1)",
                          space="table-2x1", balls=[dict(kind=b["colour"], x=b["x"], y=b["y"], visible=b["visibility"] == "Visible", id=b.get("id")) for b in fr["balls"]],
                          sampleId=fr["sampleId"], tags=fr.get("tags"), corpus="o09-layout03-validation-v2")))
    # 2c. Restaged opposite-cue positives 1691-1783 (user-reviewed source-pixel centres incl. black).
    d = jload(RESTAGED)
    colour = {"Y": "yellow", "R": "red", "B": "black", "C": "cue"}
    for fr in d["frames"]:
        extra.append((fr["sourceImageSha256"].lower(), dict(source=RESTAGED, format="user-reviewed-source-image-centre-set (source pixels)", space="source-px",
                      balls=[dict(kind=colour[k[0]], x=v["x"], y=v["y"], visible=bool(v["visible"]), id=k) for k, v in fr["reviewedLabels"].items()],
                      sampleId="884a3abb604048379cc148185728df73-%d" % fr["frameId"], tags=[d["status"], d["setupGroup"]], corpus="o08-restaged-opposite-cue-review")))
    # 2d. O01 still regression fixture (table-2x1, reviewed: true; no receipt).
    d = jload(O01)
    for fr in d["frames"]:
        extra.append((fr["pixelSha256"].lower(), dict(source=O01, format="corpus-labels.json v1 (table-2x1)", space="table-2x1",
                      balls=[dict(kind=b["colour"], x=b["x"], y=b["y"], visible=b.get("visibility", "Visible") == "Visible") for b in fr["balls"]],
                      sampleId=fr["sampleId"], tags=["Regression"], corpus="o01-seed-regression")))

    for h, lab in extra:
        r = row_for(h); r["corpora"].add(lab["corpus"])
        if h in labels: r["labelSources"].append(dict(source=lab["source"], format=lab["format"], used=False, reason="labels.jsonl copy preferred"))
        labels.setdefault(h, lab)
    o01_sha = jload(O01)["frames"][0]["pixelSha256"].lower()
    rows[o01_sha]["path"] = "tests/fixtures/poolgame/pixel-table.jpg"
    rows[o01_sha]["notes"].append("No receipt; calibration 'manual-pixel-table-2026-10-03' has no stored corners; labels are already table-2x1.")

    # 3. Frame 1365: assistant draft only, black unresolved -> not truth.
    d = jload(D1365); r = row_for(d["source"]["sha256"].lower())
    r["labelSources"].append(dict(source=D1365, format="manual-labels draft (source pixels)", used=False, reason=d["status"] + "; black centre unresolved"))

    # 4. Argon per-frame rows and lighting-table frames.
    argon = jload(ARGON); argon_rows = argon["per_frame_details"]
    by_sample = {}
    for r0 in receipts:
        if r0["operationId"] and r0["frameId"] is not None:
            by_sample.setdefault("%s-%s" % (r0["operationId"].replace("-", ""), r0["frameId"]), r0["sha256"])
    sample_to_sha = dict(by_sample)
    for h, lab in list(labels.items()): sample_to_sha.setdefault(lab["sampleId"], h)
    argon_map = []
    for i, a in enumerate(argon_rows):
        h = sample_to_sha.get(a["sampleId"])
        argon_map.append(dict(row=i, corpus=a["corpus"], sampleId=a["sampleId"], model=a["model"], sha256=h))
        if h: row_for(h)["corpora"].add("argon-aggregated-benchmark")
    for p, name in ARGON_LIGHT.items():
        hold = is_holdout(p)
        h = None
        for r0 in receipts:
            if r0["pixel"] == p and r0["sha256"]: h = r0["sha256"]; break
        if h is None and not hold and os.path.exists(os.path.join(ROOT, p)): h = file_hash(p, "sha256")
        if h is None:  # holdout without receipt: keep id only, keyed by inventory SHA-1
            h = "sha1:" + inv[p]["sha1"] if p in inv else "missing:" + p
        r = row_for(h, p); r["corpora"].add("argon-lighting-table"); r["names"].append("argon-light:" + name)
        r.setdefault("path", p)

    # 5. Fill each row.
    out = []
    for h, r in rows.items():
        recs = by256.get(h, [])
        s = sel.get(h)
        frame_ids = sorted({x["frameId"] for x in recs if x["frameId"] is not None})
        sample_ids = sorted({"%s-%s" % (x["operationId"].replace("-", ""), x["frameId"]) for x in recs if x["operationId"]})
        if r["holdout"]:
            sha1 = inv.get(r.get("path") or (recs[0]["pixel"] if recs else ""), {}).get("sha1") or (h[5:] if h.startswith("sha1:") else None)
            out.append(dict(sha1=sha1, sha256=None if ":" in h else h, frameIds=frame_ids, sampleIds=sample_ids, role="holdout-excluded", split="Holdout",
                            corpora=sorted(r["corpora"] | {"holdout-folder"}), names=r["names"],
                            notes=["Path contains 'holdout'. Recorded by id and hash only; pixels, labels and light not read."]))
            continue
        # path: selected receipt pixel if present, else first existing receipt pixel
        path = r.get("path")
        if path is None and s is not None and s["pixelExists"]: path = s["pixel"]
        if path is None:
            ex = [x["pixel"] for x in recs if x["pixelExists"]]
            path = sorted(ex, key=lambda p: (("source-receipts" in p), len(p)))[0] if ex else None
        lab = labels.get(h)
        if path is None and lab:
            for cand in lab.get("dirImages", []):
                if file_hash(cand, "sha256") == h: path = cand; break
        sha1 = inv[path]["sha1"] if path in inv else (file_hash(path, "sha1") if path and os.path.exists(os.path.join(ROOT, path)) else None)
        all_paths = sorted({x["pixel"] for x in recs if x["pixelExists"]})
        sha1s = {inv[p]["sha1"] for p in all_paths if p in inv}
        corners = s["corners"] if s else None
        corner_sets = {json.dumps(x["corners"]) for x in recs if len(x["corners"]) == 4}
        lt = light.get(h[:12]); tv = int(lt["tableV"]) if lt else None
        row = dict(sha1=sha1, sha256=h, path=path, otherPaths=[p for p in all_paths if p != path][:20], nOtherPaths=max(0, len(all_paths) - 1),
                   frameIds=frame_ids,
                   sampleIds=sample_ids or ([lab["sampleId"]] if lab else []),
                   calibration=dict(corners=corners, calibrationId=s["calibration"] if s else None, receipt=s["receipt"] if s else None,
                                    cornerSetsAcrossReceipts=len(corner_sets)),
                   light=dict(tableV=tv, stratum=stratum(tv), source="work/evidence-audit/robust-light.csv" if lt else None),
                   pixelPresent=bool(path and os.path.exists(os.path.join(ROOT, path))))
        if lab and row["pixelPresent"]: row["pixelSha256Verified"] = file_hash(path, "sha256") == h
        if len(sha1s) > 1: r["notes"].append("Receipt pixel copies disagree on SHA-1: %s" % sorted(sha1s))
        # labels
        if lab:
            if lab["space"] == "source-px":
                if not corners: raise SystemExit("no corners for source-pixel labels " + h)
                tp = to_table(corners, [[b["x"] / 1920.0, b["y"] / 1080.0] for b in lab["balls"]])
            elif lab["space"] == "image-normalized":
                tp = to_table(corners, [[b["x"], b["y"]] for b in lab["balls"]])
            else:
                tp = [[b["x"], b["y"]] for b in lab["balls"]]
            balls = [dict(kind=b["kind"], x=float(p[0]), y=float(p[1]), visible=b["visible"], zone=zone(float(p[0]), float(p[1]))) for b, p in zip(lab["balls"], tp)]
            vis = collections.Counter(b["kind"] for b in balls if b["visible"])
            absent = sorted(k for k in FULL if vis[k] < FULL[k])
            row["labels"] = dict(source=lab["source"], corpus=lab["corpus"], format=lab["format"], originalSpace=lab["space"], space="table-2x1",
                                 reviewed=True, nVisible=sum(vis.values()), absentKinds=absent, balls=balls)
            row["role"] = "empty-control" if absent and set(absent) <= {"black", "cue"} else "positive"
            row["blackVisible"] = vis["black"] > 0
            row["zoneTags"] = sorted({"%s:%s" % (b["kind"], b["zone"]) for b in balls if b["visible"] and b["kind"] in ("black", "cue")}
                                     | {"absent-%s:%s" % (b["kind"], b["zone"]) for b in balls if not b["visible"]}
                                     | {"any:" + b["zone"] for b in balls if b["visible"]})
        else:
            row["labels"] = None
            row["role"] = "unlabelled-reference"
            row["zoneTags"] = []
        row["labelSources"] = r["labelSources"]
        # split
        sp = {x for x in splits.get(h, set()) if x}
        if path and "work/o09/development-v3/" in path: sp.add("Development")
        if lab and lab.get("tags") and "Development" in (lab["tags"] or []): sp.add("Development")
        if lab and lab["corpus"] == "o08-restaged-opposite-cue-review": sp.add("Development")
        row["split"] = "/".join(sorted(sp)) if sp else "unknown"
        if row["split"] == "Development": row["notes"] = r["notes"] + ["Development: tuning data."]
        else: row["notes"] = r["notes"]
        # setup group from the frozen v3 plan
        grp = None
        if path and "/development-v3/raw/" in path: grp = groups.get(path.split("/development-v3/raw/")[1].split("/")[0])
        if h == rc["sourceSha256"]: grp = groups.get("warm-train-layout-01")
        if grp: row["setupGroup"] = dict(id=grp["setupGroupId"], role=grp["role"], lightingStratum=grp["lightingStratum"], planSha256=plan_sha)
        row["corpora"] = sorted(r["corpora"] | locks.get(h, set()) | ({"project-o09-development-v3"} if (path and "/development-v3/" in path) or grp else set()))
        row["splitLockSequences"] = sorted(seqids.get(h, set()))
        row["names"] = r["names"]
        fid = set(row["frameIds"])
        for n in NAMED_CONTROLS:
            if n in fid: row["names"].append("plan-empty-control:%d" % n)
        for n in NAMED_BLACK:
            if n in fid: row["names"].append("plan-black-positive:%d" % n)
        if s:
            row["storedResult"] = dict(receipt=s["receipt"], hasVision=s["hasVision"], algorithm=s["algorithm"], configuration=s["configuration"],
                                       capturedAt=s["capturedAt"], balls=s["balls"] if s["hasVision"] else None, uncertain=s["uncertain"],
                                       distinctResultsAcrossReceipts=len({json.dumps(x["balls"]) for x in recs if x["hasVision"]}))
        else:
            row["storedResult"] = None
        out.append(row)

    # A named frame id can occur in several unrelated operations; keep the plan tag only on the
    # labelled or Development copy that the plan means.
    for n in NAMED_CONTROLS + NAMED_BLACK:
        tagged = [r for r in out if any(x.endswith(":%d" % n) and x.startswith("plan-") for x in r.get("names", []))]
        if len(tagged) > 1:
            keep = [r for r in tagged if r["role"] != "unlabelled-reference"] or [r for r in tagged if r.get("split") == "Development"] or tagged[:1]
            for r in tagged:
                if r not in keep: r["names"] = [x for x in r["names"] if not (x.startswith("plan-") and x.endswith(":%d" % n))]

    # Holdout receipts can claim different SHA-256 values for one file; merge them by SHA-1.
    merged = {}
    for r in [r for r in out if r["role"] == "holdout-excluded"]:
        k = r["sha1"] or r["sha256"]
        if k in merged:
            m = merged[k]; m["frameIds"] = sorted(set(m["frameIds"]) | set(r["frameIds"])); m["sampleIds"] = sorted(set(m["sampleIds"]) | set(r["sampleIds"]))
            m.setdefault("claimedSha256", [m["sha256"]]).append(r["sha256"]); m["names"] += r["names"]; out.remove(r)
        else: merged[k] = r
    # One row per SHA-1 (rows whose pixel file is missing are keyed by the receipt SHA-256).
    for r in out: r["key"] = r["sha1"] or "sha256:" + str(r["sha256"])
    seen = collections.Counter(r["key"] for r in out)
    dup = [k for k, v in seen.items() if v > 1]
    if dup:
        for r in out:
            if r["key"] in dup: print("DUP", r["sha1"], r.get("sha256"), r.get("path"), r.get("role"), r.get("frameIds"), r.get("corpora"))
        raise SystemExit("duplicate SHA-1 rows: %s" % dup)
    out.sort(key=lambda r: ({"positive": 0, "empty-control": 1, "unlabelled-reference": 2, "holdout-excluded": 3}[r["role"]], r.get("path") or "", r["sha1"] or ""))

    argon_ids = collections.Counter(a["sampleId"] for a in argon_map)
    summary = dict(rows=len(out), byRole=dict(collections.Counter(r["role"] for r in out)),
                   labelledByStratum=dict(collections.Counter(r["light"]["stratum"] for r in out if r["role"] in ("positive", "empty-control"))),
                   allByStratum=dict(collections.Counter(r["light"]["stratum"] for r in out if "light" in r)),
                   labelledBySource=dict(collections.Counter(r["labels"]["corpus"] for r in out if r.get("labels"))),
                   bySplit=dict(collections.Counter(r["split"] for r in out)))
    doc = dict(schemaVersion=1, id="skyeye-detection-corpus-v1",
               description="One row per unique pixel file (SHA-1). Labels are reviewed only, normalised to table-2x1 (0..2 x 0..1). Built by tools/evidence/build_corpus_manifest.py.",
               rules=["Holdout: rows marked holdout-excluded carry ids only; never open their pixels or labels.",
                      "Development rows are tuning data.",
                      "Light strata from table V: dim <80, mid 80-109, bright >=110.",
                      "Zones: pocket = within 0.11 of a pocket point; cushion = within 0.075 of a rail; else open.",
                      "role empty-control = labelled frame whose only missing balls are the black and/or cue."],
               inputs=dict(splitPlan=dict(path=SPLIT_PLAN, sha256=plan_sha), splitLocks=SPLIT_LOCKS, argon=ARGON),
               summary=summary,
               argonMapping=dict(rows=len(argon_map), uniqueSampleIds=len(argon_ids), repeatedSampleIds=sorted(k for k, v in argon_ids.items() if v > 1),
                                 unmapped=[a["sampleId"] for a in argon_map if not a["sha256"]],
                                 summaryOnlyFramesNotInPerFrameRows=["1587", "31", "266 (warm_train_266_summary.comparisons is empty)", "post-shot 13-ball frame"],
                                 rowsDetail=argon_map),
               frames=out)
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8", newline="\n") as f: json.dump(doc, f, indent=1)
    print(json.dumps(summary, indent=1))
    print("argon rows", len(argon_map), "unique ids", len(argon_ids), "unmapped", doc["argonMapping"]["unmapped"])


if __name__ == "__main__":
    main()
