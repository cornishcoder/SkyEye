# Local raw-model proposal gate replay — 31 October 2026

This replay answers a narrow question: can the saved raw-image centres from PoolTableModeller pass through SkyEye's existing candidate checks on the same source frames? It does not run an independent raw-frame detector, and it does not modify live table or match state.

**Exact-profile follow-up — 1 November:** the daylight positive set was replayed through the captured width-800 `.4` analyzer in the [new exact-profile note](../code70-raw-proposal-exact-profile-20261101.md). This does not change the status of the original runs below: they used width 1000 at `.65`, remain configuration-mismatched, and their separate dim negative set is not a matched control.

## Method

The replay validates the model file's `sourceSha256` against the exact saved PNG and checks the decoded frame dimensions. Each raw centre is mapped through the receipt's `TableCalibration.Map` and `AnalysisCoordinates.FromTable`; candidate radius is the canonical 27px. The proposal then uses the same `ProcessCandidate` sequence as Hough candidates: table margin, pocket exclusion, duplicate checks, cloth rejection, boundary fit and support, fitted-pocket recheck, reclassification and final duplicate/body checks. The report also samples SkyEye's existing colour classifier at each mapped proposal using the exact HSV buffer from that analyzer invocation.

The path is internal and diagnostic-only. The normal `Analyze` entry point does not supply model proposals. No camera capture, Cast/TV action, camera package update, production app update, or match-state write occurred. Pixel 11 ran the isolated `uk.co.skyeye.visionchecks` replay while the mounted camera `/status` remained `Stopped`.

## Results

The six daylight frames (1365, 1425, 1457, 1555, 1645 and 1736) are repeated views of one black-present pocket layout. In all six, SkyEye's baseline and proposal-assisted inventory stayed at 14 balls: 7 reds and 7 yellows, with readiness still uncertain. Fourteen of the local model's 16 proposals duplicated existing SkyEye candidates. The cue and black were never admitted:

| Proposal | SkyEye classifier | Existing boundary gate | Outcome across six frames |
| --- | --- | --- | --- |
| Cue (`yolov8n`) | `cue`, quality 0.895–0.897 | Minimum 24/32; measured 16–19/32 | Rejected for insufficient support in all six |
| Black (`pocket-black-specular`) | `black`, quality 0.829–0.835 | Minimum 24/32; measured 14–16/32 in four frames, no fit in two | Rejected in all six |

This puts the observed daylight miss after colour recognition: SkyEye recognizes the proposed cue and black, but their pocket-overlapped contours do not meet the current global boundary-support rule. The local-model black proposal is produced by a hand-written `pocket-black-specular` fallback, not YOLO.

The six dim empty-pocket frames (978, 1072, 1105, 1150, 1179 and 1198) are repeated views of a different capture session and lighting condition. Baseline and proposal-assisted inventory stayed at 15 balls (cue, 7 reds and 7 yellows) in every frame, with readiness still uncertain. The cue proposal was accepted in five frames at exactly 24/32 support and was a duplicate in one; it did not change final inventory. The model's extra `chromatic-peak` red appeared in two frames. Both proposals were rejected at the table margin, and SkyEye's same-frame classifier diagnostic called that location `unknown` with quality 0.

The positive and negative sets are not same-light, same-session controls, and their centre labels remain provisional. These results localize a failure; they do not validate a detector improvement or justify lowering the boundary gate.

## Model-seeded pocket-arc probe

To check whether a local arc can distinguish the pocket-overlapped black from a dark empty pocket, the same unreviewed PoolTableModeller black proposal `(99,897)` in source pixels was mapped through calibration and used as the reference point for one daylight black-present frame and one dim empty-pocket frame. The probe is diagnostic-only; it does not add candidates or change analyzer/game output. Reports explicitly mark the anchor as `unreviewed-model-proposal` so it cannot be mistaken for a human-reviewed centre.

| Source frame | Result at mapped anchor | Existing SkyEye colour classifier | Pocket-aware partial-arc diagnostic |
| --- | --- | --- | --- |
| Daylight black-present, frame 1365 | Candidate centre `(64.98,41.04)`, 8.41px from the proposal; 11/32 supported sectors, 6 contiguous, 56.25° span | `black`, quality 0.869 (not a probability) | Diagnostic checks passed |
| Dim empty pocket, frame 978 | Best candidate centre `(43.98,35.04)`, 20.74px from the proposal; 5/32 supported sectors, 3 contiguous, 22.5° span | `black`, quality 0.900 (not a probability) | Diagnostic checks failed |

This is a useful lead: colour sampling alone calls the empty pocket black too, while the local arc checks reject that candidate and retain the black-present one. It is only one frame per side, from separate lighting timelines, with an unreviewed model anchor; the reports say no matched absent-pocket control is available. The positive diagnostic fit also has only 11/32 global sectors, far below production's 24/32 gate. Do not enable it in production or relax the global gate. Next, review the black centre and repeat the probe on a same-light, same-pocket present/absent pair plus more pocket positions.

The source frame SHA-256 values are `602e00c4eb9fc92f3b124106137d8ca7c56f932e107f995f1f2c7d824dd25de4` (positive) and `b368348c63d924f5c7834e8e849b7287225ab525b470bdc3b85f7a8278cbbe70` (empty pocket). Run `arcmodelp3-165347` report SHA-256 is `00aa76c842111d80ae2cefbf53330c937be8e46acac4f817ce7db7794207310a`; `arcmodeln3-165402` report SHA-256 is `2f8ace6026158f317f168f0dd9a308cb5ca96c9525b0fd77b69ab38f09248153`. Both reports and manifests are retained under `work/production/evidence/local-raw-proposal-gates-20261031/`.

