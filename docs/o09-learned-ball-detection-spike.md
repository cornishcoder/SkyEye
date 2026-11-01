# O09 — Learned Ball Detection Spike

8 October 2026 · **P0 detector investigation; live diagnostics/dashboard, capture labelling and offline tooling implemented in source.** The user reports that table vision is borderline unusable under current room conditions, with multiple errors among missed balls, false detections in shadows and wrong colours. O09 now has opt-in on-device frame labelling, a read-only laptop dashboard served over local Wi-Fi, an O01 importer, a Development-only YOLO dataset exporter and a frozen three-candidate comparison command; none has been run against a reviewed room-lighting holdout. The [O08 reliability plan](o08-mounted-table-reliability-plan.md) remains the release gate for safe, current table observations.

## Outcome

### 1 November 2026 Development checkpoint

The reviewed apparent image-space radii now support a Development export, but the first two learned baselines do not approach a usable detector: config-only scratch training scored 0/4 complete layouts, and pretrained YOLO11n transfer scored 16/62 boxes (all yellow), 384 false positives, 46 misses, and 0/4 complete layouts on the same four-frame, single-group Development validation set. Holdout remains unopened. Do not integrate either checkpoint or tune repeatedly against that validation group. The seven-frame O09 Development expansion is recorded below; it improves target-control coverage but does not establish broader rack/spread or lighting coverage. The next target is `O09-TRANSFER-NEW-CONTROLS-PROBE`: score the already frozen transfer checkpoint on the seven newly reviewed frames without training or tuning. See the [transfer result](evidence/live-detect-o09-training-dev-transfer-baseline-20261101.md), [expansion report](evidence/live-detect-o09-dev-corpus-expansion-20261101.md), and [targeted test ledger](evidence/live-detect-targeted-test-ledger-20261101.md).

### O09 Development expansion follow-up — 1 November 2026

The user reviewed the seven displayed cyan radius-guide sheets for frames 2831, 542, 295, 288, 959, 562 and 1691, replying **“all look correct.”** The apparent source-image radii were appended to a versioned O01 derivative, `work/o09/radius-reviewed-corpus-v2-verified/`; v1 and corpus-v4 remain unchanged. The new derivative has 17 Development frames/17 source timelines, 272 visible/expected-location rows, 264 visible-ball boxes with apparent radii, eight expected-empty points without radii, and five setup groups. Workbench validation, freeze and export succeeded; round-trip checks pass 17/17 samples, 264/264 boxes, 5/5 groups and 32/32 unchanged source/provenance inputs. The exporter assigns 15 images/233 boxes to train and 2 images/31 boxes from one correlated group to validation; all seven additions are training samples. Holdout is unopened. The 13 black/cue target-state IDs are not 13 independently varied full-table layouts; most red/yellow arrangement positions remain in a small family. No measured lighting stratum is assigned. Frame 2831 and the opposite-cue present/absent pair are explicitly excluded from matched-light claims. The acceptance contract v2 preserves the prior gates unchanged; see the [expansion evidence](evidence/live-detect-o09-dev-corpus-expansion-20261101.md).

The expansion builder and O01 validation path are reproducible with:

