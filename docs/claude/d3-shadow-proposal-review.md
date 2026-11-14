# D3 shadow proposals: review of the device runs (11 November 2026)

**Target test:** `D3-SHADOW-PROPOSAL-REVIEW`. **Question:** are the settled-change shadow rules (U1 hold, U2
infer black/cue, U3 departure support, U4 pocket, and the logged-only "unexplained change") right often
enough on real play to be allowed to act? **Answer: not yet; there is no live evidence on U1 to U4, and the
only rule that fired was wrong 10 of 10 times, for two identifiable reasons.** Offline review of logs
already pulled; no capture.

## What was reviewed

Every schema-15 Live Detect run on the device today, from the five pulls under
`work/production/evidence/*/after/live-detect/`:

| Run | Version | Passes | Comparisons | What happened |
|---|---|---|---|---|
| `755281db` 12:38 | 0.8.63 | 344 | 5 | shot after a rack, daylight |
| `7c151fba` 14:29 | 0.8.64 | 259 | 2 | rack and break, no baseline (false obstruction) |
| `65ccfdec` 15:57 | 0.8.65 | 2 | 0 | stopped by the NaN logging bug |
| `3a434fbb` 16:33 | 0.8.66 | 481 | 3 | rack, break |
| `5a519dc2` 20:32 | 0.8.67 | 275 | 4 | rack, break, red potted |

Each run was replayed through the **current** policy (`--golden-shadow` logged replay, which includes the
chain-anchor fix the morning run lacked), and every ball-sized region checked against the saved raster pairs
and Live Detect's own ball positions at that pass.

## Result

- **U1 hold, U2 infer black/cue, U3 departure support, U4 pocket: 0 firings in 5 runs.** In every run the
  detector saw all the balls at the baseline, so there was nothing to hold or infer. These rules have only
  their offline golden checks.
- **"Unexplained change": 10 firings, all real changes, none unexplained.**

| Proposals | Run | Real change (rasters) | Why it was not explained |
|---|---|---|---|
| 8 | `3a434fbb` break | 8 balls arriving after the break | the pass was **obstructed**, so the current layout was untrusted and empty; nothing could explain them |
| 1 | `755281db` shot | the cue leaving baulk | region centre **0.050** from the baseline cue; match radius `Near` = 1.6 x 0.027 = **0.0432** |
| 1 | `5a519dc2` break | a yellow arriving at the cushion | region centre **0.043** from the detected yellow, just outside `Near` |

## Finding: change-region centres are pulled toward the cast shadow

Region centre minus the nearest detected ball, at each comparison:

- Evening lamp (`3a434fbb`, `5a519dc2`): the ball sits +0.015 to +0.050 along the table and about -0.01
  across from the region centre, growing with distance from the lamp (up to one ball diameter, 0.050).
- Daylight (`755281db`): the offset is smaller and points the other way.

A changed region contains the ball and its shadow, so its centroid moves toward the shadow. With
`Near` = 0.0432, a correctly detected ball can fail to explain its own region. This affects:

- the unexplained-change rule (false proposals, as above);
- **U3 departure support** (a departure region may fall outside `Near` of the missing ball: support lost);
- **U2's exclusion** of regions that a detected ball already explains (a shadow-shifted arrival of a
  detected ball looks unclaimed). U2 also needs a dark appearance, which a shadow can supply, so this is the
  case to fix before U2 may act.

The raster sheets show the same offset visually (circles up-left of the balls in the evening runs).

## Recommendation

1. **Fix the region position before any rule acts.** Either take the region centre from the part of the
   change that is ball-like (colour or brightness of a ball, not the darkened cloth), or match against the
   region's extent rather than its centroid. Re-score on these five runs: the 2 shadow cases must become
   explained, and nothing else may change.
2. **Treat obstructed passes as "cannot judge", not "unexplained".** The 8 break proposals came from an
   untrusted current layout; the logged-only rule should not fire without a trusted layout.
3. **Get live evidence on U1 and U2** with the case they exist for: the black at the top cushion
   (1.48, 0.067), which the detector misses at the baseline. That needs a short staged session (new staging,
   Richard's call), with source frames saved for exact replay.

Until 1 to 3 are done, the rules stay in shadow.

Software, detector and physical metrics carried forward from `ef66dd1` and `da51c59` (no code change in this
review). Detector inventory 0/3 and physical gates D1/D2/E 0/3 carried forward.
