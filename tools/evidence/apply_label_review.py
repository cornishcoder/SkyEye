#!/usr/bin/env python3
"""T9: turn Richard's reply to the label-review sheets into reviewed labels.

Reads the drafts written by build_label_review_sheets.py and a plain-text reply
(grammar in work/evidence-audit/t9/README.md), and writes reviewed documents.
A frame or clip becomes reviewed ONLY if the reply has an explicit "<item> ok"
line; corrections without "ok" are reported as incomplete and nothing is
written for that item. Drafts are never modified.

    python3 tools/evidence/apply_label_review.py REPLY.txt [--out DIR] [--annotator NAME] [--dry-run]

Default --out is work/evidence-audit/t9/reviewed/ (index_labels.py skips
work/evidence-audit/, so nothing there enters labels.jsonl or the manifest until
someone deliberately publishes it; see the README).
Sheet 1 output: <out>/sheet1/<item>_<frame>/labels.json (sequence labels schema 2,
table-2x1, reviewed true). Sheet 2 output: <out>/sheet2/<item>_<clip>.labels.json
(t9-clip-labels v1: intervals in the project's interval shape plus pots and
rest positions). A report is written to <out>/apply-report.json.
"""
import argparse, csv, datetime, json, os, re, sys

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
T9 = os.path.join(ROOT, "work", "evidence-audit", "t9")
COLOURS = {"red": "red", "yellow": "yellow", "cue": "cue", "white": "cue", "black": "black", "r": "red", "y": "yellow", "c": "cue", "b": "black"}
VIS = {"occluded": "Occluded", "uncertain": "Uncertain", "visible": "Visible"}
POCKETS = {"TL", "TM", "TR", "BL", "BM", "BR"}
NUM = r"-?\d+(?:\.\d+)?"
TNUM = r"\d+(?:\.\d+)?"   # times: never signed, so "2.5-5.2" reads as a range

def load_index(sheet):
    p = os.path.join(T9, sheet, "index.csv")
    return {r["item"]: r for r in csv.DictReader(open(p, encoding="utf-8"))} if os.path.exists(p) else {}

def coords(s):
    m = re.findall(NUM, s)
    if len(m) < 2: raise ValueError("need x y table coordinates")
    x, y = float(m[0]), float(m[1])
    if not (-0.1 <= x <= 2.1 and -0.1 <= y <= 1.1): raise ValueError("coordinates outside table-2x1 (x 0-2, y 0-1): %s" % s)
    return x, y

def tsec(s):
    v = float(s)
    if v < 0: raise ValueError("negative time")
    return v

def parse(lines, s1, s2):
    """returns {item: [commands]} and errors. Segments split by ';' inherit the previous item."""
    by_frame = {r["frameId"]: k for k, r in s1.items()}
    out, errs = {}, []
    for ln, raw in enumerate(lines, 1):
        line = raw.split("#", 1)[0].strip()
        if not line: continue
        cur = None
        for seg in [x.strip() for x in line.split(";") if x.strip()]:
            seg = re.sub(r"^frame\s+(\d+)\s*:?", lambda m: (by_frame.get(m.group(1)) or "Fxx-" + m.group(1)) + " ", seg, flags=re.I)
            m = re.match(r"^([FC])0*(\d+)\b\s*:?\s*(.*)$", seg, re.I)
            if m:
                cur = "%s%02d" % (m.group(1).upper(), int(m.group(2))); rest = m.group(3).strip()
            elif cur:
                rest = seg
            else:
                errs.append("line %d: no item (F01.. or C01..): %s" % (ln, raw.strip())); continue
            if cur not in s1 and cur not in s2:
                errs.append("line %d: unknown item %s" % (ln, cur)); continue
            out.setdefault(cur, []).append((ln, rest))
    return out, errs

