# Frame 1365 provisional centre labels

This draft was made by visual inspection of the exact decoded raw PNG, using the 100px full-frame and 10px lower-left coordinate grids produced by `label_frame1365_draft.py`. No detector, local-model, candidate, or prior overlay output was consulted while placing the coloured-ball and cue centres.

- Source: `1365.png`, frame 1365, PTS `80806601200`, SHA-256 `602e00c4eb9fc92f3b124106137d8ca7c56f932e107f995f1f2c7d824dd25de4`.
- The expected inventory is 7 reds, 7 yellows, cue, and black. Fifteen centres are marked; the black is unresolved.
- The black body merges into the lower-left pocket/jaw and overlaps the cue ball. No centre is assigned. The earlier `(102,903)` reference is an interior point only and was not used as a centre.
- Other centres are approximate, generally within 3–4px by visual estimate. Balls near jaws and cushions should be user-reviewed before scoring any detector error.
- `manual-centres-frame-1365-draft.png` shows all proposed centres. `manual-centres-frame-1365-lower-left-review.png` enlarges the ambiguous group. Both are review aids, not truth overlays.

The JSON status is `assistant draft; user review needed`. Do not use these points as ground truth or as a production gate until reviewed and, especially for the black, a defensible centre can be identified.
