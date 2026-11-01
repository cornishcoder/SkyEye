# Frame 1049 Y3 boundary-fit diagnostic and extent experiments — 1 November 2026

This report records a trace of the reviewed lower-right Y3 miss and two predeclared, Development-only width-800 boundary-search extent experiments. The experiments do not change production settings and do not pass D1 inventory/readiness, D2 installed-phone readiness, or E physical events/clips.

## Reviewed source and trace

Frame 1049 is the black-and-cue-present member of the user-reviewed lower-right Development control group. The review overlay and crop were shown before scoring; the user confirmed the Y/R centres and C/B/absence points, then confirmed that the enlarged centres were within an estimated ±3–4 source pixels. The estimate is not a detector tolerance. The source image and receipt remain the immutable inputs in the [Development score record](black-cue-development-score-20261101.md).

The trace-only audit in commit `484f2c5` records eligible colour-body connected components and why each is accepted or rejected; it does not change detection gates. A one-frame full-audit replay used the same width-800 `.65` profile and source frame. It showed:

- The `packed-yellow` proposal at analysis seed `(386,61)`, radius `21.4243`, was boundary-fitted to `(384.76,48.56)`, radius `21.8`, with support `.625` and yellow classification.
- The independently reviewed Y3 centre maps to `(385.2299,58.6160)` in the analysis image. The packed-colour fit is about 10 analysis pixels from the reviewed point and scores `.012584` table-2x1 units away, exceeding the frozen `.010` Development matching tolerance.
- `sparse-colour-add` added the same fit. A nearby generic `colour-body` component centred at `(386.449,58.810)` was rejected as `rejected-separation`; the nearby Hough proposal was rejected as `rejected-duplicate-before-fit`. Neither became a competing accepted centre.
- Adding the trace increased audit candidate rows from 66 to 80, while the 800px and 1000px detections, counts, and uncertainty fields exactly matched the pre-instrumentation replay.

The diagnostic therefore points to over-shift in the packed-colour boundary fit for this sample. It does not by itself establish general benefit or safety for an extent change.

## Predeclared Development experiments

Both plans were written before their runs. They reuse the exact four reviewed lower-right frames, receipts, pixels, calibration, source, device path, batch shape, screening mode, `.010` tolerance and frozen Development split. Only `VisionSettings.PackedColourBoundarySearchExtentFraction` at width 800 changed; width 1000 remained `.65`. APK SHA-256 for the replay runs was `4e6de239d97ca98306ac1f2af2cc7b7e7d6589c194d6d4808775f0a4fa828d77`.

| Frame | Extent `.65` baseline | Candidate `.40` | Candidate `.50` |
| --- | --- | --- | --- |
| 1049 | 15/16; one extra and Y3 miss (`.012584`); uncertainty false | 16/16, no extra/miss; Y3 `.008181596`; uncertainty changed to true | 16/16, no extra/miss; Y3 `.006932136`; uncertainty stays false |
| 461 | 15/15; uncertainty true | 15/15; uncertainty true; p95 `.007270458` | 15/15; uncertainty true; p95 `.006529293` |
| 477 | 15/15; uncertainty true | 15/15; uncertainty true; p95 `.006665629` | 15/15; uncertainty true; p95 `.006665629` |
| 375 | 16/16; uncertainty false | 16/16; uncertainty changed to true; p95 `.009963852` | 16/16; uncertainty stays false; p95 `.009963852` |

All four `.50` frame inventories and matched colours agree with their reviewed labels, with zero extras and misses. Per-frame matched-centre p95 values at `.50` are `.008327824` (1049), `.006529293` (461), `.006665629` (477), and `.009963852` (375). The `.50` result reduces Y3's error from `.012584` to `.006932136`, but frames 1049 and 375 remain above the frozen proposed `.00675` centre-error p95 ceiling. Ball radii are not independently reviewed, and these image-centre estimates do not establish physical cloth-plane accuracy.

### Why `.40` was rejected

The frozen selection rule rejected `.40`: in frame 1049, side-pocket red support fell from 8/8 to 7/8 facing sectors, leaving 319 unexplained pocket pixels and triggering review; frame 375 rose from 60 to 65 unexplained pixels, crossing its review threshold of 64. The candidate also changed broad uncertainty from false to true in frames 1049 and 375. This is a pocket-safety/uncertainty regression, despite improving the Y3 match.

### `.50` selection and limit

The predeclared **Development selection rule** retains `.50` for additional Development study: it puts frame 1049 Y3 within `.010` with no extra prediction, loses no previously matched visible ball, and preserves the reviewed inventory, colour, uncertainty and no-extra/no-miss outcomes in frames 461, 477 and 375. This selection rule is not the acceptance contract, a Holdout score, or permission to change production settings. The four images are one correlated setup group, and only four of six frozen corpus labels are scored by this candidate export; the two lower-left samples remain unscored. The corpus still has zero Holdout and `accuracyGateReady=false`.