def apply_frame(item, cmds, row, errs):
    draft = json.load(open(os.path.join(ROOT, row["draft"]), encoding="utf-8"))
    fr = draft["frames"][0]
    balls = {int(b["id"].split("-")[1]): dict(b) for b in fr["balls"]}
    added, ok, skip, notes, changes = [], False, None, [], []
    for ln, c in cmds:
        lc = c.lower()
        try:
            if lc in ("ok", "all ok", "done"): ok = True
            elif lc.startswith("skip"): skip = c[4:].strip() or "skipped"
            elif lc.startswith("note"): notes.append(c[4:].strip(" :"))
            elif lc.startswith("colours") or lc.startswith("colors"):
                words = lc.split()[1:]
                if len(words) != len(balls) or any(w not in COLOURS for w in words):
                    raise ValueError("'colours' needs exactly %d colours, one per draft number in order" % len(balls))
                for n, w in zip(sorted(balls), words):
                    if balls[n]["colour"] != COLOURS[w]: changes.append("draft %d colour %s -> %s" % (n, balls[n]["colour"], COLOURS[w])); balls[n]["colour"] = COLOURS[w]
            elif lc.startswith("add"):
                m = re.match(r"add\s+(\w+)\s+(.*)$", c, re.I)
                col = COLOURS.get(m.group(1).lower()) if m else None
                if not col: raise ValueError("add needs a colour (red, yellow, cue, black)")
                x, y = coords(m.group(2)); vis = next((VIS[w] for w in VIS if w in m.group(2).lower()), "Visible")
                added.append(dict(colour=col, x=x, y=y, visibility=vis)); changes.append("added %s at (%.3f, %.3f)" % (col, x, y))
            else:
                m = re.match(r"(?:ball\s*)?(\d+)\s*(.*)$", c, re.I)
                if not m: raise ValueError("cannot read: %s" % c)
                n = int(m.group(1)); what = m.group(2).strip().lower()
                if n not in balls: raise ValueError("no draft ball %d" % n)
                b = balls[n]
                if what in ("ok", "right", "correct", ""): b["_confirmed"] = True
                elif what in ("wrong", "delete", "none", "no ball", "remove", "not a ball"):
                    balls.pop(n); changes.append("removed draft %d" % n)
                elif what.startswith("is ") or what in COLOURS:
                    col = COLOURS.get(what.replace("is ", "").strip())
                    if not col: raise ValueError("unknown colour in: %s" % c)
                    changes.append("draft %d colour %s -> %s" % (n, b["colour"], col)); b["colour"] = col
                elif what.startswith("at ") or what.startswith("move") or re.match(NUM, what):
                    x, y = coords(what); changes.append("draft %d moved (%.3f, %.3f) -> (%.3f, %.3f)" % (n, b["x"], b["y"], x, y)); b["x"], b["y"] = x, y
                elif what in VIS:
                    b["visibility"] = VIS[what]; changes.append("draft %d visibility %s" % (n, VIS[what]))
                else: raise ValueError("cannot read: %s" % c)
        except Exception as e:
            errs.append("%s line %d: %s" % (item, ln, e))
    if skip: return None, "skipped: " + skip
    if not ok: return None, "incomplete: no '%s ok' line" % item
    out_balls, counts = [], {}
    for n in sorted(balls):
        b = balls[n]; col = b["colour"]
        if col == "unknown": errs.append("%s: draft %d is still colour 'unknown'; give its colour (e.g. '%s %d is red') or delete it" % (item, n, item, n)); return None, "incomplete: unknown colour"
        counts[col] = counts.get(col, 0) + 1
        out_balls.append(dict(id="%s-%02d" % (col, counts[col]), colour=col, x=b["x"], y=b["y"], visibility=b.get("visibility", "Visible")))
    for a in added:
        counts[a["colour"]] = counts.get(a["colour"], 0) + 1
        out_balls.append(dict(id="%s-%02d" % (a["colour"], counts[a["colour"]]), colour=a["colour"], x=a["x"], y=a["y"], visibility=a["visibility"]))
    return out_balls, dict(changes=changes, notes=notes, counts=counts)

