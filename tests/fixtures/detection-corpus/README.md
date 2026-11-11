# Detection corpus manifest

`manifest.json` is the single list of frames for scoring ball detection
offline. It has one row per pixel file, keyed by SHA-1 (the SHA-256 is also
given). Each row gives:

- the image path (under `work/`, which is git-ignored);
- calibration corners and the receipt they came from;
- reviewed labels, converted to table coordinates (x 0 to 2, y 0 to 1),
  with colour, visibility and zone (pocket, cushion or open) per ball;
- light stratum from the table's V value: dim below 80, mid 80 to 109,
  bright 110 and above;
- role, corpora of origin, split and setup group;
- the production result stored in the receipt at capture time.

A scorer needs nothing else.

Roles:

- `positive`: reviewed labels; black and cue both visible, or only coloured
  balls missing.
- `empty-control`: reviewed labels; the black or cue is deliberately absent.
- `unlabelled-reference`: a receipt-bound frame with no reviewed labels.
- `holdout-excluded`: a frame in a folder named holdout. Only ids and hashes
  are recorded. Do not open these pixels or labels.

Development rows are tuning data.

## Regenerate and score

    python3 tools/evidence/build_corpus_manifest.py
    python3 tools/evidence/score_detections.py --manifest tests/fixtures/detection-corpus/manifest.json
    python3 tools/evidence/score_detections.py --manifest tests/fixtures/detection-corpus/manifest.json --corpus claude-review-labels

The builder reads `work/evidence-audit/` (inventory, receipts, labels,
robust light), the project split locks, the frozen v3 split plan and four label
sources that `index_labels.py` does not read. Add `--detections FILE` to score
a candidate detector instead of the stored results.

## Contents

| | Rows |
|---|---|
| Total | 495 |
| Positive | 23 (bright 10, mid 4, dim 7, unknown 2) |
| Empty control | 14 (bright 9, mid 4, dim 1) |
| Unlabelled reference | 440 (bright 171, mid 88, dim 179, unknown 2) |
| Holdout excluded | 18 |

The 37 labelled frames come from:

- 26 from `labels.jsonl` (this review);
- 5 from the restaged opposite-cue review (1711, 1726, 1743, 1764, 1783);
- 4 from Layout03 validation v2 (2083, 2263, 957, 962);
- 1 is warm frame 266;
- 1 is the O01 still fixture.

Every labelled pixel file was re-hashed, and each matches its claimed
SHA-256. Splits: 125 Development, 1 Regression, 18 Holdout, 351 unknown.

The plan's named frames are tagged in `names`:

- empty controls: 461, 844, 542, 562, 206, 978;
- black positives: 375, 678, 1365, 1691, 1726, 1764, 2831.

Frame 1365 has only an assistant draft with the black unresolved, so it has
no labels.

## Scores from stored results

- **Original 19 frames** (`--corpus claude-review-labels`): 292 of 295 balls
  and black 12 of 15. This is identical to
  `work/evidence-audit/score-production-at-capture.json`, apart from row order.
- **All 24 labelled frames that have a stored result:**
  - 368 of 374 balls, no false positives, colours all correct;
  - black 13 of 19, with 5 misses at a cushion and 1 in the open;
  - every other colour 100%.
- **The 13 labelled frames with no stored result:** 978, 1085, 1274, 382,
  3360, 395, 2263, 962, 1711, 1743, 1783, O01 and the post-shot frame.
  They can only be scored with a candidate's detections.

By light (stored results; one analyser, `...-v25-...-support21`):

| Stratum | Labelled frames | Scored | Balls found | Black found | Cushion balls found |
|---|---|---|---|---|---|
| Dim (V 69 to 71) | 8 | 4 | 61/64 | 1/4 | 18/21 |
| Mid | 8 | 6 | 92/93 | 4/5 | 11/11 |
| Bright | 19 | 14 | 215/217 | 8/10 | 73/75 |

Read the dim row with care:

- The 4 scored dim frames are only two layouts. Frames 1691, 1726 and 1764
  are stills of one restaged table, and 266 is the other.
- All dim frames sit just under the V 80 boundary.
- The three dim black misses are the same ball at the same spot,
  (1.48, 0.067) on the top cushion.
- That spot is also where the two bright misses are (2831 and 288).
- So the misses follow position, not light.
- Warm frame 266 is read perfectly.

## Frame 266

Argon's evidence says the black and cue were missed on warm frame 266. The
project's reviewed score says 16 of 16. **The project is right.**

- Both refer to the same file (SHA-256 `f41627016c50...`).
- Every stored result for that file reports 16 balls including cue and black:
  the capture receipt and three later replays.
- The width-800 first pass finds 15. The width-1000 fallback recovers the 16th.
- Argon's export has no per-frame row for 266, and its 266 comparison list is
  empty.

This manifest's own score of the capture receipt agrees: 16 of 16, colours
correct.

## Argon's benchmark

`docs/argon/data/aggregated_regression_benchmark.json` has 32 per-frame rows
over 28 unique frames. All 28 are mapped to hashes in `argonMapping`.

- The four Layout03 validation frames (2083, 2263, 957, 962) appear twice,
  before and after the duplicate fix, with identical numbers.
- Frames 1587, 31, 266 and the post-shot frame appear only in its corpus
  summaries.

## Obstruction hand controls

`obstruction-hand-controls.json` is a separate list, not part of the manifest
above, using the same frame schema with role `obstruction-control`. It holds
31 dim frames (table V 59 to 76) decoded from the saved code57 event clips of
29 October 2026, each with an `expect` field:

- `obstructed` (16): a hand, arm or body clearly over the playing surface;
  the detector must report obstructed;
- `edge` (12): a bridge hand at or on the cushion; recorded, not scored;
- `cue-only` (3): only the cue stick over the cloth; recorded, not scored.

The cloth in these clips is blue-grey, not warm-tinted, so they do not cover
the warm-cloth case of manifest frame `hand-2s`. Corners are the current-mount
set, checked visually; the session's own calibration was not saved with
corners. Score with:

    dotnet run --project tests/SkyEye.Vision.Desktop.Runner -- detect --manifest tests/fixtures/detection-corpus/obstruction-hand-controls.json --roles obstruction-control --output OUT.jsonl

## Black-absent dim rack control

`black-absent-dim-rack.json` (role `black-absent-control`): five lossless verifier frames of a full rack of
7 reds and 7 yellows with the black left out, cue in baulk, evening light (table V 66), captured 11 November
2026 (run `aa485778`). A detector must report no black. Score with
`detect --manifest tests/fixtures/detection-corpus/black-absent-dim-rack.json --roles black-absent-control`.

## Known gaps

- Dim coverage is two scored layouts, and no frame is darker than V 69.
- Zone tags exist only for labelled frames.
- 351 rows have no recorded split. Treat them as unknown, not Development.
- `work/o09/label-captures/` holds reviewed single-ball labels. They are partial
  and not used.
- Argon's lighting table uses three images from
  `code66-lighting-holdout-20261031`. They are listed here as
  holdout-excluded.
