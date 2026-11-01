# Live Detect continuation goal — 1 November 2026

Copy the prompt below into the next Live Detect goal. This file prepares the continuation; it does not itself start capture or replace an active goal.

```text
Continue and complete SkyEye Live Detect reliability in C:\code\SkyEye. Work through implementation, measured evaluation and physical acceptance. Keep the goal open until both the inventory/readiness gate and the event/clip gate pass on the current implementation.

Start by reading AGENTS.md and these files:
- docs/live-detect-plan.md, especially the dated current-status section and next-work gates.
- docs/evidence/live-detect-control-ledger-20261031.md.
- docs/o08-mounted-table-reliability-plan.md and docs/o09-learned-ball-detection-spike.md for corpus, split, comparison and release requirements.
- docs/device-control-guide.md before any device action.
- docs/evidence/code70-exact-config-replay-20261101.md, code70-raw-proposal-exact-profile-20261101.md and code65-raw-proposal-exact-profile-20261101.md.
- docs/evidence/pocket-cushion-centre-labels-20261031/negative/user-reviewed-centres-frame-978.json and user-confirmed-point-frame-1179.json.

Inspect git status, recent commits, current source and the referenced artifacts. The prompt was prepared from clean commit d9fef42; verify the actual checkout rather than assuming that commit or any installed package is still current. Older plan/handoff sections include historical device versions, camera status and diagnostic settings. Resolve discrepancies using dated artifacts, receipt/configuration identities and current code. Never treat an old Stopped readback as a current device observation.

Outcome

Live Detect must report a fresh settled inventory with unique, correctly counted and classified balls, including balls beside pockets and cushions, across the required lighting/layout groups. Movement, obstruction, stale frames and unexplained changes must preserve honest unknown state. After that gate passes, prove the physical event sequence and one correctly bounded clip per eligible shot, with review through the existing canonical SkyEye.GameState path.

Verified starting evidence to recheck

1. Latest retained physical Code70 run: 151 checks, 14/16 balls in 147 and 13/16 in four; every frame uncertain, 146 layout-uncertain; no trusted baseline, events or clips. Detector p50/p95 338/395ms, full pass 397/491ms, source age 825/894ms. These are historical measurements, with no matched live before/after benchmark.
2. Exact captured-profile Code70 six-frame replay uses width 800, packed-colour extent .4 and radius-first sparse-black ranking. It returns seven red/seven yellow, no cue/black, and uncertain/layout-uncertain output on all six. Raw-proposal experiments do not admit the missing cue/black through existing support gates. Width-1000/.65 and earlier mismatched runs remain diagnostic evidence. Read full settings from receipts; a partial settings list is insufficient.
3. Exact-profile Code65 dim black-absent replay returns 15 balls on five frames and 14 on frame 1179; all uncertain. The user confirmed all 15 cyan centre markers in frame 978: cue + seven red + seven yellow, black absent. The blue rail item is Taom chalk, excluded from the ball inventory. Preserve the original draft, review/overlay hashes and rough 4–7px uncertainty. No numeric review tolerance was agreed. This is one Development frame, with assistant-placed points subsequently checked by the user.
4. Frame 1179 has only one user-confirmed red point at source pixel (125.2,194.4), selected by a prior model proposal. That proposal fits 20/32 sectors against the unchanged 24/32 Hough gate. Its other centres and complete inventory are unreviewed. Do not transfer frame 978 labels to other frames merely because the layout appears stationary. Code70 centres remain unreviewed and its black centre unresolved.
5. Same-camera/same-light black-present/absent and cue-present/absent controls are missing. Code65 dim negatives and Code70 daylight positives are different source sessions and do not form that pair. The latest user report was that it was too dark to play; do offline work until suitable lighting and the physical layout are established.
6. The O08 corpus has one complete reviewed Development/StillRegression label file and no accepted held-out set. The separate frame 978 review has not been imported into a frozen scoreable corpus. Repeated adjacent frames from one session are not independent trials.
7. Current export-o08, merge-o08 and O09 comparisons accept screening/probe-only reports. The audited raw-proposal-replay report instead has samples[].baseline and samples[].withRawProposals and lacks required report fields including independentFrames, per-sample detectionRequested and fullAnalysis800.observations. Baseline circles exist, but no compatible export or score has been produced.
8. Raw-versus-rectified studies are candidate/stage diagnostics. Fixed-fit raw boundary sampling gained no supported sectors in 23 candidate pairs. Neither that result nor local-model proposals establish the accuracy of a complete raw-space detector. Replay preparation speedups, builds and installs do not establish live reliability.

Execution order

A. Establish one reproducible baseline and close the scoring path.
Verify hashes, source frame IDs/PTS, pixel dimensions, calibration provenance, analyzer/APK identity, settings, device, warm-up treatment, batch size and sample-to-observation mapping. Record what is missing. Reuse completed runs where valid.

Choose the smallest defensible route into the existing frozen O08/O09 scorer: a supported exact-profile replay if it closes a specific evidence gap, or an explicit baseline-only export path for the retained reports. Preserve original report mode/hash, branch selection, receipts, source timing, exact configuration, skipped samples, uncertainty and runtime scope. Verify any omitted execution fields from authoritative artifacts or record them as unavailable and reject unsupported comparisons; do not invent them or relabel a diagnostic report as screening. Keep proposal-assisted output explicitly separate. Exercise meaningful mapping, rejection and provenance checks for any bridge changes.

Import frame 978 with its complete review provenance as Development data. Complete further saved-frame reviews where useful. Display each actual image/overlay inline before asking the user to review it, and show readable label IDs/crops when needed. Preserve reviewed versus draft versus proposal-conditioned status. Freeze labels, session-separated splits, matching tolerance and candidate configurations before opening holdout results. Record numerical centre-accuracy and repeat-count criteria before evaluation; retain rough label uncertainty and do not describe new criteria as previously agreed.

B. Collect the missing physical controls with bounded captures.
First establish suitable lighting and a hands-clear layout with the user. Keep camera, calibration, source settings, lighting and non-target balls fixed. Start with a visible black centre by the lower-left pocket and cue nearby without overlap. Capture settled frames for (1) both present, (2) remove only black, (3) restore black to its reviewed position and remove only cue, (4) restore cue. Stop capture before waiting for layout changes, review, builds or replay. Retain PTS, exact image/receipt hashes, post-settle timing and configuration. Reject comparisons affected by light/camera/non-target drift.

Collect the overlapping pocket cluster separately; never fabricate an occluded black centre. Include other-colour/pocket, empty-interior, rail/chalk/shadow, cue-absent and obstruction negatives, plus independent rack, spread, cushion and lighting sessions. Keep an entire recording session and all related captures in one control/setup group on the same side of the split, including recording restarts between layout changes. Frame 978 stays Development data. Review visible centres and absence locations before scoring.

C. Fix one measured failure mechanism at a time.
Trace exact source frames through preprocessing, proposals, boundary fitting, classification, duplicates and final uncertainty. Select a bounded change from that evidence and evaluate the complete positive/negative comparison. Iterate on Development data; freeze the candidate and criteria before opening held-out results. If holdout feedback drives tuning, treat that set as Development and obtain a fresh session holdout. Preserve calibrated geometry, C# vision ownership and existing support/classification/ambiguity/pocket/duplicate safeguards. Do not promote relaxed thresholds, cue Hough recovery, arc fitting, mutual occlusion credit or raw-space detection from draft or unmatched evidence. Use the existing O09 comparison seams if a learned/hybrid candidate is warranted; require the same data and Pixel resource gates.

Report per-frame and per-session/lighting-group recall, misses, false positives, duplicates, colour errors, reviewed centre error, uncertainty/readiness, and p50/p95 runtime. Match warm-up, batch shape, source frames, settings and device path for before/after claims. Measure processing time and source age separately. Retain the 1.4s Live Detect target with its documented meaning; explicitly distinguish cadence, processing and source freshness and apply the agreed criteria to each. Record sample sizes and percentile definitions. Shared Pixel replay jobs must run serially; respect existing batch caps.

D. Pass the inventory/readiness gate before play acceptance.
On repeated held-out rack, spread, pocket/cushion and lighting cases, require unique correct 16/16 inventories when all balls are present, correct deliberately reduced inventories in absence controls, centre accuracy within the precommitted bound, fresh source evidence and clear readiness. Require no false movement, pot, return or re-rack on untouched tables and no false readiness during obstruction/staleness. Audit the actual readiness predicate so missing cue/black or uncertain geometry cannot be hidden by count matching or ignored flags. Verify the visible current/last-settled/unknown readout and recovery after obstruction, source gaps and acquisition failures. A failure returns to offline diagnosis.

E. Pass the physical event, clip and canonical-review gate.
Only after D passes, run a bounded sequence from a trusted baseline covering no-pot, object-ball pot, cue scratch/return and intentional re-rack, plus obstruction/no-touch recovery. Review before/after inventories and source-aligned movement/settle/event PTS. Ambiguous or interrupted intervals remain unknown. Verify exactly one correctly linked and bounded clip per eligible shot, including two quick distinct shots and stopping during pending post-roll. Check same-timeline requested windows, the existing 2s lead-in/post-roll policy, decoded content, keyframe effects, coverage and finalization; incomplete clips retain their uncertainty. Surface outcomes through ReviewLiveOutcomeAsync and the existing canonical GameState review path. Verify accepted facts and canonical state agree. Keep historical reducer/clip tests separate from this current physical acceptance.

Working and device rules

Proceed autonomously on authorized implementation and offline analysis. Make concrete progress after the initial audit; avoid repeated broad diagnostics or documentation-only cycles without a named evidence question. Use small commits and focused meaningful tests, preserve unrelated work, and update docs/live-detect-plan.md plus the evidence ledger after each result. Use GPT-6 Luna at max reasoning for bounded independent subagent audits when useful, without overlapping edits or concurrent device jobs.

Follow AGENTS.md and the device guide: mounted camera capture stopped except during the bounded evidence step; Home End recording session at the first safe boundary; verify /status state Stopped and Home Start live session before waiting/building/replay/finishing. A failed endpoint read means stop status is unverified. Preserve clips/app data; preserve app identity/storage for companion installs; do not update the mounted camera package without explicit authorization. Do not use Cast/TV. A match is unnecessary for still-table perception checks. Reuse existing state, scene, replay, command and media seams.

If user participation or lighting blocks physical work, finish independent offline tasks, state the exact missing layout/evidence, and wait with capture stopped. Never infer a review or physical success from silence. Report the last concrete result, current commit, remaining gate and verified device state. Do not mark this goal complete until D and E both pass with linked evidence on the current implementation.
```
