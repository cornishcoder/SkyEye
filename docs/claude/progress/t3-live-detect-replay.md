# T3 progress: Live Detect reducer replay (paused 11 November 2026)

Status: **half done**. The reducer port, the log merge and a first full analysis run exist. The
lost-time classification has a known bug (below), so the numbers are **preliminary and must not
be quoted as results**. The final report `docs/claude/t3-live-detect-replay.md` is not written yet.
Nothing under `src/` or `docs/argon/` was changed. No git writes.

## Files

- `tools/evidence/prototypes/live_detect_reducer_replay.py`: a Python port of the HEAD reducer
  (header lists what was ported and what was not), plus `collect` and `analyse` sub-commands.
- `work/evidence-audit/t3/live-detect-merged.jsonl.gz`: 88 saved `*.jsonl` logs under
  `work/production/evidence/` merged and deduplicated into 48 runs and 18,760 frames. Nothing else
  under `work/` holds live-detect logs larger than 100 kB. `_files.txt` lists the source files.
- Output of a first `analyse` run (preliminary): `inventory.csv`, `per-run.csv`,
  `lost-time-by-cause.csv`, `still-segments.csv`, `counterfactual-hold.csv`,
  `events-port-vs-logged.csv`, `sample-trace.csv.gz`, `summary.json` (which also holds the
  false-clip lookup, not yet reviewed).

To regenerate (from the repository root on the Linux shell, with about 15 s for each step):

    python3 tools/evidence/prototypes/live_detect_reducer_replay.py collect
    python3 tools/evidence/prototypes/live_detect_reducer_replay.py analyse

## Verified

- The port reproduces the code66 replay documented in `live-detect-plan.md` for run `cfd3b9d4`
  exactly: 199 `layout_uncertain`, 37 `baseline_pending`, 1 `source_generation_changed`, no table
  events.
- Logged check outcomes agree with the port almost completely for schema 4 to 12 runs from builds
  older than c2caf21 (for example code43 146/146 and 55/55 events, code45 1470/1484, code57
  `da070311` 278/278 and 19/19 events). Events agree less often in long play runs (for example
  `7f8990c2` has 187 of 209 logged events matched by type and source time).
- Documented candidates: code37 yellow 7→6 (run `9044724b`) and code39 yellow 4→3 (run
  `e3f4c154`) both reappear, **one sample later** (73322367177 rather than 73321967222, and
  78085023677 rather than 78084556966). Commit c2caf21 (31 Oct) raised
  `RequiredConfirmationSamples` to 3, and that change accounts for the one-sample delay. The HEAD
  port is therefore correct to emit later.
- For code62, code63 and code65 (`1c89005d`, `b3ad94b6`, `6cdb8824`), the logs show baselines,
  clear_unchanged results and events, but HEAD gives only `baseline_pending`. The cause is the later
  fix: a broad `Uncertain` (black not seen) may not seed the first baseline. In those runs the
  black was **intentionally off the table** (see device-acceptance.md), so under HEAD no baseline
  can ever form.

## Preliminary numbers (affected by the bug, do not quote)

The still-table proxy covered 7,000 s, with 2,402 s lost (no settled baseline matching the
current view). The first pass attributes this as follows: flags 2,140 s (pocket-colour
layout-uncertain 1,149 s, obstructed 941 s, unknown ball 38 s), flicker 148 s (almost all
"black"), other 113 s (confirmation latency 75 s, position jitter 38 s), persistent miss 0.8 s.
The counterfactual per-ball hold recovers about 1.4 s and adds 4 to 411 s of newly lost time.
Both need investigation before anyone relies on them.

## Known bug and half-done items

1. **Persistent-miss classification is wrong.** `presence()` uses the highest count seen in the
   segment, so a ball that is never detected is counted as present. The 144 s of "flicker:black"
   comes from runs where black was 0 in every sample. Fix: give each run an expected inventory
   from the docs. Black was intentionally absent in code59 `2e887ca1`, code61 `b9a3826d`, code63
   `b3ad94b6` and code65 `6cdb8824` (code62 `1c89005d` probably too; check). It was present in
   code66 `cfd3b9d4`, code70 `f9dd4379` and `38961617` (check), code59 `85a34387` and `d081fa28`.
   Then split the time lost to "black or cue absent before the first baseline" into
   (i) ball truly absent, which is a reducer design limit and goes under *other*, and
   (ii) ball present but missed, which is a persistent miss. In code66 and code70 the
   pocket-colour flag hides a persistent black and cue miss; report that as "flags with a
   persistent miss underneath" (add a secondary column).
2. **Still-segment proxy:** this is colour-blind position compatibility with the previous sample
   (≤ .018) plus `spatiallyCompatible != false`. Obstructed samples are included. The proxy is
   unvalidated, and code42 `1ef8e6de` (a false obstruction on a clear table, 249 s lost) needs
   checking.
3. **Counterfactual hold** (`replay_with_hold`): investigate the "newly lost" time. One likely
   cause is held phantom balls from a baseline, or a held ball conflicting with a real change.
   Holds only apply after a baseline exists, so they cannot help runs that never form one (all
   code59 to code70 pocket runs). Report that explicitly.
4. **False clips:** `summary.json` → `false_event_clips` matches by byte size. Clip-003
   (8,286,913 bytes) is `activity_ended_without_layout_change`, requested at frame 2249, source
   44498958700, run `1c89005d`. Clip-004 (7,477,917 bytes) is a `shot_episode` requested at
   frame 2699, source 44513960022. Not yet reviewed: read the frame rows (counts, obstructed
   flags, `spatiallyCompatible`, `max_shift_vs_first`). HEAD forms no baseline in this run, so it
   would not have produced these clips.
5. Write `docs/claude/t3-live-detect-replay.md` (British English, tables, verified vs
   hypothesis, the commands above), then send the coordinator report.

## Port caveats

- Python port, not compiled or tested against the C#.
- The source identity omits `TableAnalysisSnapshot.Generation`, which is not logged.
- The frame cursor, clip queueing and shot-episode grouping are not ported (`shot_episode` events
  are logged only).
- Schemas 1 and 2 have no movement or rack fields, and schema 1 has no `layoutUncertain`, which
  falls back to `uncertain`.
- All logs are replayed through the HEAD reducer, whatever build wrote them.
- Run labels (`earliest_folder_tag`) are only the earliest folder tag, because logs were copied
  between folders. Use the run IDs and docs for attribution.
