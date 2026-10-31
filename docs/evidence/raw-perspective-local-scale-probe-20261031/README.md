# Position-aware raw Hough radius probe — 31 October 2026

This offline follow-up asks whether the earlier raw-perspective Hough screen was harmed by using one median radius for the whole camera image. It estimates the local camera-space ellipse of a 27px canonical ball at each raw Hough centre using the inverse homography Jacobian, then retains candidates whose Hough radius is within a chosen relative tolerance of the ellipse's geometric-mean radius. Proposals are mapped back to the rectified analysis grid and matched one-to-one against the same draft labels as the existing screen.

This remains proposal-only: it does not run SkyEye's colour classifier, `BallBoundaryRefiner`, duplicate checks, pocket acceptance, uncertainty gates, or canonical state. Its planar scale model also ignores ball-height parallax. Unmatched proposals are not confirmed false balls because the centre labels are provisional and the daylight black has no centre label. The daylight positive and dim-pocket control are from separate capture timelines.

## Results

Each cell is matched draft labels / label count, followed by unmatched proposals. “Local ±20%” and “Local ±35%” are raw proposals after the radius filter.

| Frame | Reference votes | Rectified | Raw unfiltered | Local ±20% | Local ±35% |
| --- | ---: | ---: | ---: | ---: | ---: |
| Daylight pocket, 1365 | 9 | 14/15, 147 | 12/15, 149 | 8/15, 79 | 10/15, 131 |
| Dim pocket control, 978 | 9 | 9/15, 207 | 10/15, 256 | 7/15, 173 | 9/15, 249 |
| Packed rack, 1 | 9 | 8/16, 63 | 9/16, 121 | 5/16, 72 | 9/16, 106 |
| Daylight pocket, 1365 | 15 | 2/15, 46 | 5/15, 40 | 5/15, 22 | 5/15, 34 |
| Dim pocket control, 978 | 15 | 5/15, 59 | 4/15, 90 | 3/15, 57 | 4/15, 89 |
| Packed rack, 1 | 15 | 2/16, 8 | 2/16, 27 | 2/16, 18 | 2/16, 25 |

At the stricter 20% tolerance and vote threshold 15, the filter reduced unmatched proposals while retaining the same number of matched labels in the daylight pocket and rack. It lost one matched label in the dim control. At the looser 9 threshold, the 20% filter dropped labelled matches in every case; the 35% filter lost matches in daylight and dim light but retained the rack matches. The 35% filter at vote 15 retained the unfiltered match counts but barely changed proposal counts in the dim frame. Across candidates, median local ellipse anisotropy was only about 1.03–1.07; estimated geometric-mean raw radii ranged about 19.8–25.1px. These results show that local scale filtering can trim some proposals, but do not show a consistent recall or accuracy improvement.

**Decision:** position-aware radius filtering alone is not a meaningful detector fix. Keep production unchanged. The next raw-frame experiment needs a better candidate signal and should pass mapped proposals through the same rectified classifier and acceptance gates before comparing held-out, reviewed, session-separated positives and matched absence controls.

## Artifacts

- [`summary.json`](summary.json) — all cases, thresholds, hashes, label status and metrics.
- [`candidates.csv`](candidates.csv) — raw Hough candidates and their local projected-radius estimates.
- [`daylight-raw-local-scale-0.35.png`](daylight-raw-local-scale-0.35.png) — cyan draft labels and magenta locally filtered raw proposals for frame 1365.
- [`dim-control-raw-local-scale-0.35.png`](dim-control-raw-local-scale-0.35.png) — corresponding view for frame 978.
- [`rack-raw-local-scale-0.35.png`](rack-raw-local-scale-0.35.png) — corresponding view for the packed rack.

The full per-run files, including rectified overlays, are retained under `work/production/evidence/raw-perspective-local-scale-probe-20261031/`. Re-run with `scripts/Compare-RawPerspectiveLocalScale.py` and the exact input bundles listed in `summary.json`.
