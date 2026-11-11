#!/usr/bin/env python3
"""T3: replay saved Live Detect logs through a Python port of the Live Detect reducer.

THIS IS A PYTHON PORT, NOT THE PRODUCTION C#. It was written by reading the
source at the working-tree HEAD on 11 November 2026 and has not been compiled
against or tested alongside the C#. Ported:

  * src/SkyEye.Core/Analysis/StableTableChangeTracker.cs  (Observe, IsUsable,
    AccumulateProbe, RequiredStableSamples = 3)
  * src/SkyEye.Companion.Hybrid/LiveDetectCountCandidateTracker.cs
    (LiveDetectCountCandidateTracker: 3 samples, 5 s confirmation gap)
  * src/SkyEye.Companion.Hybrid/LiveDetectTableEventTracker.cs  (Observe:
    eligibility, activity, movement, settled events, check outcome)
  * SettledLayoutReconciler.Compare / SameLayout / PositionsCompatible in
    src/SkyEye.GameState/SettledStateContracts.cs (MovementThreshold .018,
    StableLayoutTolerance .010)
  * the sample construction in src/SkyEye.Companion.Hybrid/ShellLiveDetect.cs
    (~lines 310-332): counts from logged balls, layout = ball x/2, y and empty
    if any unknown ball; MovementObserved = spatiallyCompatible == false;
    PackedRack = packedRackEvidence.

Not ported / approximations (see docs/claude/t3-live-detect-replay.md):
  * EvidenceSource includes TableAnalysisSnapshot.Generation, which is not
    logged. It only changes on calibration/manual correction, so the source is
    approximated as (cameraConnectionId, timeline, calibrationId).
  * The frame cursor (LiveDetectFrameCursor) is not re-run: the logged frames
    are the frames the device already accepted.
  * Clip queueing, shot-episode grouping (LiveDetectShotClipGrouping), the HUD
    read-out policy and log writing are not ported; only the event types and
    check outcomes are.
  * Logs before schema 6 have no spatiallyCompatible / packedRackEvidence, and
    schema 1 has no layoutUncertain (HasUncertainLayout then falls back to
    Uncertain, as the C# property does).
  * Every log was written by the build installed at the time (code27..code70);
    this replays all of them through the HEAD reducer, so logged events and
    replayed events are not expected to match for older builds.

Sub-commands:
  collect   merge every saved live-detect *.jsonl (deduplicated by run/frame)
  analyse   replay, measure, classify lost time, counterfactual, clip lookup

Example (from the repository root):
  python3 tools/evidence/prototypes/live_detect_reducer_replay.py collect
  python3 tools/evidence/prototypes/live_detect_reducer_replay.py analyse
"""
import argparse, collections, csv, gzip, json, math, os, sys, uuid
from functools import lru_cache

MOVEMENT_THRESHOLD = .018
STABLE_LAYOUT_TOLERANCE = .010
LIVE_POSITION_NOISE_TOLERANCE = MOVEMENT_THRESHOLD
REQUIRED_STABLE_SAMPLES = 3
MAX_CONFIRMATION_GAP_US = 5_000_000
REQUIRED_CONFIRMATION_SAMPLES = 3
COLOURS = ("red", "yellow", "cue", "black")

# ---------------------------------------------------------------- reconciler

def _max_matching(old, new, thr):
    """Size of a maximum bipartite matching using edges with distance <= thr."""
    adj = [[j for j, b in enumerate(new) if math.hypot(a[0] - b[0], a[1] - b[1]) <= thr] for a in old]
    match = [-1] * len(new)
    def aug(i, seen):
        for j in adj[i]:
            if j in seen: continue
            seen.add(j)
            if match[j] < 0 or aug(match[j], seen):
                match[j] = i; return True
        return False
    return sum(1 for i in range(len(old)) if aug(i, set()))


def _exact_colour(old, new, thr):
    """Exact port of the C# exhaustive Search: minimise moved, then sum of squares."""
    n = len(old)
    d = [[math.hypot(o[0] - q[0], o[1] - q[1]) for q in new] for o in old]
    @lru_cache(maxsize=None)
    def best(i, used):
        if i == n: return (0, 0.0, 0.0)
        res = None
        for j in range(n):
            if used >> j & 1: continue
            m, s, l = best(i + 1, used | 1 << j)
            cand = (m + (1 if d[i][j] > thr else 0), s + d[i][j] ** 2, max(l, d[i][j]))
            if res is None or cand[0] < res[0] or cand[0] == res[0] and cand[1] < res[1]: res = cand
        return res
    return best(0, 0)


def compare(before, after, threshold=MOVEMENT_THRESHOLD, need_largest=False):
    """SettledLayoutReconciler.Compare -> (comparable, moved, largest)."""
    if not before or not after: return (False, 0, 0.0)
    moved, largest = 0, 0.0
    for c in COLOURS:
        o = [(x, y) for k, x, y in before if k == c]
        q = [(x, y) for k, x, y in after if k == c]
        if len(o) != len(q): return (False, 0, 0.0)
        if not o: continue
        if need_largest and len(o) <= 10:
            m, _, l = _exact_colour(tuple(o), tuple(q), threshold)
            moved += m; largest = max(largest, l)
        else:
            moved += len(o) - _max_matching(o, q, threshold)
    return (True, moved, largest)


def same_layout(a, b, tol=STABLE_LAYOUT_TOLERANCE):
    comp, moved, _ = compare(a, b, tol)
    return comp and moved == 0


def positions_compatible(first, second, tol=LIVE_POSITION_NOISE_TOLERANCE):
    if not first or not second or tol < 0: return False
    smaller = first if len(first) <= len(second) else second
    remaining = [(x, y) for _, x, y in (second if len(first) <= len(second) else first)]
    for _, bx, by in smaller:
        if not remaining: return False
        nearest, nd = 0, float("inf")
        for i, (x, y) in enumerate(remaining):
            s = (x - bx) ** 2 + (y - by) ** 2
            if s < nd: nearest, nd = i, s
        if math.sqrt(nd) > tol: return False
        remaining.pop(nearest)
    return True

# ---------------------------------------------------------------- StableTableChangeTracker
# Settled counts are (reds, yellows, cue, black) as in SettledTableCounts.

