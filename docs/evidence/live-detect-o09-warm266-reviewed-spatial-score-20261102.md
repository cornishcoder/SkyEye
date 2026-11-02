# Warm frame266 reviewed spatial score — 2 November 2026

`O09-WARM266-REVIEWED-SPATIAL-SCORE`: diagnostic16/16 associated,16/16 correct colour,0 unmatched,0 misses for both current800+fallback and direct1000 saved outputs from final exact-duplicate-count replay. Each arm scored once; repeated timing trials are not additional spatial samples. Reviewed source/draft hashes bind the accepted16 centres to frame266 pixel hash and exact receipt calibration. Full result and mapped labels retained in work/o09/development-v3/warm-batch-20261102/warm266-reviewed-spatial-score.json.

A NumPy eight-equation projective transform uses receipt corners in the same order and table targets(0,0),(2,0),(2,1),(0,1) as TableCalibration. One-to-one minimum-cost association uses0.010 table2x1 as a diagnostic gate, not a reviewer-approved accuracy bound. Inverse mapping of associated outputs gives median source error2.41836722 px and maximum4.40621224 px for each arm. These descriptive errors include approximate reviewer-centre uncertainty. A direct C# Map/Unmap parity check remains before canonical corpus ingestion; this Python score is not a replacement implementation in production.

No radius labels, accepted centre tolerance, matched-light claim or D1/D2 pass. The one-frame verifier remains1/5 SampleGap. Full v3 export is incomplete; no Holdout pixels/labels opened or split lock changed. Counts remain captured13/24,reviewed4/12,lighting0/2,D1/D2/E0/3. Next target: C# transform parity and frozen split-plan group alignment for this reviewed frame, then radius-guide review needed for training export. No new replay or camera action occurred.

## C# transform and planned-group alignment

`O09-WARM266-CSHARP-TRANSFORM-ALIGNMENT`: PASS16/16 reviewed points through actual SkyEye.Core TableCalibration.Map/Unmap. Maximum difference from Python score coordinates4.002966042486721e-16 table units; maximum source-pixel round-trip residual2.4457182613372134e-13 px. Residuals prove numerical transform agreement, not label accuracy. Scratch C# project references the repository Core project; output retained at warm-batch-20261102/csharp-transform-alignment.json.

Frozen plan SHA6c0d224aca32e5781c03a3452560aac5fd13a25311cd2ea241791ffe7f34d834 contains setupGroupId o09-v3-warm-train-layout-01, role train, stratum warm-dim-playable. Role/light membership checks pass. Official corpus/split lock unchanged; a complete transitive session-isolation check awaits full corpus assembly. Warm light measured metadata remains incomplete. Next eligible offline task: provisional source-image radius guides from raw pixels at accepted centres, then human rim review before any radius labels or training export. No detector output may supply radii.

## Provisional radius review pack

O09-WARM266-RAW-RADIUS-GUIDES prepared16/16 guides using the existing source-image CIELAB radial-gradient proposal function at accepted centres. Search12..31 px; no search-boundary hits. Source SHA retained. Raw crop sheet displayed and user review requested; no radii accepted or corpus labels written. Proposals/crop sheet retained in warm-batch-20261102/raw-radius-guides-frame266/. Black27 px and R4 25 px are provisional, with touching-ball contours requiring review. Detector radii were not used. Next action requires reviewer rim judgement before a versioned radius-labelled derivative.