```powershell
python scripts/Create-O09ReviewedControlExpansion.py --source-corpus work/o09/radius-reviewed-corpus-v1 --source-review-input work/o09/reused-control-radius-v1 --radius-guides work/o09/reused-control-radius-v1/provisional-guides/radius-guides.json --reviewer-response "all look correct" --review-date 2026-11-01 --output work/o09/radius-reviewed-corpus-v2-verified
dotnet run --project src/SkyEye.Observation.Workbench/SkyEye.Observation.Workbench.csproj -c Release --no-restore -- validate --root work/o09/radius-reviewed-corpus-v2-verified --manifest work/o09/radius-reviewed-corpus-v2-verified/manifest.json
dotnet run --project src/SkyEye.Observation.Workbench/SkyEye.Observation.Workbench.csproj -c Release --no-restore -- freeze --root work/o09/radius-reviewed-corpus-v2-verified --manifest work/o09/radius-reviewed-corpus-v2-verified/manifest.json --output work/o09/radius-reviewed-corpus-v2-verified/split-lock.json
dotnet run --project src/SkyEye.Observation.Workbench/SkyEye.Observation.Workbench.csproj -c Release --no-restore -- export-yolo --root work/o09/radius-reviewed-corpus-v2-verified --manifest work/o09/radius-reviewed-corpus-v2-verified/manifest.json --lock work/o09/radius-reviewed-corpus-v2-verified/split-lock.json --output-dir work/o09/yolo-reviewed-radii-v2-verified
python scripts/Verify-O09ReviewedRadiusExport.py --corpus-root work/o09/radius-reviewed-corpus-v2-verified --manifest work/o09/radius-reviewed-corpus-v2-verified/manifest.json --export-dir work/o09/yolo-reviewed-radii-v2-verified
```

Determine whether a small learned detector, alone or paired with the existing OpenCV geometry refinement, materially improves ball recall, false-positive rate and colour accuracy across held-out lighting conditions while meeting Pixel latency and resource needs. Keep the production path unchanged unless a candidate wins the frozen evaluation and passes O08 safety and release gates.

The current production pipeline remains the reference baseline. The 8 October build adds opt-in sequential diagnostics, local illumination normalization and circular hue clustering, but no reviewed striped-sunlight holdout result has been collected. Diagnostics sample fresh frames about once per second and expose recent detector records through Sky. The Pixel-served dashboard shows the exact latest analyzed image with every classified ball and circle candidate labelled, plus frame age/identity, illumination and recognition telemetry, warnings, tracked table state and canonical `SkyEye.GameState`. It retains one paired JPEG and is read-only; it does not change confirmed table/game state. A separate optional capture action holds one fresh JPEG at a time for manual box/class labelling, records frame receipts and per-frame lighting/motion tags, and hides detector output from the label editor and Sky while the image is open. Images persist only after an explicit save, with a 4 MiB/frame and 40 MiB/120-frame app-local bound. Nonempty frames now require an explicit confirmation that every visible ball is labelled; empty frames require their own confirmation. Receipt schema 2 carries this review state, and the importer rejects older receipts. The detector changes remain candidate branches, not evidence that the problem is solved.

## Comparison candidates

Immediate incremental work follows the [cue ball recovery plan](cue-ball-detection-recovery-plan.md): inspect proposal loss during colour correction, recover only from supported current pixels and measure first/normalized/final stages. Freeze each baseline configuration and retain before/after Development evidence; this work does not establish a learned-model result. Keep separate-frame consensus as a later measured extension after this detector outcome.

| Candidate | Proposal and refinement | Question |
| --- | --- | --- |
| A — Current baseline | Existing HSV/cloth mask, Hough/body proposals, colour classifier and current geometry checks | What does the production path do on each same labelled frame? |
| B — Learned detector | Small model proposes cue, black, red and yellow ball regions, with confidence/abstention retained as uncertainty | Does learned appearance generalize across room lighting, shadows, overlap and table positions? |
| C — Learned proposal plus OpenCV refinement | Learned regions constrain local search; existing ball-size and radial-edge evidence refines centre/radius and checks geometry | Can the hybrid keep precise geometry while reducing shadow-driven proposals and colour errors? |

Keep the detector output as candidate evidence. An uncertain/unknown candidate must remain unknown; inventory must not force a red, yellow, cue or black label. The learned path must not depend on finding one cue ball to set global white balance. Compare reasonably raw colour input with a fixed, documented training normalization; retain cloth/exposure/reference readings as diagnostic features unless results show they are needed.

## Data and evaluation

Use the existing O01 workbench and its split metadata; do not create a parallel corpus. Extend its reviewed still/negative sequence support as needed. Capture original local Pixel table frames through an explicit opt-in, size-limited path with the actual source/frame receipt, calibration/configuration identity and lighting/motion condition. The current rolling detection dump alone is not labelled source imagery; do not turn on unbounded recording or auto-upload as part of the debug workflow.

