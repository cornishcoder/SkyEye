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

Run the nested O08 schema regression check offline with:

```powershell
./scripts/Test-PocketArcCandidateProbeSummary.ps1
```

It uses `tests/fixtures/pocket-arc/ranked-sparse-black-probe-report.json`, including `classifier.evidence`, and writes only to a unique temporary directory.
