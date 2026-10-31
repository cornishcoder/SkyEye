# Raw-perspective colour-body proposal probe — 31 October 2026

This offline A/B isolates the colour-body proposal mask used by `VisionAnalyzer.AddColourBodies`.

- **Raw-mask branch:** threshold HSV on the decoded camera frame, then nearest-neighbour warp the binary mask to the canonical 1000×2000 analysis grid.
- **Rectified baseline:** warp decoded BGR to the same grid with the existing linear interpolation, then threshold HSV.
- Both branches use the production colour-body thresholds, connected components, component radius, aspect/fill filters, 23px edge margin, and 48px pocket exclusion on the canonical grid. Candidate centres are compared one-to-one against the provisional manual centres within 27px.

The input images and receipts are hash-checked through the shared frame loader in `scripts/Compare-RawPerspectiveHoughProposals.py`. Raw-frame coordinates are transformed through the same calibration homography before scoring. The diagnostic does not call or alter `VisionAnalyzer`.

## Results

| Frame | Raw-mask proposals | Rectified proposals | Raw matches | Rectified matches | Median centre error (raw / rectified) |
| --- | ---: | ---: | ---: | ---: | ---: |
| Daylight pocket-positive, frame 1365 | 8 | 8 | 8/14 | 8/14 | 1.700 / 1.666 px |
| Dim pocket-control, frame 978 | 8 | 8 | 8/14 | 8/14 | 1.212 / 1.317 px |
| Packed rack, frame 1 | 0 | 0 | 0/14 | 0/14 | — |

The two pocket frames have the same six unmatched draft labels: red-01, red-03, red-07, yellow-01, yellow-03 and yellow-07. All eight emitted candidates match a red/yellow label in this small draft set. The raw mask created 22 components versus 15 on the daylight rectified image, and 35 versus 41 on the dim frame; after component-size filtering, each branch retained the same eight candidates. Every rack component exceeded the isolated-body size gate, so the result says nothing about packed-cluster recovery.

This does **not** mean the full detector only finds eight coloured balls: Hough and packed-colour proposal paths also contribute to `VisionAnalyzer`, and the production replay of the daylight frame reported 7 reds and 7 yellows while missing the cue and black. It means that thresholding decoded source pixels before the warp did not improve this isolated colour-body proposal stage on these frames. It does not rule out raw-space boundary sampling, packed-cluster proposals, or neutral/dark-ball recovery.

Labels are provisional and the daylight/dim frames come from different capture timelines. No full classifier, deduplication against other proposal stages, boundary refinement, uncertainty gate, event reducer, or live capture was involved. Treat the counts as diagnostic only, not acceptance evidence.

## Reproduction

```powershell
python scripts/Compare-RawPerspectiveColourBodyProposals.py `
  --case daylight work/production/evidence/code70-daylight-pocket-capture-20261031/exact-replay-input/1365.png work/production/evidence/code70-daylight-pocket-capture-20261031/exact-replay-input/1365.json docs/evidence/pocket-cushion-centre-labels-20261031/positive/manual-labels-frame-1365-draft.json `
  --case dim-pocket-control work/production/evidence/code65-source-frame-check-20261031/exact-replay-input/978.png work/production/evidence/code65-source-frame-check-20261031/exact-replay-input/978.json docs/evidence/pocket-cushion-centre-labels-20261031/negative/manual-centres-frame-978-draft.json `
  --case packed-rack work/production/evidence/o08-holdout-20261029/raw-session-e3c02bb9dc4542948c829a512630041a/frame-01.png work/production/evidence/o08-holdout-20261029/raw-session-e3c02bb9dc4542948c829a512630041a/01.json work/production/evidence/o08-holdout-20261029/raw-session-e3c02bb9dc4542948c829a512630041a/manual-labels-frame-01.json `
  --output docs/evidence/raw-perspective-colour-body-probe-20261031
```

The output directory contains the hash manifest and component-level stage counts in `summary.json`, accepted proposals in `candidates.csv`, and raw/rectified overlays for all three frames.

## Next experiment

Compare raw-mask-before-warp and rectified-mask-after-warp for the actual packed red/yellow peak generation, followed by the sparse black and cue candidate paths. Reuse the existing canonical boundary/classification gates in the first diagnostic branch, preserve every rejection reason, and use reviewed session-separated positives plus empty-pocket/absent-ball controls. If raw proposals map to the same centres and fail the same gates, test raw-coordinate boundary evidence separately before considering any production path change.
