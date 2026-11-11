# SkyEye detection: working plan

Last updated 10 November 2026, 23:50. Owner: whoever picks this up next. This
is the live plan for the detection work; update the thread table and the log
at the bottom whenever a thread moves.

Inputs: this review's [diagnosis](skyeye-detection-diagnosis.md) and
[evidence register](skyeye-detection-evidence.md), the
[Argon review](../argon/engineering-diagnosis.md), two external comparisons
of those reviews (ChatGPT and Luna, 10 November), and a re-check of the code
and the project's own ledger made while writing this plan. Where the re-check
changed an earlier conclusion, section 2 says so.

Nothing here is implemented. All detector scores quoted are from results
stored at capture time, not a re-run of HEAD.

## 1. Where the project actually is

From `docs/live-detect-plan.md` and the targeted-test ledger:

- Project metrics: software suites 3/3, detector inventory cases 0/3,
  physical gates 0/3.
- The blocking case is a **persistent** miss, not flicker. In the Code70 run
  the detector reported 14 of 16 balls in 147 of 151 checks at a pocket
  cluster, never formed a clear baseline, and so emitted no events or clips.
- Single-frame fixes for the black at a cushion have been tried and have
  failed: full seed enumeration (support 18, 19, 18 of 32 against 21),
  radial-offset probe, a partial-arc probe (accepts an empty point: frame
  461 at 10/32, frame 844 at 20/32), and a learned detector spike (0 of 7
  layouts). A frozen "bright number patch inside a dark ring" feature scored
  8/10 on Development frames.
- The ledger's own conclusion: the next change "must introduce independent
  discriminating evidence or a different bounded candidate model".

That last line sets the direction of this plan.

## 2. Corrections to earlier conclusions

Found by re-reading the code on 10 November. The diagnosis and evidence
register carry a pointer to this section.

| Earlier statement | What the code or record shows | Effect |
|---|---|---|
| "Frames without exactly one black and one cue are flagged uncertain", ranked as a main cause | True only for the broad `Uncertain` flag (`VisionAnalyzer.cs:507-508`). Live Detect and the session tracker use `HasUncertainLayout`, which that rule does not set. The ledger records this as already fixed for Live Detect | Demoted. It affects the Home readout and advice only. "7 of 19 reported clear" overstated the live problem |
| Still-burst replay (26/42 settle, 38/42 with per-ball rule) describes Live Detect | The replay used `TableTracker.SameLayout` (0.015 tolerance), which serves the observation session and verifier. Live Detect uses `StableTableChangeTracker` and `LiveDetectCountCandidateTracker`: three matching clear samples, exact count equality, and a looser position rule in `SettledLayoutReconciler` | The all-or-nothing structure is the same, but the numbers do not transfer. Thread T3 re-measures on the real reducer |
| "Colour correction only works if the cue has been found" | In a verification burst the last valid gains are reused after a cue dropout (`VisionAnalyzer.cs:404-408`). Single-frame analysis has no reuse | Narrowed, not removed |
| "Cloth mask is a fixed hue band" | Hue, saturation floor and value floor are sampled from the cloth each frame (`ClothRange`), with a neutral low-saturation fallback. Only the band width and the 45 saturation ceiling of the fallback are fixed | The warm-cloth failure stays a hypothesis and is weaker than stated |
| "No reviewed labels in dim or warm light" | Warm frame 266 has 16 reviewed centres and scores 16/16 with correct colours (`docs/evidence/live-detect-o09-warm266-reviewed-spatial-score-20261102.md`). It was missed because its labels are stored in a different format | One warm frame exists and it is read perfectly. Argon reports black and cue missed on the same frame; reconcile by hash in T1 |
| Solution 2: require support only on the cloth-facing arc | Already tried as a diagnostic and rejected: it accepts empty rail points. `HasConservativeOcclusionCoverage` already handles neighbour occlusion with a 14/32 floor | Dropped as written. Replaced by T4 |

Checked and still standing: the whole-colour demotion in
`BallClassifier.ConstrainInventory`; the 64-seed and more-than-4-candidate
caps; no exposure or white-balance lock in `CaptureEngine.java`; red and
yellow accuracy; the two "false event" clips being motionless; and the
detector having no desktop build.

What the two external reviews got right and wrong is in section 7.

## 3. Direction