Freeze development and held-out recording sessions **before training or augmentation**. Keep every frame and augmented derivative from one source session on the same side of the split. Augment training data only: exposure changes up to ±2 stops, brightness/gamma/contrast, warm/cool white balance, saturation, directional and partial shadows, blind-like stripes, specular highlights, dim/bright rooms, motion blur, slight defocus, compression/noise, overlaps and partial occlusion. Synthetic augmentation supports real data; it does not replace real held-out sessions.

Include the user's closed-blind sunlight pattern as a separately labelled lighting stratum, recording stripe position/change over time and whether balls are settled or moving. Also cover ordinary, dim and bright room lighting; shadows, touching/overlapping balls, cushion/pocket edges, partial occlusion and empty regions where false positives can occur. Review labels without seeing model output: visible/unknown/occluded status, class, centre/radius or box, and motion/lighting interval.

Run A, B and C on the exact same frozen frames and report results per lighting and scene stratum, with denominators and session-aware uncertainty:

- visible-ball recall, candidate precision and false positives per frame;
- colour accuracy and abstention/unknown rate;
- centre error relative to ball diameter, radius error, merges/splits and near-cushion/touching-ball performance;
- false-ready/unsafe outcomes under O08's existing conservative gates;
- whole-path and model latency, peak memory, CPU/GPU use, thermal behavior and battery cost on Pixel 11 at the real input resolution.

Keep two outcomes separate. First score each detector's independent per-frame candidates so a temporal layer cannot hide missed or false detections. Then, for timestamped sequences, replay each candidate's detections through the existing O08 burst association and verification consensus and report verified-layout success, uncertainty, latency and false-ready outcomes. Do not interlace or average source frames before detection: it can soften ball boundaries and create ghost evidence when a ball or lighting pattern moves. Consensus may tolerate an intermediate dropout, but every accepted ball still needs fresh support in the final frame; a stale position never fills a final miss. The live dashboard continues to show raw per-frame output so flicker stays diagnosable.

Set comparison and acceptance thresholds before opening the held-out results. A high aggregate score cannot hide a failure in the sunlight-stripe or dim-light stratum. Augmented or synthetic-only accuracy is not release evidence.

## Android and architecture limits

Benchmark a small model on Pixel CPU/GPU first. Measure settled/request-time inference, since the detector is needed after setup, when the table settles, on an explicit request or for bounded recovery; do not run full-frame learned detection at 30/60 fps. Keep O02/O03 responsible for lightweight ROI/flow tracking during motion, seeded by a reviewed settled layout. Investigate a Pixel accelerator only if measured CPU/GPU results justify the integration cost.

Prefer the hybrid path if it improves held-out detections: let the model propose a neighbourhood, then use calibrated ball dimensions and local edge/body evidence to refine centre/radius. Do not infer precise table geometry from a bounding box alone. Preserve source timestamps, uncertainty, current-support rules and the single `TableAnalysisSession`/`VisionAnalyzer` ownership path; no parallel table state or game ledger.

Treat Poolgame-AI-shot-prediction, Pool Vision, ai-billiard-system, Pool-Table-Tracker and Cuevision as architecture/evaluation leads only. Before reusing code, weights, training assets or model artifacts, verify the exact revision, file/model licence, notices and SkyEye distribution rights. In particular, inspect the existing [Poolgame port provenance](poolgame-port.md); its checked-out source did not include the described YOLO assets and had no detected licence. Do not assume a README's model claims or a repository-level licence covers absent weights.

## Bounded on-device labelling

