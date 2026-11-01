# O09 source-image radius guide review — 1 November 2026

## Result

**`O09-IMAGE-EDGE-RADIUS-GUIDES` — PASS 155/155 guide accounting; user-reviewed as apparent source-image radii.** Ten source-image sheets displayed cyan rim guides around the previously reviewed centres. The user replied “all look correct” after being asked whether the circles followed the visible rims. This approves the displayed apparent image-space radii for a separate O09 label derivative. It does not establish physical cloth-plane ball size, a detector score, or a numeric centre/radius uncertainty bound.

## Provenance and method

The proposal-only script `scripts/Propose-O09SourceImageRadiusGuides.py` read the read-only review pack and its hash-bound source PNGs. For each visible centre, it sampled 360 angles in a 112×112 source-pixel crop, blurred CIELAB channels with a 5×5 Gaussian kernel (sigma 1), and chose the radius from 12–31 source pixels with the strongest 35th-percentile radial gradient response. Cyan circles are guides; the `q35` caption is an image-edge response, not a confidence score. No guide reached a search boundary.

The 155 provisional radii span 16–25 source pixels (median 22, nearest-rank p95 24). The response spans 17.599–224.845 (median 115.846, nearest-rank p95 197.718). These ranges describe the reviewed image guides; they are not tolerance limits. The user’s review confirms the displayed circles followed the visible rims, without supplying numeric uncertainty values.

Guide report SHA-256: `CACDC4ED432A2CDBCAF920C3552B89737BD3D70E0718B3626C819E021D13B410`. The generator accounted for **155/155** visible points across **10/10** frames and confirmed **20/20** source image and label files unchanged. The report and sheets are retained at `work/o09/radius-guide-proposals-radial-v1/`.

An earlier Hough-circle pilot was rejected: it detected 154/155 points, but centre offsets had p50 2.9 source px, p95 8.7 px and maximum 10.7 px; pocket-adjacent guides visibly landed on unrelated rail/pocket edges. It is not used in the reviewed derivative. One initial PowerShell summary command failed on quoting and was rerun with a here-string; this was a command-orchestration failure, not a detector or label failure.

## Decision and boundary

The accepted guides are recorded as apparent source-pixel radii in the user-approved derivative only. For an image of width `W` and height `H`, the derivative uses `x = sourceXpx/W`, `y = sourceYpx/H`, `radiusX = radiusSourcePx/W`, and `radiusY = radiusSourcePx/H`. This preserves image geometry for YOLO boxes. The original O08 corpus, its labels, transform and frozen split remain unchanged. No model was trained, and no inference, candidate comparison, Holdout, Pixel resource result, or physical acceptance result follows from this review.

The exact response and guide-report hash are recorded in `work/o09/radius-reviewed-corpus-v1/o09-radius-review-approval.json`. See the [round-trip and export audit](live-detect-o09-radius-label-roundtrip-20261101.md) for derivative checks and current gates.
