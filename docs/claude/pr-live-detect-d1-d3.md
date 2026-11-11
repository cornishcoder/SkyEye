# PR draft: Live Detect D1-D3, desktop detector runner, obstruction and packed-black fixes

Branch `claude/live-detect-d1-d3` into `master`: 47 commits, 95 files, about 10,300 lines added, 122 removed
(11 November 2026). Draft only: nothing has been posted. Companion 0.8.69 / code 77 is installed on the
Pixel 11 and has run live on 0.8.63 to 0.8.68 builds of this branch.

## Summary

- **Live Detect state (D1, D2):** the pocket-colour flag is scoped to its pocket region instead of the whole
  table, and a first baseline can form without the black or cue ("not seen" instead of no baseline).
- **D3 settled-change evidence, shadow only:** compares settled table states before and after activity and
  proposes holds, inferences and departures beside the reducer. Nothing it produces changes Live Detect's
  output (golden-shadow replay identical on every device log).
- **Desktop detector runner (T2):** the Android detector sources build on Windows against an OpenCvSharp
  shim, with parity 4/4 against device replay proofs, and `detect`, `audit`, `analyze` and settled-change
  commands for offline scoring.
- **Detector fixes, all behind `VisionSettings` flags that replay the old output when off:** surplus-only
  inventory demotion (T5), packed-stage cue handover, three obstruction-mask fixes, packed-cluster local
  unknowns, packed-black dark-body check and adjacent-ball occlusion.
- **Companion fixes:** a Live Detect stop on a not-seen ball (NaN in the log), and the source-frame budget
  now covers the moments around shots.

## Changes by area

### Detector (`src/SkyEye.Vision.Android`, identity suffixes in brackets)

| Change | Flag (default) | Desktop evidence |
|---|---|---|
| Surplus-only demotion of extra reds/yellows | `SurplusOnlyInventoryDemotion` (on) `-surplus-sat40` | colour accuracy 0.970 to 0.996 on 35 labelled frames |
| Packed-stage cue handover | `PackedCueHandover` (on) `-cuehandover75` | cue found 25/28 to 28/28 |
| Warm-filled cast shadow is cloth (obstruction mask) | `ObstructionShadowTolerance` (on) `-obstruction-shadow60` | false obstructions 36 to 22, controls kept |
| Only unknown balls near the rack make it unresolved | `PackedClusterLocalUnknowns` (on) `-packed-local-unknown25` | afternoon device racks 5/6 to 0/6 obstructed |
| Low-chroma dim cloth is cloth (obstruction mask) | `ObstructionDimChromaTolerance` (on) `-obstruction-chroma14` | 22 to 20, controls kept |
| Packed black on occlusion evidence must be dark | `PackedBlackOcclusionDarkBody` (on) `-packed-black-darkbody28` | black-absent dim rack control 5/5 false black to 0/5 |
| Packed black edge against a touching ball counts as occluded | `PackedBlackAdjacentBallOcclusion` (on) `-packed-black-adjacent125` | +9 real rack blacks, obstructed 20 to 14 |

Labelled set throughout: 0 false positives; black recall 14/28 unchanged (the remaining misses are
mostly one cushion spot that single-frame detection cannot reach without lowering the 21/32 gate).

### D3 settled-change evidence (shadow)

- `SettledChangeMath` (Core), `SettledChangeComparer`, `SettledChangeProcessor`, `SettledSegmentTracker`,
  `SettledChangeLiveObserver`, cost guard (median over 20 passes above 40 ms disables it).
- `LiveDetectSettledChangePolicy`: U1 hold, U2/U5 infer black/cue, U3 departure support (now also for a
  not-seen ball that leaves), U4 pocket regions; unexplained changes only on a trusted layout.
- Region centres from ball pixels, not the cast shadow (`SettledChangeSettings.ShadowFreeCentre`).
- Log schema 15 `frame.settledChange`; raster sidecars in `live-detect/settled-change/`.

### Companion (`src/SkyEye.Companion.Hybrid`)

- Not-seen ball states have null coordinates (NaN stopped Live Detect on 0.8.65).
- Source frames: up to 12 per run, interval samples only at the start, change frames 5 s apart, a frame at
  every settled-change comparison with a ball-sized change.
- Version 0.8.62 / code 70 to 0.8.69 / code 77.

### Tools and fixtures

- `tests/SkyEye.Vision.Desktop.Runner`: parity, detect, classify, audit, analyze, settled-change,
  settled-change-clips, settled-change-rasters, settled-change-selfcheck.
- `tests/SkyEye.AgentUi.Checks`: `--golden-shadow`, `--pot-veto` replays of device logs.
- Fixtures: `tests/fixtures/detection-corpus/manifest.json` (T1, 495 rows), `obstruction-hand-controls.json`
  (31 dim hand/arm frames), `black-absent-dim-rack.json` (5 frames). Pixels stay under git-ignored `work/`.
- Python: corpus manifest builder and scorer, D3 clip/still scoring, T4 prototype with ball-centred
  reference.

## Evidence

Software (newly executed at the last commit): `Build-SkyEye.ps1 -Target All` 0 errors; `SkyEye.Checks` and
`SkyEye.AgentUi.Checks` pass; Layout03 parity 4/4.

Physical (Pixel 11, 11 November):

| Check | Result |
|---|---|
| D3 shadow wiring (0.8.63) | evidence on every pass, median about 14 ms; chain-anchor defect found and fixed |
| D3 chain anchor (0.8.66) | PASS: intact through rack, break and settle |
| Packed-black rules (0.8.67) | PASS: rack 155/155 clear with the black, red pot detected and settled correctly |
| Staged cushion black (0.8.68) | D3 inferred the black where Live Detect called a false pot |

Detector inventory cases 0/3 and physical gates D1/D2/E 0/3: unchanged by this branch.

## Open

- D3 rules stay in shadow. U1 cannot veto an accepted pot as designed; a "pot without motion" rule is
  proposed (`docs/claude/d3-pot-veto-analysis.md`) but needs lone slow-pot motion measured first.
- Remaining false obstructions in dim warm light (#10 to #16) need warm-light hand controls.
- The top-cushion black at (1.48, 0.067) is still missed by the single-frame detector.

## How to review

1. Read `docs/live-detect-plan.md` (top entries) and the ledger
   `docs/evidence/live-detect-targeted-test-ledger-20261101.md`.
2. Each detector change has a write-up under `docs/claude/` with its scores and the flag that turns it off.
3. Reproduce: `scripts\Build-SkyEye.ps1 -Target All`, then the two check suites, then
   `dotnet run --project tests/SkyEye.Vision.Desktop.Runner -- detect --manifest tests/fixtures/detection-corpus/manifest.json --output OUT.jsonl`
   (needs the git-ignored `work/` evidence).