Three attempts to decide "is there a black ball here?" from one frame's edge
evidence have failed for the same reason: a dark ball against a dark cushion
shadow or pocket throat looks like an empty one. More of the same evidence
will not separate them. Two other sources of evidence exist and are unused:

1. **Change between settled states.** The camera is fixed. A ball that comes
   to rest at a cushion changes those pixels relative to the settled frame
   before the shot; an empty jaw does not change. After compensating for
   brightness, any ball-sized change that no detected ball explains is a
   candidate arrival, and any vanished ball with no change at its old
   position has not moved.
2. **Permanence.** A ball cannot leave the table without motion. A ball that
   was confirmed and is not re-detected, with no change at its position and
   no table motion since, is still there.

Both need per-ball state across frames, which is also what stops one
flickering ball from blocking the whole table. So the tracker and the
black-ball problem are one piece of work, not two.

Limits, stated now: neither source helps on the very first frame of a
session if the black already sits on a cushion. Play normally starts from a
rack, where the black is found inside the pack, so this may not matter in
use; staged still-table tests are the hard case. The readout must show such
a ball as "held, not re-observed" with its age, as the required outcome in
`live-detect-plan.md` demands.

## 4. Threads

Status: `open` not started, `active`, `blocked`, `done`, `dropped`.

| ID | Thread | Status | Needs | Next action |
|---|---|---|---|---|
| T0 | Protect the evidence | done (11 Nov 08:54) | - | `D:\SkyEye\backup-report.txt` PASS. Re-run the script after new captures or regenerated outputs |
| T1 | One corpus manifest | done | - | Report in `tests/fixtures/detection-corpus/README.md`. All five cushion black misses are one table position, (1.48, 0.067), dim and bright |
| T2 | Desktop detector runner | done: Layout03 parity PASS 4/4; `detect` scores the manifest | - | JPEG parity before trusting JPEG scores (`t2-desktop-parity.md`) |
| T3 | Replay Live Detect's real reducer on saved logs | done | - | Report `t3-live-detect-replay.md`. Lost still time is flags (pocket-colour 1,149 s, obstruction 941 s), not flicker (1.4 s) |
| T4 | Change-between-settled-states experiment | done: promising, conditional | T9 labels to score properly | Report `t4-settled-change-experiment.md`. Black at cushion/jaw 9/9 staged; fails under moving daylight |
| T5 | Surplus-only inventory demotion | done on desktop: colour 0.970 to 0.996 | On-device check (needs Richard) | `t5-surplus-demotion.md`. Ranked by saturation with a 40 margin, not quality |
| T6 | Per-ball state in the reducer | D1+D2 built, offline checks pass; D3 designed | On-device check of D1+D2 (needs Richard); D3 shadow mode | See `d1-d2-implementation.md` and `d3-settled-change-design.md` |
| T7 | Camera exposure metadata, then lock | open | Richard: explicit approval (camera package) | Log first, lock later |
| T8 | Cap removal, cushion position bias, verifier latency | open | T2 | Measure only; act if shown to matter |
| T9 | Label existing frames and clips | sheets ready | Richard: two sittings of about 40 min | `work/evidence-audit/t9/README.md` |

Order: T0, T1, T3 and T4 can start now and need nothing from anyone except
T0's destination. T2 runs alongside. T5 and T6 follow their inputs. T7 and
T8 are last.

### T0 Protect the evidence

`work/` is git-ignored: 5,248 media files, 5.7 GB, including about 37
minutes of unique video. The laptop disk was 99% full during the audit.
Copy to external or cloud storage. **Done when** every SHA-1 in
`work/evidence-audit/inventory.csv` is present in the copy.

### T1 One corpus manifest

There is no single list of test frames. This review used 26 reviewed
frames; Argon's export has 32 rows over 28 frame IDs (four Layout03 frames
appear before and after a fix with identical results, which inflates its
totals); the project has corpus-v4 (10 stills) and a v3 Development set.

- Create `tests/fixtures/detection-corpus/manifest.json`: one row per image
  hash, with path, calibration corners, label source, light stratum, zone
  tags, role (positive, empty control), and which corpus it came from.
- Include frame 266 and convert its labels. Resolve the 266 conflict by
  comparing image hash, receipt and analyser version between Argon's claim
  and the 16/16 score.
- Include the named empty controls (461, 844, 542, 562, 206, 978) and the
  black positives (375, 678, 1365, 1691, 1726, 1764, 2831).
