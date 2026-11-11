# T3: replaying saved Live Detect logs through the current reducer

11 November 2026. Thread T3 of [the detection plan](skyeye-detection-fix-plan.md). Offline analysis
of saved logs only: no camera, device or `src/` change.

## Answer in brief

On a still table, almost all of the time the reducer failed to form or hold a baseline was spent
on **quality flags**. Flicker of individual balls cost almost nothing. Behind one flag, the
pocket-colour warning, there is a large **persistent miss of the black and cue** in the
pocket-lip fixtures.

| Lost time on a still table (2,402 s of 7,000 s) | Seconds | Share of lost |
|---|---:|---:|
| (c) Flags: pocket-colour layout uncertainty | 1,149 | 48% |
| (c) Flags: obstructed | 941 | 39% |
| (c) Flags: unknown ball, packed cluster, other, empty | 50 | 2% |
| (d) Black deliberately off the table, so no first baseline is possible (code62, code63, code65) | 113 | 5% |
| (d) Confirmation latency (first samples of a still period) | 75 | 3% |
| (d) Position jitter (> .010 between samples) | 38 | 2% |
| (a) Persistent miss, unflagged (black, code66 and code70) | 30 | 1% |
| Black or cue absent or missed; run notes do not say whether it was on the table | 4 | <1% |
| (b) Flicker (a usually seen ball drops out) | 1.4 | <0.1% |

The pocket-colour bucket splits three ways once the flag is ignored (a counterfactual, below):

- **370 s** would have become settled, stable readings: the code48 and code55 play runs, where
  counts were steady (16 and 15 balls).
- **534 s** shows a persistent black (and often cue) miss underneath. Of that, **381 s** is in
  runs where the run notes say the black and cue were on the table: code59 holdouts, code66 and
  code70.
- **204 s** is in runs where the black had been deliberately removed. The current reducer cannot
  form a first baseline there in any case.
- The remaining **41 s** stays lost for other reasons, such as position jitter and confirmation
  latency.

**Consequence for T6.** A per-ball hold built on the current baseline rule recovers almost
nothing: 1.4 s of flicker, and no persistent-miss time. Most of the problem is a baseline that is
never formed at all. That has two causes: flags, and the rule that the first baseline needs a
black and a cue. Per-ball state is only worth building if it also changes how the **first**
baseline is formed and how the pocket-colour and obstruction flags gate it. The 26/42 and 38/42
figures from the earlier still-burst replay should not be used for Live Detect. The numbers in
this note replace them.

## Data

- 88 saved `*.jsonl` logs, all under `work/production/evidence/`. No other log over 100 kB was
  found under `work/`.
- The logs merge into 48 runs and 18,760 unique frames (27 to 31 October 2026, about 7,300 s of
  samples), on schemas 1 to 14. Many files are overlapping snapshots of the same rolling log, so
  frames are deduplicated by run, timeline, generation, frame ID and source time.
- `work/evidence-audit/t3/inventory.csv` lists each run: folders, schema, frames, time span,
  logged events and clips.
- Sample quality over all frames: usable 70%, layout-uncertain 15%, obstructed 13%, "uncertain
  before any baseline" (black or cue not seen) 2%.

## Method

1. **Port.** `tools/evidence/prototypes/live_detect_reducer_replay.py` is a **Python port** of
   the current C# reducer, written by reading the source. It covers:
   - `StableTableChangeTracker` (three matching usable samples, `IsUsable`);
   - `LiveDetectCountCandidateTracker` (three samples within 5 s);
   - `LiveDetectTableEventTracker.Observe`;
   - `SettledLayoutReconciler.Compare`, `SameLayout` (.010) and `PositionsCompatible` (.018);
   - the way `ShellLiveDetect` builds samples: ball x/2, the layout is emptied if any ball is
     unknown, and movement means `spatiallyCompatible == false`.

   It has not been compiled against or run beside the C#.
2. **Still table (proxy).** A still segment is a run of at least three consecutive samples on one
   timeline, with gaps of 5 s or less. Each sample's detected balls must be position-compatible
   (.018, colour ignored, smaller set inside larger) with the previous sample's, and no
   `spatiallyCompatible == false` may be logged. Obstructed samples stay in a segment, so some
   obstructed time may be a real hand rather than a false flag. Nothing here has ground truth for
   motion.