class StableTracker:
    def __init__(self):
        self.source = None; self.last_t = None
        self.baseline_counts = None; self.baseline_layout = (); self.baseline_t = None
        self.probe_counts = None; self.probe_layout = (); self.probe_first_t = None; self.probe_n = 0
        self.events = []
        self.diag = ""

    @staticmethod
    def is_usable(s):
        c = s["counts"]
        if not s["clear"] or s["source"] is None or s["t"] < 0 or c is None: return False
        r, y, cue, bl = c
        if not (0 <= r <= 16 and 0 <= y <= 16 and 0 <= cue <= 1 and 0 <= bl <= 1): return False
        lay = s["layout"]
        if not lay or len(lay) > 16: return False
        if not all(k in COLOURS and math.isfinite(x) and math.isfinite(yv) and 0 <= x <= 1 and 0 <= yv <= 1 for k, x, yv in lay): return False
        return (len(lay) == r + y + cue + bl and sum(k == "red" for k, _, _ in lay) == r
                and sum(k == "yellow" for k, _, _ in lay) == y and sum(k == "cue" for k, _, _ in lay) == cue
                and sum(k == "black" for k, _, _ in lay) == bl)

    def snapshot(self):
        return dict(established=self.baseline_counts is not None, baseline_t=self.baseline_t,
                    baseline_counts=self.baseline_counts, stable=self.probe_n,
                    last_event=self.events[-1] if self.events else None)

    def observe(self, s):
        if not self.is_usable(s):
            self._reset_probe(); self.diag = "unusable"; return self.snapshot()
        if self.source != s["source"]:
            self._reset_baseline(); self.source = s["source"]; self.last_t = None; self.diag_reset = "source"
        elif self.last_t is not None and s["t"] <= self.last_t:
            self._reset_baseline(); self.last_t = None
        self.last_t = s["t"]
        if self.baseline_counts is None:
            if not self._accumulate(s): return self.snapshot()
            self._promote(); self.diag = "baseline_established"; return self.snapshot()
        if s["counts"] == self.baseline_counts and same_layout(self.baseline_layout, s["layout"]):
            self._reset_probe(); self.diag = "held_same"; return self.snapshot()
        if s["counts"] == self.baseline_counts:
            comp, moved, _ = compare(self.baseline_layout, s["layout"])
            if not comp or moved == 0:
                self._reset_probe(); self.diag = "held_jitter"; return self.snapshot()
        if not self._accumulate(s):
            self.diag = "change_" + self.diag; return self.snapshot()
        if s["counts"] == self.baseline_counts:
            comp, moved, largest = compare(self.baseline_layout, self.probe_layout, need_largest=True)
        else:
            comp, moved, largest = False, 0, 0.0
        ev = dict(id=str(uuid.uuid4()), before=self.baseline_counts, after=self.probe_counts,
                  before_t=self.baseline_t, after_t=s["t"], moved=moved if comp else None,
                  largest=largest if comp else None, before_layout=self.baseline_layout,
                  after_layout=self.probe_layout, after_layout_t=self.probe_first_t)
        self.events.append(ev); self.events = self.events[-12:]
        self._promote(); self.diag = "change_event"
        return self.snapshot()

    def _accumulate(self, s):
        if self.probe_counts is None or not self.probe_layout:
            why = "probe_start"
        elif self.probe_counts != s["counts"]:
            why = "probe_reset_count"
        elif not same_layout(self.probe_layout, s["layout"]):
            why = "probe_reset_position"
        else:
            why = None
        if why:
            self.probe_counts = s["counts"]; self.probe_layout = s["layout"]
            self.probe_first_t = s["t"]; self.probe_n = 1; self.diag = why
        else:
            self.probe_n += 1; self.diag = "probe_%d" % self.probe_n
        return self.probe_n >= REQUIRED_STABLE_SAMPLES

    def _promote(self):
        self.baseline_counts = self.probe_counts; self.baseline_layout = self.probe_layout
        self.baseline_t = self.probe_first_t; self._reset_probe()

    def _reset_probe(self):
        self.probe_counts = None; self.probe_layout = (); self.probe_first_t = None; self.probe_n = 0

    def _reset_baseline(self):
        self.baseline_counts = None; self.baseline_layout = (); self.baseline_t = None; self._reset_probe()

# ---------------------------------------------------------------- LiveDetectCountCandidateTracker
# Live counts are (reds, yellows, black, cue, other) as in LiveDetectCounts.

class CountCandidateTracker:
    def __init__(self): self.reset()

    def reset(self):
        self.baseline = None; self.baseline_rack = False; self._clear_pending(); self.last_t = -(1 << 63)

    def _clear_pending(self):
        self.pending = None; self.pending_t = 0; self.pending_n = 0; self.pending_rack = False

    def _set_baseline(self, c, rack):
        self.baseline = c; self._clear_pending(); self.baseline_rack = rack

    def observe(self, t, c, eligible, rack=False):
        if t <= self.last_t:
            self.reset(); return None
        self.last_t = t
        r, y, bl, cue, oth = c
        if not eligible or not (0 <= r <= 7) or not (0 <= y <= 7) or not (0 <= bl <= 1) or not (0 <= cue <= 1) or oth != 0:
            self._clear_pending(); return None
        rack = rack and c == (7, 7, 1, 1, 0)
        if self.baseline is None:
            self._set_baseline(c, False)
            if rack: self.pending = c; self.pending_t = t; self.pending_n = 1; self.pending_rack = True
            return None
        trusted = self.baseline
        if c == trusted and rack == self.baseline_rack:
            self._clear_pending(); self.baseline_rack = rack; return None
        if self.pending is not None and c == self.pending and rack == self.pending_rack and t - self.pending_t <= MAX_CONFIRMATION_GAP_US:
            self.pending_n += 1
            if self.pending_n < REQUIRED_CONFIRMATION_SAMPLES: return None
            if c == trusted and not rack:
                self._set_baseline(c, rack); return None
            if rack and not self.baseline_rack: kind = "Racked"
            elif _named_decrease(trusted, c): kind = "Decrease"
            elif _named_decrease(c, trusted): kind = "Increase"
            else: kind = "Changed"
            self._set_baseline(c, rack)
            return (trusted, c, kind)
        self.pending = c; self.pending_t = t; self.pending_n = 1; self.pending_rack = rack
        return None


def _named_decrease(b, a):
    return (a[0] <= b[0] and a[1] <= b[1] and a[2] <= b[2] and a[3] <= b[3] and a[4] == b[4]
            and (a[0] < b[0] or a[1] < b[1] or a[2] < b[2] or a[3] < b[3]))

# ---------------------------------------------------------------- LiveDetectTableEventTracker

