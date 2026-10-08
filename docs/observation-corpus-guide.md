# O01 observation corpus and offline workbench

6 October 2026. The workbench validates and replays indexed frame receipts, freezes corpus versions and scores stored detector outputs. It does not decode MP4s, run native CV, track motion or submit GameState events. The checked-in seed has one previously tuned real still and one synthetic contract sequence; it has zero real timed shots and zero held-out shots.

## Run the checked-in seed

Run from the repository root with its .NET 10 SDK. No camera, TV, Android workload or native OpenCV is required.

```powershell
dotnet run --project tests/SkyEye.Observation.Checks
dotnet run --project src/SkyEye.Observation.Workbench -- validate --root tests/fixtures --manifest tests/fixtures/observations/v1/manifest.json --lock tests/fixtures/observations/v1/split-lock.json
dotnet run --project src/SkyEye.Observation.Workbench -- replay --root tests/fixtures --manifest tests/fixtures/observations/v1/manifest.json --lock tests/fixtures/observations/v1/split-lock.json --output work/observation-replay.json
dotnet run --project src/SkyEye.Observation.Workbench -- baseline --root tests/fixtures --manifest tests/fixtures/observations/v1/manifest.json --lock tests/fixtures/observations/v1/split-lock.json --detections tests/fixtures/observations/v1/stored-detections.json --output work/observation-baseline.json
```

Outputs refuse overwrite: choose a new report filename for each experiment. `--manifest`, `--lock`, `--detections` and `--output` resolve from the working directory. Paths inside the manifest/index resolve from `--root`. Omit `--output` to print JSON. The seed generator is `seed tests/fixtures observations/NEW-VERSION`; it refuses a populated destination. Do not regenerate the published v1 in place.

## Author a sequence

Use the [v1 manifest](../tests/fixtures/observations/v1/manifest.json), frame indexes and labels as schema examples. Manifests and frame indexes use schema version 1; sequence labels support versions 1 and 2, with v2 adding optional per-frame condition tags. Documents use UTF-8 and LF line endings. Unknown JSON fields are rejected. Keep original media and extracted frame bytes immutable.

| File | Required evidence |
| --- | --- |
| Manifest | Stable corpus/sequence IDs; recording-session ID; Real/Synthetic kind; Development/Holdout/Regression/Unassigned split; Shot/StillRegression/Contract purpose; source origin, file, SHA256, dimensions, clock domain and calibration version; index/label paths; coverage tags and limits |
| Frame index (JSONL) | One receipt per line in observed arrival order: globally unique sample ID, nonnegative source frame ID, source PTS in microseconds or null, source/timeline/generation/calibration scope, payload file/hash, encoding and dimensions |
| Sequence labels | Annotator, review status, sequence/calibration IDs; exact sample and payload hash; coordinate space, ball IDs/colours/centres/visibility; optional per-frame lighting/scene tags; source-time event intervals and uncertainty notes |
| Stored detections (JSON array) | Exact labelled sample/hash/coordinate space; model/version, provenance, uncertainty; detected ball centres/colour/confidence |

`RecordingMicroseconds` denotes the recording's monotonic timeline; `ClipMicroseconds` denotes that clip's relative decoder timeline. Preserve the actual timestamp domain rather than borrowing wall time, nominal frame rate or the live player's most recent timestamp. A still with unavailable PTS uses `Unknown` and null PTS, and cannot be a timed Shot. Synthetic timing uses `SyntheticMicroseconds`. `nominalFps` documents configuration; it is not a cadence measurement.

JPEG payload dimensions are checked from their headers; Gray8 payloads must contain exactly width × height bytes. The synthetic contract fixture embeds an int64 little-endian frame ID in the first eight Gray8 bytes. Set `embeddedFrameId` only for such controlled fixtures. Hashes establish byte identity, not proof that a real decoder paired those pixels with that PTS. O02 must prove that pairing on the native path with visible/embedded frame IDs.

Keep sample IDs separate from source frame IDs and ball identities. A rejected/duplicate source frame still needs its own sample ID. Source frame IDs are monotonic within a scope; a source, timeline, decoder generation or calibration change starts a new continuity scope. Use a new labelled sequence for a changed physical calibration; preserve the calibration artifact and its origin alongside the corpus. V1 records the version reference and validates consistent sequence-label metadata; it does not yet embed a calibrated geometry artifact or independently verify a homography.

## Label without inventing observations

Mark visible centres on the actual frame before consulting detector results. Use stable human ball IDs across a shot only while identity can be supported; mark ambiguous crossing/recovery as uncertain and explain it. Colour is `red`, `yellow`, `cue`, `black` or `unknown`. Visibility is `Visible`, `Occluded`, `Uncertain` or `Absent`. Do not declare a pot from a single disappearance. Preserve uncertain contact/order/pocket evidence in interval notes.

Coordinates are either image-normalized (X/Y each 0–1) or the existing calibrated `table-2x1` space (X 0–2, Y 0–1). These are dimensionless coordinates, not metres. Transform all truth and predictions with the same historical calibration. Default baseline tolerance is 0.027 in the selected coordinate space; set `--tolerance` explicitly for a different experiment. Image-normalized distances do not account for image aspect; cross-space/aspect comparisons need an explicit transform and appropriate threshold.

Suggested interval kinds are `shot`, `moving`, `settled`, `pot`, `miss`, `occlusion`, `crossing`, `scratch`, `rerack` and `interruption`. Use source microseconds, with `fromUs <= toUs`, `confirmed` or `uncertain` certainty and referenced ball IDs. Describe uncertain endpoints/order in notes. V1 stores generic intervals but does not score temporal events or enforce a domain-specific event ontology. A reviewer should inspect labels, intervals, historical calibration and coverage before setting `reviewed: true`.

