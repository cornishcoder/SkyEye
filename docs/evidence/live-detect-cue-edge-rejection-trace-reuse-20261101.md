# Neutral-cue image-edge rejection trace — reused result, 1 November 2026

Targeted question `CUE-EDGE-REJECTION-TRACE`: did the production 1.7R image-edge rejection explain the missing neutral cue on the saved Code67 daylight pocket frame? The existing exact-frame Code67 diagnostics answer this for that frame; this check reuses those artifacts and does not replay the analyzer.

## Existing evidence

- Source frame 554, PTS `71298295155` µs, pixel SHA-256 `f159966b194cfedb32a7afd56b7552715804e0039661aa67293e9f1f459d8449`.
- Report: `work/production/evidence/code67-daylight-pocket-20261031/device-replays/code67clipgrid-090100/o08-replay-proof-code67clipgrid-090100-blacktrace.json`.
- Report SHA-256: `f3ab98483bac8db4c22f63627844eea478d034f2ab4a77d24c016d31f75c52cc`.
- The retained report does not include a source commit field, so this remains diagnostic evidence scoped to that saved report/configuration, not a claim about a newly installed package.

| Analysis width | Cue recovery | Components rejected before edge check | Image-edge rejects | Margin-clipped component at 1.5R / 1.7R |
| ---: | --- | --- | ---: | --- |
| 800 | 0 candidates; `no-unique-first-pass-cue` | 175 radius, 2 aspect | 0 | 29×41 / 25×41; both rejected-aspect |
| 1000 | 0 candidates; `no-unique-first-pass-cue` | 287 radius, 1 aspect, 1 fill | 0 | 36×51 / 31×51; both rejected-aspect |

The production telemetry recorded no `rejected-image-edge` components at either width. The synthetic edge-mask probes changed connected components but still left the point-near component too elongated for the existing aspect gate. This frame therefore does not support the 1.7R edge rejection as the cause of the miss. Earlier grouping/shape rejection remains visible in the trace.

## Decision and next evidence

Do not rerun this frame or sweep the edge margin again unchanged. A calibrated cushion-aware margin comparison remains untested; it needs matched cue-present/absent controls plus wood/chalk negatives, fixed camera/layout and adequate stable lighting. The existing controls are not a valid matched-light pair, and the latest user report said lighting had become darker. No production margin or detector setting changes from this result.

This is a detector-path diagnostic, not a labelled accuracy case or acceptance denominator. It does not change production inventory `0/3` or physical D1/D2/E `0/3` proven.