def to_live(sc):
    return None if sc is None else (sc[0], sc[1], sc[3], sc[2], 0)


def is_stable_inventory(c):
    r, y, bl, cue, oth = c
    return 0 <= r <= 7 and 0 <= y <= 7 and bl in (0, 1) and cue in (0, 1) and oth == 0 and 0 < sum(c) <= 16


class EventTracker:
    def __init__(self): self.reset()

    def reset(self):
        self.cand = CountCandidateTracker(); self.stable = StableTracker()
        self.timeline = None; self.media_gen = None; self.last_change_id = None; self.last_clear = None
        self.established = False; self.baseline_counts = None; self.baseline_layout = ()
        self.at_baseline_n = 0; self.cur_baseline_n = 0; self.activity = None

    def observe(self, s):
        if self.timeline is not None and self.timeline != s["timeline"]: self.reset()
        gen_changed = self.media_gen is not None and s["gen"] != self.media_gen
        self.media_gen = s["gen"]; self.timeline = s["timeline"]
        cand_eligible = not s["layout_uncertain"] and not s["obstructed"]
        eligible = (cand_eligible and is_stable_inventory(s["counts"]) and len(s["layout"]) == sum(s["counts"])
                    and (self.established or not s["uncertain"]))
        lc = s["counts"]; settled = (lc[0], lc[1], lc[3], lc[2])
        before = self.stable.snapshot()
        snap = self.stable.observe(dict(source=s["source"], t=s["t"], counts=settled if eligible else (0, 0, 0, 0),
                                        layout=s["layout"] if eligible else (), clear=eligible))
        events = []; pass_type = "source_generation_changed" if gen_changed else None
        adv = False; clip = None
        def add(tp, before=None, after=None, moved=None):
            events.append(dict(type=tp, t=s["t"], frame=s["frame"], before=before, after=after, moved=moved,
                               activity=self.activity["id"] if self.activity else None))
        def start(reason):
            if self.activity is not None or not self.established: return
            self.activity = dict(id=str(uuid.uuid4()), start_t=s["t"], reason=reason, moved=False, suppressed=False)
            add("activity_started", after=to_live(self.baseline_counts))
        if gen_changed:
            self.at_baseline_n = 0; self.cur_baseline_n = 0
            if self.activity is not None:
                add("activity_interrupted"); self.activity = None
        if before["established"] and self.last_clear is True and not eligible:
            start("obstruction" if s["obstructed"] else "uncertain view")
            add("obstruction_started" if s["obstructed"] else "layout_uncertain")
        elif self.last_clear is False and eligible and self.activity is not None:
            add("clear_view_returned", after=lc)
        moved_pos = self.established and eligible and not positions_compatible(self.baseline_layout, s["layout"])
        observed_movement = s["movement"] or moved_pos
        if self.established and eligible and observed_movement and not gen_changed:
            start("ball positions or inventory changed")
            if self.activity is not None and not self.activity["moved"]:
                self.activity["moved"] = True; pass_type = "movement_observed"
                add("movement_observed", before=to_live(self.baseline_counts), after=lc)
        self.last_clear = eligible
        rack_reset = False
        c = self.cand.observe(s["t"], lc, eligible, s["rack"])
        if c is not None:
            b, a, kind = c
            tp = {"Racked": "rack_detected", "Increase": "balls_returned_candidate",
                  "Changed": "table_inventory_changed_candidate"}.get(kind, "possible_pot")
            pass_type = tp
            n = 1
            if kind == "Decrease": n = sum(1 for i in range(4) if a[i] < b[i])
            for _ in range(n): add(tp, before=b, after=a)
            if kind == "Increase" and self.activity is not None: self.activity["suppressed"] = True
            rack_reset = kind == "Racked"
        if rack_reset and self.activity is not None:
            add("activity_ended"); self.activity = None
        if not before["established"] and snap["established"]:
            self.established = True; self.baseline_counts = snap["baseline_counts"]; self.baseline_layout = s["layout"]
            self.at_baseline_n = 0; adv = True; pass_type = "settled_layout_baseline"
            add("settled_layout_baseline", after=lc)
        ch = snap["last_event"]
        if ch is not None and ch["id"] != self.last_change_id:
            self.last_change_id = ch["id"]; self.baseline_counts = ch["after"]; self.baseline_layout = ch["after_layout"]
            self.at_baseline_n = 0; adv = True
            rack = s["rack"]; had_activity = self.activity is not None
            movement = self.activity is not None and self.activity["moved"]
            if not rack and self.activity is None: start("stable layout transition confirmed")
            same = ch["before"] == ch["after"]
            bl, al = to_live(ch["before"]), to_live(ch["after"])
            dec = any(al[i] < bl[i] for i in range(4)); inc = any(al[i] > bl[i] for i in range(4))
            tp = ("rack_layout_settled" if rack else
                  ("no_pot_observed" if movement else "settled_layout_change_unclassified" if had_activity
                   else "layout_changed_without_activity_boundary") if same else
                  "possible_pot_settled" if dec and not inc else "balls_returned_settled" if inc and not dec
                  else "settled_inventory_change")
            pass_type = tp
            add(tp, before=bl, after=al, moved=ch["moved"])
            if self.activity is not None:
                add("activity_ended", before=bl, after=al, moved=ch["moved"])
                if not rack and tp in ("no_pot_observed", "possible_pot_settled", "settled_inventory_change") and (
                        not self.activity["suppressed"] or tp == "possible_pot_settled"):
                    clip = tp
                self.activity = None
        if (eligible and self.established and self.activity is not None and self.baseline_counts == settled
                and same_layout(self.baseline_layout, s["layout"], MOVEMENT_THRESHOLD)):
            self.at_baseline_n += 1
        else:
            self.at_baseline_n = 0
        if not eligible: self.cur_baseline_n = 0
        elif adv: self.cur_baseline_n = REQUIRED_STABLE_SAMPLES
        elif self.established and self.baseline_counts == settled and same_layout(self.baseline_layout, s["layout"]):
            self.cur_baseline_n += 1
        else: self.cur_baseline_n = 0
        if self.activity is not None and self.at_baseline_n >= REQUIRED_STABLE_SAMPLES:
            act = self.activity
            tp = "activity_ended" if act["suppressed"] else "activity_ended_without_layout_change"
            add(tp, before=to_live(self.baseline_counts), after=lc); pass_type = tp
            if act["moved"] and not act["suppressed"]: clip = "activity_ended_without_layout_change"
            self.activity = None; self.at_baseline_n = 0
        if s["obstructed"]: outcome = "obstructed"
        elif s["layout_uncertain"]: outcome = "layout_uncertain"
        elif pass_type: outcome = pass_type
        elif not self.established: outcome = "baseline_pending"
        elif self.baseline_counts == settled and same_layout(self.baseline_layout, s["layout"]): outcome = "clear_unchanged"
        elif self.baseline_counts == settled: outcome = "layout_change_pending"
        else: outcome = "inventory_change_pending"
        matches = eligible and self.cur_baseline_n >= REQUIRED_STABLE_SAMPLES
        return dict(outcome=outcome, events=events, eligible=eligible, matches=matches, clip=clip,
                    baseline_counts=self.baseline_counts,
                    stable_diag=self.stable.diag, established=self.established,
                    activity=self.activity is not None, cur_n=self.cur_baseline_n)

