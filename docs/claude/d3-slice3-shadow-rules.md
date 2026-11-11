# D3 slice 3: settled-change rules in shadow

11 November 2026, branch `claude/live-detect-d1-d3`. Third slice of
[D3](d3-settled-change-design.md), after the [comparer](d3-slice1-comparer.md)
and [settle detection](d3-slice2-settle.md). Adds the reducer side: rules U1
to U5 computed from settled-change evidence, **shadow only**. Nothing creates
the evidence on the device yet (live wiring is the next slice), so nothing on
the phone changes.

**Target test:** design section 5.2 reducer-policy checks 1 to 9, including
the golden shadow test. **Before:** nothing consumed D3 evidence.
**Outcome:** the policy checks pass, and the golden shadow test shows the
reducer's outcomes and events are identical with and without evidence on
32,000 generated passes and on every saved Live Detect log.

## Result

| Check (design 5.2) | Result |
|---|---|
| 1. Hold a missing ball with no change near it; proposal not applied | PASS |
| 2. No hold when any change-grid bit is near it (also under obstruction) | PASS |
| 3. Chain breaks: gated link, sequence gap, new epoch, no-reference evidence, first link not from the baseline view; a new baseline restarts it | PASS |
| 4. Infer black: one dark arrival infers it (recheck pending); two candidates, a weak hint, or a held black elsewhere give nothing | PASS |
| 5 and 7. Not seen to inferred (U5 says "upgrades_not_seen"); without evidence it stays not seen | PASS |
| 6. D1 pocket region unchanged or changed since the baseline | PASS |
| U3. Departure region at a missing ball supports it leaving; no hold | PASS |
| Unexplained ball-sized change is logged only | PASS |
| Mode Off computes nothing | PASS |
| 9. Golden shadow test, generated: 80 streams of 400 passes (seeds 1 to 40, D1/D2 and previous reducer options) | identical; 18,887 table events, 1,236 proposals of all 7 kinds |
| 9. Golden shadow test, saved logs: 63 files under `work/`, 285 runs | identical; 72,832 frames, 4,882 table events |

The golden test runs the reducer twice on the same observations, the second
time with random settled-change evidence attached (random regions, grids,
gates, statuses and sequence gaps), and compares everything except random
IDs: check outcome, every table event (type, message, counts, times,
positions), clip candidate, baseline counts and time, match samples, D2 and
D1 flags. The generated run also checks that the streams exercise the
reducer (events above 1,000) and the rules (proposals above 1,000, including
hold, departure support and unexplained change), so a quiet stream cannot
pass vacuously. On the saved logs the random evidence rarely lines up with a
baseline, so only 232 proposals were computed there; the generated streams
cover the rules.

Software, newly executed: `Build-SkyEye.ps1 -Target All` 0 errors (the
companion app compiles the policy; warning counts unchanged, none in the new
or changed files); `SkyEye.Checks` and `SkyEye.AgentUi.Checks` pass.

Item 8 of the design (timeline reset clears D3 state) is covered by the
tracker's reset, which now resets the policy; not checked separately.

## What was built

- `src/SkyEye.Companion.Hybrid/LiveDetectSettledChangePolicy.cs`: the pure
  policy. It keeps the evidence chain since the baseline and returns
  proposals (`hold`, `infer_black`, `infer_cue`, `departure_support`,
  `pocket_unchanged`, `pocket_changed`, `unexplained_change`, each with
  `Applied = false`) and non-observed ball states (`held`, `inferred`,
  `not_seen`). Distances in table-width units, near = 1.6 ball radii.
- `LiveDetectTableEventTracker`: the observation gains an optional
  `SettledChange` pass; the options gain `SettledChange` mode (`Shadow` by
  default, `Off`); the batch gains a `SettledChange` shadow record. The
  policy runs after every reducer decision, on copies of the state, and
  nothing reads its output. Without evidence the batch carries no shadow
  record.
- `tests/SkyEye.AgentUi.Checks/LiveDetectSettledChangeChecks.cs`: the checks
  above, and `--golden-shadow <log>...` for saved logs.

Simplifications against the design, to revisit before `Active`:

- U2's 3 s recheck is not built (it needs a second comparison in the
  processor); every inference is marked `recheck_pending`.
- Obstruction: a hold is refused when the current pass is obstructed. The
  design's per-component overlap across chain links needs obstruction
  components in the evidence.
- The first link's anchor accepts only a reference segment that contains the
  baseline time, not the design's "ends after it with no image motion in
  between".
- There is no `Active` mode: nothing can apply a proposal.

## Next slice

Live wiring: rectify the live bitmap in the Live Detect frame observer, run
`SettledChangeProcessor`, pass its result on the observation, and log the
shadow output (schema 15). This is where Richard's open questions in the
design (section 6) need answers before it is built: shadow on by default and
its cost on the Pixel, the raster sidecars and their storage, hold time
limits, obstruction handling, and how the mode is set.

Regenerate:

    dotnet run --project tests\SkyEye.AgentUi.Checks\SkyEye.AgentUi.Checks.csproj --no-restore
    dotnet run --project tests\SkyEye.AgentUi.Checks\SkyEye.AgentUi.Checks.csproj --no-restore -- --golden-shadow <live-detect.jsonl> [...]
