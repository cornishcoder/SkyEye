# Calibration reconnect preservation — 1 November 2026

## Target and result

`CALIBRATION-RECONNECT-PRESERVATION` passes **9/9 focused policy and geometry checks**. The full `SkyEye.AgentUi.Checks` runner exits 0 with **24 passing check groups**. The Hybrid Production Release build succeeds with **0 errors and 9 warnings**. These are software checks; no camera capture, installation, or physical acceptance was performed.

## Change

When a live connection has a saved calibration that has not been confirmed for that connection, table analysis is gated. A fresh corner proposal and a missing proposal both leave the gate closed. The table controls now offer two explicit paths: confirm that the camera mount has not moved, or recalibrate. Confirmation reuses the saved corners after the reconnect check; a new proposal does not replace them. Explicit recalibration forgets the stored calibration before collecting or editing corners. Stream resolution/crop configuration changes continue to invalidate calibration through `FramingGeometryFence`.

The pose check is an explicit user confirmation, not a sensor measurement. Equal corner coordinates and a new calibration identifier are not treated as evidence that the physical mount stayed fixed.

## Evidence and rerun rule

The focused checks cover: saved calibration with an unverified pose and either proposal outcome requires confirmation; confirmed same-connection pose retains saved geometry; a new mount uses a valid proposal or requests manual calibration; transient unknown stream dimensions retain stored geometry; and known resolution/crop changes invalidate it. The Agent UI suite and Release build were run on the working tree based on source commit `5dabe5f`.

Rerun this target if reconnect gating, calibration persistence, explicit recalibration, the user-confirmation control, or stream-geometry invalidation changes. Do not repeat the unchanged test. Physical control receipts must still bind identical corner coordinates, transform, source geometry, and analyzer configuration across matched steps.

## Metrics and device state

- Existing scorecard: software suites **3/3 carried forward**; focused motion feed **20/20 carried forward**; detector inventory **0/3**; physical D1/D2/E **0/3 proven**. This calibration guard does not improve detector recall or readiness coverage.
- Camera `/status`: **Stopped**, revision **64**, read before the Release build. No capture or table action occurred.
- The last read-only Pixel 11 foreground check showed Twitter; Home `Start live session` was not verified in this cycle. No app launch, install, or package update was attempted.
- Release build warnings: five generated OpenCV member-hiding warnings, two existing Blazor JS interop warnings, and two duplicate native-library packaging warnings.

## Next target

Continue the continuation prompt's offline comparison work without repeating saved tests. Reuse the completed frame-978 screening baseline and continue with the next unclosed provenance/scoring requirement or a measured detector failure. Before any new physical controls, batch the stages and confirm stable playable lighting; current O09 training-data readiness remains blocked and D1/D2/E remain open.