# ---------------------------------------------------------------- collection

FRAME_FIELDS = ("capturedAt", "cameraConnectionId", "mediaGeneration", "frameId", "sourceTimeUs", "timeline",
                "calibrationId", "reds", "yellows", "black", "cue", "other", "uncertain", "layoutUncertain",
                "obstructed", "warnings", "checkOutcome", "spatiallyCompatible", "packedRackEvidence",
                "baselineMatchSamples", "settledBaselineMatchesCurrent", "events")


def collect(root, out):
    import subprocess
    files = subprocess.run(["timeout", "150", "find", root, "-maxdepth", "7", "-name", "*.jsonl", "-size", "+100k"],
                           capture_output=True, text=True).stdout.split()
    runs = {}
    for f in sorted(files):
        rel = os.path.relpath(f, root)
        for line in open(f, encoding="utf-8-sig", errors="replace"):
            line = line.strip()
            if not line.startswith("{"): continue
            try: r = json.loads(line)
            except ValueError: continue
            rid = r.get("runId"); kind = r.get("kind")
            if not rid or kind not in ("frame", "tableEvent", "clipSave", "runEvent"): continue
            run = runs.setdefault(rid, dict(files=set(), schemas=set(), frames={}, events={}, clips={}, runEvents={}))
            run["files"].add(rel); run["schemas"].add(r.get("schemaVersion"))
            body = r.get(kind) or {}
            if kind == "frame":
                key = (body.get("timeline"), body.get("mediaGeneration"), body.get("frameId"), body.get("sourceTimeUs"))
                if key in run["frames"]: continue
                fr = {k: body.get(k) for k in FRAME_FIELDS}
                fr["recordedAt"] = r.get("recordedAt"); fr["schema"] = r.get("schemaVersion")
                fr["balls"] = [[(b.get("colour") or b.get("kind") or "").strip().lower(), b.get("x"), b.get("y")]
                               for b in body.get("balls") or []]
                run["frames"][key] = fr
            elif kind == "tableEvent":
                run["events"][body.get("eventId") or json.dumps(body, sort_keys=True)] = body
            elif kind == "clipSave":
                run["clips"][(body.get("operationId"), body.get("stage"))] = body
            else:
                run["runEvents"][(r.get("recordedAt"), body.get("status"))] = body
    with gzip.open(out, "wt", encoding="utf-8") as g:
        for rid, run in runs.items():
            frames = sorted(run["frames"].values(), key=lambda fr: (fr["recordedAt"] or "", fr["sourceTimeUs"] or 0))
            g.write(json.dumps(dict(runId=rid, files=sorted(run["files"]), schemas=sorted(s for s in run["schemas"] if s is not None),
                                    frames=frames, events=list(run["events"].values()), clips=list(run["clips"].values()),
                                    runEvents=list(run["runEvents"].values()))) + "\n")
    print("collected %d files, %d runs, %d frames -> %s" % (len(files), len(runs),
          sum(len(r["frames"]) for r in runs.values()), out))


if __name__ == "__main__" and len(sys.argv) > 1 and sys.argv[1] == "collect":
    ap = argparse.ArgumentParser(); ap.add_argument("cmd"); ap.add_argument("--root", default="work/production/evidence")
    ap.add_argument("--out", default="work/evidence-audit/t3/live-detect-merged.jsonl.gz")
    a = ap.parse_args(); os.makedirs(os.path.dirname(a.out), exist_ok=True); collect(a.root, a.out); sys.exit(0)

# ---------------------------------------------------------------- replay helpers

COLOUR_ALIASES = {"red": "red", "yellow": "yellow", "black": "black", "cue": "cue", "cue ball": "cue", "cue-ball": "cue"}


def build_layout(balls):
    """CaptureLiveDetectLayout: empty if any ball is unknown or out of range."""
    if not balls or len(balls) > 16: return ()
    out = []
    for k, x, y in balls:
        c = COLOUR_ALIASES.get(k)
        if c is None or x is None or y is None: return ()
        x = x / 2.0
        if not (math.isfinite(x) and math.isfinite(y)) or not (0 <= x <= 1) or not (0 <= y <= 1): return ()
        out.append((c, x, y))
    return tuple(out)


def build_sample(fr, balls_override=None):
    balls = fr["balls"] if balls_override is None else balls_override
    if balls_override is None:
        lc = tuple(int(fr.get(k) or 0) for k in ("reds", "yellows", "black", "cue", "other"))
    else:
        cnt = collections.Counter(COLOUR_ALIASES.get(k, "other") for k, _, _ in balls)
        lc = (cnt["red"], cnt["yellow"], cnt["black"], cnt["cue"], cnt["other"])
    lu = fr.get("layoutUncertain")
    return dict(source=(fr.get("cameraConnectionId"), fr.get("timeline"), fr.get("calibrationId")),
                t=int(fr.get("sourceTimeUs") or 0), gen=fr.get("mediaGeneration"), frame=fr.get("frameId"),
                timeline=fr.get("timeline"), counts=lc, layout=build_layout(balls),
                obstructed=bool(fr.get("obstructed")), uncertain=bool(fr.get("uncertain")),
                layout_uncertain=bool(fr.get("uncertain")) if lu is None else bool(lu),
                movement=fr.get("spatiallyCompatible") is False, rack=bool(fr.get("packedRackEvidence")))


def replay(frames, overrides=None):
    tr = EventTracker(); out = []
    for i, fr in enumerate(frames):
        s = build_sample(fr, None if overrides is None else overrides.get(i))
        out.append(tr.observe(s))
    return out


