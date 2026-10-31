# Raw-perspective packed colour-peak probe — 31 October 2026

This offline probe compares the seed-generation portion of `PackedBallClusterRecovery.FindColourPeakBodies`:

- **Raw-mask branch:** make the red/yellow HSV masks from decoded camera pixels, then nearest-neighbour warp each mask to the canonical 1000×2000 analysis grid.
- **Rectified baseline:** linearly warp decoded BGR pixels first, then make the same HSV masks.
- Both run the production distance transform, 17×17 local-maximum test, minimum interior radius, and canonical edge/pocket/radius seed gates. Seeds are matched by colour to the provisional manual centres within 27px.

The source frame, receipt and label hashes are checked through the existing shared frame loader. This compares candidate seeds only. It does not run the outer-boundary fit, classifier, dense-window selection, duplicate logic or final uncertainty gates; a matching seed is not a detected ball.

## Results

| Frame | Raw seeds | Rectified seeds | Raw labels matched / unmatched seeds | Rectified labels matched / unmatched seeds |
| --- | ---: | ---: | ---: | ---: |
| Daylight pocket-positive, frame 1365 | 15 | 15 | 14 / 1 | 14 / 1 |
| Dim pocket-control, frame 978 | 15 | 15 | 14 / 1 | 14 / 1 |
| Packed rack, frame 1 | 22 | 20 | 14 / 8 | 14 / 6 |

All 14 labelled reds/yellows in each case had at least one colour-matched seed in both branches. On the rack, the raw mask made two additional red seeds without improving label coverage. Median seed-to-label offsets were 6–8px on the rack and 2–8px on the pocket frames. These are draft-label comparisons, not detector centre errors.

The source-colour-mask change therefore shows no meaningful recall gain for red/yellow seed generation in these samples. It also indicates the raw-versus-rectified question should now focus on subsequent boundary fitting and on the dark/neutral black and cue paths, which were the missing detections in the daylight production replay. Do not relax boundary support from these seed results.

The labels are provisional, the pocket frames come from different capture timelines, and no session-separated holdout has been scored. A raw boundary experiment should project each canonical ball perimeter through the inverse calibration and compare sector evidence at the same physical centres, then retain the existing acceptance threshold and matched absent-ball controls.

## Reproduction

```powershell
python scripts/Compare-RawPerspectivePackedColourPeaks.py `
  --case daylight work/production/evidence/code70-daylight-pocket-capture-20261031/exact-replay-input/1365.png work/production/evidence/code70-daylight-pocket-capture-20261031/exact-replay-input/1365.json docs/evidence/pocket-cushion-centre-labels-20261031/positive/manual-labels-frame-1365-draft.json `
  --case dim-pocket-control work/production/evidence/code65-source-frame-check-20261031/exact-replay-input/978.png work/production/evidence/code65-source-frame-check-20261031/exact-replay-input/978.json docs/evidence/pocket-cushion-centre-labels-20261031/negative/manual-centres-frame-978-draft.json `
  --case packed-rack work/production/evidence/o08-holdout-20261029/raw-session-e3c02bb9dc4542948c829a512630041a/frame-01.png work/production/evidence/o08-holdout-20261029/raw-session-e3c02bb9dc4542948c829a512630041a/01.json work/production/evidence/o08-holdout-20261029/raw-session-e3c02bb9dc4542948c829a512630041a/manual-labels-frame-01.json `
  --output docs/evidence/raw-perspective-packed-colour-peaks-20261031
```

The output contains `summary.json`, the accepted seed list in `seeds.csv`, and overlays for both branches in each case.
