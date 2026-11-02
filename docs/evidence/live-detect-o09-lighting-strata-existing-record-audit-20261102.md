# O09 lighting-strata existing-record audit — 2 November 2026

## Result

**`O09-LIGHTING-STRATA-METADATA-EXISTING-RECORD-AUDIT`: PASS for record reconciliation; complete measured strata remain 0/2.** No new capture or device action occurred. The normal-pool-room stratum has one provisional handset ambient-light sample associated with the requested centre placement by timing inference. The warm-dim-playable stratum has only an earlier user report of playability; no warm/dim sensor or image-condition measurement was found.

| Stratum | Existing evidence | Status |
|---|---|---|
| `normal-pool-room` | User reported daylight and no direct glare. Pixel 11 TMD3743 events were 77.41, 86.18 and 66.29 lux over 4.819 s; median 77.41 lux, range 66.29–86.18. Association with the face-up table-centre placement is inferred from the requested sequence; sensor events contain no device location. | Partial only: uncalibrated handset sensor, one short sample window, no colour-temperature or camera-exposure reading. |
| `warm-dim-playable` | User reported that this condition was playable in the 11:23 preflight. | Unmeasured: no saved ALS sample, source description, glare report, image-patch comparison, or exposure reading tied to this stratum. |

## Evidence and limits

- The saved [ALS observation](../../work/o09/device-state/normal-room-light-observation-20261102.json), SHA-256 `6cfd9e2594b25bd6bc41a5b163dd08a39f603557167f3dc033e316349be27637`, records the sensor, values, user-reported daylight/no-glare and inferred placement association. It explicitly marks the reading as non-calibrated and the stratum as incomplete.
- The [sensor event excerpt](../../work/o09/device-state/normal-room-als-samples-20261102.txt), SHA-256 `a3db991af68e2640aa320db8b3a90f6b42a0cdf5d4e6d1b1b9fc7179212b6eff`, contains 50 events. Only three fall within the requested centre-hold interval. The other readings lack phone-location metadata and were not pooled as table-centre measurements.
- Frame 1530's [saved receipt](../../work/o09/development-v3/raw/normal-train-layout-01/attempt-274aea9ea1494645aa42851854c29a5b/274aea9ea1494645aa42851854c29a5b/1530.json), SHA-256 `06a8767f6514475b252d9b52cfb66bfe2394955d4192d9e03a5817bddfa787a2`, contains no exposure or lighting field. The camera status contract likewise supplies no exposure state. Exposure therefore remains unknown; no Kelvin estimate is available.
- The normal-validation Layout03 [fixed-patch report](live-detect-o09-development-lighting-expansion-20261102.md) independently shows median absolute cloth-luminance changes of 7.51, 6.29 and 8.93 levels in frames 957, 962 and 2263 versus frame 2083, with a maximum absolute shift of 18.73. This is image-condition drift evidence, not an ambient lux measurement or a verified same-light interval.
- The earlier [lighting preflight](live-detect-o09-lighting-metadata-preflight-20261102.md) passed **4/4 collection prerequisites**, not lighting measurements. Its two playable-condition statements are user reports. The user has since said no more daylight shots; this audit requests none.

The normal condition has a useful descriptive sensor sample but lacks independent placement timing, repeat measurements, colour temperature and exposure. The warm/dim condition lacks measured metadata entirely. Do not mark either stratum criteria-complete, compare them as measured exposure bands, or claim that daylight and warm-dim data are ready for an acceptance split.

## Next work

Next planned task: **`O09-DEVELOPMENT-REMAINDER-REFERENCE-SHEETS`**. Prepare clear schematic references for each remaining Development state and the independent Layout01 replacement before asking for any future staging. Label ball colours, counts, target regions, absent balls and positions to leave unchanged. No staging or capture request is part of this offline task.