### Six-frame follow-up — 31 October

The same fixed anchor `(99,897)` was replayed across all six saved daylight positives and all six dim empty-pocket controls. It is the stable black proposal emitted by the local model on the positive frames (centres were `(99,897)` in five frames and `(98,897)` in one); it is deliberately applied to the corresponding empty-pocket region, where the model itself emits no black proposal.

| Set | Frames | Arc diagnostic | Candidate offset from anchor | SkyEye classifier at candidate |
| --- | --- | --- | --- | --- |
| Daylight black-present | 1365, 1425, 1457, 1555, 1645, 1736 | Passed 6/6; 8–13/32 supported sectors, 5–6 contiguous, 45–56.25° | 6.52–12.89px | `black` 6/6; quality 0.865–0.889, not probability |
| Dim empty pocket | 978, 1072, 1105, 1150, 1179, 1198 | Failed 6/6; 4–5/32 supported sectors, 2–3 contiguous, 11.25–22.5° | 10.49–20.74px | `black` 6/6; quality 0.900, not probability |

This repeats the single-frame separation across the retained frames and makes the point of the test clearer: SkyEye colour classification calls the pocket candidate black in both sets, while the local arc support rejects every empty-pocket candidate. The unchanged full analyzer still returns 14/16 (7 red, 7 yellow) and uncertain/layout-uncertain on every positive, and 15/16 (cue, 7 red, 7 yellow) and uncertain/layout-uncertain on every negative. The reports mark the replay configuration as mismatched to the capture, so these are not current-production detector acceptance results. These are six repeated views of each fixed layout, not independent scenes or same-light controls; the positive and negative captures have different lighting and session histories. The fit remains an evaluation-only diagnostic and never changes `VisionAnalyzer` output. Production's 24/32 global support gate is unchanged, and these results do not establish pocket-wide selectivity. In particular, the separate target-free seed sweep found arc-qualified hypotheses at other empty-pocket locations, so all-pocket and other-colour negatives remain necessary.

The Pixel 11 replay ran while the mounted camera remained `Stopped` (final `/status`: revision 94, 139 clips); no live capture or Cast/TV action occurred. Replay manifests retain the camera readback with read tokens and URL query strings removed. Positive run `modelarcpos6-1710-170232` report SHA-256 is `3892059ccdc73e78ae28f02fcd3961335e07f27034d431fb6fbbea3dc97d6763` and manifest SHA-256 is `8f7a5de2aacaca1e56f78a477294b7cb7d7eee44cd83cb18fdc1515cd655a93b`. Negative run `modelarcneg6-1711-170335` report SHA-256 is `d8baf8970f00a71a70e7751f4962a4cfe0ebbe1c95ff8555c154ee5fccb57b9c` and manifest SHA-256 is `66b4552d30086aa373cf3a0bf5357f605c46e789b6a2c64df54ab7f4de766c38`.

## Performance clarification

PoolTableModeller reported 4,572ms and 4,671ms end-to-end for these frames. The laptop has an RTX 3060 Laptop GPU, but the service process used Python 3.13 with CPU-only `torch 2.15.0+cpu` and `torch.cuda.is_available()` was false. Those timings therefore do not measure the 3060's inference speed, and they are not a Pixel 11 benchmark. The local service also listens on `127.0.0.1:5001`; the phone cannot call it directly.

## Next work

Keep the global 24/32 gate unchanged. The model-seeded arc probe passed all six retained black-present frames and rejected all six retained empty-pocket frames, while the classifier called every candidate black. This is encouraging but still inconclusive: the two sets are repeated views under different lighting/session conditions, and target-free searches produce arc-qualified hypotheses elsewhere. Obtain a reviewed black centre and a same-light present/absent pair at the same pocket. Then test all six pockets with black-absent and other-colour controls before considering any production integration.

## Reproduction records

`scripts/Run-PocketLipBoundaryAudit.ps1 -RawProposalReplay -RawProposalDirectory <model-run>` performs hash-checked saved-frame replay and accepts up to 12 frames from one source timeline per run. The two six-frame reports below include the per-proposal audit and same-frame SkyEye classifier evidence. Work-tree reports and manifests are retained under `work/production/evidence/local-raw-proposal-gates-20261031/`.

- Daylight black-present run `rawclpos6-164614`: report SHA-256 `4f9789bac8a98724db78900199586fddc04e67bf810fc3dba7de11ac1b3e39c6`; manifest SHA-256 `1e748c01de6efab2241bd8b0b521eb1bf9b0dea1abbe641f47543e1b24bc2239`.
- Dim empty-pocket run `rawclneg6-164655`: report SHA-256 `743256a5d63446ae54b5a960d5f29951e903a02f604e24bb644eb4c93adfb92f`; manifest SHA-256 `b19b0c2e3d0ea4e0ec9bcf9498a52dc85703a08bb3e7ce8a934a2a1f096e6ff8`.

The source-centre proposal comparison and its manual-label caveats are documented in [the local model centre check](../local-model-raw-centre-check-20261031/README.md).
