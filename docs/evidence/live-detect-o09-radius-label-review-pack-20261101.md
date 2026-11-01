# O09 radius-label review pack — 1 November 2026

## Target

`O09-RADIUS-LABEL-REVIEW-PACK` creates a visual aid to inspect ball rims around previously reviewed centres. It must preserve source and label receipts and leave radius values blank until a reviewer provides them.

## Generated pack

The repeatable builder is [Build-O09RadiusReviewPack.py](../../scripts/Build-O09RadiusReviewPack.py). It reads the frozen `corpus-v4` manifest, reviewed labels, source PNGs, and `source-to-table-transform.json`. For each visible label it checks the manifest, label and transform pixel hashes, verifies table-space centre agreement, and places a small centre cross on a source crop. It does not read detector output or draw a radius proposal.

Output: `work/o09/radius-review-v1/`

- `review-pack.json` — source, label, transform and image hashes plus frame/point index.
- `radius-review-template.csv` — one row per visible ball with source-pixel centre and blank radius/uncertainty fields.
- `frames/<id>-overview.png` — full-frame source image with reviewed centre IDs.
- `frames/<id>-centre-crops.png` — 3× enlarged source crops, grouped by frame and ball ID.
- `README.md` — review limits and instructions.

The ten crop sheets were displayed inline for user review. No radius measurement has been reviewed or accepted yet. These views support apparent source-image radii only; they do not establish physical cloth-plane radius or resolve ball-height/parallax uncertainty.

## Targeted checks

Command:

```powershell
python scripts/Build-O09RadiusReviewPack.py --corpus-root work/o08/live-detect-black-pair-v1/corpus-v4 --transform work/o08/live-detect-black-pair-v1/corpus-v4/source-to-table-transform.json --output work/o09/radius-review-v1
```

Recorded result:

- Frame hash/label/transform binding: **10/10**.
- Visible source-centre crop coverage: **155/155**.
- Original source images and label files unchanged: **20/20**.
- Radius values written/proposed: **0**; blank reviewer-template rows: **155**.
- Process exit code: **0**.

This is a review-pack integrity result, not a detector test or O09 training-readiness pass. No label, manifest, split lock, or transform was modified. O09 eligible exports remain **0** because current corpus-v4 has **0/155** reviewed numeric radii. Overall detector inventory remains **0/3** and physical D1/D2/E remain **0/3 proven**. Software suites **3/3** and focused motion-feed **20/20** are carried forward, not rerun.

The next step depends on the visible rim review. Mark any clipped, blurry, overlapping or otherwise ambiguous frame/ID as uncertain and leave its CSV radius blank. Do not replace missing radii with detector circles or use this Development corpus as Holdout.
