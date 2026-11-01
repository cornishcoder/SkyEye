# Draft centres for black-absent pocket/cushion frame 978

`manual-centres-frame-978-draft.json` contains provisional visual estimates for the 15 visible balls in the exact decoded frame. The source PNG hash and the receipt hash are recorded in the JSON. The coordinates were placed from the raw source image with a 50-pixel coordinate grid; no detector or model output was consulted.

The labels are **assistant draft; user review needed**. The red and yellow balls near rails and pocket jaws have wider uncertainty. In particular, the cue ball and red near the lower-left pocket, and the pairs beside the upper and lower pockets, need review before any centre-error score or detector gate decision.

`manual-centres-frame-978-draft-overlay.png` shows the provisional centres on the raw source. `coordinate-grid-50px.png` is the source with the coordinate grid used to place them.

Frame 978 is a black-absent, empty-mouth spatial control from timeline `2ea48e0c35cd474eb627acf970a7eec5`. It is from a separate, dimmer timeline than the black-present daylight frame 1365. The two saved calibration corner sets match, but the pair is not a same-light or same-session comparison.

## One-point review for frame 1179

After seeing the model proposal, the user confirmed that source pixel `(125.2, 194.4)` is the centre of the visible red ball near the upper-left pocket in saved frame 1179. [The point record](user-confirmed-point-frame-1179.json) binds that proposal-conditioned confirmation to the source PNG and receipt, source PTS, proposal file, exact-profile replay report, and manifest. It is not an independently placed ground-truth centre. This is a point-level label only: it does not review the frame's full inventory or any other ball centre, and it is not a held-out accuracy score.

`frame1179-red-proposal-review.png` remains a separate review aid for the YOLOv11 proposal. Its marker shows the model proposal; it is not itself the user confirmation or a full-frame label.