def apply_clip(item, cmds, row, errs):
    d = json.load(open(os.path.join(ROOT, row["draft"]), encoding="utf-8"))
    ivs = {iv["id"].upper(): dict(iv) for iv in d["intervals"]}
    rests = {r["id"].upper(): dict(r) for r in d["restPositions"]}
    new_iv, pots, added, ok, skip, notes, changes = [], [], [], False, None, [], []
    for ln, c in cmds:
        lc = c.lower().strip()
        try:
            if lc in ("ok", "all ok", "done"): ok = True
            elif lc.startswith("skip"): skip = c[4:].strip() or "skipped"
            elif lc.startswith("note"): notes.append(c[4:].strip(" :"))
            elif re.match(r"^i\d+\b", lc):
                m = re.match(r"^(i\d+)\s*(.*)$", lc); k = m.group(1).upper(); what = m.group(2)
                if k not in ivs: raise ValueError("no draft interval %s" % k)
                if what in ("ok", ""): ivs[k]["_confirmed"] = True
                elif what in ("wrong", "delete", "none"): ivs.pop(k); changes.append("removed %s" % k)
                else:
                    t = re.findall(TNUM, what)
                    if len(t) != 2: raise ValueError("retime as '%s %s a-b' (seconds)" % (item, k))
                    a, b = tsec(t[0]), tsec(t[1])
                    if b < a: raise ValueError("end before start")
                    ivs[k]["fromUs"], ivs[k]["toUs"] = int(a * 1e6), int(b * 1e6); changes.append("%s retimed %.1f-%.1f s" % (k, a, b))
            elif re.match(r"^(shot|settled|moving|rolling)\b", lc):
                m = re.match(r"^(\w+)\s+(.*)$", lc); kind = "moving" if m.group(1) == "rolling" else m.group(1)
                t = [tsec(x) for x in re.findall(TNUM, m.group(2))]
                if not t: raise ValueError("needs a time")
                a, b = (t[0], t[0] + 0.4) if len(t) == 1 else (t[0], t[1])
                new_iv.append(dict(kind=kind, fromUs=int(a * 1e6), toUs=int(b * 1e6))); changes.append("added %s %.1f-%.1f s" % (kind, a, b))
            elif lc.startswith("pot"):
                words = lc.split()
                col = COLOURS.get(words[1]) if len(words) > 1 else None
                if not col: raise ValueError("pot needs a colour")
                t = re.findall(TNUM, lc); pk = next((w.upper() for w in words if w.upper() in POCKETS), None)
                pots.append(dict(colour=col, atS=float(t[0]) if t else None, pocket=pk)); changes.append("pot %s" % col)
            elif re.match(r"^s\d+\b", lc):
                m = re.match(r"^(s\d+)\s*(.*)$", lc); sh = m.group(1); what = m.group(2).strip()
                if what in ("ok", ""):
                    for r in rests.values():
                        if r["shot"] == sh: r["_confirmed"] = True
                elif what.startswith("add"):
                    mm = re.match(r"add\s+(\w+)\s+(.*)$", what); col = COLOURS.get(mm.group(1)) if mm else None
                    if not col: raise ValueError("add needs a colour")
                    x, y = coords(mm.group(2)); added.append(dict(shot=sh, colour=col, x=x, y=y, t4Class="arrival", source="reviewer"))
                    changes.append("%s added %s rest (%.3f, %.3f)" % (sh, col, x, y))
                else:
                    mm = re.match(r"^(r\d+)\s*(.*)$", what)
                    if not mm: raise ValueError("cannot read: %s" % c)
                    k = ("%s-%s" % (sh, mm.group(1))).upper(); w2 = mm.group(2).strip()
                    if k not in rests: raise ValueError("no draft region %s" % k)
                    r = rests[k]
                    if w2 in ("ok", ""): r["_confirmed"] = True
                    elif w2 in ("wrong", "delete", "none", "shadow", "hand"): rests.pop(k); changes.append("removed %s (%s)" % (k, w2))
                    elif w2 in ("arrival", "departure"): r["t4Class"] = w2; changes.append("%s is %s" % (k, w2))
                    elif w2.startswith("is ") or w2 in COLOURS:
                        col = COLOURS.get(w2.replace("is ", "").strip())
                        if not col: raise ValueError("unknown colour")
                        r["colour"] = col; changes.append("%s colour %s" % (k, col))
                    elif w2.startswith("at") or re.match(NUM, w2):
                        r["x"], r["y"] = coords(w2); changes.append("%s moved" % k)
                    else:
                        # combined forms like "red arrival"
                        parts = w2.split(); done = False
                        for p in parts:
                            if p in COLOURS: r["colour"] = COLOURS[p]; done = True
                            if p in ("arrival", "departure"): r["t4Class"] = p; done = True
                        if not done: raise ValueError("cannot read: %s" % c)
                        changes.append("%s %s" % (k, w2))
            else: raise ValueError("cannot read: %s" % c)
        except Exception as e:
            errs.append("%s line %d: %s" % (item, ln, e))
    if skip: return None, "skipped: " + skip
    if not ok: return None, "incomplete: no '%s ok' line" % item
    intervals = [dict(kind=iv["kind"], fromUs=iv["fromUs"], toUs=iv["toUs"], certainty="confirmed", ballIds=[], notes="draft %s confirmed" % iv["id"] if not any(iv["id"] in ch for ch in changes) else "draft %s corrected by reviewer" % iv["id"]) for iv in ivs.values()]
    intervals += [dict(kind=iv["kind"], fromUs=iv["fromUs"], toUs=iv["toUs"], certainty="confirmed", ballIds=[], notes="added by reviewer") for iv in new_iv]
    for p in pots:
        if p["atS"] is not None: intervals.append(dict(kind="pot", fromUs=int(p["atS"] * 1e6), toUs=int(p["atS"] * 1e6), certainty="confirmed", ballIds=[], notes="%s pot%s" % (p["colour"], (" in " + p["pocket"]) if p["pocket"] else "")))
    intervals.sort(key=lambda i: (i["fromUs"], i["kind"]))
    rp = [dict(id=r["id"], shot=r["shot"], coordinateSpace="table-2x1", x=r["x"], y=r["y"], colour=r.get("colour", "unknown"), change=r["t4Class"], zone=r.get("zone"), source="t4-draft-confirmed") for r in rests.values()]
    rp += [dict(id="%s-add%d" % (a["shot"], i + 1), shot=a["shot"], coordinateSpace="table-2x1", x=a["x"], y=a["y"], colour=a["colour"], change="arrival", zone=None, source="reviewer") for i, a in enumerate(added)]
    return dict(intervals=intervals, pots=pots, restPositions=rp), dict(changes=changes, notes=notes)

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("reply"); ap.add_argument("--out", default=os.path.join(T9, "reviewed"))
    ap.add_argument("--annotator", default="Richard"); ap.add_argument("--dry-run", action="store_true")
    a = ap.parse_args()
    out = os.path.abspath(a.out)
    if os.path.commonpath([out, os.path.join(T9, "sheet1")]) == os.path.join(T9, "sheet1") or os.path.commonpath([out, os.path.join(T9, "sheet2")]) == os.path.join(T9, "sheet2"):
        sys.exit("refusing to write inside the draft sheets")
    s1, s2 = load_index("sheet1"), load_index("sheet2")
    lines = open(a.reply, encoding="utf-8-sig").read().splitlines()
    cmds, errs = parse(lines, s1, s2)
    today = datetime.date.today().isoformat(); src = os.path.basename(a.reply)
    report = dict(reply=os.path.abspath(a.reply), annotator=a.annotator, date=today, reviewed=[], incomplete={}, errors=errs, untouched=[])
    for item in sorted(set(s1) | set(s2)):
        if item not in cmds: report["untouched"].append(item); continue
        if item in s1:
            row = s1[item]; e0 = len(errs)
            balls, info = apply_frame(item, cmds[item], row, errs)
            if balls is None or len(errs) > e0: report["incomplete"][item] = info if balls is None else "errors in reply lines"; continue
            draft = json.load(open(os.path.join(ROOT, row["draft"]), encoding="utf-8"))
            fr = draft["frames"][0]
            doc = dict(schemaVersion=2, sequenceId=draft["sequenceId"].replace("t9-sheet1-", "t9-reviewed-"), annotator="%s (T9 reply %s, %s); review of assistant draft from the stored at-capture detector result" % (a.annotator, src, today),
                       calibrationVersion=draft["calibrationVersion"], reviewed=True, intervals=[],
                       frames=[dict(sampleId=fr["sampleId"], pixelSha256=fr["pixelSha256"], coordinateSpace="table-2x1", balls=balls,
                                    notes="T9 sheet 1 %s. Reviewer confirmed the displayed draft centres with these corrections: %s. Review was conditioned on the draft; added or moved centres were read off a 0.25 grid / 0.05 crop ticks (about 0.01 table units). Inventory: %s.%s" % (
                                        item, "; ".join(info["changes"]) or "none", ", ".join("%s %d" % kv for kv in sorted(info["counts"].items())), (" Reviewer notes: " + " | ".join(info["notes"])) if info["notes"] else ""),
                                    tags=[t for t in fr.get("tags", []) if t != "t9-draft"] + ["t9-reviewed", "user-reviewed-complete-visible-inventory"])])
            path = os.path.join(out, "sheet1", "%s_%s" % (item, row["frameId"]), "labels.json")
        else:
            row = s2[item]; e0 = len(errs)
            body, info = apply_clip(item, cmds[item], row, errs)
            if body is None or len(errs) > e0: report["incomplete"][item] = info if body is None else "errors in reply lines"; continue
            draft = json.load(open(os.path.join(ROOT, row["draft"]), encoding="utf-8"))
            doc = dict(format="t9-clip-labels v1", clip=draft["clip"], clipId=draft["clipId"], item=item, timeDomain="ClipMicroseconds", reviewed=True,
                       annotator="%s (T9 reply %s, %s); review of assistant draft from T4 motion and change regions" % (a.annotator, src, today),
                       calibration=draft["calibration"], notes="Corrections: %s.%s" % ("; ".join(info["changes"]) or "none", (" Reviewer notes: " + " | ".join(info["notes"])) if info["notes"] else ""), **body)
            path = os.path.join(out, "sheet2", "%s_%s.labels.json" % (item, row["clipId"]))
        report["reviewed"].append(dict(item=item, path=path))
        if not a.dry_run:
            os.makedirs(os.path.dirname(path), exist_ok=True)
            json.dump(doc, open(path, "w", encoding="utf-8", newline="\n"), indent=2)
    if not a.dry_run:
        os.makedirs(out, exist_ok=True)
        json.dump(report, open(os.path.join(out, "apply-report.json"), "w", encoding="utf-8", newline="\n"), indent=2)
    print("reviewed %d, incomplete %d, errors %d, untouched %d" % (len(report["reviewed"]), len(report["incomplete"]), len(errs), len(report["untouched"])))
    for k, v in report["incomplete"].items(): print("  incomplete", k, v)
    for e in errs: print("  error", e)
    return 1 if errs else 0

if __name__ == "__main__":
    sys.exit(main())
