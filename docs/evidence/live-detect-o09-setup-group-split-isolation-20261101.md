# O09 setup-group split isolation — 1 November 2026

## Target

`O09-SETUP-GROUP-SPLIT-ISOLATION` asks whether related staged controls remain together across O09 train/validation and whether setup/control groups are prevented from crossing O01 Development/Holdout. This is corpus-integrity infrastructure only; it does not score a detector or make O09 training-ready.

## Change

- Diagnostic session assignments accept optional `setupGroupId`. The importer validates it, writes `setup-group:<id>` on O01 sequences, and rejects a setup group assigned to both Development and Holdout.
- O01 corpus validation rejects any setup/control group appearing in more than one assigned split. The parser also recognizes existing `matched-setup-group-...` tags.
- O09 connects Development sequences that share either a camera recording session or setup group. Connected components are kept whole when deterministically assigning train/validation roles. A valid plan needs at least three recording sessions and three independent components.
- Export report schema 2 records split-group IDs, their sessions and setup groups, and each session's group/role.
- `docs/o09-learned-ball-detection-spike.md` now documents assignment and split rules.

## Focused test record

Command:

```powershell
dotnet run --project tests/SkyEye.Observation.Checks/SkyEye.Observation.Checks.csproj -c Release --no-restore
```

Attempts are retained to make the retry sequence auditable:

1. Initial attempt failed compilation with CS0407 because a `SelectMany` method group returned `ImmutableArray<string>`; no assertions ran. This was a compile error, not a test failure.
2. Replacing the method group with a lambda and `.AsEnumerable()` compiled. The first focused version passed **6/6** setup-group assertions; the Observation corpus/replay checks completed and the process exited 0.
3. Import propagation and Development/Holdout leakage cases were then added. The final focused target passed **8/8** assertions; the Observation corpus/replay checks completed and the process exited 0.

The Workbench Release build was also attempted once, but its tool output was truncated without an exit status. Because the result could not be recorded, it was rerun once:

```powershell
dotnet build src/SkyEye.Observation.Workbench/SkyEye.Observation.Workbench.csproj -c Release --no-restore -v:minimal
```

The recorded run exited 0 with **0 warnings and 0 errors**. The summary SDK preview messages were informational.

## Data readiness and acceptance status

The read-only corpus-v4 audit found 10 Development frames in 10 camera recording sessions across 3 tagged setup groups. Its 155 visible-ball labels have **0/155 numeric radii**; the source frames are PNG in `table-2x1`, while the O09 exporter requires reviewed image-normalized centres and X/Y radii. The new split planner does not make these data exportable and no training/export/detector comparison was run.

This cycle does not change the separate project denominators: software suites **3/3 carried forward**, focused motion-feed **20/20 carried forward**, production detector inventory **0/3**, physical D1/D2/E **0/3 proven**. No camera capture, device install, or physical acceptance check occurred.

The play-test estimate remains multiple evidence cycles over at least two separate Holdout sessions. Requirements still include five new Holdout layouts plus enough reviewed layouts to reach 12 total, 200 safe request windows, 100 unsafe windows, representative 30-second stillness soaks, installed-phone D2 checks, and seven E event/clip cases after D1/D2 pass. These can share staged sessions, so they are not an additive calendar estimate. See [the delivery plan](../live-detect-plan.md) for current counts and gating.

## Next target

`O09-RADIUS-LABEL-REVIEW-PACK`: build a visual review pack from the existing hash-bound source frames and hand labels. Any displayed radius guide will remain a proposal for human review; no radius value should be written or used for export until explicitly reviewed and recorded. Reuse the existing pixel receipts and transforms; do not replay detector output or synthesize labels.
