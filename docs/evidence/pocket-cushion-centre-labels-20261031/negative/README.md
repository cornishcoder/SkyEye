# Draft centres for black-absent pocket/cushion frame 978

`manual-centres-frame-978-draft.json` contains the original visual estimates for the 15 visible balls in the exact decoded frame. The source PNG hash and the receipt hash are recorded in the JSON. The coordinates were placed from the raw source image with a 50-pixel coordinate grid; no detector or model output was consulted.

On 1 November 2026, the user reviewed the displayed overlay and confirmed that all 15 cyan crosses match the visible cue, seven red, and seven yellow ball centres; no correction or missing visible ball was reported. The separate [user-review record](user-reviewed-centres-frame-978.json) binds this review to the draft and overlay hashes. The draft file is preserved unchanged. Its red and yellow balls near rails and pocket jaws retain rough uncertainty bounds; the review did not set a numeric centre-error tolerance.

`manual-centres-frame-978-draft-overlay.png` shows the assistant-estimated centres that the user reviewed on the raw source. `coordinate-grid-50px.png` is the source with the coordinate grid used to place them.

The blue item near `(942, 44)` source pixels is Taom chalk on the wooden rail outside the cloth, not a ball; it is excluded from the inventory. Frame 978 is a black-absent, empty-mouth spatial control from timeline `2ea48e0c35cd474eb627acf970a7eec5`. It is from a separate, dimmer timeline than the black-present daylight frame 1365. The two saved calibration corner sets match, but the pair is not a same-light or same-session comparison.

## One-point review for frame 1179

After seeing the model proposal, the user confirmed that source pixel `(125.2, 194.4)` is the centre of the visible red ball near the upper-left pocket in saved frame 1179. [The point record](user-confirmed-point-frame-1179.json) binds that proposal-conditioned confirmation to the source PNG and receipt, source PTS, proposal file, exact-profile replay report, and manifest. It is not an independently placed ground-truth centre. This is a point-level label only: it does not review the frame's full inventory or any other ball centre, and it is not a held-out accuracy score.

`frame1179-red-proposal-review.png` remains a separate review aid for the YOLOv11 proposal. Its marker shows the model proposal; it is not itself the user confirmation or a full-frame label.

The [raw-only visual draft note](../raw-only-visual-drafts-20261101.md) includes separate source-only centre estimates for the six Code65 frames. They remain assistant drafts; only the single point above has user confirmation.
