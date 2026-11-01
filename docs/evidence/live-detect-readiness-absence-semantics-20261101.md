# Live Detect absence and current-readout semantics — 1 November 2026

## Target and result

Target `READINESS-ABSENCE-SEMANTICS-CONTROLS` passes **5/5 focused controls**. The full `SkyEye.AgentUi.Checks` runner exits 0 and prints **24 passing check groups**, including this target. These are software behavior checks, not detector accuracy or physical acceptance.

The implementation separates two facts that had been conflated in the Home readout:

- After a trusted baseline, stable cue or black loss can still settle as a `possible_pot_settled` event candidate. That history-conditioned event behavior is preserved.
- An uncertain or obstructed frame cannot be shown as a clear, settled current inventory. The readout now requires the strict current-inventory predicate as well as a matching settled baseline; otherwise the existing unknown readout retains last-clear and last-settled context.
- An uncertain first view missing cue and black remains `baseline_pending` with no settled baseline.
- A fully clear, settled inventory remains eligible for the current-state readout.

The pre-existing frame-978 Development baseline is reused for the separate intentional-absence scoring case: its complete review marks black absent and the explicit-tolerance Workbench score is 15/15 visible balls. That does not establish a D2 live baseline or physical readiness; the score was not rerun here. See [frame 978 screening baseline](frame978-screening-baseline-20261101.md).

## Implementation and test identity

The change was tested in a worktree based on `aad85a4c1f3a59a162c429632329d97d29ff9c5d` with the implementation and test files in this report. The readout policy is in `src/SkyEye.Companion.Hybrid/LiveDetectInventoryReadoutPolicy.cs`; `ShellLiveDetect` uses it for strict-clear calculation and settled-current presentation. The focused checks are in `tests/SkyEye.AgentUi.Checks/LiveDetectInventoryReadoutChecks.cs`.

Command:

```powershell
dotnet run --project tests/SkyEye.AgentUi.Checks/SkyEye.AgentUi.Checks.csproj -c Release --no-restore
```

Result: **exit 0**. Five focused controls passed: unexplained initial cue/black absence, cue absence after a trusted baseline, black absence after a trusted baseline, obstruction, and a clear settled inventory. The two post-baseline absence controls each verify that the tracker still emits a settled possible-pot candidate while the current readout policy returns no current inventory.

Companion integration build:

```powershell
dotnet build src/SkyEye.Companion.Hybrid/SkyEye.Companion.Hybrid.csproj -c Release --no-restore -m:1 --nologo --verbosity minimal
```

Result: **success, 0 errors, 13 warnings, 1:53.28**. Warnings covered generated OpenCV member hiding, unavailable WebRTC binding types, two diagnostic-labeler JS interop calls, and duplicate native-library package entries. No APK was installed and no app or camera package was changed.

## Device and acceptance metrics

A read-only camera `/status` read after the build at `2026-11-01T18:42:53.6510846Z` reported `Stopped`, revision 64, timeline `c5d9a60d9fb44210888abaabc77545ed`, 39,676 completed frames, 139 clips, zero viewers and no host owner. No capture or clip was started.

This code change does not change the detector, analyzer configuration, canonical state or any acceptance threshold. The full software scorecard remains **3/3 suites PASS** (carried forward), the focused motion-feed remains **20/20 PASS** (carried forward), production inventory remains **0/3 PASS**, and physical **D1/D2/E remain 0/3 proven**. The 24 Agent UI check groups and this focused 5/5 target are separate software-check denominators. The next detector evidence target is still `BOUNDARY-MATCHED-NEGATIVE-CONTROL`; no adequately lit, same-location reviewed negative is available, and the user reported the current lighting is darker. Keep camera capture stopped until that control can be staged and reviewed.
