# SkyEye detection diagnosis

Independent review, 10 November 2026, repository HEAD `f3431ec7`. Diagnostic
only: no production detection code was changed. Evidence register:
[skyeye-detection-evidence.md](skyeye-detection-evidence.md). Handover notes:
[skyeye-detection-investigation-notes.md](skyeye-detection-investigation-notes.md).
A concurrent review in [`docs/argon/`](../argon/engineering-diagnosis.md) reaches
compatible conclusions; differences are noted where they matter.

## Summary

SkyEye does not mainly fail because of lighting. On every labelled frame in
mid or bright light, reds and yellows are found 266/266 with correct colours
and no false positives. Three things cause most of the unreliability:

1. **A frame-level, all-or-nothing state model turns small per-ball noise
   into large table-level failure.** Only 4.5% of balls flicker in still
   bursts, yet 16 of 42 still bursts never settle, and correct readings of
   any table without exactly one black and one cue are flagged uncertain.
2. **The black ball, and the cue ball next to it, are lost at cushions and
   pocket jaws** because of a cascade of absolute thresholds and hard caps.
   This happens in daylight as well as dim light.
3. **Lighting is a real but secondary problem.** The camera never locks
   exposure or white balance, colour correction depends on first finding the
   cue ball, and the cloth model is hue-based. Below about V 75 and under warm
   light (cloth saturation under about 20) jaw balls drop out; in near
   darkness every label collapses to unknown.

A process problem sits underneath all three: the detector only builds for
Android, so no change can be evaluated without a phone, and there was no
reusable labelled corpus. That is why physical staging kept being repeated.

## A. Evidence coverage

- **5,248 media files** (5.7 GB): 5,019 images, 136 videos and 93 raw-pixel
  files, of which 2,922 are unique. 899 are unique camera frames and 127 are
  unique videos (about 37 minutes).
- **1,032 frame receipts** resolve to **492 unique receipt-bound frames**
  from 83 capture operations over 10 days. 202 of them carry the production
  result computed at capture time.
- **26 frames have human-reviewed labels** (401 visible balls). All are from
  1-2 November in mid or bright light.
- **Distinct scenarios:** seven scene families (evidence section 4). About 45
  of the 101 capture operations are the same full rack restaged; 16 are the
  same pocket-lip layout.
- **Inspected:** every tile of the six capture-operation sheets (101 tiles),
  the four detector-failure sheets (46) and the two video sheets (126), all at
  thumbnail scale.
- **Not inspected or inaccessible:** one non-image `.png`, one undecodable
  video, the raw-pixel dumps, and the screenshots and crops individually.
  Clips were not watched end to end. No file anywhere records exposure or
  white-balance settings.

## B. Current pipeline

| Stage | Code | What it does | Assessment |
|---|---|---|---|
| Capture | `SkyEye.Camera/CaptureEngine.java` (request builder) | 1080p30 `TEMPLATE_RECORD`, continuous AF; AE and AWB left automatic | Weakness: exposure and white balance drift with scene content and room light |
| Frame acquisition | `SkyEye.Companion/PlayerTableFrameSource.cs` `TryReadFrameOnce` | Snapshot of the newest decoded frame with exact PTS pairing | Strength: clean provenance; every saved frame is replayable |
| Admission | `SkyEye.Core/Analysis/TableAnalysisSession.cs` `RunUpdateAsync` | One analysis at a time, bound to source and calibration | Sound |
| Rectification | `VisionAnalyzer.cs:259`, `Warp` 634-646 | Four-corner homography to an 800x1600 raster | Sound; pocket holes are cut at the raster border |
| Optional illumination normalisation | `VisionAnalyzer.cs:266-271`, 553-633 | Large-blur gain field | Disabled by default after it caused a false body |
| Cloth mask | `ClothMask` 911-943 | One global hue band (±7) plus neutral S < 45; pocket discs painted as cloth | Weakness: undefined when the cloth is grey (about half the frames) |
| Proposals | `Detect` 647-822: packed-cluster peaks, colour bodies, `AddDarkBodies` (`DarkBodyRecovery.cs` 12-52, V ≤ 40), Hough on the binary mask | Ordered rescue passes, at most 32 candidates | Weakness: absolute thresholds; edge margin 23 px (715-720); pocket exclusion 48 px before and after fitting (722, 769); seed cap of 64 (`PackedBallClusterRecovery.cs:506`); cue recovery abandons with more than 4 candidates (`CueBallRecovery.cs:290`) |
| Boundary fit | `BallBoundaryRefiner.cs`; support gates 0.75 (Hough), 20/32 (sparse colour), 21/32 (sparse black), 18/32 (verification) | 32-sector radial edge test | Weakness: a black or cue sector is rejected when its outside differs from the cloth by more than 40 levels (line 617); cushions, pocket throats and an adjacent cue all do that |
| Colour | `VisionAnalyzer.cs:362-417`; `BallClassifier.Classify` 28-57 | White balance from the cue's 95th-percentile pixels, only if exactly one cue was found; absolute HSV rules | Weakness: circular (detection is needed for colour correction, and correct colour is needed for detection); absolute V and S cut-offs |
| Inventory | `BallClassifier.ConstrainInventory` 18-27, called at `VisionAnalyzer.cs:479` | More than 7 of a colour, or more than 1 cue or black, turns the whole group `unknown` | Defect: one misread ball erases every ball of that colour |
| Frame uncertainty | `VisionAnalyzer.cs:504-533` | Uncertain unless exactly 1 cue and 1 black; also packed-cluster, boundary, pocket-colour, obstruction and overlap flags | Defect: "the scene is not a full rack" is treated as "the detector is unsure" |
| Coordinates | `AnalysisCoordinates.ToTable` | Rectified pixels to table 2x1 | Sound |
| Settled state | `TableTracker.Observe` 14-45, `SameLayout` 77-88 | Whole-frame comparison against the previous frame and an anchor; any count, kind or > 0.015 move resets the 2 s clock; uncertain frames freeze the last layout as occluded | Defect: no per-ball identity or hysteresis |
| Verification | `TableVerification.cs` `VerificationConsensus.Resolve` 126-190; `VerificationStillnessPolicy` 26-44 | 3 or more samples; first and final counts must match; a dropout is tolerated only mid-burst | Closer to per-ball but still fails on any final-frame dropout |
| Live events | `LiveDetectCountCandidateTracker.cs` (3 confirmations, line 50), `StableTableChangeTracker.cs` (3 samples, line 36) | Count changes become pot candidates | Inherits detection flicker |