3. **Lost time.** A sample in a still segment is lost when the reducer's
   `SettledBaselineMatchesCurrent` is false, weighted by the time to the next sample (capped at
   5 s).
4. **Cause.** Each lost sample takes the first cause that applies:
   - Flags: obstructed; layout-uncertain, sub-divided by warning text; or an invalid inventory.
   - Black or cue not detected before any baseline exists (the broad `Uncertain` flag): the ball
     is counted as *persistent miss* if it was seen in under half the segment's samples, and as
     *flicker* otherwise.
   - Exceptions to that: if run notes say the ball was deliberately off the table, the cause is
     *kind absent* (class d). If the notes do not say, the cause is left as *absent or missed*.
   - A count change between usable samples is classified by the same per-ball rule.
   - A position change is position jitter.
   - Samples that are still counting up to three take the cause of the last break.
5. **Run notes used for black and cue.** These are the `EXPECTED` table in the script, taken from
   `docs/device-acceptance.md`.
   - Black deliberately removed: code59 `2e887ca1`, code61 `b9a3826d`, code62 `1c89005d`, code63
     `b3ad94b6`, code65 `6cdb8824`.
   - Black and cue on the table: code59 `567720c1` and `d081fa28`, code66 `cfd3b9d4`, code70
     `f9dd4379`. For code59 `85a34387` only the black is stated.
   - Not stated: code59 `dee49d3d`, `23796ede` and `334dcf5f`, and code70 `38961617`.

## Verified (checked against recorded evidence)

- **Code66 replay matches exactly.** Run `cfd3b9d4` gives 199 `layout_uncertain`, 37
  `baseline_pending`, one source-generation change and no table events, as recorded in
  `live-detect-plan.md`.
- **Agreement with logged outcomes.** Over all runs that log outcomes, 14,588 of 15,111 per-frame
  check outcomes match (97%). Examples:
  - code42 `1ef8e6de`: 948/950 outcomes, 15/16 events;
  - code43 `40f329c8`: 146/146 outcomes, 55/55 events;
  - code52 `7f8990c2`: 1,209/1,242 outcomes, 187/209 events.

  The mismatches are where the logging build differs from today's code:
  - code62 to code66 (`1c89005d`, `b3ad94b6`, `6cdb8824`, `cfd3b9d4`) formed baselines on the
    device that the current rule (no first baseline while the broad `Uncertain` flag is set)
    forbids;
  - some long play runs differ in event counts.
- **Documented candidates reappear one sample later.** The code37 yellow 7→6 appears at PTS
  73322367177 (documented at 73321967222). The code39 yellow 4→3 appears at 78085023677 (device
  logged 78084556966). Commit c2caf21 (31 October) raised `RequiredConfirmationSamples` from two
  to three, so the current code is expected to emit one sample later.
- **Movement input.** The logged `spatiallyCompatible` is the device's value at capture time.
  Commit c2caf21 also changed `TableAnalysisSession.SpatiallyCompatible` from colour-blind
  `SameLayout` to `PositionsCompatible`. Replaying with a recomputed signal (`--movement
  recomputed`, an approximation; see caveats) does not change any lost-time figure. It does
  reduce `movement_observed` events from 99 to 73.

## Pocket-colour layout uncertainty (the largest bucket)

The warning reads "Check a coloured ball at a pocket opening; the pocket mask may hide part of
it." It appears on 3,449 of 9,763 frames in the 24 runs where it occurs at all. On 2,818 of those
frames it is the **only** trigger for layout uncertainty: no obstruction, overlap, unknown ball or
boundary warning. Per-pocket audit data exists only from schema 11 onwards (code56 and later). See
`work/evidence-audit/t3/pocket-colour.csv`.

