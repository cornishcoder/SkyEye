# T9 progress: labels from existing frames and clips

Status: **sheets ready; waiting for Richard's two review sittings** (11 November 2026).
No labels have been marked reviewed. Nothing under `src/` or `docs/argon/` was changed.
No git writes were made. No Holdout row was opened.

## Done

- `tools/evidence/build_label_review_sheets.py` (stages `select`, `sheet1`, `sheet2`;
  each fits a 180 s call; `--force` redraws).
- `tools/evidence/apply_label_review.py`: turns a reply text file into reviewed labels.
  It was tested on the fake reply `work/evidence-audit/t9/example-reply.txt`, with output
  written outside the repo and then deleted. The test covered:
  - confirm, add, move, recolour, delete, the positional `colours` list, skip and note;
  - interval retime, pot, and region colour or class;
  - error lines (unknown item; time ranges such as `2.5-5.2`, which were read as negative
    until fixed);
  - refusal to write into the draft folders.
  It has not been run on a real reply.
- Review guide: `work/evidence-audit/t9/README.md`. It covers what to check, the reply
  grammar, and what happens to a reply.

### Sheet 1 (`work/evidence-audit/t9/sheet1/`)

There are 39 frames. For each one, the folder has:

- a review JPEG: the rectified table with a 0.25 grid, numbered DRAFT circles, and crops
  of the six jaws and of the (1.48, 0.067) spot;
- `drafts/<item>.labels-draft.json`: sequence-label schema 2, `table-2x1`, `reviewed: false`;
- a row in `index.csv`.

The drafts are the detector result stored at capture. There are 495 draft balls.

The frames were chosen from the T1 manifest's `unlabelled-reference` rows. Excluded:
Holdout rows, gamma- or ffmpeg-processed copies, and frames with no stored result.
Bursts were limited to 1 or 2 frames per directory, and duplicate layouts were skipped.

| Category | Frames |
|---|---|
| Dim (V 28-76), cue or black at a cushion or jaw, or black not detected | 19 |
| Rack (5 dim) | 10 |
| Ball at or near (1.48, 0.067) | 4 |
| Empty control (ball deliberately removed: mid-cushion black, mid-cushion cue, opposite cue, Code65 cue-absent jaw) | 4 |
| Bright, cue at a cushion | 2 |

- By light: 24 dim, 7 mid, 8 bright.
- Two frames are V 28. These are the darkest frames in the corpus; no labelled frame is
  darker than V 69.
- In 28 of the 39 frames the stored result has no black.
- Frame 978 already has reviewed labels and is not repeated. Three frames from its Code65
  session are included: 1331, 423 and 455.
- Splits: 3 Development, 36 unknown.

### Sheet 2 (`work/evidence-audit/t9/sheet2/`)

This covers all 35 clips from 29 October that T4 scanned (678 s in total). For each clip
there is:

- a timeline JPEG: a 1 s rectified thumbnail grid, the T4 motion trace, and numbered DRAFT
  intervals (159 in total: settled, moving, and a 0.4 s "shot" at each motion onset);
- a rest panel for each of the 28 T4 shot pairs: before and after, with 102 DRAFT T4
  grey-v3 change regions and before/after zooms;
- `drafts/<item>.labels-draft.json`: format `t9-clip-labels-draft v1`; intervals in the
  project shape, with `certainty: uncertain`;
- a row in `index.csv`.

### Sheets viewed for legibility

`F01_250` (V 28), `F10_646` (V 71), `F21_370` (the black at 1.48 is visible and was not
detected), `C12` timeline and `C12 s0` rest panel. The first version had ghosted text,
because this OpenCV build's font widens with thickness. It also had clipped jaw crops.
Both are fixed.

## Decisions and caveats

- The drafts follow the project's precedent (frame 978): review starts from a draft, and
  the reviewed label records that. Added or moved centres are read off the grid to about
  0.01 table units.
- Reviewed output goes to `work/evidence-audit/t9/reviewed/`, which `index_labels.py`
  skips. Publishing into the manifest is a separate, deliberate step: copy to
  `work/o09/t9-label-review-v1/<item>/labels.json`, then rerun `index_labels.py` and
  `build_corpus_manifest.py`.
- Clip labels have no indexed frames or hashes, so they cannot enter the observation
  workbench as they are. They are meant for scoring T4 (and later T6) offline.
- Rest-panel coordinates use T4's substitute 29 October calibration corners.

## Next steps

1. Richard does sitting 1 (frames, about 40 min) and sitting 2 (clips, about 40 min) and
   replies per the README.
2. Run `apply_label_review.py` on each reply. Fix any lines it reports and rerun it until
   no item is unexpectedly incomplete.
3. Decide whether to publish the sheet 1 labels into the manifest, then rebuild and
   rescore (`score_detections.py --manifest`). This would add the first labelled frames
   below V 69 and more black-at-cushion cases.
4. Score T4 against the sheet 2 labels: moved balls against regions, false regions, and
   cushion or jaw arrivals.