In **Table controls → Live vision dashboard → Optional labelled training frames**, start sequential sampling, choose **Capture fresh frame to label**, and drag a box around every visible ball in the captured image, including partly hidden balls. Choose cue/black/red/yellow or unknown, and visible/occluded/uncertain. A nonempty frame cannot be saved until you confirm every visible ball has a box; empty frames require a separate explicit checkbox. Add the lighting, settled/moving and scene tags; use the note field for blind-stripe position/change and other context. The label view hides the detector dump and Sky's diagnostic readout until the image is saved or discarded. A saved frame contains the hand-drawn image-normalized centre and X/Y radii, exact pixel hash, source/frame/time scope, calibration corners and analyzer settings identity. The capture receipt pairs that label with the exact detector output for later O04 evaluation; the detector output remains hidden during labelling.

### Pixel-served laptop dashboard

With an active live camera session and calibrated table, open **Settings → Table analysis and shot display → Live vision dashboard** on the Pixel and tap **Start laptop dashboard**. The app requests local-network permission if Android has not granted it. Open the displayed address on a laptop connected to the same Wi-Fi. The address uses fixed port 8770 and has no per-session key; any device on that Wi-Fi can read the live camera frame, detector diagnostics and game state while the dashboard is active. HTTP is unencrypted, so start it only on a trusted network. The full-width camera frame appears first; scroll below it for the ball list, detector details, game state and sample history. The page updates every 500 ms but only new sequential analyses (about once per second) produce a new labelled image. It shows classified detections with a heuristic colour-feature score (not a calibrated probability that the label is correct), unknowns among the circle candidates, illumination normalization and gains, per-sample reference brightness, warnings, source frame ID/time/age, rolling sample history, current table tracker state and the canonical game snapshot. Unknown circles remain in detector uncertainty but are not counted or labelled as classified balls. The screen stays awake while the dashboard is active. Keep SkyEye in the foreground for fresh camera sampling. The page stays available through transient sampling pauses; old images are cleared and eligible sampling restarts automatically. Stop the dashboard from the phone when finished; background process survival is not guaranteed. All dashboard routes accept GET only.

Frames are saved under the app's external `files/o09-labels/<diagnostic-session-id>` directory. After stopping diagnostics, choose **Finish reviewed label session** to write a batch summary. Each **Save reviewed frame** action is independently reviewed, so the importer can also recover saved frames if the app closes before the batch summary is written. The UI shows both the diagnostic batch ID and the camera recording-session ID: if one camera recording spans multiple diagnostic batches, keep those batches in the same O01 split. The local image budget is 40 MiB and 120 reviewed frames, with a 4 MiB limit per image; there is no automatic upload or deletion.

Copy the lab app's capture directory into `work/o09/label-captures`:

```powershell
adb pull /sdcard/Android/data/uk.co.skyeye.companion.lab/files/o09-labels work/o09/label-captures
```

Create `work/o09/session-assignments.json` before training or augmentation, assigning every diagnostic batch, condition, and (when separate camera recordings share a staged layout/control family) setup group. `setupGroupId` is optional for unrelated batches; use the same stable ID for sibling captures whose pixels must stay together. The importer carries it into O01 as `setup-group:<id>` and rejects a group split across Development and Holdout. O01 validation independently rejects that leakage if metadata is edited later. O09 train/validation planning joins sequences that share either the original camera recording session or a setup group, including transitive links, before assigning roles.

```json
{
  "schemaVersion": 1,
  "sessions": [
    { "diagnosticSessionId": "DIAGNOSTIC_BATCH_ID", "split": "Development", "holdoutCondition": null, "setupGroupId": "dev-pocket-control-a" },
    { "diagnosticSessionId": "ANOTHER_BATCH_ID", "split": "Holdout", "holdoutCondition": "still", "setupGroupId": "holdout-layout-a" }
  ]
}
```

Use `still`, `moving`, `obstructed`, `lighting-changed` or `camera-changed` for Holdout conditions. Each Holdout diagnostic batch needs two or more fresh frames with increasing source time. Keep lighting and scene detail in the per-frame tags/notes so changing blind stripes are not flattened into one session label.

