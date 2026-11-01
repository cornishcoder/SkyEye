# Code70 raw-proposal replay at the captured profile — 1 November 2026

## Question and provenance

The 31 October raw-model proposal replay used a width-1000 analyzer at extent `.65`, so it did not test the captured primary profile. This follow-up asks whether the saved local-model raw centres pass through the existing candidate path when that path uses the exact captured width-800 `.4` settings. It reuses the six retained daylight Code70 frames; no new capture was made.

Each proposal file's `sourceSha256` was checked against its PNG. The replay report binds each frame ID, source PTS, PNG SHA-256 and parsed receipt SHA-256; the manifest records the source frame, PNG and receipt hashes plus each proposal-file hash and source-image hash. All six samples used the captured analyzer identity (`configurationComparison=exact-match`): width 800, packed-colour search extent `.4`, `SparseBlackRankByRadius=true`, illumination normalization off and pocket radius 48. The separate width-1000 analyzer remained at `.65`, but the raw-proposal measurements used only width 800. The replay refuses a 1000px packed-cluster fallback because its boundary buffer would not match the traced width-800 fit; no fallback occurred in any sample.

Raw model centres are mapped through the receipt calibration into width-800 analysis coordinates, then injected into the existing rectified `VisionAnalyzer` candidate path. The baseline and proposal-assisted passes use the same analyzer. This is not a raw-perspective detector: the production analyzer warps the source before candidate processing. A separate diagnostic colour sample reads each proposed centre from the same width-800 classifier buffer. Model proposals are unreviewed hypotheses, not ground truth.

The successful run is `code70raw800-003354` on the isolated Pixel 11 VisionChecks package. The report SHA-256 is `8342d4dddde493e6467fdd907a291f830be3c0df39817ded2aa749a532c31e90`; manifest SHA-256 is `94a6c0808649ba7a03e843c081b83a4c75ded973a18e38984086f90f4186ee79`; APK SHA-256 is `39ee43d25d85016e47f1ed83c31e75657caf1a9b89e5bb0327bf32f7f94c369a`. The diagnostic changes are committed in `8e2edd4`.

See the [full report](../../work/production/evidence/code70-daylight-pocket-capture-20261031/exact-config-replay-20261101/raw-proposal-exact800/runs/code70raw800-003354/o08-replay-proof-code70raw800-003354-rawproposals.json) and [run manifest](../../work/production/evidence/code70-daylight-pocket-capture-20261031/exact-config-replay-20261101/raw-proposal-exact800/runs/code70raw800-003354/manifest.json). The source frame PTS and PNG hashes are listed in the [pocket-control ledger](live-detect-control-ledger-20261031.md).

## Results

For every frame, the raw-centre classifier output matched the unreviewed local proposal labels (`cue` and `black`). Quality values ranged from `.895` to `.897` for cue and `.830` to `.841` for black; these are classifier quality scores, not probabilities and do not independently validate the proposal labels. Candidate processing still rejected both objects in every frame:

| Frame | Cue boundary support | Black boundary support | Candidate result |
| ---: | ---: | ---: | --- |
| 1365 | 20/32 | 16/32 | Both rejected below support gate |
| 1425 | 18/32 | 13/32 | Both rejected below support gate |
| 1457 | 18/32 | 16/32 | Both rejected below support gate |
| 1555 | 16/32 | 12/32 | Both rejected below support gate |
| 1645 | 19/32 | No boundary fit | Cue rejected below support gate; black has no fit |
| 1736 | 18/32 | 16/32 | Both rejected below support gate |

The existing raw-proposal boundary gate requires `.75`, or 24 of 32 sectors. Across 96 proposals (16 per frame), 84 were rejected as duplicates before fitting, 11 failed boundary support, and one black proposal had no boundary fit. The baseline and proposal-assisted outputs were identical on every frame: 14 circles (seven red and seven yellow), with both `uncertain` and `layoutUncertain` true. No cue or black was admitted.

## Limits and next gate

This is six repeated views of one black-present layout, with draft centres and copied calibration corners that lack independent provenance. It contains no matched black-absent or cue-absent negative, no independent pocket/cushion holdout, and no lighting comparison. It therefore localizes this diagnostic path at candidate duplication and boundary acceptance; it does not score accuracy, centre error, false positives or readiness, and it does not justify relaxing the support gate or adding the model proposals to production.

No detector setting or normal `Analyze` decision path changed. The internal diagnostic classifier helper now accepts a validated width-800 buffer as well as width 1000; the isolated VisionChecks APK was installed in place on Pixel 11. The mounted camera was `Stopped` at revision 94 before and after replay (23% battery, 139 clips both times); no camera capture or Cast/TV action occurred. The user had previously said it was too dark to play when asked about collecting the matched layout; that report is historical and does not establish current lighting.

The next useful capture remains the matched layout in the ledger: same camera, stable reviewable lighting, source configuration and non-target balls; black and cue present with separately reviewable centres; then remove only black, restore it, remove only cue, and restore it. Include hands-clear settled frames and source PTS for each step. Keep separate empty-pocket, other-colour and all-pocket controls. Until reviewed positive/negative holdouts pass the inventory gate, the event and clip gate stays closed.

## Reproduction

Build the isolated replay package with:

```powershell
dotnet build tests/SkyEye.Vision.Device/SkyEye.Vision.Device.csproj -c Release --nologo --verbosity minimal
```

Then run the saved frames through the width-800 profile. Leave `PackedColourBoundarySearchExtent` at its default `.65`; the replay does not use the separate width-1000 analyzer, and changing that value to `.4` enables an unrelated ablation that raw-proposal mode rejects.

```powershell
& '.\scripts\Run-PocketLipBoundaryAudit.ps1' `
  -SourceRelativePath 'work/production/evidence/code70-daylight-pocket-capture-20261031/exact-replay-input' `
  -Frames @(1365,1425,1457,1555,1645,1736) `
  -RawProposalReplay `
  -RawProposalDirectory 'work/production/evidence/code70-daylight-pocket-capture-20261031/exact-config-replay-20261101/raw-proposal-exact800/input-proposals' `
  -RunPrefix 'code70raw800' `
  -Purpose 'Hash-bound Code70 raw-proposal comparison through captured width-800 analyzer profile.' `
  -EvidenceRelativePath 'work/production/evidence/code70-daylight-pocket-capture-20261031/exact-config-replay-20261101/raw-proposal-exact800/runs' `
  -Production800BoundarySearchExtent 0.4
```

The command creates a new timestamped run directory; the report and manifest linked above are the retained successful run.