def load(path):
    with gzip.open(path, "rt", encoding="utf-8") as g:
        return [json.loads(l) for l in g]


def run_label(run):
    import re
    tags = sorted({m.group(0) for f in run["files"] for m in [re.search(r"code\d+", f.split("/")[0])] if m})
    base = sorted({f.split("/")[0] for f in run["files"]})
    return (tags[0] if tags else "") , ";".join(base)

# ---------------------------------------------------------------- analysis

WARNING_REASONS = [("large object", "obstruction"), ("pocket opening", "pocket_colour"),
                   ("edge support", "boundary_support"), ("Packed colour cluster", "packed_cluster"),
                   ("Overlapping circle", "overlap"), ("share nearly the same centre", "coincident"),
                   ("No validated burst colour", "no_colour_reference")]
MAX_DT_S = 5.0
STILL_TOL = MOVEMENT_THRESHOLD


def flag_reason(fr):
    w = " ".join(fr.get("warnings") or [])
    subs = [name for key, name in WARNING_REASONS if key in w and name != "obstruction"]
    if int(fr.get("other") or 0) > 0: subs.insert(0, "unknown_ball")
    if any(int(fr.get(k) or 0) > 7 for k in ("reds", "yellows")) or any(int(fr.get(k) or 0) > 1 for k in ("black", "cue")):
        subs.insert(0, "over_count")
    return subs[0] if subs else "unspecified"


def quality(fr, s, r, established_before):
    if s["obstructed"]: return "obstructed", "obstructed"
    if s["layout_uncertain"]: return "layout_uncertain", flag_reason(fr)
    if r["eligible"]: return "usable", ""
    if not is_stable_inventory(s["counts"]) or len(s["layout"]) != sum(s["counts"]):
        return "invalid_inventory", "empty" if sum(s["counts"]) == 0 else "unknown_or_range"
    if s["uncertain"] and not established_before:
        miss = [k for k, i in (("black", 2), ("cue", 3)) if s["counts"][i] == 0]
        return "uncertain_before_baseline", "+".join(miss) if miss else "other_uncertain"
    return "other_ineligible", ""


def all_balls(fr):
    return tuple((k, (x or 0) / 2.0, y or 0) for k, x, y in fr["balls"] if x is not None and y is not None)


