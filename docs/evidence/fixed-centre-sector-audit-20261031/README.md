# Fixed-centre pocket-sector pixel audit — 31 October 2026

## Question

Does the exact image around the lower-left pocket contain repeatable boundary evidence at the candidate black-ball location, and how much of that evidence also appears when the pocket is empty?

This is an offline pixel diagnostic. It does not run the detector, select candidates, change canonical table state, or prove a ball centre. It uses two six-frame series from distinct source timelines and lighting conditions, so the empty-pocket series is a spatial control, not a matched negative trial.

## Inputs and fixed references

Every source PNG was SHA-256 checked against its original frame receipt before the calibrated 1000×2000 warp. Each frame also had its exact source PTS, timeline and calibration ID checked against the manifest.

| Series | Frames | Source timeline | Lighting/layout |
|---|---|---|---|
| Black present | 554, 635, 676, 713, 748, 789 | `cd17a3fa0a654f7fac57d2d122ba30e2` | Side daylight; black, cue and red clustered at the lower-left pocket |
| Black absent | 978, 1072, 1105, 1150, 1179, 1198 | `2ea48e0c35cd474eb627acf970a7eec5` | Dimmer session; same pocket region empty |

Two source-pixel references were held fixed within each series:

- `(99,897)`: unreviewed local-model proposal.
- `(104,896)`: manually inspected interior point from the saved black-present frame. It is not a reviewed centre label.

The absent series uses both coordinates in the empty-pocket region. The radii are fixed at 27 analysis pixels. Calibration maps each source point separately; no circle fit or proposal search occurs.

## Measurements

For each sector, the exporter recomputes generic boundary support from the exact warped BGR pixels. It separately counts a diagnostic dark transition when inner value is at most 40, outer value is above 40, and edge contrast is at least 16. “Combined” is the union, so a sector passing both rules is counted once. The final columns count sector indices that pass in at least four of six frames; frames are repeated observations of one layout, not independent trials.

| Series / reference | Mean normal sectors | Mean dark-transition sectors | Mean union sectors | Persistent normal ≥4/6 | Persistent dark ≥4/6 | Persistent union ≥4/6 |
|---|---:|---:|---:|---:|---:|---:|
| Black present, model proposal | 7.67 | 8.17 | 10.33 | 7 | 7 | 9 |
| Black present, interior point | 9.00 | 9.33 | 11.33 | 9 | 9 | 11 |
| Black absent, model point | 3.50 | 3.83 | 3.83 | 4 | 4 | 4 |
| Black absent, interior point | 0.67 | 3.67 | 3.67 | 0 | 3 | 3 |

The interior point produces a larger positive/empty-pocket gap in these retained frames than the local-model point: 11 versus 3 persistent union sectors, compared with 9 versus 4. This shows sensitivity to the chosen fixed location. It does not establish that the interior point is the true centre, that the absent control represents daylight, or that a new temporal acceptance rule is safe. All positive unions remain far below the production black gate of 21/32, and the existing false-positive behavior at other dark rail/pocket candidates remains a separate concern.

Visual review of the [positive contact sheet](present-six-contact.png) and [empty-pocket contact sheet](absent-six-contact.png) confirms the positive anchor circles land on the visible black body, while the pocket jaw, cue and red interrupt parts of its contour. The empty pocket is also dark, but has fewer persistent boundary sectors at these coordinates. No detector threshold or production behavior changed.

The initial [five-anchor snapshot](fixed-anchor-sector-contact.png) provides nearby controls from one positive frame: black proposal 6 normal / 5 dark / 8 union; cue 10 / 0 / 10; adjacent red 6 / 1 / 6; rail shadow 2 / 1 / 3. The same black proposal point in empty-pocket frame 978 gave 3 / 3 / 3. Frame 978 is from another session and lighting level, so these counts are exploratory only.

## Reproduction

Run the two source timelines separately; the exporter hash-checks every frame and receipt. The local pixel export does not require an APK build, Pixel replay or camera session.

```powershell
python scripts/Export-PocketArcSectorPixelEvidence.py `
  --evidence-directory work/production/evidence/code66-pocket-cushion-replay-20261031/physical-side-daylight/exact-replay-input `
  --fixed-anchor-manifest docs/evidence/fixed-centre-sector-audit-20261031/present-six.json `
  --output-directory work/production/evidence/fixed-centre-sector-audit-20261031/export/present-six

python scripts/Export-PocketArcSectorPixelEvidence.py `
  --evidence-directory work/production/evidence/code70-daylight-pocket-capture-20261031/pocket-arc-holdout `
  --fixed-anchor-manifest docs/evidence/fixed-centre-sector-audit-20261031/absent-six.json `
  --output-directory work/production/evidence/fixed-centre-sector-audit-20261031/export/absent-six
```

The source manifests are [present-six.json](present-six.json) and [absent-six.json](absent-six.json). Each export also writes `fixed-anchor-series-summary.csv` with sector frequencies and the persistent-sector indices, so repeated-frame summaries do not need a separate analysis script. The generated per-sector CSV/JSON, series summaries and contact sheets are retained under `work/production/evidence/fixed-centre-sector-audit-20261031/export/`.

## Next

Acquire a same-camera, same-light, same-pocket present/absent pair. First inspect a clear present view and a clear empty-pocket view; then repeat the fixed-pixel audit and full detector replay. Keep the 21/32 gate and candidate cap unchanged until the true ball and dark-shadow controls separate across all six pockets and neighboring colors.