**Strengths:** frame provenance and receipts are excellent, and are what made
this offline review possible. Red and yellow detection is accurate. Position
error is small (median 0.03 ball diameters). The verification path's
photometric residual already separates exposure change from motion.

**Architectural weaknesses:** (1) state is decided per frame, not per ball;
(2) recall at the table edges depends on a chain of hand-tuned absolute
thresholds, each fitted to a few frames (see the comments in
`VisionSettings.cs`); (3) colour constancy depends on the cue ball; (4) the
detector cannot run off-device, so there is no regression harness.

## C. Failure analysis

V = verified by measurement or code reading in this review; H = hypothesis.
Tile IDs (D…) refer to `work/evidence-audit/sheets/detector-fail-index.csv`.

| # | Observed failure | Evidence | Code path | Cause | Supporting / contradicting evidence | Status |
|---|---|---|---|---|---|---|
| 1 | Still table never settles; layout flips between frames | 16/42 still bursts unstable; 31/126 pairs fail `SameLayout` (`burst-stability.csv`) | `TableTracker.cs:33-41, 77-88` | One ball dropping out in one frame resets the whole layout | Brightness constant within bursts (0-1 level). Per ball only 4.5% intermittent, no colour flips. Per-ball replay: 38/42 stable | V |
| 2 | Correct readings reported uncertain | 9 of 16 fully correct labelled frames flagged U (black or cue deliberately absent) | `VisionAnalyzer.cs:507-508` | Exactly one cue and one black is hard-coded as a validity rule | Scores in `score-production-at-capture.json` | V |
| 3 | Black missed at a cushion or pocket jaw, in all light | Labelled 12/15, all 3 misses at the top cushion (1 Nov 12:48, 15:49, 17:13); D2-D9, D11-D18, D21, D26, D31-D34, D37, D38, D41-D43 | `DarkBodyRecovery.cs:18-31`; `BallBoundaryRefiner.cs:617`; `PackedBallClusterRecovery.cs:506`; `VisionSettings` 21/32 | The black merges with the dark cushion shadow or pocket throat; its outward sectors fail the exterior test; support stops at 17-20/32 | Earlier trace: seeds 18-19/32 against 21 (scorecard). Argon traced frames 1365 and 1691 to the same gates. Contradiction: none found | V |
| 4 | Cue missed in a corner jaw next to the black | D18, D31-D34, D38, D41-D43 | `CueBallRecovery.cs:290` (more than 4 candidates abandons); image-edge rejection; refiner exterior test | A bright cue against glare or the black fails the same edge tests | Interior cues: 14/14 found | V (code) / H (exact gate per frame) |
| 5 | Whole colour group becomes unknown | D20: 7 red found, 8 unknown | `BallClassifier.cs:18-27` | Cue misread as yellow gives 8 yellows; every yellow is demoted | Argon F1: same on Layout03 frames 957 and 2263 | V |
| 6 | Jaw pairs lose one ball in dim warm light | D3, D5-D9, D13 (V 68-76, saturation 6-17) | `ClothMask` 911-943; absolute S and V gates | Grey cloth leaves the hue band empty; contrast falls below fixed cut-offs | Later versions read 15/16 on the same layout (D12, D14, D15), so thresholds moved the problem rather than removing it | H (plausible; no labelled dim frames) |
| 7 | Near darkness: everything unknown | D1 (V 28) | `BallClassifier.cs:40-55` | Absolute V < 40 "dark" and S > 100-160 colour rules | Single operation | V (code) / H (generality) |
| 8 | Exposure steps look like motion | 5 clips with 14-20% brightness steps; raw change 33-60%, 3-5% after per-tile gain compensation | `CaptureEngine.java` (no AE/AWB lock); live motion feed `TableMotionFeed.cs` | Auto-exposure | Verifier's photometric residual already compensates. Whether the live-detect motion path does was not traced | V (signal) / H (impact on live events) |
| 9 | False pot / false shot events | `code62-false-pot-clip-003.mp4`, `code62-false-shot-clip-004.mp4`: no pixel change at all | `LiveDetectCountCandidateTracker.cs:50, 101`; `StableTableChangeTracker.cs:36` | Not image change. Most likely detection flicker of a jaw ball reaching 3 confirmations | Clip content measured; the matching JSONL log was not traced | H (lighting excluded: V) |
| 10 | Cushion-ball position bias | Argon F6: 26.8 px outward on one cushion ball | `Warp` 634-646 | Ball height under an oblique view | This review: labelled centre error p95 0.15 ball diameters overall, so small in aggregate | H here (not reproduced) |
| 11 | Verifier `SampleGap` and timeouts | Workflow note; Argon F7 | `VisionAnalyzer.cs:205-244` (full 1000 px re-run); capture inside the loop | Latency: median 273 ms, maximum 694 ms per frame from receipts | Argon measured 800-967 ms sample gaps | V (latency) / H (exact cause) |

