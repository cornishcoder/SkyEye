"""Compare desktop detector output (T2 `detect` JSONL) with the production results stored in
the corpus manifest at capture time, and score both on the same frames.

    python tools/evidence/compare_desktop_detections.py --manifest tests/fixtures/detection-corpus/manifest.json \
        --detections work/t2/detect-labelled-production.jsonl [--out work/t2/compare.json]

Per frame with both a stored result and a desktop result: ball counts by kind, and one-to-one
greedy matching of stored to desktop balls within 0.00625 table units (5 px at width 800).
Stored results came from the phone's live pipeline (camera bitmap, OpenCV 4.14 arm64, the
analyser and settings of that build); desktop reads the saved pixel file. A difference is
therefore not by itself a desktop error: check `algorithmMatchesReceipt` and `settingsMatchReceipt`.
Scores use score_detections.py unchanged, restricted to frames present in both.
"""
import argparse, collections, json, math, os, sys

sys.path.insert(0, os.path.dirname(__file__))
import score_detections as sd  # noqa: E402

PX = 1 / 800.0
MATCH = 5 * PX


def match(a, b):
    pairs = sorted((math.hypot(p[1] - q[1], p[2] - q[2]), i, j) for i, p in enumerate(a) for j, q in enumerate(b))
    ui, uj, out = set(), set(), []
    for d, i, j in pairs:
        if d > MATCH: break
        if i in ui or j in uj: continue
        ui.add(i); uj.add(j); out.append((i, j, d))
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--manifest", required=True); ap.add_argument("--detections", required=True); ap.add_argument("--out")
    a = ap.parse_args()
    rows = {r["sha256"]: r for r in json.load(open(a.manifest, encoding="utf-8"))["frames"]}
    desk = [json.loads(l) for l in open(a.detections, encoding="utf-8")]
    desk = {d["sha256"]: d for d in desk if "balls" in d}

    per, totals = [], collections.Counter()
    for h, d in desk.items():
        sr = (rows[h].get("storedResult") or {})
        if not sr.get("hasVision"): continue
        s = sr["balls"]; m = match(s, d["balls"])
        kinds_equal = collections.Counter(b[0] for b in s) == collections.Counter(b[0] for b in d["balls"])
        same_kind = sum(s[i][0] == d["balls"][j][0] for i, j, _ in m)
        identical = kinds_equal and len(m) == len(s) == len(d["balls"]) and same_kind == len(m)
        maxd = max((x for _, _, x in m), default=0) / PX
        algo = d.get("algorithmMatchesReceipt"); sett = d.get("settingsMatchReceipt")
        totals["frames"] += 1; totals["identicalBalls"] += identical
        totals["sameBuild"] += bool(algo and sett); totals["identicalSameBuild"] += bool(identical and algo and sett)
        per.append(dict(sha12=h[:12], path=d["path"], ext=os.path.splitext(d["path"])[1].lower(), names=d.get("names"),
                        stored=len(s), desktop=len(d["balls"]), matched=len(m), kindsEqual=kinds_equal,
                        identicalBalls=identical, maxCentreDeltaPx=round(maxd, 2),
                        storedUncertain=sr.get("uncertain"), desktopUncertain=d.get("uncertain"),
                        algorithmMatchesReceipt=algo, settingsMatchReceipt=sett,
                        storedOnly=[s[i] for i in range(len(s)) if i not in {i for i, _, _ in m}],
                        desktopOnly=[d["balls"][j] for j in range(len(d["balls"])) if j not in {j for _, j, _ in m}]))

    data = sd.load_manifest(a.manifest)
    common = {p["sha12"] for p in per}
    rec, labels, met = data
    labels_common = [l for l in labels if l["sha256"][:12] in common]
    stored_dets = {h: r["balls"] for h, r in rec.items() if r["hasVision"] and h[:12] in common}
    desk_dets = {h: d["balls"] for h, d in desk.items() if h[:12] in common}
    s_stored = sd.score(stored_dets, "stored-at-capture (common frames)", quiet=True, data=(rec, labels_common, met))
    s_desk = sd.score(desk_dets, "desktop (common frames)", quiet=True, data=(rec, labels_common, met))

    print("Desktop vs stored at capture, frames with both: %d" % totals["frames"])
    print("  identical balls (count, kind, centres within 5 px): %d / %d" % (totals["identicalBalls"], totals["frames"]))
    print("  frames whose receipt has the same algorithm and settings: %d; identical among them: %d" % (totals["sameBuild"], totals["identicalSameBuild"]))
    for p in sorted(per, key=lambda p: (p["identicalBalls"], p["sha12"])):
        if not p["identicalBalls"]:
            print("  DIFF %s %s %s stored %d desktop %d matched %d max %.2f px algo=%s settings=%s stored-only %s desktop-only %s" % (
                p["sha12"], p["ext"], ",".join(map(str, p["names"] or [])), p["stored"], p["desktop"], p["matched"], p["maxCentreDeltaPx"],
                p["algorithmMatchesReceipt"], p["settingsMatchReceipt"], p["storedOnly"], p["desktopOnly"]))
    print("  max centre delta on identical frames: %.2f px" % max((p["maxCentreDeltaPx"] for p in per if p["identicalBalls"]), default=0))
    for s in (s_stored, s_desk):
        al = s["strata"].get("all", {}); bl = s["strata"].get("colour:black", {})
        print("%-36s frames %d, complete %d, balls %d/%d, fp %d, colour %.3f, black %d/%d" % (
            s["name"], s["frames"], s["framesComplete"], al.get("tp", 0), al.get("gt", 0), al.get("fp", 0),
            al.get("colourAccuracy") or 0, bl.get("tp", 0), bl.get("gt", 0)))
    if a.out:
        json.dump(dict(totals=totals, perFrame=per, scoreStored=s_stored, scoreDesktop=s_desk), open(a.out, "w"), indent=1)


if __name__ == "__main__":
    main()
