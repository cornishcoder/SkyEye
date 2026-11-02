# O09 Development staging references — 2 November 2026

**`O09-DEVELOPMENT-REMAINDER-REFERENCE-SHEETS`: PASS 13/13 diagrams across 7 planned setup groups.** This pack covers the twelve remaining warm-stratum states plus the independent normal/train Layout01 replacement. Each image was checked against its role, stratum, layout and `setupGroupId` in the frozen split plan (SHA-256 `6c0d224aca32e5781c03a3452560aac5fd13a25311cd2ea241791ffe7f34d834`). The [manifest](reference-manifest.json) records image hashes and ball counts; [the contact sheet](remaining-staging-references-contact-sheet.png) shows the full set.

These are labelled, approximate schematic staging guides. They are not photographs, calibrated coordinates, detector labels or proof that either lighting stratum is available. The warm/dim condition remains unmeasured and normal daylight capture is paused. **No staging or capture is requested by this reference pack.**

## Normal/train replacement

| Planned group | State | Reference |
|---|---|---|
| `o09-v3-normal-train-layout-01` | Independent full-rack replacement, 7R + 7Y + B + C, marking orientation A | [normal-train-layout01-replacement-reference.png](normal-train-layout01-replacement-reference.png) |

Use a fresh setup/session. The earlier frame was reviewed after detector output was shown, so it is not an independent label. When this state is eventually collected, freeze raw-only labels before opening detector output.

## Warm/train references

| Planned group | State | Reference |
|---|---|---|
| `o09-v3-warm-train-layout-01` | Full rack, 7R + 7Y + B + C, visible marks oriented differently from normal/train A | [warm-train-layout01-reference.png](warm-train-layout01-reference.png) |
| `o09-v3-warm-train-layout-02` | Six-region full spread, close pair, ball near a pocket mouth, open mouths, rail chalk/shadow negatives | [warm-train-layout02-reference.png](warm-train-layout02-reference.png) |
| `o09-v3-warm-train-layout-03` | Both present; anchor | [warm-train-layout03-both-present-reference.png](warm-train-layout03-both-present-reference.png) |
| `o09-v3-warm-train-layout-03` | Black absent; cue and other balls fixed | [warm-train-layout03-black-absent-reference.png](warm-train-layout03-black-absent-reference.png) |
| `o09-v3-warm-train-layout-03` | Restore black, remove cue; other balls fixed | [warm-train-layout03-cue-absent-reference.png](warm-train-layout03-cue-absent-reference.png) |
| `o09-v3-warm-train-layout-03` | Restore cue only | [warm-train-layout03-both-restored-reference.png](warm-train-layout03-both-restored-reference.png) |

## Warm/validation references

| Planned group | State | Reference |
|---|---|---|
| `o09-v3-warm-validation-layout-01` | Reduced, 7R + 7Y + C; black absent | [warm-validation-layout01-reference.png](warm-validation-layout01-reference.png) |
| `o09-v3-warm-validation-layout-02` | Six-region full spread, pocket-mouth and touching/partly-hidden case | [warm-validation-layout02-reference.png](warm-validation-layout02-reference.png) |
| `o09-v3-warm-validation-layout-03` | Both present; anchor | [warm-validation-layout03-both-present-reference.png](warm-validation-layout03-both-present-reference.png) |
| `o09-v3-warm-validation-layout-03` | Black absent; cue and other balls fixed | [warm-validation-layout03-black-absent-reference.png](warm-validation-layout03-black-absent-reference.png) |
| `o09-v3-warm-validation-layout-03` | Restore black, remove cue; other balls fixed | [warm-validation-layout03-cue-absent-reference.png](warm-validation-layout03-cue-absent-reference.png) |
| `o09-v3-warm-validation-layout-03` | Restore cue only | [warm-validation-layout03-both-restored-reference.png](warm-validation-layout03-both-restored-reference.png) |

For each Layout03 control sequence, keep camera position, chalk, light and the fourteen non-target balls fixed. Change one ball at each requested boundary. Dashed `B?`/`C?` circles are expected-empty locations; they are not balls to place. In the warm-validation Layout02 diagram, R7 is shown behind Y6 to illustrate camera-view occlusion. Place balls touching, never physically overlapping; if a centre is hidden in the mounted image, leave it unknown rather than marking the concealed centre.

## Safe use

- Use the separate session/setup boundary from the split plan for each layout group; keep all four Layout03 states together within their group.
- Treat the coordinates as approximate placements. Review each actual saved image independently before scoring it.
- Keep capture stopped while anyone stages or changes the table. Any future bounded capture starts only after a playable, recorded lighting condition and hands-clear confirmation; stop and verify `/status:Stopped` at the first safe boundary.
- Do not use these diagrams to claim warm-dim playability, photometric matching, detector accuracy, D1, D2, or event/clip acceptance.

Generator: [generate_references.py](generate_references.py). The script checks exact plan membership, group role/stratum, unique ball IDs, 7/7 colour counts, black/cue presence markers and unchanged non-target Layout03 positions before emitting each pack.

The first generator attempt stopped before producing images because its alternate rack colour recurrence consumed an uneven red/yellow count. It was replaced by an explicit seven-red/seven-yellow sequence. The final run passed the checks above and produced all 13 references; no failed-run image was retained.
