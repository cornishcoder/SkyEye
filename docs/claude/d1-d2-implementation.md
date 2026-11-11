# D1 and D2: implementation note

11 November 2026. Decisions D1 and D2 of the [detection plan](skyeye-detection-fix-plan.md),
approved by Richard. Branch `claude/live-detect-d1-d3`. **The C# is unbuilt and untested**: this
environment has no .NET SDK. The behaviour was first measured through the Python port of the
reducer ([T3](t3-live-detect-replay.md)) on the 48 saved runs.

## What changed in behaviour

**D1. A pocket-colour flag covers its pocket region, not the whole table.**

- `VisionAnalyzer` still sets `LayoutUncertain` for the whole table, so every existing consumer
  (observation session, verifier, advice, the Identify Balls overlay) behaves as before. It now
  also publishes, only when the pocket-colour check fires:
  - `UncertainRegions`: one region per flagged pocket, in table units (x 0..2, y 0..1). Radius is
    the pocket exclusion (48 of 1,000 px) plus two ball radii, about .102, so any ball whose body
    reaches the exclusion is inside;
  - `LayoutUncertainOutsideRegions` and `UncertainOutsideRegions`: the flags the rest of the
    table would have had without the pocket check (packed cluster, boundary, unknown or excess
    balls, obstruction, overlap, coincident candidates, and the fallback pass's unresolved
    cluster all count as "outside").
- `ShellLiveDetect` passes the regions to the reducer only when they are the sole layout trigger.
- `LiveDetectTableEventTracker`, once a baseline exists, treats such a sample as clear **unless
  its red or yellow count differs from the baseline**; then it waits (`layout_uncertain`) until
  the region clears. The pocket-colour test looks for red/yellow colour, so any red or yellow
  count change may be the ball at that pocket: pots into that pocket and returns there are held
  back, everything else settles and produces events. Black and cue changes are not held.
  Balls inside a region are counted and shown as uncertain.
- Before any baseline, a region-flagged sample still waits. Letting it seed a first baseline is
  an option (`FirstBaselineWithPocketRegion`), **off by default**, for the reason in the table
  below.

**D2. A first baseline may form without the black or cue.**

- A stable first view whose only doubt is a missing black and/or cue now forms a baseline. The
  baseline records the kind as *not seen*. Other broad uncertainty still blocks it.
- Before the first baseline, such samples do not feed the inventory candidate tracker, and the
  baseline then seeds it, so an unseen black cannot become a "possible pot".
- The first later sighting of that kind is a **correction**: a `baseline_corrected` event, never
  `balls_returned_candidate` or `balls_returned_settled`, no activity, no clip. If other balls
  changed in the same settled step, those changes alone decide the event (for example a red pot).
  After correction the ball is ordinary: losing it later is a pot candidate.
- Readout: the settled baseline reads "… · black not seen · 1 cue ball" instead of "0 black".

Options: `LiveDetectReducerOptions(PocketColourRegionOnly = true, FirstBaselineWithPocketRegion =
false, FirstBaselineWithoutBlackOrCue = true)`; `LiveDetectReducerOptions.Previous` is the old
behaviour. There is no user-facing setting; `ShellLiveDetect` uses the defaults.

## Files and line ranges (after the change)

| File | Lines | Change |
|---|---|---|
| `src/SkyEye.Core/Analysis/TableAnalysisSession.cs` | 71-73, 87-95 | `UncertainTableRegion` record; three nullable `VisionResult` properties, omitted from JSON when null |
| `src/SkyEye.Vision.Android/VisionAnalyzer.cs` | 237-239, 530-532, 538-548, 566-568 | flags before the pocket check, overlap flag, regions; fallback pass keeps them honest |
| `src/SkyEye.Companion.Hybrid/LiveDetectTableEventTracker.cs` | 11-43, 52-58, 67, 81-84, 101, 114-144, 256-257, 290-297, 313-343, 359, 374-384, 439, 448-475 | observation fields, region struct, options, batch fields, D1 eligibility, D2 eligibility, not-seen state, correction in candidate and settled paths |
| `src/SkyEye.Companion.Hybrid/LiveDetectCountCandidateTracker.cs` | 131-134, 157 | `SeedBaseline`; `IsNamedCountDecrease` made internal |
| `src/SkyEye.Companion.Hybrid/LiveDetectInventoryReadoutPolicy.cs` | 17-24 | `BaselineSummary` ("black not seen") |
| `src/SkyEye.Companion.Hybrid/ShellLiveDetect.cs` | 327-331, 336-337, 340, 360-366, 442 | pass regions; "N balls at a pocket mouth uncertain"; not-seen summary |
| `tests/SkyEye.AgentUi.Checks/LiveDetectTableEventTrackerChecks.cs` | 4-5, 32-37, 121-122, 352-475, 486-487, 495 | new checks (below); old first-baseline check pinned to `Previous` |
| `tests/SkyEye.AgentUi.Checks/LiveDetectInventoryReadoutChecks.cs` | 32-40 | initial-absence check updated to the D2 readout |
| `tools/evidence/prototypes/live_detect_reducer_replay.py` | variants | D1/D2 variants and the `variants` sub-command |

