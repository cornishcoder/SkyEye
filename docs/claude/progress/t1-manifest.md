# T1 corpus manifest: progress note

Resumed and finished 11 November 2026. T1 done; awaiting commit by the coordinator.

## Done

- `tools/evidence/build_corpus_manifest.py` (new). Runs cleanly in about a minute:
  `python3 tools/evidence/build_corpus_manifest.py`.
- `tests/fixtures/detection-corpus/manifest.json` (generated). 495 rows, one per
  pixel SHA-1 (one row has no pixel on disk and is keyed by receipt SHA-256).
  - Roles: 23 positive, 14 empty-control, 440 unlabelled-reference, 18 holdout-excluded.
  - Labelled rows (37) by light: 19 bright, 8 mid, 8 dim, 2 unknown (O01 still, post-shot 13-ball frame).
  - Labelled rows by source: 26 labels.jsonl, 5 restaged opposite-cue review
    (1711, 1726, 1743, 1764, 1783; 1691 already in labels.jsonl), 4 Layout03
    validation v2 (2083, 2263, 957, 962), 1 warm 266, 1 O01 still fixture.
  - Splits: 125 Development, 1 Regression, 18 Holdout (ids only), 351 unknown.
  - All 37 labelled pixel files re-hashed: SHA-256 matches the label/receipt claim in every case.
  - All six named empty controls and all seven named black positives are tagged
    (`names`). 1365 has only an assistant draft with the black unresolved, so it
    stays unlabelled-reference.
- Holdout rule: any receipt path containing "holdout" gives a row with ids and
  hashes only. No Holdout pixels or labels were opened. Note: Argon's lighting
  table uses `code66-lighting-holdout-20261031/input/{daylight,all,off}-01.png`.

## Verified findings

- **Frame 266.** Pixel SHA-256 f41627016c50... (SHA-1 66643e34578a...) is the
  same file in Argon's link and in the project's score. Every stored result for
  that hash (capture receipt `266.json`, algorithm `...-v25-...-support21`,
  config o08-provisional-v21; timing screen; both duplicate-fix replays, 5
  outputs each) reports 16 balls including cue and black. The width-800 primary
  pass finds 15 (incomplete packed cluster, `primaryBallCount 15`) and the
  width-1000 fallback recovers 16 (`recovered: true`). Argon's per-frame data
  has no 266 row (`warm_train_266_summary.comparisons` is empty) and no file
  with a 14-ball or no-black/no-cue result for 266 was found. The project's
  16/16 stands; Argon's claim is unsupported. Converting the reviewed centres
  through the receipt corners matches the project's table coordinates to 1e-7.
- **Argon export.** 32 per-frame rows, 28 unique sample ids, all 28 mapped to
  hashes. Repeated: Layout03 validation 2083, 2263, 957, 962 (pre and post
  duplicate fix, identical numbers). Frames 1587, 31, 266 and the post-shot
  frame appear only in its corpus summaries.

## Finished on resume

- `score_detections.py` now takes `--manifest PATH [--corpus TAG]`; default use unchanged
  (re-run gives the same 19-frame result). `--manifest ... --corpus claude-review-labels`
  reproduces 19 frames, 292/295, black 12/15, identical strata, misses and per-frame rows
  (order aside). Score file: `work/evidence-audit/score-corpus-v1-production-at-capture.json`.
- Whole manifest, stored results: 24 of 37 labelled frames scored, 368/374 balls, 0 false
  positives, black 13/19 (5 cushion, 1 open). Dim: 4 of 8 frames scored (two layouts),
  61/64, black 1/4; the three dim black misses and the two bright ones (2831, 288) are all at
  table (1.48, 0.067). 266 is 16/16.
- `tests/fixtures/detection-corpus/README.md` written.

## Known gaps

- No stored detector result for 978, 1085, 1274, 382, 3360, 395, 2263, 962,
  1711, 1743, 1783, O01 or the post-shot frame; they need a candidate's detections to be scored.
- `work/o09/label-captures/22a6...` has reviewed but single-ball (partial) labels; not used.
- Zone tags come from label positions only; unlabelled rows have none.
- 351 rows have no split recorded anywhere; treat as unknown, not Development.
