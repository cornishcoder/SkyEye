# Joint pocket-cluster occlusion audit — 31 October 2026

## Purpose and method

Evaluation-only diagnostic for unsupported sectors on the daylight cue/black pocket proposals. It runs the existing exact raw-proposal replay and, for each cue or black proposal with a fitted boundary, samples all 32 sectors from the same decoded and rectified BGR frame. Each unsupported sector is credited only when it is marked `ExteriorRejected` and its outer sample lies within `1.25 × 27 px` of an independently accepted circle whose centre is at least `1.1 × 27 px` from the fitted candidate. The report also calls the existing `BallBoundaryRefiner.HasConservativeOcclusionCoverage` helper with the same sector evidence and accepted centres.

This diagnostic runs only in `SkyEye.Vision.Device` raw-proposal replay. It does not feed candidates to production detection, modify `VisionResult`, or write `GameState`. No production threshold or gate changed. It measures the existing helper; it does not test iterative or mutual cue/black credit, since neither missing ball is an independently accepted neighbour.

## Exact-frame results

| Case | Source SHA-256 | Fit sectors: supported + explained + unexplained | Helper | Inventory before → with raw proposals |
|---|---|---:|---|---|
| Daylight black present, frame 1365 | `602e00c4eb9fc92f3b124106137d8ca7c56f932e107f995f1f2c7d824dd25de4` | Cue: 19 + 4 + 9 = 23; black: 14 + 5 + 13 = 19 | Both false | 14 (7 red, 7 yellow) → 14 |
| Dim black-absent control, frame 978 | `b368348c63d924f5c7834e8e849b7287225ab525b470bdc3b85f7a8278cbbe70` | Cue: 24 + 0 + 8 = 24; no black proposal was present | False for cue | 15 (cue, 7 red, 7 yellow) → 15 |

On frame 1365, the cue clears the helper’s combined 23-sector minimum but fails its separate minimum of 22 directly supported visible sectors (19 observed). The black has only 14 supported sectors and 19 combined, so it also fails. The nearest already accepted neighbour is a red at about 58 px from the cue fit and 47 px from the black fit; other unsupported sectors remain unexplained. Both model proposals retain their original `rejected-insufficient-boundary-support` decision. This does not recover either missing ball.

On frame 978, the model cue proposal is classified as cue and accepted at 24/32, while the baseline already contains a cue. It adds no inventory entry and the raw replay still returns 15 balls. The black-absent input has no black proposal, so this one frame does not measure false-black rejection when a black hypothesis is actually supplied.

The control frames are from separate capture sessions and lighting conditions, not a matched pair. These two frames do not establish a false-positive rate. The main remaining risks are weak/biased candidate fits, the limited number of accepted neighbouring circles, and circular evidence if cue and black were allowed to explain each other without independent support. Keep all production gates unchanged.

## Reproduction

From `C:\code\SkyEye`, with the mounted camera stopped and the existing Pixel 11 isolated Vision Device package available:

```powershell
dotnet build tests/SkyEye.Vision.Device/SkyEye.Vision.Device.csproj -c Release -m:1 --no-restore
./scripts/Run-PocketLipBoundaryAudit.ps1 -RawProposalReplay -Frames @(1365) -SourceRelativePath 'work/production/evidence/code70-daylight-pocket-capture-20261031/exact-replay-input' -RawProposalDirectory 'work/production/evidence/local-raw-proposal-gates-20261031/joint-occlusion/proposals-positive' -RunPrefix 'jointocclp' -Purpose 'Evaluation-only joint occlusion sector audit for model cue and black proposals against accepted VisionResult neighbours.' -EvidenceRelativePath 'work/production/evidence/local-raw-proposal-gates-20261031/joint-occlusion'
./scripts/Run-PocketLipBoundaryAudit.ps1 -RawProposalReplay -Frames @(978) -SourceRelativePath 'work/production/evidence/code65-source-frame-check-20261031/exact-replay-input' -RawProposalDirectory 'work/production/evidence/local-raw-proposal-gates-20261031/joint-occlusion/proposals-negative' -RunPrefix 'jointoccln' -Purpose 'Evaluation-only joint occlusion sector audit on black-absent pocket control against accepted VisionResult neighbours.' -EvidenceRelativePath 'work/production/evidence/local-raw-proposal-gates-20261031/joint-occlusion'
```

Both wrappers receipt/hash-checked the exact PNG and proposal JSON inputs, confirmed camera `/status` was `Stopped` before and after, and updated only the isolated `uk.co.skyeye.visionchecks` package in place. The focused build succeeded (23 warnings, 0 errors). Final replay run IDs and proof SHA-256 values:

- `jointocclp-204101`: report `dadcb0ed90c6e81045a4862234d3041b128e6b695790b219685cc0af66a64d4f`
- `jointoccln-204115`: report `3c98ed81b23a2adbff48ae5b33dbeec84dfc008450426848d5b4c795490f3aa5`

Raw reports, manifests, logs and proposal copies are under `work/production/evidence/local-raw-proposal-gates-20261031/joint-occlusion/`. Replay instrumentation took 7.1 s (positive) and 9.3 s (negative); full wrapper runs took 10.9 s and 12.7 s. An earlier replay attempt was killed by signal 9 and did not produce a report; the final reruns succeeded. Two earlier runs using HSV pixels for BGR sector inspection are retained in the evidence folder but superseded; only the final BGR runs above support these results.