Line endings preserved: `TableAnalysisSession.cs` and `VisionAnalyzer.cs` are CRLF in the working
tree, the others LF. `git diff --ignore-cr-at-eol` shows only the edits.

## Measured effect (Python port, 48 runs, 7,000 s of still table)

Movement input recomputed as at HEAD (c2caf21), which is what the current device code uses.
"New" inventory events are those with no HEAD event of the same type in the same run within 5 s.
Fixture runs are the static code59-code70 pocket-lip fixtures, where any movement, pot or clip is
suspect.

| Variant | Lost still time | of which pocket flag | Runs with a baseline | Inventory events | Returns | New inventory events | Fixture: movement / no-pot / clips |
|---|---:|---:|---:|---:|---:|---:|---|
| HEAD | 2,402 s | 1,149 s | 33 | 123 | 20 | - | 0 / 0 / 0 |
| D1 (region, after a baseline) | 2,244 s | 987 s | 33 | 123 | 20 | 0 | 0 / 0 / 0 |
| D2 | 2,328 s | 1,186 s | 37 | 125 | 20 | 2 | 0 / 0 / 0 |
| **D1 + D2 (default)** | **2,092 s** | **936 s** | **37** | **125** | **20** | **2** | **0 / 0 / 0** |
| D1 + D2 + first baseline with a region | 1,891 s | 665 s | 46 | 125 | 20 | 2 | 11 / 2 / 7 |
| D1 "hold" (replace region balls with baseline) | 2,030 s | 738 s | 33 | 127 | 20 | 4 | 0 / 0 / 0 |

Lost time by cause, HEAD → default: pocket flag 1,149 → 936 s; black deliberately off the table
113 → 0 s; persistent black miss 30 → 0 s (both now form a "black not seen" baseline);
obstruction 941 → 968 s (more time now has a baseline to lose); confirmation latency 75 → 86 s.

Where it comes from:

- D1: code48 `f5832114` 179 → 27 s lost (pocket flag 154 → 0 s). code55 `e5bf9b4d` does not
  recover: after the break a red sits at the middle pocket and the red count (6) differs from the
  rack baseline (7), so the rule waits, as intended.
- D2: code62/63/65 (black off the table) form 7/7/0/1 baselines reading "black not seen"; code66
  `cfd3b9d4` forms 7/7 with black and cue not seen (both were on the table but missed: the readout
  says "not seen", not "absent"); code57 `11fdc464` forms its baseline earlier with the cue unseen.
- The 2 new inventory events are both `possible_pot_settled` beside a `baseline_corrected`
  (code57 frame 1058: red 2 → 1 as the cue is first seen; code58 `b0ec0ced` frame 4528: yellow
  4 → 3 as the black reappears). HEAD emits a `possible_pot` candidate at the same frame in both,
  and the device logged a `settled_inventory_change` there, so these are the same pots now
  settled against a baseline, not spurious. Corrections: 2; balls-returned events unchanged (20).

Why the defaults:

- D1 region scope and D2: **on**. No new spurious events in the measurement.
- First baseline while a region is flagged: **off**. It recovers another 201 s, but on the static
  fixtures it adds 11 movement events, 2 no-pot observations and 7 clips (sparse detections of
  4-8 of 16 balls change subset between frames). With the old logged movement signal D2 alone also
  adds 8 fixture clips; with HEAD's movement signal it adds none.
- "Hold" (substituting baseline balls inside the region) was rejected: it emitted false pots when
  a ball rolled into a flagged region (code48 yellow 7 → 6, code55 red 7 → 5).

Caveats:

- The pocket audit (with the flagged pocket centre) exists only from schema 11 (code56 on). The
  logs do not say which ball triggered the flag; the audit's `ballSupportAudit` names nearby
  red/yellow circles but not undetected colour. Older logs are approximated with all six pockets
  as regions. Only the "balls at a pocket mouth" count depends on the regions; the D1 wait rule
  depends on counts, so the lost-time figures do not.
