# Radial boundary-offset audit

This offline, diagnostic-only experiment checked whether the square centre-search window in `BallBoundaryRefiner.Refine` lets a fit drift diagonally far enough to explain the missing black near the lower-left pocket. It compared the unchanged square search with the same fitter and scoring under a Euclidean 1.3-ball-radius centre limit. The diagnostic fit never feeds `VisionResult`, production candidates, or `GameState`.

## Inputs and replay receipts

- Black-present frame 1365: source image SHA-256 `602e00c4eb9fc92f3b124106137d8ca7c56f932e107f995f1f2c7d824dd25de4`; source timeline `76ec0f8c622045378494b0c250198ce6`; calibration `27a80900d5fb4cd983a91c6a367389f0`.
- Black-absent frame 978: source image SHA-256 `b368348c63d924f5c7834e8e849b7287225ab525b470bdc3b85f7a8278cbbe70`; source timeline `2ea48e0c35cd474eb627acf970a7eec5`; calibration `6d304dd864f84c8486ee30844fe685b1`.

The inputs have matching calibration corner coordinates but are from different timelines, calibration IDs and lighting sessions. Frame 1365's black location came from a local model proposal, not an independently reviewed centre. Frame 978 did not contain a black proposal; the diagnostic deliberately supplied the frame-1365 source point through frame 978's own calibration to check that exact location. These are failure-localization controls, not a matched acceptance pair. The captured configuration also does not match the current primary profile.

## Results

| Probe | Square search | Radial 1.3R limit | Interpretation |
|---|---|---|---|
| Frame 1365 black proposal | 14/32 sectors; offset 0.516R; classified black | Identical fit and classification | Centre drift is not why this black is rejected; both remain below the 21/32 sparse-black support gate. |
| Frame 1365 rail-shadow seed `(587.5,56)` | 14/32; offset 1.886R; classified red | No fit | The radial limit suppresses this far, weak shadow fit. |
| Frame 1365 interior yellow proposal | 24/32; offset 0.120R; classified yellow | Identical fit and classification | The bound preserves this ordinary interior control. |
| Frame 978 forced black proposal | No fit | No fit | No false black was added at the tested empty-pocket point. This does not test other absent-pocket locations. |
| Frame 978 rail-shadow seed `(587.5,56)` | 24/32; offset 1.545R; classified red | 16/32; offset 1.262R; classified red | A radial cap can change support on a non-black fit, so broader color/pocket controls are needed before changing production behavior. |

The paired reports are `device-replays/radialoffpos-210103/` and `device-replays/radialoffneg-210118/`. Each contains a hash-checked replay manifest and the full `radialOffsetComparison` JSON. The diagnostic build succeeded with zero errors (23 existing warnings); both exact-frame replays completed. The mounted camera remained stopped.

## Decision and next work

Do not change production search geometry from this experiment. A radial limit removes the demonstrated rail-shadow drift but does not recover the actual pocket black: the black proposal still has only 14 directly supported sectors against the 21-sector gate. Continue separating the two failure stages: candidate generation must propose the ball, and the boundary fit must then receive enough valid contour support. Next inspect why the fixed-centre dark-transition trace sees persistent black contour evidence that ordinary exterior-supported sectors reject; validate any temporal or boundary-credit idea against absent-pocket and neighboring-color controls before wiring it into detection.