The frame 1049 p95 `.008327824`, frame 375 p95 `.009963852`, and offline analyzer-call measurements are descriptive. The `.50` offline analyzer times were 959.4494, 981.8596, 995.5066, and 824.2768ms for frames 1049, 461, 477, and 375. They are single saved-frame analysis measurements, not live end-to-end completion, source age, camera freshness, resource gates or latency acceptance.

## Provenance and artifacts

- Source checkout and trace APK: commit `484f2c5`; APK SHA-256 `4e6de239d97ca98306ac1f2af2cc7b7e7d6589c194d6d4808775f0a4fa828d77`.
- Trace report `work/o08/live-detect-black-pair-v1/runs/lr1049-cbtrace-113918/o08-replay-proof-lr1049-cbtrace-113918.json`, SHA-256 `E80C70902942CF597E89F43F75BFE00F5B1E0A42B80837E66FB216B3D4D70665`.
- `.40` predeclared plan SHA-256 `E1D4484DB2429E978800BF339008AB620BAF5134F00AC9143DABB2686A307510`; score SHA-256 `5DA582EAC52A320810F5C60F06C3CCD333FFFAB07B2FEA96834D93EF246D05CB`; detections SHA-256 `924B1AF91465E5A8ABBB08810B47B232C702F700F88D509746E986F5BDE10BDB`; merged export manifest SHA-256 `220593A35D490768D4CF22B2D59594C5D35778D011452D928ABFFD4F717BD7C0`.
- `.50` predeclared plan SHA-256 `418115DCECD090824BD952781AB224E297B72FB2288B5818614C1EB952F02C05`; score SHA-256 `508620E0E3DB7135F1418CC636F14FEF54DFD40EDC68AD54FB49AB23C1A6D950`; detections SHA-256 `B50054DA0A0ECC49C37442A1B8013331D7FFF376205484C3F9D42F8EB1E6140D`; merged export manifest SHA-256 `9135A64D0F85905F4D772D8E136E9D997A7FB0454569B4F791EA93B6EE0F5960`.
- `.50` per-frame report SHA-256 values: frame 1049 `90806088BE5DF7AA393B70D8EEBE91E94056800D1A4798DDA874430CC66CD261`; 461 `805F7817D73C8E45233E7895379722AA4C31624244424D89828976DA74A5E04A`; 477 `827CB35BD1695C48730ED8B863B011BDE336A1F7E68BA19BB46FB751418BD777`; 375 `56C7A079C28783189BB5C5B7F84564E268C792380B693B849D7DFD620AB49DB3`.
- The candidate overlays use the receipt-bound inverse calibration and reproduce the existing baseline point mapping with a maximum of 0 source pixels discrepancy. [Four-frame candidate contact sheet](../../work/o08/live-detect-black-pair-v1/experiments/packed-colour-boundary-extent-050/visuals/source-overlay-contact-ext050.png), SHA-256 `2312A53186BA12634CF116D65A7A0A0E3034627FE4A4F7C3F3D0D039ED6726F4`; [enlarged Y3 crop](../../work/o08/live-detect-black-pair-v1/experiments/packed-colour-boundary-extent-050/visuals/source-overlay-y3-ext050.png), SHA-256 `2554FF976663B5D541F31F1DE57211847B7F70048A61FB6D456896DA7E433E49`. Green rings are reviewed centres, orange dashed rings are expected absent locations, and magenta crosses are candidate outputs. The user reviewed the displayed `.50` overlays and replied “Looks correct.”

## Device state and next work

Each replay's recorded before/after camera status remained `Stopped`, revision 28, with zero viewers and 18,800 completed frames; no capture was started. A fresh read-only `/status` GET at `2026-11-01T11:52:11Z` again reported `Stopped`, revision 28, timeline `94748b6e18d648f18b3709842afba4cb`, 18,800 completed frames, 139 clips, zero viewers, no host and armed. Its token-free selected fields are saved at `work/o08/live-detect-black-pair-v1/device-state/pixel10-status-after-ext050-candidate-20261101.json`, SHA-256 `6D7B9BBC0D8538F5F026F2CC66385F0D36A9473986C733A44E8C2DBCD91B1C9B`. This read does not verify the companion's current Home screen. No production companion or camera package was updated, and no clips, Cast/TV or match state were touched.

Next use `.50` only as a frozen Development candidate on additional distinct reviewed layout/session groups, including matched pocket/cushion and rail/chalk negatives. Refine the lower-left labels before scoring them for geometry. Freeze session-group splits and a separate Holdout protocol before opening Holdout results. Do not tune this candidate from Holdout feedback; a changed candidate needs new independent Holdout groups. D1, D2 and E remain open.