| Run group | Frames with the warning | What the audit shows |
|---|---:|---|
| code59 pocket-lip holdouts (black and cue on the table, all 16 balls labelled by the user) | 277 to 397 per run, every frame | 3 to 5 of 6 pockets flagged; median 460 to 575 unexplained pixels. At most flagged pockets **no ball was detected within .08** (1,257 of 1,385 in `567720c1`). Detections 4 to 8 of 16. |
| code61 and the code59 black-removed run | every frame | 3 pockets flagged; flagged pockets often have a detected red or yellow nearby |
| code66 and code70 pocket cluster (black and cue on the table) | 146 to 199 per run | 1 to 2 pockets flagged, mostly the two middle pockets, each with a detected red or yellow nearby; median about 90 unexplained pixels. Black and cue detected in 0% of these frames. |
| code62, code63, code65 (black removed) | 17 to 35 per run | middle pockets, near a detected red or yellow; mostly together with an obstruction flag |
| code48 `f5832114`, code55 `e5bf9b4d` play runs | 400 and 741 | no per-pocket audit in these schemas; counts steady at 16 and 15 |

**Counterfactual: ignore the warning when it is the only trigger.** This is not production
behaviour; see `counterfactual-pocket-flag-ignored.csv`.

- In the play runs it recovers 152 s (code48) and 212 s (code55): steady baselines of 7/7/1/1
  form at once.
- In every pocket-lip fixture it recovers nothing. The samples become blocked by the missing
  black instead: *persistent miss* where the black was on the table, *kind absent* where it had
  been removed.

**Hypothesis, not verified.** In the fixtures the warning is a symptom: balls at the pocket mouths
really are not detected. In the play runs it may be a false review trigger on a stable table.
Checking that needs the source frames, which T1 and T9 would supply.

## Obstruction

Obstruction accounts for 941 s, of which 763 s is in ordinary (non-fixture) runs. The largest is
code42 `1ef8e6de` at 249 s, where the run notes state the table was clear. code43 had 25 of 146
stationary frames flagged. Other runs (`7f8990c2`, `11fdc464`) include play, where some
obstruction is real. Without video labels this report does not separate real from false
obstruction.

## Counterfactual: per-ball hold

This is not production behaviour.

**Rule.** A ball in the current settled baseline is added back for up to N samples in which it is
not re-detected, provided all of these hold:

- no movement is seen (a logged spatial incompatibility, an activity with movement, or the
  current detections not position-compatible with the baseline);
- the frame is not obstructed;
- adding the ball does not exceed the legal 7/7/1/1 inventory.

Flags are left as they are.

| N | Flicker recovered | Persistent miss recovered | Flags recovered | Newly lost |
|---|---:|---:|---:|---:|
| 3 | 1.4 s | 0 s | 0.3 s | 0.8 s |
| 5 | 1.4 s | 0 s | 0.3 s | 1.0 s |
| 10 | 1.4 s | 0 s | 0.3 s | 0.9 s |
| 30 | 1.4 s | 0 s | 0.3 s | 0.9 s |
| unlimited | 1.4 s | 0 s | 0.3 s | 0 s |

Possible-pot candidates are unchanged (53), and settled changes go from 81 to 80 at N unlimited.
A hold cannot help any of the 15 runs that never form a baseline under the current code (all the
pocket-lip fixtures and code30 `5bbeea13`), because there is nothing to hold.

**About the earlier "4 to 411 s added loss".** That figure came from a first version of the rule
with no positional movement test and no legal-inventory cap. Schema 1 and 2 logs (code27 to
code39) record no movement field, so after a break or re-rack, balls from the old baseline were
held indefinitely. They stacked up past seven of a colour, which made the frames unusable. With
the positional test and the cap, the added loss falls to under 1 s: one code36 run where a held
red changes when a probe restarts.

## The two motionless "false event" clips (code62, run `1c89005d`)

The clips were matched to camera receipts by byte size from `work/evidence-audit/inventory.csv`.

- **`code62-false-pot-clip-003.mp4`** (8,286,913 bytes, 10.9 s). This is an
  `activity_ended_without_layout_change` clip, requested at frame 2249 (source 44498958700). The
  activity began at frame 1935, when one red (logged x 0.10, y 0.06, at the corner pocket) was
  not detected (7→6 reds). The device logged `spatiallyCompatible = false` for that frame and
  emitted `movement_observed`. Obstruction and layout-uncertain flags followed, then the table
  returned to the same layout, which ended the activity with a clip.
