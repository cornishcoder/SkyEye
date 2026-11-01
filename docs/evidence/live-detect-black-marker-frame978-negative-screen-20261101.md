# Frame 978 frozen black-marker negative screen — 1 November 2026

Targeted test `BLACK-MARKER-CUE-REVIEWED-NEGATIVE-EXPANSION` applies the unchanged whole-frame bright-circle/dark-neutral-annulus feature to frame 978, a user-reviewed complete Development inventory that is outside corpus-v4. The frozen thresholds and input hashes were recorded in the predeclaration before execution.

## Result

| Measure | Result |
| --- | ---: |
| Reviewed inventory | cue 1, red 7, yellow 7, black 0 |
| Bright circular components before annulus filtering | 7 |
| Selected black-marker candidates | 0 |
| Collisions with reviewed visible-ball centres within 20 source px | 0 |
| Whole-frame black-absence screen | **PASS (1/1)** |

This adds one reviewed Development negative screen. It does not score black-centre distance because the reviewed inventory has no expected black centre. Zero candidates is consistent with the complete 15-ball visible inventory and the black-absent review.

## Provenance and limits

- Source: frame 978, PTS `48922017388` µs, timeline `2ea48e0c35cd474eb627acf970a7eec5`, 1920×1080.
- Source PNG SHA-256: `b368348c63d924f5c7834e8e849b7287225ab525b470bdc3b85f7a8278cbbe70`.
- Review record SHA-256: `4f8a2c1139b6e007e4a4789b1e6a9592b4d880d042772a709b36b4ff210b2fc1`.
- Source receipt SHA-256: `653e1ea4bc6c927ba592ad796a6c084e49103c27003d13e5d23d0c98a3144034`.
- Predeclaration SHA-256: `d66e27229b1144e53c0782163ea4f7e78355640be92fffefc2dc97d047d5b12d`.
- Evaluator SHA-256: `d5287927cf7145065be831d0e8d365896c145e55c44303718e630e07ac219c4f`.
- Results SHA-256: `147cb341fd9c7103ff86c12fe1c43734093c475ad84bd8f6bd4a90390549f430`.
- Raw result: `work/o08/frame978-development-v1/physical-controls/black-marker-negative-screen-v1/results.json`.

The review confirmed assistant-drafted centres and retains rough 4–7 source-pixel uncertainty; no numeric tolerance was set. The frame is dim, is not a same-light matched control, and is not a Holdout. The copied calibration corner provenance limits geometry-derived interpretation. This screen does not change the frozen corpus-v4 score of 8/10, production detector inventory of 0/3, or D1/D2/E status of 0/3 proven. It is not a detector acceptance or physical readiness result.

Command:

```powershell
python scripts/evaluate-black-marker-reviewed-negative.py `
  --predeclaration work/o08/frame978-development-v1/physical-controls/black-marker-negative-screen-v1/predeclaration.json `
  --output work/o08/frame978-development-v1/physical-controls/black-marker-negative-screen-v1/results.json
```

Exit code: `0` (declared negative screen passed). The earlier frame-978 `Observation.Workbench baseline` milestone is already complete in [the screening baseline report](frame978-screening-baseline-20261101.md); it was inspected and not rerun.
