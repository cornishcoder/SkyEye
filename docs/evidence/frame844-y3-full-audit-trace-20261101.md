# Frame 844 Y3 extent full-audit trace — 1 November 2026

This paired, exact-source full-audit replay explains the v4 Development frame-844 `.50` Y3 score regression. It is a one-frame diagnostic, not a new score, candidate promotion, acceptance result, or production configuration change.

## Bound source and execution

Both replays used source frame 844 from `work/o08/live-detect-black-pair-v1/physical-controls/replay-inputs/upper-right-development-v4`: PNG SHA-256 `5cfa1e1902e97169ca4cc7f2df769948f3e32af7252495fb69f549cc3f21df8a`, receipt SHA-256 `0f24966b6f03d1c64717f0410ee5c1de6535fe7ae60f30f376bbe8a39bb35661`. The source label `Y3` is `(0.0733038875, 0.5172111067)` in table-2x1 units, from the user-reviewed frozen v4 labels.

The source checkout was `24f0de114071938fc8a6ebd668251fc4ac7c8bda` (documentation-only changes after detector code `484f2c5`). Both serial runs used Pixel 11 Pro XL `66181FDDV001HS`, isolated VisionChecks APK SHA-256 `4e6de239d97ca98306ac1f2af2cc7b7e7d6589c194d6d4808775f0a4fa828d77`, and `full-audit` mode. Each run manifest records camera `/status` as `Stopped` before and after. The candidate changed only the width-800 `Production800BoundarySearchExtent` from `.65` to `.50`; `PackedColourBoundarySearchExtent` remained `.65` (the width-1000 setting), as did the other explicit receipt settings.

| Arm | Run | Width-800 extent | Candidate audits | Manifest SHA-256 | Replay report SHA-256 |
| --- | --- | ---: | ---: | --- | --- |
| Current | `v4y3trace065-123748` | `.65` | 102 | `ebd84905b772d241f3f0b60abfd646997f23b9d932678e71d60be14114989b73` | `d34b744a7b128477505f33e644b906fa0f6b253a3c1556dea33e6f6dfdf8fe31` |
| Candidate | `v4y3trace050-123802` | `.50` | 102 | `1b7dfb60a9857a127ad1c6cfe8498890ca427beb4c25d44ae8f5a7daa1d8222c` | `bf8871cdc6a624460ef1df8a2ba3c3a168122d0425b4d290dfd1d8aa6174a482` |

Artifacts are under `work/o08/live-detect-black-pair-v1/physical-controls/device-state/replays/v4-y3-trace/`. Both source PNG and receipt hashes in the manifests match byte-for-byte. These full-audit outputs are diagnostic reports; they were not relabelled or exported as screening scores.

## Exact fit responsible for the score change

The reviewed Y3 centre maps to rectified analysis coordinates near `(386, 60)`. In each report the `initial / packed-yellow / yellow / index 3` candidate starts at proposal `(386, 60)` with proposal radius `21.01` and is classified as yellow. The accepted fits are:

| Arm | Fitted analysis centre | Fitted radius | Boundary support | Decision | Final `yellow-1` table centre | Distance to reviewed Y3 |
| --- | ---: | ---: | ---: | --- | ---: | ---: |
| `.65` | `(387.16, 62.76)` | `22.6` | `.65625` (21/32 sectors) | `accepted-colour-peak` | `(0.07845, 0.51605)` | `.005275` |
| `.50` | `(384.00, 50.80)` | `21.8` | `.65625` (21/32 sectors) | `accepted-colour-peak` | `(0.06350, 0.52000)` | `.010193` |

The same accepted yellow detection moves by about 11.6 analysis pixels. The stored table mapping is `tableX = fittedY / 800`, `tableY = 1 - fittedX / 800`. At the frozen `.010` Workbench tolerance, `.65` matches Y3. At `.50`, that same detection is just outside tolerance, so the scorer counts it as both the Y3 miss and the unmatched yellow extra. It is not a new second detector output.

The trace therefore confirms the packed-yellow boundary fit as the path affected by the width-800 extent change. Source calls `FindColourPeakBodies` from [`PackedBallClusterRecovery.cs`](../../src/SkyEye.Vision.Android/PackedBallClusterRecovery.cs#L41) and passes `settings.PackedColourBoundarySearchExtentFraction` to `BallBoundaryRefiner.Refine` at [line 435](../../src/SkyEye.Vision.Android/PackedBallClusterRecovery.cs#L435). Both fits retained the same recorded support; the `.50` radius constraint admits a different accepted centre. This single labelled point does not identify a safe replacement value or establish that a fitted image centre equals the physical cloth-plane centre.

The candidate remains Development-only and unpromoted. Across the full v4 Development comparison it regresses frame 844 while improving frame 1049 and upper-right frame 697; both aggregate p95 centre errors remain above `.00675`, and the corpus has zero Holdout. The fit trace adds causal localization, not accuracy or release coverage. D1, D2 and E remain open.

## Reproduction

The two commands were run serially with the same explicit settings and source directory; only `-Production800BoundarySearchExtent` differed:

```powershell
./scripts/Run-PocketLipBoundaryAudit.ps1 -SourceRelativePath 'work/o08/live-detect-black-pair-v1/physical-controls/replay-inputs/upper-right-development-v4' -Frames 844 -RunPrefix v4y3trace065 -Purpose 'Full-audit trace of the frozen v4 frame-844 Y3 output at current-source width-800 extent 0.65; paired exact-source comparison with declared 0.50 experiment.' -EvidenceRelativePath 'work/o08/live-detect-black-pair-v1/physical-controls/device-state/replays/v4-y3-trace' -EdgeMargin 23 -PocketRadius 48 -BoundarySupport 0.75 -SparseColourSupport 0.625 -Production800BoundarySearchExtent 0.65 -PackedColourBoundarySearchExtent 0.65 -SparseBlackSearchExtent 1.3 -SparseBlackSupport 0.65625 -SparseBlackCandidateLimit 12 -SparseBlackRankByRadius $true -SparseBlackMinimumPeakRadiusFactor 8

./scripts/Run-PocketLipBoundaryAudit.ps1 -SourceRelativePath 'work/o08/live-detect-black-pair-v1/physical-controls/replay-inputs/upper-right-development-v4' -Frames 844 -RunPrefix v4y3trace050 -Purpose 'Full-audit trace of the frozen v4 frame-844 Y3 output at declared width-800 extent 0.50; paired exact-source comparison with current-source 0.65.' -EvidenceRelativePath 'work/o08/live-detect-black-pair-v1/physical-controls/device-state/replays/v4-y3-trace' -EdgeMargin 23 -PocketRadius 48 -BoundarySupport 0.75 -SparseColourSupport 0.625 -Production800BoundarySearchExtent 0.50 -PackedColourBoundarySearchExtent 0.65 -SparseBlackSearchExtent 1.3 -SparseBlackSupport 0.65625 -SparseBlackCandidateLimit 12 -SparseBlackRankByRadius $true -SparseBlackMinimumPeakRadiusFactor 8
```
