# O09 Development layout and lighting batch plan — 1 November 2026

## Status

The next physical target is `O09-DEVELOPMENT-LAYOUT-LIGHTING-EXPANSION`. The user reports the table is off for tonight and the lighting stops play. No capture was started for this plan. Current physical metrics remain D1/D2/E **0/3 proven**; measured lighting strata remain **0**.

The preceding frozen-checkpoint probe remains `O09-TRANSFER-NEW-CONTROLS-PROBE` **FAIL 0/7** (109 GT, 36 TP, 664 capped FP, 73 FN). It is not a play-test candidate. The current Development export has five transitive setup/session groups; its automatic 20% assignment leaves one validation group with two frames/31 boxes, all from the lower-left controls. Another training run on that split would not measure useful layout/light transfer.

## Predeclared split plan

The new versioned plan is [live-detect-o09-development-split-plan-20261101.json](live-detect-o09-development-split-plan-20261101.json), for future corpus `live-detect-20261101-black-cue-development-v4-o09-reviewed-layouts-v3`. It keeps the five existing groups train-only and adds 12 planned layout groups:

Plan SHA-256: `6c0d224aca32e5781c03a3452560aac5fd13a25311cd2ea241791ffe7f34d834`.

| Lighting stratum | Train layouts | Validation layouts | Total |
| --- | ---: | ---: | ---: |
| `normal-pool-room` | 3 | 3 | 6 |
| `warm-dim-playable` | 3 | 3 | 6 |
| **New layouts** | **6** | **6** | **12** |

Each new layout has its own stable `setupGroupId` and `layoutId`. A present/absent series stays in the same setup group and role. The planned partition is Development train/validation only; no O01 Holdout labels or pixels are used. The prior v2 split and reports remain immutable. The v3 acceptance contract must bind the new corpus and plan hash before training or evaluating another candidate.

Workbench preflight ran against the committed plan and returned **17 setup groups**, **12 planned layouts**, and **3 layouts/groups in each of the four role-by-light cells**. This is a plan-shape result, not evidence that either lighting condition has been collected. After capture, `export-yolo --split-plan` performs the stronger check: exact observed/planned group equality, transitive session/setup isolation, no session crossing train and validation, and at least three independent connected components per role/light cell. If a recording session joins different roles, export fails closed.

The focused Observation Checks report `O09-PREDECLARED-DEVELOPMENT-SPLIT-PLAN` **PASS 7/7** and `O09-PREDECLARED-DEVELOPMENT-SPLIT-EXPORT` **PASS 6/6**. The planner checks role preservation, order invariance, missing/unexpected groups, under-covered role/lighting assignments and a session that bridges train and validation. The synthetic export checks plan-hash binding, balanced train/validation assignment, per-sample layout/light metadata, and rejection of a frame whose lighting tag conflicts with its planned stratum. The existing `O09-SETUP-GROUP-SPLIT-ISOLATION` remains **PASS 8/8**. The first suite run found that an unexpected group reached dictionary lookup before exact-set validation (`KeyNotFoundException`); the guard was moved ahead of role resolution and the focused run then passed. The default legacy hash split remains unchanged.

Preflight command:

```powershell
dotnet run --project src/SkyEye.Observation.Workbench/SkyEye.Observation.Workbench.csproj -c Release --no-restore -- validate-o09-split-plan docs/evidence/live-detect-o09-development-split-plan-20261101.json
dotnet run --project tests/SkyEye.Observation.Checks/SkyEye.Observation.Checks.csproj -c Release --no-restore
```

The post-capture exporter will receive the same plan file using `--split-plan`; its report binds the plan SHA-256 and includes per-sample lighting/layout IDs. Its post-capture component audit, not this preflight, determines whether setup groups are still independent.

## Staging outline

Collect one reviewed anchor per layout at minimum. Use this staging matrix so the six layouts in each stratum cover the required scene types while keeping validation arrangements distinct from training:

| Stratum / role | Layout 01 | Layout 02 | Layout 03 |
| --- | --- | --- | --- |
| Normal / train | Full rack; rotate cue/black markings | Open spread with central balls | Varied spread; black near lower-left pocket and cue nearby; matched controls |
| Normal / validation | Partial rack with different marking orientation | Touching/close pair near a cushion, remaining balls spread | Distinct full-table arrangement; same lower-left black/cue control position |
| Warm / train | Full rack with changed marking orientation | Open spread with rail-adjacent balls | Varied spread; black near lower-left and cue nearby; matched controls |
| Warm / validation | Partial rack with another arrangement | Pocket-lip and touching cases across the table | Distinct full-table arrangement; same lower-left black/cue control position |

The `layout-03` group in each role/light cell is reserved for matched controls. Keep its non-target balls, cue/black position and light fixed through this sequence:

1. Black and cue present.
2. Remove only the black.
3. Restore only the black and remove only the cue.
4. Restore only the cue.

Those four groups add three control frames each beyond the 12 layout anchors, for **24 frames total at the one-anchor minimum**. Each changed table state gets bounded capture, then capture stopped and `/status: Stopped` verified before the next move. For a valid matched pair, retain the same lighting, camera/calibration identity, cloth reference patches and all untouched balls; record source receipts and review saved images together. A lighting or camera change splits the setup group and invalidates the paired claim. Measure and label the actual normal/warm condition; do not assign a stratum from its name alone. If warm-dim light is not playable, do not collect it as supported evidence.

The batch covers train and validation under both contract strata without splitting siblings. It is Development evidence only. It does not open Holdout or pass detector inventory, D1, D2 or E. No threshold or model run is scheduled until labels, the v3 corpus, acceptance contract and split lock are frozen and the export reports the planned balanced coverage.