- Respect the project's split: never open Holdout; mark Development frames
  as tuning data.
- **Done when** no hash appears twice and the scoring script runs from the
  manifest alone.

### T2 Desktop detector runner

Measured size of the job: the detector is about 3,100 lines in 9 files
(`VisionAnalyzer.cs` is 1,395). It uses about 25 OpenCV functions (colour
conversion, connected components, morphology, Hough circles, warp, distance
transform, blur, Canny, corner detection). `BallBoundaryRefiner` and the
classifier are already in `SkyEye.Core`. Android-only pieces are bitmap
conversion (3 call sites) and the `Org.Opencv` namespaces.

- Approach: multi-target the detector, with a thin shim so the same source
  compiles against the Android binding and OpenCvSharp. Do not rewrite or
  port the algorithm.
- Step 1 (spike, half a day): compile and run one labelled frame on desktop.
- Step 2: `SkyEye.Checks` command that runs the T1 manifest and writes the
  JSONL `tools/evidence/score_detections.py` reads.
- **Done when** desktop output equals the device output on the frames with a
  current-source device replay (the Layout03 set: 59/62 found, 0 false,
  black missed 3 of 3), same balls and kinds, centres within 1 px.
- **Risk:** numeric differences between OpenCV builds, most likely in Hough.
  If parity fails, keep the device as the reference and use desktop only for
  before/after comparisons, labelled as such.
- First check whether `SkyEye.Observation.Workbench` already provides part
  of this.

### T3 Replay the real Live Detect reducer

Question: in saved Live Detect runs, how often did a still table fail to
form or hold a baseline, and was the cause a persistent miss, flicker, or
the uncertainty flags?

- Inputs: saved `live-detect.jsonl` logs (Code37 to Code70 runs), which hold
  per-frame counts, quality and positions. The plan notes saved logs already
  replay through `LiveDetectTableEventTracker`.
- Measure per run: share of samples not usable and why; number of baseline
  resets; time to first baseline; candidate events with no motion.
- Include the two motionless "false pot" and "false shot" clips: find what
  the reducer saw at those times.
- **Done when** a table splits lost time between persistent miss, flicker
  and flags. This decides how much T6 is worth and replaces the 26/42 and
  38/42 figures.

### T4 Change-between-settled-states experiment

Hypothesis: brightness-compensated difference between two settled frames of
the same timeline separates "ball arrived here" from "empty cushion or
jaw", where single-frame edge support cannot.

- Data: the 29 October real-play clips (continuous video, about 35 clips)
  and bursts within one timeline. Take a settled frame before and after each
  shot, rectify with the receipt corners, compensate gain per tile (as
  `tools/evidence/video_change_signals.py` already does), and extract
  ball-sized change regions.
- Score by eye on sheets first, then against labels from T9: does each ball
  that moved produce a departure and an arrival region; do cushion and jaw
  arrivals show; how many change regions appear where nothing moved
  (shadows of players, cue left on the rail, exposure steps).
- **Kill criterion:** if arrivals at cushions are not clearly separable from
  no-change controls on these clips, or player shadows produce ball-sized
  regions that cannot be rejected, drop this and record it. Then the
  remaining options are the frozen number-patch feature and permanence
  alone.
- Cost: Python only, no device, no staging.

### T5 Surplus-only demotion

`BallClassifier.ConstrainInventory` turns every ball of a colour into
`unknown` when it sees more than seven. Demote only the surplus, least
confident first. Verified defect, small change, but it changes production
output, so score it through T2 on the manifest. **Done when** the Layout03
frames keep their yellows (colour-correct now 43 of 59) with no new false
positives.

### T6 Per-ball state in the reducer

Design after T3 and T4 report. Expected shape:

- Each confirmed ball has a state: observed, held (not re-detected, no
  change at its position, no motion since), or gone (motion seen and absent
  from repeated clear frames).
- Baseline and events are judged on ball states, not on whole-frame count
  equality.
- An unexplained arrival region from T4 can re-anchor a held or missing
  black or cue; a count decrease without a departure is not a pot candidate.
- Lives in the image-free reducer behind the existing
  observation-in, events-out seam. `GameState` and the `Ball[]` contract do
  not change. Behind a setting, off by default.
- **Done when**, on replayed logs and the labelled clips: the Code70 pocket
  cluster forms a baseline; no real pot or shot in the labelled clips is
  missed or delayed by more than one sample; no held ball is reported as
  freshly observed.
