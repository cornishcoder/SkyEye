# O09 normal/train Layout03 four-state control — 2 November 2026

**Status: capture complete; spatial review pending.** The Development setup group contains both-present, black-absent, cue-absent, and restored both-present states. No selected frame has been detector-scored. The bounded verifier returned one `Unsettled (InsufficientEvidence)` and three `Unsettled (TimedOut)` outcomes; none is a stillness/readiness pass.

## Why capture paused, and capacity change

At 12:49:28 UTC the black-absent capture did not start. Home reported `9 MiB free; 18 MiB is required for up to 7 frames`. The device had about 270 GiB free on shared storage; the application was enforcing its separate 256 MiB O08 evidence-store cap, already occupied by 253,629 KiB. No image was written by that attempt.

The bounded cap is now **1 GiB**. The 18 MiB preflight reservation, 8 MiB per-image limit, and per-write checks are unchanged. No clips or app data were deleted. The evidence directory occupied 288,220 KiB after this group.

`VERIFICATION-EVIDENCE-BUDGET-EXPANSION-RESERVE-BOUNDARY` passed **24/24** focused assertions, including the exact reservation and frame-write boundaries. The Production Release build passed with **0 errors and 13 warnings**. Companion **0.8.62/code70** was installed in place; installed APK SHA-256 is `9901000426CD00FAACCB6B48E75849C12A0336E316B5DD9C02F7076863F71551`. The install preserved package identity and private storage. The mounted-camera package was not changed.

## Captured states

Each row was captured in its own bounded session after the user confirmed the requested table change and clear hands. Selection uses the lower median valid `sourceTimeUs` within each operation, before label review. Source PTS values are local to each of the four timelines and must not be compared across them.

| State | Operation | Valid pairs | Selected frame / source PTS | Calibration | Verifier result |
| --- | --- | ---: | --- | --- | --- |
| Black and cue present (anchor) | `c0414088-4cd7-4996-a58e-73412a53b3f6` | 8/8 | 1274 / 8408291966 us | `3795b1fc087645aeac4c7429707741c3` | `Unsettled (InsufficientEvidence)` |
| Black absent, cue present | `20ef43d2-0270-4f44-8f5c-9128608be854` | 8/8 | 3360 / 9382610644 us | `c634fbb6da0341c1994b10b862f2de87` | `Unsettled (TimedOut)` |
| Black present, cue absent | `3a80a2cf-cf8f-4f9f-9ab2-8f902df8475e` | 7/7 | 395 / 9558192622 us | `c634fbb6da0341c1994b10b862f2de87` | `Unsettled (TimedOut)` |
| Black and cue restored | `2d605c22-7daf-4fbd-bf21-e5f030f42cea` | 7/7 | 382 / 9622031722 us | `c634fbb6da0341c1994b10b862f2de87` | `Unsettled (TimedOut)` |

All **30/30** source-image/receipt pairs pass SHA-256 binding, report 1920×1080, and have unique frame IDs and source PTS within their operation. Two operations have eight valid pairs although Home reported seven; a late receipt completed after the timed-out status, and the UI count is capped at seven. Keep every valid pair, but review only the four preselected frames unless additional frames are independently labelled.

All four operations use the same analyzer-configuration identity (SHA-256 `6d4e4f3e2fc28bba1a56c637f6783e1d743e082d7807362324344a102fefa414`; width 800, packed-colour boundary extent `.65`). Calibration corners are byte-for-byte equal across the group. The calibration ID changed once after reconnect and pose confirmation; record both IDs rather than presenting identity continuity. The four source timelines differ because capture was stopped between table changes.

## Lighting and raw-only review status

The user described the normal-pool-room condition as daylight with no direct glare and kept lighting unchanged during the sequence. A fixed empty-cloth patch in the four selected raw images has median RGB values **(103,115,122), (101,115,122), (102,115,122), and (102,115,122)**. This small image-space variation supports the matched-light description; it is not an ambient-light-sensor measurement or a frozen lighting criterion.

The raw-only draft sheets are under `work/o09/development-v3/reviews/layout03-control-raw-only/`, with sources, hashes, per-frame centre/radius proposals, crops, and the rendering script. The script reads source PNG pixels only and did not read receipt measurement fields or invoke the SkyEye detector. Red/yellow centres were estimated separately from each raw frame; cue/black points and apparent radii remain visual drafts. The user has not reviewed these four frames yet.

**Protocol exposure:** during the reconnect preflight, the Settings screen displayed one aggregate live Table-awareness result (`7 reds · 7 yellows · 0 cue balls · 0 black`, uncertain/obstructed) before the raw-only label review. No per-frame proposal coordinates or receipt measurement payloads were opened. Treat the upcoming labels as raw-image spatial review with aggregate detector-count exposure, not as a strictly blind review. Do not score until the user review is recorded.

## Current safe state and metrics

At `2026-11-02T13:05:27.3428428Z`, mounted-camera `/status` was **Stopped**, revision 25, 22,043 completed frames, `clipCount=139`; Pixel 11 Home showed **Camera ready** and **Start live session**. The safe-state snapshot and Home screenshot are in `work/o09/device-state/layout03-control-group-poststop-20261102/`. No match, Live Detect, Cast/TV action, new clip, package reset, or camera-package update occurred.

- Software: focused budget assertions **24/24 pass**; Production Release build **0 errors / 13 warnings**; the three broader software suites **3/3** and focused motion **20/20** are carried forward, not rerun.
- Detector: selected-frame labels and scoring **pending**; detector inventory acceptance remains **0/3**. The aggregate UI count above is not a scored sample.
- Physical: four verifier results, **0/4** stillness passes; **30** valid raw pairs retained. D1/D2/E remain **0/3 proven**; Holdout remains unopened.

**Next named test:** `O09-NORMAL-TRAIN-LAYOUT03-RAW-ONLY-REVIEW`. After the user reviews the four overlays and crops, freeze the labels and then run the single current-source Development baseline. Do not tune from this group before its labels and split assignment are frozen.