```powershell
dotnet run --project src/SkyEye.Observation.Workbench -- import-diagnostic-labels work/o09/label-captures work/o09/corpus-v1 work/o09/session-assignments.json
dotnet run --project src/SkyEye.Observation.Workbench -- freeze --root work/o09/corpus-v1 --manifest work/o09/corpus-v1/manifest.json --output work/o09/corpus-v1/split-lock.json
```

The importer preserves reviewed hand labels, exact frame receipts and original camera recording-session IDs in O01 sequence-label schema v2. The Holdout verifier supports still/negative condition receipts; these image-normalized labels serve O09 comparison and are not a substitute for O08's table-coordinate safety verification.

### Prepare O04 timed-shot sequences from the same captures

For temporal tuning, give each captured ball a stable ID and reuse it for that physical ball in later frames. Split whole recording sessions before reviewing tuning results. Use a separate assignment file with explicit `purpose: "Shot"` and no Holdout condition:

```json
{
  "schemaVersion": 1,
  "sessions": [
    { "diagnosticSessionId": "DEVELOPMENT_BATCH_ID", "split": "Development", "holdoutCondition": null, "purpose": "Shot" },
    { "diagnosticSessionId": "HELD_OUT_BATCH_ID", "split": "Holdout", "holdoutCondition": null, "purpose": "Shot" }
  ]
}
```

Run the shot importer to create a separate O04 corpus:

```powershell
dotnet run --project src/SkyEye.Observation.Workbench -- import-diagnostic-shots work/o09/label-captures work/o04/corpus-v1 work/o04/shot-assignments.json
dotnet run --project src/SkyEye.Observation.Workbench -- freeze --root work/o04/corpus-v1 --manifest work/o04/corpus-v1/manifest.json --output work/o04/corpus-v1/split-lock.json
```

The importer maps hand-labelled image centres through the exact saved table calibration to `table-2x1`, retains source PTS and identity/lighting tags, and writes `detections-development.json` and/or `detections-holdout.json` from the detector result paired with each exact labelled frame. The hand-label view still hides detector output. Use those split-specific files with `association`; every reviewed frame already has a matching candidate record, including explicit empty results. Diagnostic sampling is about once per second and manually saved, so this is useful for settled layouts, lighting changes and slow motion; it cannot establish fast-shot recall or the O02.05 live-feed gates.

## Implemented offline workbench slice

### Read-only source-radius review pack

`scripts/Build-O09RadiusReviewPack.py` creates full-frame centre overlays, enlarged source crops and a blank reviewer CSV from an O01 corpus plus its receipt-bound `source-to-table-transform.json`. It verifies image/label/transform pixel hashes and maps each visible table-space centre back to its exact source point. The pack does not load detector output, draw a radius guide, estimate a radius, or modify labels. Radius and uncertainty columns remain blank until a reviewer supplies measurements. Current run evidence and coverage counts are in [the O09 radius-pack report](evidence/live-detect-o09-radius-label-review-pack-20261101.md).

```powershell
python scripts/Build-O09RadiusReviewPack.py --corpus-root work/o08/live-detect-black-pair-v1/corpus-v4 --transform work/o08/live-detect-black-pair-v1/corpus-v4/source-to-table-transform.json --output work/o09/radius-review-v1
```

`scripts/Propose-O09SourceImageRadiusGuides.py` is a separate proposal-only step. It computes a radial CIELAB gradient response from each raw source crop around the reviewed centre and renders a cyan circle guide; it does not read detector output or write O01 radii. The report preserves the search range and response score. Human inspection remains required, especially for low-contrast black/cue rims and pocket/cushion edges; these apparent image-space guides do not establish physical cloth-plane size. See [the guide review report](evidence/live-detect-o09-image-edge-radius-guides-20261101.md).

```powershell
python scripts/Propose-O09SourceImageRadiusGuides.py --corpus-root work/o08/live-detect-black-pair-v1/corpus-v4 --review-pack work/o09/radius-review-v1 --output work/o09/radius-guide-proposals-radial-v1
```

