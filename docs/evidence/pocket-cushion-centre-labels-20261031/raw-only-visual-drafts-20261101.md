# Raw-only visual centre drafts — 1 November 2026

## Scope and status

Two separate GPT-6 Luna agents inspected the raw source PNGs only, one for each six-frame group below. They were instructed not to inspect analyzer outputs, model proposals, or existing label drafts. Coordinates are approximate source-pixel visual estimates with rough uncertainty bounds, not calibrated errors. The reviewers saw no discernible movement or camera drift within either group and reported the same approximate centres across its six frames.

These are assistant drafts only. They do not change any label file's `reviewed` status, constitute ground truth, or support an accuracy score. The exact source PTS, pixel and receipt hashes, capture conditions, and replay identities remain indexed in the [pocket-control ledger](live-detect-control-ledger-20261031.md) and the [Code65 replay addendum](code65-raw-proposal-exact-profile-20261101.md).

## Code65 dim black-absent group

Frames 978, 1072, 1105, 1150, 1179 and 1198, 1920×1080 source pixels. The point estimates below apply approximately to each inspected frame.

| Visual label | Centre (x, y) px | Rough uncertainty |
| --- | ---: | ---: |
| Red | (124, 194) | ±6 px |
| Red | (785, 147) | ±5 px |
| Red | (1628, 105) | ±6 px |
| Red | (116, 510) | ±4 px |
| Red | (120, 862) | ±6 px |
| Red | (865, 928) | ±6 px |
| Red | (1607, 931) | ±6 px |
| Yellow/orange appearance | (84, 201) | ±6 px |
| Yellow/orange appearance | (847, 148) | ±5 px |
| Yellow/orange appearance | (1638, 152) | ±6 px |
| Yellow/orange appearance | (814, 535) | ±4 px |
| Yellow/orange appearance | (1629, 543) | ±4 px |
| Yellow/orange appearance | (800, 931) | ±6 px |
| Yellow/orange appearance | (1656, 917) | ±6 px |
| Cue, cream/white | (141, 906) | ±6 px |

A blue sphere around `(942, 44)` is on the wooden rail outside the cloth and is excluded from the playing-surface inventory. The raw-only draft's red estimate near `(124, 194)` is close to the separately stored, proposal-conditioned user confirmation in frame 1179; that agreement does not turn either estimate into an independent ground-truth centre or a frame-level score.

## Code70 daylight black-present group

Frames 1365, 1425, 1457, 1555, 1645 and 1736, 1920×1080 source pixels. The point estimates below apply approximately to each inspected frame.

| Visual label | Centre (x, y) px | Rough uncertainty |
| --- | ---: | ---: |
| Red/magenta appearance | (125, 195) | ±5 px |
| Red/magenta appearance | (786, 146) | ±5 px |
| Red/magenta appearance | (1627, 108) | ±5 px |
| Red/magenta appearance | (117, 510) | ±4 px |
| Red/magenta appearance | (123, 862) | ±5 px |
| Red/magenta appearance | (866, 929) | ±5 px |
| Red/magenta appearance | (1611, 930) | ±5 px |
| Yellow/orange appearance | (86, 201) | ±5 px |
| Yellow/orange appearance | (848, 147) | ±5 px |
| Yellow/orange appearance | (1638, 151) | ±5 px |
| Yellow/orange appearance | (814, 534) | ±3 px |
| Yellow/orange appearance | (1630, 542) | ±4 px |
| Yellow/orange appearance | (801, 930) | ±5 px |
| Yellow/orange appearance | (1657, 917) | ±5 px |
| Cue, cream/white | (141, 906) | ±6 px |

A blue sphere around `(944, 43)` is on the wooden rail outside the cloth and is excluded from the playing-surface inventory. A small dark/pale feature at the lower-left pocket lip remains unresolved; no black-ball centre is assigned. The existing frame 1365 draft also leaves the black centre unresolved.

## Next review boundary

Review the raw images and draft overlays before converting any point into a user-reviewed label. Keep the Code65 frame 1179 point in its separate proposal-conditioned record; do not use it as independent ground truth. Neither group supplies a matched same-light control or a held-out sequence, and the held-out inventory and event/clip gates remain open.