## Freeze and protect the holdout

Group by original recording session, including neighboring clips/extractions of the same shot. Assign entire sessions to development or holdout before tuning. The validator rejects a session or identical source hash shared between holdout and development/regression, and rejects synthetic/unreviewed holdouts. Reviewed real `Shot`, `StillVerification` and `NegativeVerification` purposes have separate coverage. Different encodes/crops can hide duplicate content from a byte hash: session assignment and review must catch that leakage.

O08 verification labels require two or more labelled frames and a confirmed source-time condition interval: `still` for positive windows; `moving`, `obstructed`, `lighting-changed`, `camera-changed` or `source-invalid` for negatives. Still receipts must increase in one scope. A reviewed `source-invalid` negative may deliberately preserve duplicate, backwards or changing-scope receipts; replay rejects them rather than silently cleaning the index. Optional `radiusX`/`radiusY` are independently reviewed ellipse radii in the same coordinate space as the centre, with both axes supplied together. Baseline reports radius-relative centre error only for matches with those labels; missing radii cannot satisfy the O08 geometry gate.

The opt-in table-controls diagnostic can save one fresh frame at a time for hand labelling under the companion's external `files/o09-labels` folder. It retains exact hashes/timing, calibration corners, image-normalized ball boxes and per-frame lighting/motion/scene tags, with a 40 MiB/120-frame budget and no automatic deletion or upload. The label view hides detector results. After saving frames and finishing a session, assign each diagnostic batch to a frozen split in the O09 assignment file and run `import-diagnostic-labels`; batches from the same camera recording session must stay together. The separate `/observe?verify=1&capture=1` path still saves up to six unreviewed O08 snapshots per request under `files/o08-corpus`; `import-verification` creates empty-truth `StillRegression` material and does not replace manual labels.

```powershell
dotnet run --project src/SkyEye.Observation.Workbench -- freeze --root PATH-TO-CORPUS --manifest PATH-TO-MANIFEST --output PATH-TO-NEW-LOCK
```

Always pass `--lock` during accepted replay/scoring. It freezes exact manifest/index/label bytes, source hashes and indexed pixel hashes. Even whitespace changes invalidate the lock. Corrections require a new reviewed corpus version, retained old reports and a new lock. Fixture checkout attributes and JSON/index writers preserve LF on Windows. Extend the same attributes to any newly checked-in corpus location before freezing it.

Collect at least 60 real mounted-setup sequences with at least 20 held out by session, including at least 10 simple pots and 10 simple misses. Add separate negative challenges. Cover normal/slow/hard shots, cushions, multiple moving balls, cue scratch/replacement, black pots, near-mouth/cushion balls, crossings, close successive contacts, hands/cue occlusion, shadows/reflections, hiding, break/rerack, interruption, ordinary light and dim failures. Minimum counts do not establish coverage or accuracy. A repeatedly consulted holdout becomes development and needs replacement. `accuracyGateReady` stays false until a separate reviewed acceptance process establishes the full physical/accuracy gate.

## Interpret reports

For O08 request-level evaluation, save a JSON array of `VerificationWindow` records: unique `id` and `operationId`, `sequenceId`, `finalSampleId`, source-microsecond `fromUs`/`toUs`, target `group`, `timingKind` (`OfflineReplay` or `LiveRecorded`), and the complete `TableObservation` in `result`. Preserve refused requests too. The corpus must contain independently reviewed `StillVerification` or `NegativeVerification` sequences; do not promote development detector output into truth.

```powershell
dotnet run --project src/SkyEye.Observation.Workbench -- verification --root PATH-TO-CORPUS --manifest PATH-TO-MANIFEST --lock PATH-TO-LOCK --requests PATH-TO-REQUESTS --output PATH-TO-REPORT
```

The scorer checks reviewed source intervals, final receipt/timeline/calibration, the production publication proof, final ball count/colour/positions and reviewed radii. Any confirmed unsafe interval overlap counts as unsafe; a safe window needs a fully enclosing reviewed still interval. A Ready result with unsafe conditions, invalid proof or incorrect final layout counts as false Ready. Reports retain every request's session, split and tags; aggregate each lighting/condition stratum separately, retaining failures in denominators. Live results report whether duration is at most three seconds; offline timing is null. These reports do not themselves grant physical acceptance. The report hashes the request file as well as the frozen manifest.

Replay loads/verifies one payload at a time and emits deterministic receipt decisions. Duplicates, backwards time/ID, changed same-ID pairs and embedded pixel-ID mismatches are rejected without advancing continuity. Scope changes and source-time gaps over 250,000 microseconds reset it. Missing source frame IDs are index-gap accounting, not a hardware drop counter. The report contains no wall-clock performance timings. Rejected receipts remain available for audit.

Baseline scoring uses deterministic maximum-cardinality spatial association within tolerance. It reports visible truth, detections, spatial matches, unmatched predictions, missed visible balls, colour correctness and nearest-rank p95 matched centre error. It is not temporal identity matching or a minimum-total-error optimizer. Occluded/uncertain/absent labels are excluded from visible recall; any unmatched detection is still reported as extra, so inspect ambiguous regions before interpreting that as a physical false-positive claim. Empty denominators/error sets return null. Reports expose labelled/scored frame counts and unscored sample IDs; missing detector output cannot silently count as evaluated coverage.

The seed baseline imports the repo-associated archived C# OpenCV 4.14.0 output for `pixel-table.jpg`, with its original report checksum and human image-centre labels transformed through the existing calibration. It scores 16 visible balls, 16 spatial matches and 16 matching colours, with zero unmatched predictions/misses and p95 error about 0.00666 table units. This is historical tuned-still regression evidence. It is not a new native detector run, dim-shot accuracy or held-out validation. See the [delivery report](o01-workbench-report.md).
