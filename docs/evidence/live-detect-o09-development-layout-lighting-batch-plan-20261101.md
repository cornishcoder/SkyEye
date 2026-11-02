# O09 Development layout and lighting batch plan — 1 November 2026

## Status

The 2 November normal/train Layout02 spread produced one reviewed Development anchor. Raw receipt integrity passed **7/7**; selected frame1085 was reviewed from raw pixels, then the current-source spatial baseline scored **16/16** reviewed balls with no extras/misses. Its physical verifier failed `Unsettled (TimedOut)` after 4001.775ms. Layout03 has now captured and raw-image-reviewed all four black/cue states: receipt integrity **30/30**, spatial review **4/4**, and current-source baseline **56/62 TP, 0 FP, 6 FN**. Every frame missed the cue; black was missed in all three present frames. Its verifier remains **0/4** stillness passes. An aggregate detector count was visible during reconnect preflight, so these labels retain aggregate-output exposure and are not a strictly blind review. See the [Layout03 capture report](live-detect-o09-normal-layout03-four-state-control-20261102.md) and [current-source baseline and trace](live-detect-o09-normal-layout03-spatial-baseline-20261102.md).

Current counts: reviewed Development anchors **2/12** (Layout02 and Layout03); staged states captured **6/24** (Layout01 anchor attempt, Layout02 anchor, and the four Layout03 states); **1** no-start attempt was retried for the same Layout03 state; additional matched-control states label-frozen **3/12** (the three non-anchor states in Layout03); criteria-complete lighting strata **0/2**; detector inventory acceptance **0/3**; Holdout unopened; D1/D2/E **0/3** proven. Layout01 requires an independent replacement because its review was proposal-conditioned. **19 acquisitions remain**: 18 unattempted states plus that replacement. Layout03 remains **0/4** stillness passes; receipt tracing shows incomplete edge-template coverage in 26/30 subsequent samples, with all source gaps below the 800ms limit. The immediate target is `O09-NORMAL-TRAIN-LAYOUT03-EDGE-TEMPLATE-COVERAGE-EXPERIMENT`; the cue proposal experiment follows for the separate detector inventory miss. Future table changes should continue as the predeclared one-operation-per-state sequence with capture stopped and `/status:Stopped` checked between changes.

Earlier that day, both lighting conditions were reported playable and the camera charged. The normal-pool-room condition was described as daylight with no direct glare; complete measured lighting metadata remains **0/2**, and the warm-dim stratum still needs its repeatable measured condition. See the [lighting preflight](live-detect-o09-lighting-metadata-preflight-20261102.md) and prior [Layout01 report](live-detect-o09-normal-layout01-anchor-20261102.md).

### Capture-unit clarification

The frozen matrix contains **12 distinct layout groups** and **24 staged table states**: one anchor state for each layout, plus three additional black/cue control states for each of the four layout-03 groups. The count `24` is a count of staged states, not a count of saved images. The existing Home **Capture verifier frames** operation normally saves 5–7 image/receipt pairs per bounded operation. Therefore a complete set of successful verifier operations can retain **120–168 raw images**; this does not create 120–168 independent layouts or review labels. Select one representative source image per staged state by the lower median valid source PTS, before looking at detector output. Review/score only those 24 selected frames initially; retain the other burst images as unreviewed and exclude them from spatial accuracy and independent-window denominators unless separately reviewed and predeclared. An operation that saves fewer than five frames fails the verifier/stillness operation. If it wrote at least one hash-valid source/receipt pair, retain that image as a **spatial-only review candidate**; after independent review it may support a spatial label, but cannot count as a verifier pass, stillness/readiness result, soak, or request/unsafe window. Do not repeat solely to reach five when one spatial anchor is the planned evidence; retry only for a specific capture-integrity issue with a recorded reason. The single-frame optional labeler exists, but it requires manually labelling every visible ball in-app before saving; the bounded verifier path preserves source/receipt pairs and permits grouped visual review.

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

The v3 acceptance contract is frozen at [live-detect-o09-acceptance-contract-v3-20261101.json](live-detect-o09-acceptance-contract-v3-20261101.json). It preserves every substantive v2 acceptance gate and binds the new Development corpus plus this split-plan hash. The v2 source contract is archived unchanged at [live-detect-o09-acceptance-contract-v2-20261101.json](live-detect-o09-acceptance-contract-v2-20261101.json). `O09-V3-ACCEPTANCE-CONTRACT-BINDING` passes **10/10** focused integrity checks. This binds planning metadata only: capture is pending, Holdout remains unopened, and D1/D2/E remain `not-run`.

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
| Normal / train | Full 16-ball rack; use marking orientation A | Full spread with visible balls staged across all six pocket/cushion regions; include a close pair, an opposite-colour ball near a pocket mouth, empty mouths, and rail chalk/shadow negatives | Distinct full-table spread; black at the reviewed lower-left point and cue nearby; matched controls |
| Normal / validation | Reviewed reduced layout with cue intentionally absent and black present; vary marking orientation | Different full spread covering all six pockets/cushions; include one touching or partly occluded cluster whose hidden centres stay unlabelled | Distinct full-table spread; same lower-left black/cue control position; matched controls |
| Warm / train | Full 16-ball rack; use a different marking orientation from normal/train | Full spread with visible balls staged across all six pocket/cushion regions; change which colours sit near mouths and add a separate rail/chalk/shadow negative | Distinct full-table spread; black at the reviewed lower-left point and cue nearby; matched controls |
| Warm / validation | Reviewed reduced layout with black intentionally absent and cue present; use a distinct arrangement | Different full spread with pocket-lip and close/touching cases across the table; include one partly occluded cluster with hidden centres unknown | Distinct full-table spread; same lower-left black/cue control position; matched controls |

The `layout-03` group in each role/light cell is reserved for matched controls. Keep its non-target balls, cue/black position and light fixed through this sequence:

1. Black and cue present.
2. Remove only the black.
3. Restore only the black and remove only the cue.
4. Restore only the cue.

Those four groups add three control **states** each beyond the 12 layout anchors, for **24 staged states total at the one-reviewed-image-per-state minimum**. The layout-02 anchors make the six pocket/cushion regions explicit in all four lighting-by-role cells; layout-01 covers full racks or reviewed intentional absence; layout-03 supplies the four matched black/cue sequences. Review each selected frame's actual visible centre and expected-empty point separately. Freeze manual spatial labels from raw pixels before opening detector output, then show the raw frame and labels for review; any post-unblinding user check is proposal-conditioned and cannot be exported as independent ground truth. Each changed table state gets one bounded capture operation, then capture stopped and `/status: Stopped` verified before the next move. Keep each layout/setup group in its own recording-session boundary so sessions cannot merge separate planned groups; restart inside a matched sequence as needed, retaining all sibling controls under the same setupGroupId/role/light. For a valid matched pair, retain the same lighting, camera/calibration identity, cloth reference patches and all untouched balls; record source receipts and review the selected images together after the batch. A lighting or camera change splits the setup group and invalidates the paired claim. Measure and label the actual normal/warm condition; do not assign a stratum from its name alone. If warm-dim light is not playable, do not collect it as supported evidence.

The batch covers train and validation under both contract strata without splitting siblings. It is Development evidence only. It does not open Holdout or pass detector inventory, D1, D2 or E. The exact v2→v3 contract chain and split-plan hash are reproducibly checked by `python tests/O09AcceptanceContractV3.Checks.py`. No threshold or model run is scheduled until labels are independently reviewed, the v3 corpus/split lock is frozen, and the post-capture exporter confirms actual transitive group isolation and planned coverage.
