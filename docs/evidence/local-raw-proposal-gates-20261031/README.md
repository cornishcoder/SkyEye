# Local raw-model proposal gate replay — 31 October 2026

This replay answers a narrow question: can the saved raw-image centres from PoolTableModeller pass through SkyEye's existing candidate checks on the same source frames? It does not run an independent raw-frame detector, and it does not modify live table or match state.

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

## Performance clarification

PoolTableModeller reported 4,572ms and 4,671ms end-to-end for these frames. The laptop has an RTX 3060 Laptop GPU, but the service process used Python 3.13 with CPU-only `torch 2.15.0+cpu` and `torch.cuda.is_available()` was false. Those timings therefore do not measure the 3060's inference speed, and they are not a Pixel 11 benchmark. The local service also listens on `127.0.0.1:5001`; the phone cannot call it directly.

## Next work

Keep the global 24/32 gate unchanged. The first model-seeded arc probe is encouraging but inconclusive: one present-black frame passed diagnostic checks and one empty-pocket frame failed, while the classifier called both candidates black. Obtain a reviewed black centre and a same-light present/absent pair at the same pocket, then repeat across frames and other pocket positions. If the targeted branch stays selective, broaden it to cushion extremes before any production integration.

## Reproduction records

`scripts/Run-PocketLipBoundaryAudit.ps1 -RawProposalReplay -RawProposalDirectory <model-run>` performs hash-checked saved-frame replay and accepts up to 12 frames from one source timeline per run. The two six-frame reports below include the per-proposal audit and same-frame SkyEye classifier evidence. Work-tree reports and manifests are retained under `work/production/evidence/local-raw-proposal-gates-20261031/`.

- Daylight black-present run `rawclpos6-164614`: report SHA-256 `4f9789bac8a98724db78900199586fddc04e67bf810fc3dba7de11ac1b3e39c6`; manifest SHA-256 `1e748c01de6efab2241bd8b0b521eb1bf9b0dea1abbe641f47543e1b24bc2239`.
- Dim empty-pocket run `rawclneg6-164655`: report SHA-256 `743256a5d63446ae54b5a960d5f29951e903a02f604e24bb644eb4c93adfb92f`; manifest SHA-256 `b19b0c2e3d0ea4e0ec9bcf9498a52dc85703a08bb3e7ce8a934a2a1f096e6ff8`.

The source-centre proposal comparison and its manual-label caveats are documented in [the local model centre check](../local-model-raw-centre-check-20261031/README.md).
