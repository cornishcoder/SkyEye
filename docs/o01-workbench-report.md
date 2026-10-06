# O01 offline workbench delivery

6 October 2026. O01's offline tooling is delivered in small commits. Real timed-shot collection, frozen real holdout coverage and detector/tracking acceptance remain open. No production app, camera, TV or native media path is changed or installed in this slice.

## Delivered

- Pure BCL `SkyEye.Observation`: versioned corpus/frame/label contracts, rooted asset paths, payload/source SHA256 checks, dimension checks and corpus audit.
- Deterministic receipt replay with duplicate/backwards/pair-conflict rejection, source/timeline/generation/calibration resets, gap handling and separately reported missing indexed source IDs.
- Exact-byte split lock: session and identical-source holdout leakage checks; reviewed real timed shots required for holdout; changed manifest/index/labels/payloads refuse the frozen version.
- Console workbench: validate, replay, freeze and baseline commands, protected report output and an honest seed generator. JSON/index writers and fixture attributes preserve LF through Windows checkouts.
- Stored-output baseline scorer: visible-ball spatial matches, misses, extra detections, colour accuracy, matched centre p95 and explicit unscored-labelled-frame coverage. Deterministic maximum-cardinality association avoids a crowded-ball greedy miss.
- [Labelling and collection guide](observation-corpus-guide.md), [frozen seed](../tests/fixtures/observations/README.md) and scoped machine-readable evidence.

The seed preserves the existing human-labelled mounted Pixel still as a **tuned regression**. Its source PTS is unknown. Archived C# OpenCV 4.14.0 output is associated through the original fixture provenance and report checksum; native CV is not rerun. A separate synthetic sequence embeds frame IDs in Gray8 payloads and injects receipt failures. Synthetic cases never contribute to real-shot coverage.

## Verified evidence

| Check | Result |
| --- | --- |
| `tests/SkyEye.Checks` | Pass; one existing CS8602 warning in `ObservationCommandChecks.cs` |
| `tests/SkyEye.GameState.Checks` | Pass |
| `tests/SkyEye.Observation.Checks` | Pass: deterministic receipts, gaps/resets, duplicate/backwards/pair failures, payload corruption, JPEG dimensions, split leakage, frozen-label mutation, scorer misses/extras/colour and crowded-ball association |
| Workbench Release build | Pass, zero warnings/errors |
| Fresh indexed fixture checkout with `core.autocrlf=true` | Frozen lock validates; replay bytes match the working copy |
| Debug vs Release baseline | Byte-identical |
| Missing stored detections | Zero scored/one labelled frame and explicit missing sample ID |
| Invalid lock / existing output | Clean exit 1 / protected from overwrite |

[Replay evidence](evidence/o01-replay.json): the real still is one processed `UnknownTime` sample. Synthetic replay has seven samples, four processed, three rejected, one scope reset, one gap reset and five missing indexed source IDs. These counters are contract/index accounting, not real camera drops or a native pixel/PTS pairing test. Replay SHA256 is `F0E4AD5A2E13D12D381D94C06A8A03DBF419AFDE4CC7278CEDF92BD8BF3804EE`.

[Historical baseline](evidence/o01-baseline.json): one labelled/scored still, 16 visible balls, 16 detections and spatial matches, 16 matching colours, zero extras/misses, p95 centre error `0.00665614` in dimensionless table-2x1 coordinates at tolerance `0.027`. This repeats tuned-still evidence, not held-out accuracy. Baseline SHA256 is `974BEEAAE16EDF2EC77110D29DCD0F9021EBF4832B2E915FEC448A7545989BF4`.

[Verification provenance](evidence/o01-verification.json) identifies the checked source commit, report hashes and physical-test boundary. Production has no reference to the new observation projects; adding their solution entries does not change capture or requested perception behavior. Existing T13/T14 acceptance and the concurrent temporal/HUD design drafts remain preserved.

## Remaining gates and next slice

O01 remains open for at least 60 real mounted-setup sequences, at least 20 held out by session, reviewed positions/identity visibility/event intervals, normal/failure coverage and a fresh baseline on that corpus. Include at least 10 simple held-out pots and 10 misses plus separate negative challenges. Freeze before tuning. The workbench's `accuracyGateReady` remains false; schema/count success does not close the design's physical or outcome gates.

Next implementation is O02's atomic native decoded-frame receipt: acquire pixels and actual source PTS together, prove pairing using controlled frame IDs and preserve source/generation fencing and resource ownership. Its bounded one-active/one-latest feed and ROI/flow benchmarks follow that proof. Low light does not prevent controlled pairing tests, but real tracking/outcome accuracy still needs suitable footage. The current runner consumes extracted indexed payloads; MP4 extraction, native acquisition, ROI motion, persistent identity, shot segmentation, event proposals and GameState review integration are later O02-O05 work. No temporal event accuracy, physical cadence/latency or calibrated geometry/jaw coverage is claimed here.