- Region-only is recognised in the logs from the warning text (pocket warning and no other layout
  trigger), mirroring `LayoutUncertainOutsideRegions`.
- Python port, not compiled against the C#; same caveats as T3. No ground truth for motion.
- The runs are mostly fixtures and short play sessions; a pot into a flagged pocket is not in
  the data, so the "wait until clear" path is covered only by the synthetic check.

Regenerate:

    python3 tools/evidence/prototypes/live_detect_reducer_replay.py variants --out work/evidence-audit/t3/d1-d2
    python3 tools/evidence/prototypes/live_detect_reducer_replay.py variants --movement recomputed --out work/evidence-audit/t3/d1-d2/movement-recomputed

Outputs: `variants-summary.csv`, `variants-lost-by-cause.csv`, `variants-per-run.csv`,
`variants-event-diff.csv` (every added or removed inventory event, with `paired_with_head_event`).

## Checks added (tests/SkyEye.AgentUi.Checks)

In `LiveDetectTableEventTrackerChecks`:

- `FirstBaselineFormsWithBlackNotSeenAndLaterSightingCorrectsIt`: baseline says "black not seen";
  the black appearing gives exactly one `baseline_corrected` and no return, pot, activity or
  movement; a later loss of the black is a `possible_pot_settled`.
- `NotSeenBlackAtBaselineFormationIsNotAPotCandidate`: black seen once, then missed while the
  baseline forms: no pot candidate.
- `UncertainFirstViewWithBlackAndCueSeenStillCannotSeedBaseline`: D2 covers only black/cue.
- `PocketRegionFlagCoversOnlyItsRegion`: unchanged table under the flag reads `clear_unchanged`
  with 1 uncertain ball; a red moving elsewhere settles as `no_pot_observed`; a yellow vanishing
  under the flag waits, then becomes `possible_pot` once the flag clears; `Previous` options keep
  the old whole-table behaviour.
- `PocketRegionFlagBeforeBaselineStillWaits`.
- `VisionRegionFieldsAreOmittedWhenUnset`: JSON omits the new fields when null; `HasUncertainLayout`
  is unchanged.
- `UncertainFirstViewCannotSeedBaseline` now runs with `LiveDetectReducerOptions.Previous`.

In `LiveDetectInventoryReadoutChecks`, `UncertainInitialAbsenceDoesNotSeedCurrentInventory` now
expects a baseline that reads "black not seen · cue ball not seen" and still no current-inventory
claim.

Nothing in `VisionAnalyzer` can be checked off-device (no desktop build, T2).

## Unverified

- Compilation of every C# change (Core, Vision.Android, Companion.Hybrid, AgentUi.Checks).
- The checks above have never run.
- On-device behaviour, the readout text on the phone, and the logged frames.
- Whether a real pot into a flagged pocket is reported once the flag clears.

## How to build and test on Windows

From `C:\code\SkyEye` in PowerShell, on this branch:

    git diff --ignore-cr-at-eol --stat
    .\scripts\Build-SkyEye.ps1 -Target All
    dotnet run --project tests/SkyEye.AgentUi.Checks/SkyEye.AgentUi.Checks.csproj --no-restore
    dotnet run --project tests/SkyEye.Checks

Expect the AgentUi checks to print `PASS Live Detect table event reducer …` and
`PASS READINESS-ABSENCE-SEMANTICS-CONTROLS 5/5`. Then replay one saved run through the C#
reducer and compare with the Python figures, for example code66:

    dotnet run --project tests/SkyEye.AgentUi.Checks/SkyEye.AgentUi.Checks.csproj --no-restore -- --replay-live-detect-events work\production\evidence\code66-pocket-cushion-replay-20261031\code66-pocketside-live-detect.jsonl cfd3b9d4-4a45-4a03-80c5-a51ef6909161

That replay path builds observations from logged flags and does not pass regions, so it exercises
D2 only; with D2 code66 should now form one baseline (7 reds, 7 yellows, black and cue not
seen), where HEAD formed none and logged 199 `layout_uncertain`.
If a build error appears, the likely places are the target-typed `bool?` conditionals in
`VisionAnalyzer` and the pattern variable `regions` in `ShellLiveDetect`.

Then a short Live Detect session on the device: a table with a ball at a pocket mouth should read
"… · 1 ball at a pocket mouth uncertain" rather than voiding the table, and a table without the
black should read "black not seen".