On 1 November, the user reviewed all ten displayed guide sheets and replied **“all look correct.”** This accepts the 155 circles as apparent source-image radii for the separate O09 derivative only; it is not a physical cloth-plane radius claim. The proposal report binds 155/155 visible points across 10 frames, found no search-boundary hits, and confirmed 20/20 original image/label files unchanged. The [guide review report](evidence/live-detect-o09-image-edge-radius-guides-20261101.md) records the method, human review, and rejected Hough pilot.

`scripts/Create-O09ReviewedRadiusCorpus.py` verifies every guide against its source hash, reviewed centre, and source-to-table mapping, then writes a versioned derivative without changing O08 corpus-v4:

```powershell
python scripts/Create-O09ReviewedRadiusCorpus.py --source-corpus work/o08/live-detect-black-pair-v1/corpus-v4 --review-pack work/o09/radius-review-v1 --radius-guides work/o09/radius-guide-proposals-radial-v1/radius-guides.json --reviewer-response "all look correct" --review-date 2026-11-01 --output work/o09/radius-reviewed-corpus-v1
dotnet run --project src/SkyEye.Observation.Workbench/SkyEye.Observation.Workbench.csproj -c Release --no-restore -- validate --root work/o09/radius-reviewed-corpus-v1 --manifest work/o09/radius-reviewed-corpus-v1/manifest.json
dotnet run --project src/SkyEye.Observation.Workbench/SkyEye.Observation.Workbench.csproj -c Release --no-restore -- freeze --root work/o09/radius-reviewed-corpus-v1 --manifest work/o09/radius-reviewed-corpus-v1/manifest.json --output work/o09/radius-reviewed-corpus-v1/split-lock.json
dotnet run --project src/SkyEye.Observation.Workbench/SkyEye.Observation.Workbench.csproj -c Release --no-restore -- export-yolo --root work/o09/radius-reviewed-corpus-v1 --manifest work/o09/radius-reviewed-corpus-v1/manifest.json --lock work/o09/radius-reviewed-corpus-v1/split-lock.json --output-dir work/o09/yolo-reviewed-radii-v1
python scripts/Verify-O09ReviewedRadiusExport.py --corpus-root work/o09/radius-reviewed-corpus-v1 --manifest work/o09/radius-reviewed-corpus-v1/manifest.json --export-dir work/o09/yolo-reviewed-radii-v1
```

The derivative uses `x=sourceXpx/width`, `y=sourceYpx/height`, `radiusX=sourceRadiusPx/width`, and `radiusY=sourceRadiusPx/height`. It has 160 normalized centres, 155 visible radii, and five absent expected points without radii. Original source image/label files remain unchanged. The exporter preserves PNG inputs and extensions as well as JPEG inputs; the [round-trip report](evidence/live-detect-o09-radius-label-roundtrip-20261101.md) records 10/10 sample/hash coverage, 155/155 exported boxes, setup-group split status, and focused regression results. This produces Development train/validation data only: no Holdout images are copied.

The first bounded model run is recorded in the [Development scratch-baseline report](evidence/live-detect-o09-training-dev-baseline-20261101.md). YOLO11n initialized from its configuration with random weights, trained 20 epochs on six images and scored 0/62 boxes and 0/4 complete layouts on the four-frame, single-group Development validation split. It establishes a runnable evaluation path, not a viable detector. No pretrained weights, production inference adapter, Pixel timing/resource result or Holdout acceptance is claimed. Ultralytics 8.4.194 and its model config are AGPL-3.0; this experiment stayed in ignored work artifacts and was not integrated or distributed. Verify the exact asset license before a transfer-learning run and keep product integration behind O08 acceptance.

`export-yolo` exports reviewed real **Development** samples only. It forms deterministic connected split groups by joining every sequence that shares a camera recording session or a `setup-group:<id>` tag, then assigns whole groups to train and validation. It requires at least three independent Development recording sessions and three resulting split groups, image-normalized labels and reviewed X/Y radii, and refuses ambiguous/occluded labels. Export report schema 2 records each session's split-group ID and each group's sessions/setup groups. O01 Holdout pixels are never copied; this exporter does not perform augmentation or select a training framework. Apply the planned appearance augmentation to train only.