- **Risk:** permanence can hide a ball removed by hand under obstruction.
  Obstruction must clear held states for the covered area.

### T7 Camera exposure

No receipt records exposure, so nobody can say whether brightness steps come
from the camera or the room.

1. Add exposure time, sensitivity and white-balance state to `/status` and
   receipts. Harmless, and it settles the question on the next ordinary
   session.
2. Only if the logs show the camera stepping: lock after convergence, with a
   deliberate re-lock when the room light changes.

Both steps change the camera package and need explicit approval under
`AGENTS.md`. Low priority: within still bursts brightness was flat, and the
one reviewed warm frame is read correctly.

### T8 Measure before acting

- Replace the 64-seed and more-than-4-candidate caps with ranked limits
  only if a trace on a manifest frame shows a cap dropping a true ball.
  The ledger shows full enumeration did not recover the black, so expect
  little.
- Cushion position bias: compare centre error by zone on reviewed labels.
  The claimed 26.8 px comes from one frame, and the cited warp audit does
  not measure centre error.
- Verifier sample gaps: profile the loop. Pixel writes are already queued,
  so that is not the cause; the per-frame bitmap copy and the 1000 px
  fallback are the candidates.

### T9 Labels from existing material

- Sheet 1: about 40 saved frames (dim and warm pocket-lip operations,
  racks, cushion blacks, empty controls) with draft centres to confirm.
- Sheet 2: the 29 October clips: mark shot, rolling and settled intervals,
  pots, and where each moved ball came to rest.
- Drafts are never scored as truth. No staging.

## 5. Rules

1. One measurable change per commit, scored on the T1 manifest, reporting
   regressions with improvements.
2. Follow `AGENTS.md`: name the target test, its current result and the
   intended change before each cycle; update the Live Detect plan and the
   targeted-test ledger at each commit; keep software, detector and physical
   metrics separate.
3. Check the targeted-test ledger before choosing a target. It lists
   experiments not to repeat (support-threshold sweeps, partial-arc
   thresholds, seed budgets).
4. Do not lower the 21/32 black gate or accept partial arcs.
5. Every detector change must hold zero detections on the empty controls.
6. No new ball staging. The camera stays stopped.
7. Do not touch Holdout data or the split lock.

## 6. Not planned

A learned detector (already failed on this data volume), any pipeline
rewrite, padded homography and height correction (unproven need),
threshold tuning without a manifest score.

## 7. External reviews: what held up

**ChatGPT comparison.** Its ordering matches this review's original plan,
so it inherited the errors in section 2: it repeats the per-ball replay
figures as if they described Live Detect and endorses cloth-facing arc
scoring, which the project had already rejected. Correct and useful: the
duplicated rows in Argon's export, the backup warning, and the caution on
an unconditional exposure lock.

**Luna review.** Several of its file links point at paths that do not exist
(`src/SkyEye.Vision/...`, `docs/argon/evidence-report.md`), so treat its
line references with care. Its substantive points checked out against the
code: the gain reuse after a cue dropout, the sampled cloth range, the
frame 266 conflict, the existing neighbour-occlusion check, the partial-arc
false acceptance, queued pixel writes, and that the warp audit measures no
centre error. Its advice to keep gates unchanged and treat tracking,
exposure and geometry as separate measured experiments is adopted above.
It is right that the two reviews are not independent corroboration: this
review read Argon's material and both drew on the same evidence.

## 8. Log

- 10 Nov 23:50. Plan written. Re-check found the corrections in section 2.
  No thread started. Plan and corrections not yet committed.