- **`code62-false-shot-clip-004.mp4`** (7,477,917 bytes, 9.8 s). This is a `shot_episode` clip,
  requested at frame 2699. The same red dropped out at frames 2373, 2450 and 2535. Each time
  `spatiallyCompatible` was false, and a `possible_pot` (red 7→6) and `balls_returned` pair was
  emitted.
- **Positions.** Across both windows no detected ball moved more than **0.011** from a reference
  frame. The only difference was that one red was intermittently missing: 3 of 35 frames and 4 of
  28 frames.

What the reducer saw:

- **The device (code62 build).** Every frame was broadly uncertain, because the black was
  deliberately absent, but that build allowed a baseline anyway. The colour-blind `SameLayout`
  treated a single missing red as movement, and two-sample confirmation turned the drop-out into
  a pot candidate.
- **The current reducer** never forms a baseline in this run, so it emits nothing.
- **The current reducer with the baseline rule relaxed** (an experiment):
  - with the old logged movement signal, it still queues 8 "ended without layout change" clips;
  - with movement recomputed as at c2caf21, it queues **none**.

So the false clips came from the movement signal and the two-sample confirmation, both fixed in
c2caf21. The repeated false obstruction starts (18) remain in every variant.

## Port caveats

- Python port, never compiled or tested against the C#.
- The evidence source is approximated as (connection, timeline, calibration);
  `TableAnalysisSnapshot.Generation` is not logged.
- The frame cursor, clip queue, shot-episode grouping and HUD read-out are not ported.
- Schema 1 and 2 logs have no movement or rack fields. Schema 1 has no `layoutUncertain`, which
  falls back to `uncertain`, as the C# property does.
- Every log is replayed through **today's** reducer, whichever build wrote it.
- `--movement recomputed` approximates the C# `previous` (TableTracker's table, which is not
  logged) with the last frame that was neither obstructed nor layout-uncertain.
- Run tags in the CSVs are only the earliest folder name, because logs were copied between
  folders. Use run IDs for attribution. `f5832114` is code48.
- The still-table proxy, the 50% persistence threshold and the cause priority order are my
  choices. They are not validated against video.

## Outputs (`work/evidence-audit/t3/`)

| File | Contents |
|---|---|
| `live-detect-merged.jsonl.gz` | merged, deduplicated runs (input to `analyse`) |
| `inventory.csv` | one row per run: folders, schema, frames, time span, events, clips |
| `per-run.csv` | sample quality, first baseline time, probe resets, per-kind persistence, still and lost time, port and logged events |
| `lost-time-by-cause.csv` | lost seconds by run, class and cause, with time where black or cue was undetected under a flag |
| `still-segments.csv` | each still segment, its lost time and main cause |
| `pocket-colour.csv` | pocket-colour warning analysis per run |
| `counterfactual-hold.csv`, `counterfactual-pocket-flag-ignored.csv` | the two counterfactuals |
| `events-port-vs-logged.csv`, `sample-trace.csv.gz` | per-event and per-sample traces |
| `summary.json` | totals, verification per run, false-clip frame rows |
| `movement-recomputed/` | the same outputs with the recomputed movement signal |

## Regenerate

From the repository root (Python 3.10, standard library only; each step takes under 20 s):

    python3 tools/evidence/prototypes/live_detect_reducer_replay.py collect
    python3 tools/evidence/prototypes/live_detect_reducer_replay.py analyse
    python3 tools/evidence/prototypes/live_detect_reducer_replay.py analyse --movement recomputed --out work/evidence-audit/t3/movement-recomputed

## What this means for the plan

- **T6.** Per-ball permanence as described (a hold after a baseline) is not justified by these
  logs on its own. The value lies in two other changes:
  1. letting a **first** baseline form when the black or cue is not seen, recording it as "not
     observed" rather than refusing the baseline;
  2. stopping a single pocket-colour or obstruction flag from voiding the whole table.

  Both are design choices for Richard, and the second needs evidence that the flag is false
  (T1/T9 frames).
- **T4 and the detector.** The pocket-lip black and cue miss is real and persistent. No reducer
  change will make those balls observed.
- **Obstruction.** False obstruction (code42, code43, code62) is the second-largest loss and is
  not covered by any current thread. It should be measured on labelled frames.