Not supported as a primary cause by the evidence: global brightness change
within a session (bursts are photometrically flat), ball-on-cloth colour
confusion for reds and yellows (none observed), and calibration error (centre
error is small). Occlusion by hands and cue is detected and flagged as
designed (tiles #15 and #58).

## D. Ranked solutions

### 1. Per-ball tracking with hysteresis, and per-ball rather than per-frame uncertainty

Replace the whole-frame `SameLayout` comparison with ball tracks: confirm a
ball after k of the last n frames, drop it after missing k of n, keep its
identity by position and colour, and judge stillness from the confirmed
tracks. Report uncertainty per ball. Stop treating "no black" or "no cue" as
uncertainty. Make `ConstrainInventory` demote only the least confident
surplus ball.

- **Measured benefit on existing data:** replaying the 42 still bursts'
  stored detections (`tools/evidence/prototypes/ball_track_replay.py`), the
  current rule settles 26/42 with 31 false layout changes; 2-of-3 hysteresis
  gives 38/42 with 5. On labelled frames, clear reports could rise from 7/19
  to as many as 16/19 (other flags, such as the pocket-colour warning, may still apply).
- **Risks:** a real pot is confirmed about one frame later; a ball that is
  never detected stays missing (this does not help failure 3); identity swaps
  are possible when balls touch. The game-state engine's input contract must
  not change; keep the published `Ball[]` shape.
- **Dependencies:** none on the camera; pure `SkyEye.Core` logic, testable in
  the existing `SkyEye.Checks` project on desktop .NET.
- **Validation:** the replay script now; then the same bursts through the C#
  implementation in `SkyEye.Checks`, plus the real-play clips once their
  intervals are labelled (evidence section 9).

### 2. Geometry-aware edge model for balls at cushions and pocket jaws

Use the known table geometry. Where the circle of a candidate black or cue
overlaps the cushion band, pocket throat or a neighbouring detected ball,
treat those sectors as occluded rather than failed, and require support only
on the cloth-facing arc (Argon proposes the same, with a floor such as 14/32
cloth-facing sectors). Remove the hard caps (64 seeds, more than 4 cue
candidates) in favour of score-ranked top-K. Once tracking (solution 1)
exists, search a small region around a confirmed black or cue's last
position with that model before declaring it gone.

- **Expected benefit:** recovers failures 3 and 4, the largest remaining
  recall loss (black 12/15 labelled, and about 25 unlabelled operations).
- **Risks:** false blacks in pocket throats and cushion shadows. The negative
  prototype in this review shows how easily edge regions fire; empty-jaw
  negatives must stay at zero.
- **Dependencies:** an off-device harness (plan step 0) and labels for the
  unlabelled black-at-cushion frames (review of existing images only).
- **Validation:** labelled frames `815fbaeef922`, `fc1ad7873fa6` and
  `d59148155e7c` (positives) plus the empty-mouth controls already named in
  the scorecard and by Argon (542, 562, 461, 206, 978), scored with
  `score_detections.py`.

### 3. Lock exposure and white balance, and anchor colour to the cloth instead of the cue

After a short convergence at session start, set `CONTROL_AE_LOCK` and
`CONTROL_AWB_LOCK`, and report the locked values in `/status` and each
receipt. In the analyser, estimate the cloth's colour every frame (robust
median over the playing area, which balls cover by only a few per cent) and
classify ball colour and darkness relative to it, replacing the cue-derived
gains and the absolute V and S cut-offs.