```powershell
dotnet run --project src/SkyEye.Observation.Workbench -- export-yolo --root PATH-TO-CORPUS --manifest PATH-TO-MANIFEST --lock PATH-TO-LOCK --output-dir work/o09/train-v1
```

`compare` requires three complete `StoredDetection[]` files named current, learned and hybrid. Each file must contain exactly one record per reviewed sample in the selected frozen real `Development` or `Holdout` split; encode abstention as an explicit empty/uncertain result. Pixel hashes and coordinate spaces must match the labels. Schema 2 includes every frame's indexed frame ID, source PTS, scope and pixel hash, together with its spatial score, uncertainty, matches, misses and unmatched extras; the label hash must match its indexed receipt. It also retains input hashes, per-sample failures, per-session and per-tag metrics, centre/radius error where radii are labelled, and deterministic 95% cluster-bootstrap intervals for precision, recall and colour accuracy. Fewer than two contributing sessions yields no interval. Unmatched extras include duplicates and other unmatched detections; they are not separately classified. The report does not yet carry per-frame runtime or verify an O08 replay's calibration/configuration provenance. Use O08's separate verification scorer for false-ready/current-layout results.

```powershell
dotnet run --project src/SkyEye.Observation.Workbench -- compare --root PATH-TO-CORPUS --manifest PATH-TO-MANIFEST --lock PATH-TO-LOCK --split Holdout --current work/o09/current.json --learned work/o09/learned.json --hybrid work/o09/hybrid.json --tolerance 0.027 --output work/o09/holdout-v1.json
```

Pass `--tolerance` explicitly and freeze it before a Holdout run; image-normalized coordinates require an aspect-aware threshold choice. The default from the legacy `baseline` command is not silently reused for O09.

The checked-in O01 v1 seed has one real regression still plus synthetic receipt contracts and still cannot produce a valid O09 comparison. It is separate from the original 10-sample derivative in `work/o09/radius-reviewed-corpus-v1/` and its 17-sample Development expansion in `work/o09/radius-reviewed-corpus-v2-verified/`. The first saved Pixel label session contained two dense full-table frames with only one ball labelled in each; those receipts predate complete-frame attestation and are not eligible for import. The labeller/importer now requires complete-frame review for new captures. Local checkpoints remain under ignored `work/`; no production inference adapter, Pixel latency result, Holdout score or lighting-accuracy acceptance is claimed.

## Bounded chunks

| Chunk | Result | Gate |
| --- | --- | --- |
| O09.01 — Prepare comparable data | O01/O08 capture import, hand-labelled bounded capture, original recording-session splits and Development-only YOLO export | Capture labeller/importer/exporter are implemented in source; reviewed lighting sequences and Holdout remain to be collected |
| O09.02 — Freeze baseline and candidate contracts | Three-candidate same-sample scorer with strict coverage/hash/coordinate checks and session-aware metrics | Comparator is implemented in source; candidate inference outputs and labelled split are still needed |
| O09.03 — Train and compare | Train a small learned proposal/classifier and evaluate B/C with train-only augmentation | Not started: choose a trainer/model only after reviewed data and exact code/weight distribution rights are available |
| O09.04 — Measure Pixel inference | Package a prototype behind the existing analysis seam and compare CPU/GPU at actual resolution | Not started: requires frozen trained weights and a Pixel 11 run; no 30/60 fps full-frame inference |
| O09.05 — Decide | Publish go/no-go recommendation and retained baseline/model provenance | Open: candidate must beat A on untouched real sessions, meet per-stratum and O08 safety gates, and have distributable rights; otherwise retain A and record the result |

**No production detector replacement is part of this spike.** A winning candidate would move to a separate bounded integration slice with reproducible model/build provenance, verified distribution rights, O08 acceptance using the selected detector and a guarded installed-release check. O09 does not establish moving-ball identity, pot detection, refereeing or shot-outcome inference.
