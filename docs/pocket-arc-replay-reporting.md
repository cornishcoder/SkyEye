# Pocket arc replay reporting

`scripts/Summarize-PocketArcCandidateProbes.ps1` flattens the ranked sparse-black seed diagnostics from pocket-arc replay runs. It emits one CSV row per seed, retains the run/frame/hash, and reports the detector inventory beside the diagnostic arc fit and classifier evidence. This avoids hand-opening the nested per-frame JSON when a run evaluates up to 32 seeds on each of several frames.

Use explicit run directories and a caller-supplied case label:

```powershell
./scripts/Summarize-PocketArcCandidateProbes.ps1 `
  -CaseLabel 'black-pocket-daylight' `
  -RunDirectory 'work/production/evidence/.../cap32seedarcpos-HHMMSS' `
  -OutputPath 'work/production/evidence/.../black-pocket-daylight-probes.csv' `
  -SummaryOutputPath 'work/production/evidence/.../black-pocket-daylight-runs.csv'
```

Run the empty-pocket controls separately with their own label and output paths. Keep capture sessions, lighting conditions, and positive/negative controls separate; this script deliberately does not recursively discover runs or combine labels. Rows include `ProbeStatus=ranked-probe-field-unavailable` for reports created before ranked-seed tracing was added, and `no-ranked-candidate-seeds` when a newer report has the field but found no seed. These are different from a failed arc fit.

The values are evaluation-only. `DiagnosticArcChecksPass` and `ClassifierKind` do not alter detector candidates, `VisionResult`, readiness, or `SkyEye.GameState`; fit/classifier rates are not accuracy claims without reviewed labels and matched controls. Pixel 10 capture and Cast are not used by this summarizer. The underlying replay runner still requires its stopped-camera guard and uses the isolated Vision Device package on Pixel 11.

For a labelled-target follow-up, `scripts/Run-PocketArcTargetedAudit.ps1` skips only candidate seeds that cannot reach the chosen fit acceptance radius. The bound is calculated from the actual probe maximum seed-to-fit offset plus the chosen fit-to-label acceptance radius. For example, a 1.0-ball-radius fit acceptance uses a 1.8-ball-radius seed reach when the probe offset is 0.8. Each skipped seed remains in JSON with rank, distance, bound, and skip reason. This is a targeted diagnostic; it does not measure global candidate false positives. Keep the full-sweep reports as the baseline and run both positive and empty-pocket controls with the same acceptance radius.

```powershell
./scripts/Run-PocketArcTargetedAudit.ps1 `
  -SourceRelativePath 'work/production/evidence/code70-daylight-pocket-capture-20261031/exact-replay-input' `
  -Frames 1365,1425,1457,1555,1645,1736 `
  -TargetFitAcceptanceRadiusInBallRadii 1.0 `
  -BlackLabelX 102 -BlackLabelY 903 `
  -SparseBlackCandidateLimit 32 `
  -RunPrefix 'pockettargetpos' `
  -EvidenceRelativePath 'work/production/evidence/code70-daylight-pocket-capture-20261031/pocket-arc-holdout'
```

Use the same wrapper for negative controls with their exact source directory, frame IDs, and same label coordinates, changing only the case/run prefix and purpose. Reports show how many seeds were executed and skipped; the default full-sweep runner is unchanged.

Run the nested O08 schema regression check offline with:

```powershell
./scripts/Test-PocketArcCandidateProbeSummary.ps1
```

It uses `tests/fixtures/pocket-arc/ranked-sparse-black-probe-report.json`, including `classifier.evidence`, and writes only to a unique temporary directory.