- **Expected benefit:** removes exposure steps (failure 8), the circular
  cue-colour dependency, and most of failures 6 and 7.
- **Risks:** a locked exposure can be wrong after a large room-light change,
  so it needs a relock trigger (for example a sustained global brightness
  shift). Changes the camera package, which `AGENTS.md` says must not be
  updated without explicit approval.
- **Dependencies:** analyser change testable offline; the lock needs one
  bounded live check afterwards (the only physical step justified, recorded
  in the evidence register).
- **Validation:** needs reviewed labels for dim and warm frames that already
  exist (pocket-lip operations #49-#64, rack #18-#23 and #39-#42). None exist
  yet, so this is the one ranking that cannot yet be measured.

Not recommended now: a learned detector. The earlier spike failed for lack of
data, not for a design reason, and red and yellow are already solved
classically. Revisit only after 200+ labelled frames exist, which the
existing evidence can supply with review effort, not staging.

## E. Implementation plan

Each step is small and has a pass/fail measure on existing evidence.

0. **Off-device harness.** Make the detection core runnable on desktop
   (OpenCvSharp or a thin abstraction over the OpenCV calls in
   `SkyEye.Vision.Android`), and add a `SkyEye.Checks` command that runs it on
   the frames listed in `work/evidence-audit/labels.jsonl` and writes the
   JSONL format `score_detections.py` accepts. **Pass:** HEAD reproduces the
   receipts' stored results on the 19 frames that have one (from the same
   algorithm version), within 1 px. Until this exists, every later step needs
   the phone.
1. **Label review of existing frames.** Prepare sheets for about 40 already
   saved frames (dim, warm, jaw and open play), pre-filled with draft centres,
   for one review sitting. No staging. **Pass:** labelled frames cover V < 80
   and saturation < 20.
2. **`ConstrainInventory` surplus-only demotion** and **no uncertainty for an
   absent black or cue**. **Pass:** clear-and-correct labelled frames go from
   7/19 to 16/19; D20-type frames keep 7 correct yellows; no new false
   positives.
3. **Per-ball tracker** in `SkyEye.Core`, behind a setting, fed by the same
   `VisionResult`. **Pass:** still-burst replay at least 38/42 with at most 5
   false changes; existing GameState and verification checks unchanged.
4. **Geometry-aware edge support and caps removed.** **Pass:** black recall
   15/15 on the current labels, zero detections on empty-jaw controls, and no
   regression on reds and yellows.
5. **Cloth-relative colour and darkness**, then the **AE/AWB lock** in the
   camera package with explicit approval. **Pass:** step 1's dim and warm
   labels reach the same recall as the bright set; one bounded live check of
   lock stability.
6. **Label the 29 October real-play clips** (intervals only) and measure
   false and missed events end to end.

## F. Remaining unknowns

| Unknown | Resolvable from existing evidence? |
|---|---|
| Detector accuracy in dim and warm light | Yes: label existing frames (step 1) |
| What triggered the two false-event clips | Probably: trace the matching `live-detect.jsonl` around those clip times |
| Whether HEAD behaves like the versions stored in receipts | Yes, once step 0 exists |
| Tracking and event accuracy during real play | Yes: the 29 October clips, once intervals are labelled |
| Size of the cushion parallax bias | Yes: reviewed labels at cushions already exist; compare per zone |
| Stability of an AE/AWB lock over a full match | No: needs one bounded live session after the code change |
| Cause of verifier `SampleGap` beyond the 1000 px re-run | Partly: receipts carry timings; device profiling may be needed |

No new ball arrangement or lighting session is needed to make progress on any
of the first five plan steps.