def durations(frames):
    ts = [f["sourceTimeUs"] or 0 for f in frames]
    dts = []
    for i in range(len(frames)):
        if i + 1 < len(frames) and frames[i + 1]["timeline"] == frames[i]["timeline"] and ts[i + 1] > ts[i]:
            dts.append(min((ts[i + 1] - ts[i]) / 1e6, MAX_DT_S))
        else:
            dts.append(None)
    known = sorted(d for d in dts if d is not None)
    med = known[len(known) // 2] if known else 0.4
    return [med if d is None else d for d in dts]


def still_segments(frames):
    """Proxy for a still table: consecutive samples on one timeline, gap <= 5 s, no logged
    spatial incompatibility, and every detected ball position compatible (<= .018, colour-blind,
    smaller set inside larger) with the previous sample that had detections. Min 3 samples."""
    segs, cur, prev_b = [], [], None
    for i, f in enumerate(frames):
        b = all_balls(f)
        brk = False
        if cur:
            p = frames[cur[-1]]
            if p["timeline"] != f["timeline"] or (f["sourceTimeUs"] or 0) - (p["sourceTimeUs"] or 0) > MAX_DT_S * 1e6:
                brk = True
            elif f.get("spatiallyCompatible") is False:
                brk = True
            elif b and prev_b and not positions_compatible(prev_b, b, STILL_TOL):
                brk = True
        if brk:
            if len(cur) >= 3: segs.append(cur)
            cur = []
        cur.append(i)
        if b: prev_b = b
    if len(cur) >= 3: segs.append(cur)
    return segs


def colour_counts(fr):
    return {"red": int(fr.get("reds") or 0), "yellow": int(fr.get("yellows") or 0),
            "black": int(fr.get("black") or 0), "cue": int(fr.get("cue") or 0)}


def presence(frames, idx):
    """Per colour in a segment: highest count seen and share of non-obstructed samples at it."""
    rows = [colour_counts(frames[i]) for i in idx if not frames[i].get("obstructed")]
    out = {}
    for c in ("red", "yellow", "black", "cue"):
        if not rows: out[c] = (0, 0.0); continue
        hi = max(r[c] for r in rows)
        # cap at the legal inventory so an over-count does not define "present"
        hi = min(hi, 7 if c in ("red", "yellow") else 1)
        out[c] = (hi, sum(1 for r in rows if r[c] >= hi) / len(rows))
    return out


def classify_kind(colour, pres):
    hi, share = pres[colour]
    return ("persistent_miss" if share < .5 else "flicker"), colour


def analyse_run(run, hold_n=None):
    frames = run["frames"]
    if hold_n is None:
        res = replay(frames)
    else:
        res = replay_with_hold(frames, hold_n)
    return res


def replay_with_hold(frames, n):
    """COUNTERFACTUAL, not production behaviour: a ball in the current settled baseline is added
    back to the observation for up to n consecutive samples in which it is not re-detected, as long
    as no movement has been logged (spatiallyCompatible == false or an activity with movement).
    Flags (obstructed, layout uncertain) are left untouched."""
    tr = EventTracker(); out = []; missing = {}
    for f in frames:
        override = None
        moving = f.get("spatiallyCompatible") is False or (tr.activity is not None and tr.activity["moved"])
        if tr.established and tr.baseline_layout and not moving:
            cur = [list(b) for b in f["balls"]]
            pool = [(COLOUR_ALIASES.get(k), (x or 0) / 2.0, y or 0) for k, x, y in cur]
            used = set(); added = []
            for j, (k, bx, by) in enumerate(tr.baseline_layout):
                best, bd = None, MOVEMENT_THRESHOLD
                for q, (kk, x, y) in enumerate(pool):
                    if q in used or kk != k: continue
                    d = math.hypot(x - bx, y - by)
                    if d <= bd: best, bd = q, d
                key = (k, round(bx, 4), round(by, 4))
                if best is not None:
                    used.add(best); missing[key] = 0
                else:
                    missing[key] = missing.get(key, 0) + 1
                    if n == 0 or missing[key] <= n: added.append([k, bx * 2.0, by])
            if added: override = cur + added
        elif moving:
            missing = {}
        out.append(tr.observe(build_sample(f, override)))
        if override is not None: out[-1]["held"] = len(override) - len(f["balls"])
    return out


def cause_trace(frames, res, segs):
    """Assign a lost-time cause to each lost sample inside a still segment.

    Lost = the port's SettledBaselineMatchesCurrent is false. Classes:
      persistent_miss  the blocking ball kind is present in < 50% of the segment's samples
      flicker          the blocking ball kind is present in >= 50% of them (intermittent drop-out)
      flags            obstructed / layout uncertain (by warning) / invalid inventory
      other            position jitter, confirmation latency (first samples), anything else
    Usable samples that are still counting up to three inherit the cause of the last break."""
    n = len(frames)
    cause = [None] * n
    established_before = [False] * n
    est = False
    for i in range(n):
        if i and frames[i]["timeline"] != frames[i - 1]["timeline"]: est = False
        established_before[i] = est
        est = res[i]["established"]
    for seg in segs:
        pres = presence(frames, seg)
        pending = ("other", "confirmation_latency")
        prev_counts = None
        for i in seg:
            f, r = frames[i], res[i]
            s = build_sample(f)
            q, sub = quality(f, s, r, established_before[i])
            cc = colour_counts(f)
            if q != "usable":
                if q == "obstructed": c = ("flags", "obstructed")
                elif q == "layout_uncertain": c = ("flags", "layout_uncertain:" + sub)
                elif q == "invalid_inventory": c = ("flags", "invalid_inventory:" + sub)
                elif q == "uncertain_before_baseline":
                    miss = [k for k in ("black", "cue") if cc[k] == 0]
                    c = classify_kind(min(miss, key=lambda m: pres[m][1]), pres) if miss else ("flags", "uncertain_other")
                else: c = ("other", "ineligible_other")
                pending = c; prev_counts = None
            else:
                d = r["stable_diag"].replace("change_", "")
                if d == "probe_reset_count" and prev_counts is not None:
                    diff = [k for k in cc if cc[k] != prev_counts[k]] or list(cc)
                    pending = classify_kind(min(diff, key=lambda m: pres[m][1]), pres)
                elif d == "probe_reset_position":
                    pending = ("other", "position_jitter_>.010")
                elif d == "held_jitter":
                    pending = ("other", "position_jitter_.010-.018")
                elif d == "probe_start" and r["stable_diag"].startswith("change_") and prev_counts is not None:
                    bc = r.get("baseline_counts")
                    bcc = {"red": bc[0], "yellow": bc[1], "cue": bc[2], "black": bc[3]} if bc else cc
                    diff = [k for k in cc if cc[k] != bcc[k]]
                    pending = (classify_kind(min(diff, key=lambda m: pres[m][1]), pres) if diff
                               else ("other", "position_vs_baseline"))
                c = pending
                prev_counts = cc
            if r["matches"]:
                cause[i] = None; pending = ("other", "confirmation_latency")
            else:
                cause[i] = c
    return cause, established_before


def first_true(seq):
    for i, v in enumerate(seq):
        if v: return i
    return None

LOGGED_ONLY_TYPES = {"shot_episode"}
HOLD_VALUES = (3, 5, 10, 30, 0)  # 0 = unlimited


def mode(vals):
    return collections.Counter(vals).most_common(1)[0][0] if vals else None


def analyse(merged, outdir, clip_inventory):
    runs = sorted(load(merged), key=lambda r: r["frames"][0]["recordedAt"] if r["frames"] else "")
    os.makedirs(outdir, exist_ok=True)
    inv_rows, run_rows, lost_rows, seg_rows, cf_rows, ev_rows, trace_rows = [], [], [], [], [], [], []
    verification = {}
    total_lost = collections.Counter(); total_lost_n = collections.Counter(); total_still = 0.0
    cf_total = collections.defaultdict(collections.Counter)
    for run in runs:
        frames = run["frames"]
        rid = run["runId"]; tag, folders = run_label(run)
        if not frames:
            continue
        res = replay(frames)
        dts = durations(frames)
        segs = still_segments(frames)
        cause, est_before = cause_trace(frames, res, segs)
        in_seg = set(i for sg in segs for i in sg)
        # ---- inventory
        t0 = frames[0]["capturedAt"]; t1 = frames[-1]["capturedAt"]
        span = sum(dts)
        logged_ev = collections.Counter(e.get("type") for e in run["events"])
        inv_rows.append(dict(run_id=rid, earliest_folder_tag=tag, folders=folders, files=len(run["files"]),
                             schemas="/".join(map(str, run["schemas"])), frames=len(frames), first_captured=t0,
                             last_captured=t1, sample_seconds=round(span, 1),
                             timelines=len({f["timeline"] for f in frames}),
                             run_started_logged=any(e.get("status") == "started" for e in run["runEvents"]),
                             logged_table_events=len(run["events"]), clip_saves=sum(1 for c in run["clips"] if c.get("stage") == "completed")))
        # ---- verification against logged outcomes and events
        logged_n = sum(1 for f in frames if f.get("checkOutcome"))
        agree = sum(1 for f, r in zip(frames, res) if f.get("checkOutcome") and f["checkOutcome"] == r["outcome"])
        lset = collections.Counter((e.get("type"), e.get("sourceTimeUs")) for e in run["events"] if e.get("type") not in LOGGED_ONLY_TYPES)
        pset = collections.Counter((e["type"], e["t"]) for r in res for e in r["events"])
        both = sum((lset & pset).values())
        verification[rid] = dict(tag=tag, schemas=run["schemas"], frames=len(frames), outcome_logged=logged_n,
                                 outcome_agree=agree, events_logged=sum(lset.values()), events_port=sum(pset.values()),
                                 events_same_type_and_time=both)
        # ---- quality
        qual = [quality(f, build_sample(f), r, est_before[i]) for i, (f, r) in enumerate(zip(frames, res))]
        qsec = collections.Counter(); qn = collections.Counter(); flagsub = collections.Counter()
        for i, (q, sub) in enumerate(qual):
            qsec[q] += dts[i]; qn[q] += 1
            if q in ("layout_uncertain", "uncertain_before_baseline", "invalid_inventory"): flagsub[q + ":" + sub] += 1
        est_idx = first_true([r["established"] for r in res])
        n_baselines = sum(1 for r in res for e in r["events"] if e["type"] == "settled_layout_baseline")
        diag = collections.Counter(r["stable_diag"] for r in res)
        # probe failures while forming a first baseline vs while confirming a change
        form = collections.Counter(); conf = collections.Counter()
        for i, r in enumerate(res):
            d = r["stable_diag"]
            tgt = conf if d.startswith("change_") or d.startswith("held") else form
            if d.endswith("probe_reset_count"): tgt["count_mismatch"] += 1
            elif d.endswith("probe_reset_position"): tgt["position_mismatch"] += 1
            elif d == "unusable" and i and res[i - 1]["stable_diag"].replace("change_", "").startswith("probe_"): (conf if res[i-1]["established"] else form)["unusable_interrupt"] += 1
        # per-kind persistence over samples with logged detections that are not obstructed
        nonob = [colour_counts(f) for f in frames if not f.get("obstructed")]
        usable = [colour_counts(f) for f, (q, _) in zip(frames, qual) if q == "usable"]
        def share(rows, pred): return round(sum(1 for x in rows if pred(x)) / len(rows), 3) if rows else None
        rmode = mode([x["red"] for x in nonob]); ymode = mode([x["yellow"] for x in nonob])
        port_ev = collections.Counter(e["type"] for r in res for e in r["events"])
        still_s = sum(dts[i] for i in in_seg)
        lost_s = sum(dts[i] for i in in_seg if cause[i] is not None)
        nobase_s = sum(dts[i] for i in in_seg if not res[i]["established"])
        total_still += still_s
        run_rows.append(dict(run_id=rid, tag=tag, frames=len(frames), sample_seconds=round(span, 1),
            usable_share=round(qn["usable"] / len(frames), 3),
            **{"n_" + k: qn[k] for k in ("usable", "obstructed", "layout_uncertain", "uncertain_before_baseline", "invalid_inventory", "other_ineligible")},
            top_flag_reasons=";".join("%s=%d" % kv for kv in flagsub.most_common(4)),
            first_baseline_s=round(sum(dts[:est_idx]), 1) if est_idx is not None else None,
            baselines_established=n_baselines,
            timeline_changes=sum(1 for i in range(1, len(frames)) if frames[i]["timeline"] != frames[i - 1]["timeline"]),
            form_count_mismatch=form["count_mismatch"], form_position_mismatch=form["position_mismatch"], form_unusable_interrupt=form["unusable_interrupt"],
            change_count_mismatch=conf["count_mismatch"], change_position_mismatch=conf["position_mismatch"], change_unusable_interrupt=conf["unusable_interrupt"],
            black_share_nonobstructed=share(nonob, lambda x: x["black"] == 1), cue_share_nonobstructed=share(nonob, lambda x: x["cue"] == 1),
            red_modal=rmode, red_at_mode_share=share(nonob, lambda x: x["red"] == rmode),
            yellow_modal=ymode, yellow_at_mode_share=share(nonob, lambda x: x["yellow"] == ymode),
            black_share_usable=share(usable, lambda x: x["black"] == 1), cue_share_usable=share(usable, lambda x: x["cue"] == 1),
            still_segments=len(segs), still_s=round(still_s, 1), lost_s=round(lost_s, 1), no_baseline_s=round(nobase_s, 1),
            port_events=";".join("%s=%d" % kv for kv in sorted(port_ev.items())),
            logged_events=";".join("%s=%d" % kv for kv in sorted(logged_ev.items()))))
        lc = collections.Counter(); lcn = collections.Counter()
        for i in in_seg:
            if cause[i] is not None:
                lc[cause[i]] += dts[i]; lcn[cause[i]] += 1
        for (cls, sub), sec in sorted(lc.items()):
            lost_rows.append(dict(run_id=rid, tag=tag, cls=cls, sub=sub, seconds=round(sec, 1), samples=lcn[(cls, sub)]))
            total_lost[(cls, sub)] += sec; total_lost_n[(cls, sub)] += lcn[(cls, sub)]
        for k, sg in enumerate(segs):
            sec = sum(dts[i] for i in sg); lost = sum(dts[i] for i in sg if cause[i] is not None)
            cc = collections.Counter(cause[i] for i in sg if cause[i] is not None)
            seg_rows.append(dict(run_id=rid, tag=tag, segment=k, first_frame=frames[sg[0]]["frameId"],
                                 first_source_us=frames[sg[0]]["sourceTimeUs"], samples=len(sg), seconds=round(sec, 1),
                                 lost_seconds=round(lost, 1), baseline_at_start=res[sg[0]]["established"],
                                 baseline_matched_any=any(res[i]["matches"] for i in sg),
                                 main_cause="%s/%s" % cc.most_common(1)[0][0] if cc else ""))
        for i, (f, r) in enumerate(zip(frames, res)):
            trace_rows.append(dict(run_id=rid[:8], i=i, frame=f["frameId"], source_us=f["sourceTimeUs"], dt=round(dts[i], 3),
                                   counts="%s/%s/%s/%s/%s" % tuple(build_sample(f)["counts"]),
                                   obstructed=int(bool(f.get("obstructed"))), uncertain=int(bool(f.get("uncertain"))),
                                   layout_uncertain=int(build_sample(f)["layout_uncertain"]), spatially_compatible=f.get("spatiallyCompatible"),
                                   logged_outcome=f.get("checkOutcome"), port_outcome=r["outcome"], stable_diag=r["stable_diag"],
                                   matches=int(r["matches"]), still=int(i in in_seg), cause="/".join(cause[i]) if cause[i] else "",
                                   port_events="|".join(e["type"] for e in r["events"])))
        for r in res:
            for e in r["events"]:
                ev_rows.append(dict(run_id=rid, source="port", type=e["type"], source_us=e["t"], frame=e["frame"],
                                    before=e["before"], after=e["after"]))
        for e in run["events"]:
            ev_rows.append(dict(run_id=rid, source="logged", type=e.get("type"), source_us=e.get("sourceTimeUs"),
                                frame=e.get("frameId"), before=_cnt(e.get("before")), after=_cnt(e.get("after"))))
        # ---- counterfactual per-ball hold
        for hn in HOLD_VALUES:
            alt = replay_with_hold(frames, hn)
            alt_cause, _ = cause_trace(frames, alt, segs)
            rec = collections.Counter()
            for i in in_seg:
                if cause[i] is not None and alt_cause[i] is None:
                    rec[cause[i][0]] += dts[i]
            newly = sum(dts[i] for i in in_seg if cause[i] is None and alt_cause[i] is not None)
            alt_ev = collections.Counter(e["type"] for r in alt for e in r["events"])
            row = dict(run_id=rid, tag=tag, hold_n=hn if hn else "unlimited", lost_s=round(lost_s, 1),
                       recovered_flicker_s=round(rec["flicker"], 1), recovered_persistent_s=round(rec["persistent_miss"], 1),
                       recovered_flags_s=round(rec["flags"], 1), recovered_other_s=round(rec["other"], 1), newly_lost_s=round(newly, 1),
                       baselines=alt_ev["settled_layout_baseline"],
                       possible_pot_port=port_ev["possible_pot"], possible_pot_hold=alt_ev["possible_pot"],
                       settled_changes_port=sum(v for k, v in port_ev.items() if k in SETTLED_TYPES),
                       settled_changes_hold=sum(v for k, v in alt_ev.items() if k in SETTLED_TYPES))
            cf_rows.append(row)
            for k in ("recovered_flicker_s", "recovered_persistent_s", "recovered_flags_s", "recovered_other_s", "newly_lost_s"):
                cf_total[row["hold_n"]][k] += row[k]
    # ---- write
    def wcsv(name, rows):
        if not rows: return
        with open(os.path.join(outdir, name), "w", newline="") as fh:
            w = csv.DictWriter(fh, fieldnames=list(rows[0].keys())); w.writeheader(); w.writerows(rows)
    wcsv("inventory.csv", inv_rows); wcsv("per-run.csv", run_rows); wcsv("lost-time-by-cause.csv", lost_rows)
    wcsv("still-segments.csv", seg_rows); wcsv("counterfactual-hold.csv", cf_rows); wcsv("events-port-vs-logged.csv", ev_rows)
    with gzip.open(os.path.join(outdir, "sample-trace.csv.gz"), "wt", newline="") as fh:
        w = csv.DictWriter(fh, fieldnames=list(trace_rows[0].keys())); w.writeheader(); w.writerows(trace_rows)
    summary = dict(note="Python port of the HEAD Live Detect reducer replayed over saved logs; see docs/claude/t3-live-detect-replay.md",
                   still_seconds=round(total_still, 1), lost_seconds=round(sum(total_lost.values()), 1),
                   lost_by_class={c: round(sum(v for (cc, _), v in total_lost.items() if cc == c), 1) for c in ("persistent_miss", "flicker", "flags", "other")},
                   lost_by_cause=[dict(cls=c, sub=s, seconds=round(v, 1), samples=total_lost_n[(c, s)]) for (c, s), v in total_lost.most_common()],
                   counterfactual_hold={str(k): {kk: round(vv, 1) for kk, vv in v.items()} for k, v in cf_total.items()},
                   verification=verification)
    summary["false_event_clips"] = false_clips(runs, clip_inventory)
    with open(os.path.join(outdir, "summary.json"), "w") as fh: json.dump(summary, fh, indent=1, default=str)
    print(json.dumps({k: summary[k] for k in ("still_seconds", "lost_seconds", "lost_by_class", "counterfactual_hold")}, indent=1))


SETTLED_TYPES = {"possible_pot_settled", "no_pot_observed", "settled_layout_change_unclassified",
                 "layout_changed_without_activity_boundary", "balls_returned_settled", "settled_inventory_change"}


def _cnt(c):
    return None if not c else (c.get("reds"), c.get("yellows"), c.get("black"), c.get("cue"), c.get("other"))


def false_clips(runs, inventory):
    """Match the named clip files to clipSave records by byte size, then report what the reducer saw."""
    want = {}
    if inventory and os.path.exists(inventory):
        for row in csv.DictReader(open(inventory)):
            if "false-pot-clip" in row["path"] or "false-shot-clip" in row["path"]:
                want[int(row["bytes"])] = row["path"]
    out = []
    for run in runs:
        for c in run["clips"]:
            if c.get("stage") != "completed" or c.get("clipBytes") not in want: continue
            act = c.get("activityId")
            evs = sorted([e for e in run["events"] if e.get("activityId") == act], key=lambda e: e.get("sourceTimeUs") or 0)
            start = min([e.get("sourceTimeUs") for e in evs if e.get("sourceTimeUs")] or [c["requestSourceTimeUs"]])
            lo = start - 3_000_000; hi = c["requestSourceTimeUs"] + 500_000
            frames = run["frames"]; res = replay(frames)
            rows = []
            base = None
            for f, r in zip(frames, res):
                t = f["sourceTimeUs"] or 0
                if f["timeline"] != c.get("eventTimeline", f["timeline"]) or not lo <= t <= hi: continue
                b = all_balls(f)
                if base is None and b: base = b
                rows.append(dict(frame=f["frameId"], source_us=t, counts=build_sample(f)["counts"],
                                 obstructed=f.get("obstructed"), uncertain=f.get("uncertain"), layout_uncertain=f.get("layoutUncertain"),
                                 spatially_compatible=f.get("spatiallyCompatible"), logged_outcome=f.get("checkOutcome"),
                                 logged_events=f.get("events"), port_outcome=r["outcome"],
                                 compatible_with_first=positions_compatible(base, b) if base and b else None,
                                 max_shift_vs_first=_max_shift(base, b), warnings=f.get("warnings")))
            out.append(dict(file=want[c["clipBytes"]], run_id=run["runId"], clip_id=c.get("clipId"), event_type=c.get("eventType"),
                            activity_id=act, request_frame=c.get("requestFrameId"), request_source_us=c.get("requestSourceTimeUs"),
                            clip_seconds=c.get("clipDurationSeconds"),
                            activity_events=[dict(type=e.get("type"), source_us=e.get("sourceTimeUs"), frame=e.get("frameId"),
                                                  message=e.get("message")) for e in evs],
                            frames=rows))
    return out


def _max_shift(a, b):
    if not a or not b: return None
    worst = 0.0
    for k, x, y in b:
        same = [(xx, yy) for kk, xx, yy in a if kk == k]
        if not same: continue
        worst = max(worst, min(math.hypot(x - xx, y - yy) for xx, yy in same))
    return round(worst, 4)


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("cmd", choices=["collect", "analyse"])
    ap.add_argument("--merged", default="work/evidence-audit/t3/live-detect-merged.jsonl.gz")
    ap.add_argument("--out", default="work/evidence-audit/t3")
    ap.add_argument("--inventory", default="work/evidence-audit/inventory.csv")
    a = ap.parse_args()
    if a.cmd == "analyse": analyse(a.merged, a.out, a.inventory)