- 11 Nov 00:30. Paused overnight at Richard's request. Four agents ran T1-T4 in
  parallel; each wrote a resume note in `docs/claude/progress/`. Provisional,
  not to be quoted until each thread's report is written:
  - T1: manifest of 495 unique frames (37 labelled: 19 bright, 8 mid, 8 dim);
    frame 266 confirmed 16/16 (Argon's "missed" claim is unsupported); Argon's
    32 rows are 28 frames.
  - T3: Python port of the Live Detect reducer reproduces logged outcomes for
    code40-code61 runs and the code66 replay. First pass suggests most lost
    still-table time is quality flags (pocket-colour uncertainty, obstruction),
    and that a per-ball hold rule recovers almost nothing, because the pocket
    runs never form a first baseline. A classification bug remains.
  - T4: in 9 staged still pairs, the black's addition or removal at a cushion
    or jaw showed clearly (change 1.9-2.8 against at most 0.36 with nothing
    moved), including a black the detector misses; 0 false regions in 18
    within-clip controls. It fails across long gaps with moving daylight, and
    hands can leave false regions.
  - T2: design chooses a desktop look-alike of the OpenCV binding, linking the
    detector files unchanged. Main parity risk: phone OpenCV 4.14 with ARM
    kernels against desktop 4.13.
  Resume order: T3 report (it decides whether T6 is worth building), then T4
  review, then T1 scorer check. T2 needs a session with .NET.
- 11 Nov 08:40. T1, T3 and T4 finished (reports linked in the thread
  table). Findings that change the plan:
  - Flicker is not the problem in Live Detect. Of 2,402 s of still table
    without a matching baseline, 1,149 s is the pocket-colour layout flag
    and 941 s obstruction; flicker is 1.4 s. A per-ball hold recovers
    1.4 s, so it is dropped as a standalone change.
  - Under the pocket-colour flag, about 370 s is a correct steady layout
    (code48, code55); about 534 s hides a real black or cue miss.
  - Fifteen runs never form a first baseline; the current rule needs a
    black and a cue seen first.
  - The black misses are positional: one cushion spot, every light level.
  - Settled-frame change sees exactly that case (9/9 staged).
  These point to three design decisions, section 9.

## 9. Decisions needed from Richard

D1. **Pocket-colour flag scope.** Today one ball at a pocket mouth with
    doubtful colour marks the whole layout uncertain, which blocks every
    baseline and event. Option: mark only the balls within that pocket
    region as uncertain, so the rest of the table can settle and events
    elsewhere still fire. Risk: a pot into that pocket could be missed
    until the flag clears. Evidence: T3, about 370 s recovered on play runs.

D2. **First baseline without the black or cue.** Today no first baseline
    forms unless a black and a cue are seen. Option: allow it, with the
    readout saying "black not seen" rather than "black absent". Risk: a
    missed black is later "added" when finally seen; the event layer must
    treat that as a correction, not a ball returned to the table.

D3. **Use settled-frame change as evidence (T4).** When a settled state is
    compared with the previous one, an unexplained ball-sized change at a
    cushion or jaw would be treated as a ball arrival (and its absence as
    "nothing moved there"), which can confirm a black or cue the single-frame
    detector cannot see. Gated off when the cushion-band drift measure shows
    moving light. Needs T9 labels before it is turned on by default.

T5 (surplus-only demotion) needs no decision, only T2 to score it.

Decided by Richard, 11 Nov 08:45: D1 pocket region only; D2 allow a first
baseline with black or cue "not seen"; D3 build settled-frame change into
the pipeline, off by default until T9 labels score it. Production changes
go on branch `claude/live-detect-d1-d3`. This environment cannot compile
C#, so every C# change there is unbuilt until a Windows session runs
`scripts/Build-SkyEye.ps1` and the check suites; each is first measured
through the Python reducer port where possible.
- 11 Nov 08:30. D1+D2 implemented on `claude/live-detect-d1-d3`, unbuilt.
  Measured through the Python port on 48 runs: lost still time 2,402 s to
  2,092 s; runs forming a baseline 33 to 37; two extra events, both real
  settled pots; nothing new on static fixtures. A first baseline while a
  pocket is flagged stays off (it added false movement on fixtures). D3
  designed (shadow mode by default). T9 review sheets ready.
- 11 Nov 08:35. Session handed over to Claude Code on Richard's Windows
  machine. See the handover section of the investigation notes.
- 11 Nov. D1+D2 compiled on Windows (local Claude Code session, branch
  `claude/live-detect-d1-d3` at 4ffbeae, no source changes needed).
  `Build-SkyEye.ps1 -Target All`: SkyEye.sln and Companion.Hybrid both 0
  errors (SDK 11.0.100-rc.1). Software suites, newly run:
  `SkyEye.AgentUi.Checks` passes, including the new D1/D2 reducer checks
  and READINESS-ABSENCE-SEMANTICS-CONTROLS 5/5; `SkyEye.Checks` passes.
  On the first, cold run `LiveDetectShotClipWindowChecks` failed once at
  "post-roll rereads source observations…" (100 ms timeout in the check;
  code not touched by this branch); three reruns passed. It is a timing
  flake, recorded here, not fixed. C# replay of code66 `cfd3b9d4`: one
  `settled_layout_baseline` (7 reds, 7 yellows, black and cue not seen),
  199 `layout_uncertain` outcomes, no pot, return or clip. This matches the
  Python port. Detector cases and physical gates were not run (carried
  forward 0/3 and 0/3). On-device D1+D2 is still unverified.
- 11 Nov 08:54. T0 done. D: is a separate physical disk (Disk 1, external
  Sabrent USB, 1 TB; the laptop disk is Disk 0). An earlier run at 08:05
  reported FAIL: 13 missing and 7 size mismatches, all T3/T9 outputs
  rewritten after that copy, with 0 SHA-1 failures. Re-run
  `Backup-WorkEvidence.ps1 -Destination D:\SkyEye`: 105,523 files, 21.4 GB,
  0 missing, 0 size mismatches, 5,172 of 5,172 inventoried `work/` media
  SHA-1 verified, PASS. The other 76 inventory rows are git-tracked files
  (docs/evidence, tests/fixtures and so on), all present on origin/master.
  The script exits 1 on success because it passes through robocopy's
  "files copied" code; read the report, not the exit code.
- 11 Nov. T2 built and parity checked. The detector sources compile on
  desktop unchanged against a compat shim over OpenCvSharp 4.13
  (`src/SkyEye.OpenCv.Desktop`, `src/SkyEye.Vision.Desktop`,
  `tests/SkyEye.Vision.Desktop.Runner`, all in `SkyEye.sln`). Layout03
  parity against the device replay proofs: PASS 4/4 frames, 59/59 circles
  with the same kinds and flags, 58 centres identical, one 0.85 px. Desktop
  output can now stand in for the device on PNG frames of this kind; JPEG
  frames are untested. This is software parity, not a detector score:
  detector cases stay 0/3 and physical gates 0/3, carried forward. Report
  `t2-desktop-parity.md`. Next: the `detect` command over the T1 manifest,
  then T5.
- 11 Nov. T2 `detect` added and run on the T1 manifest (production
  settings, 35 of 37 labelled frames; 2 have no corners; Holdout unopened).
  Detector score, newly executed on desktop: 529/546 balls, 0 FP, black
  14/28, cue 25/28, colour 0.970, 19/35 frames complete. On the 24 frames
  with a stored result it ties capture on balls (368/374) and black (13/19);
  colour 0.978 against 1.000, all from 957, where the device's own
  current-source replay agrees with desktop (unknown 8). Parity now also
  checks final ball kinds; still PASS 4/4. New finding from frames that had
  no stored result: a second miss position, cue at the cushion near
  (0.062, 0.868) in 382, 1274 and 3360, and black in the corner pocket near
  (0.03, 0.928) in 382, 1274 and 395. The cushion black spot (1.48, 0.067)
  now shows in 8 frames. Detector inventory 0/3 and physical gates 0/3
  carried forward. Next: T5.
- 11 Nov. T5 implemented and scored on desktop. Classifier quality saturates
  at 0.9, so "least confident first" could not separate the surplus; the
  rule ranks by dominant-colour saturation and demotes only a surplus at
  least 40 below the lowest kept ball, else the whole colour as before
  (behind `SurplusOnlyInventoryDemotion`, default on; identity
  `-surplus-sat40`). 35 labelled frames: colour 0.970 to 0.996, balls
  529/546 and 0 FP unchanged, only 957 and 2263 change (7 yellows kept,
  cue stays unknown). 438 unlabelled frames: 20 change, all eight-to-seven
  plus one unknown, all still flagged. Suites pass; detector inventory 0/3
  and physical gates 0/3 carried forward. Report `t5-surplus-demotion.md`.
- 11 Nov. Second miss spot diagnosed on desktop (`miss-spot-2-diagnosis.md`).
  Cue at the cushion (382, 1274, 3360): proposed by the packed-yellow stage,
  fitted at 0.875 to 0.938, classified `cue`, then rejected because that
  stage keeps only yellows; a handover to the cue path is a candidate fix,
  to be scored for false cues first. Black in the corner pocket mouth (382,
  1274, 395): half its outline lies over the dark opening, best support
  19 to 20/32 against the 21/32 gate, empty jaw 16/32; same family as the
  cushion black, so D3 territory. Not the pocket exclusion. Branch not yet
  pushed: this shell cannot authenticate to GitHub.

