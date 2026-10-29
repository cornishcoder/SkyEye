# Live Detect delivery plan

Updated: 29 October 2026. Goal active; end-to-end acceptance is incomplete.

## Required outcome

While Live Detect is active, report the supported current table inventory and positions, recognise table events during normal play, and recover naturally when the user replaces balls or re-racks for another frame. No match setup should be required for perception. Keep clear what is observed, uncertain, stale, or inferred; a last good inventory must not masquerade as a fresh observation. Shot planning must not gate detection or readiness.

## Work tracker

| Step | Status | Work and acceptance evidence |
| --- | --- | --- |
| 1. Make centre evidence trustworthy | Completed for the Oct 29 holdout frame | Reproduced the user's crop, found the wrong inverse-transform term in its drawing command and replaced the plotting calculation with independent forward/reverse solves. Exact image/receipt/label/replay hashes and coordinate round trips checked. 800px mean/max centre error: 2.919/7.306 source pixels versus provisional labels; 1000px: 3.004/7.602. Full image and crop visually inspected. Further label review remains part of step 2. |
| 2. Establish current inventory and readiness on saved evidence | In progress | Replay the exact Production analyzer/observation path over stationary four-light footage, the independent holdout and spread tables. Review per-ball positions, colour counts, stillness, uncertainty and source freshness; retain hand/cue occlusion negatives. Existing four-light clips are development evidence, not independent acceptance. Existing 256/256 clear live reads cover one static session. |
| 3. Make events follow supported inventory transitions | Next implementation | Separate event eligibility from advice-only inventory requirements. Verify red/yellow loss, cue-ball loss, mixed loss and no-pot breaks with exact source timestamps. Emit each transition once, handle uncertain gaps and avoid treating a hand/cue occlusion as a pot. Replay saved player samples and inspect before/after pixels; player reports provide context rather than automatic truth. |
| 4. Handle returns and re-racking continuously | Pending | Recognise supported count increases, cue returns and return to a full rack; replace the previous event baseline only after sufficient current evidence. Report the current inventory even after many frames of play. Do not infer a new game solely from a 16-ball count: distinguish a supported packed rack from replaced/spread balls. No restart or manual baseline reset should be necessary. |
| 5. Present and preserve usable live results | Pending | Home should show fresh inventory, readiness/uncertainty, event time and recovery state clearly. Logs must retain source frame IDs/PTS, positions, quality and event evidence so Codex can join results to saved clips. Preserve existing 30-second clip saving, direct Home shortcuts, pinned menu and the single observation/state ownership path. |
| 6. Accept on a bounded live play session | Pending | Run a stable initial table, break without pot, observed pot(s), cue scratch/return and re-rack while the same Live Detect session remains active. Compare physical outcomes with timestamped logs and frames. Repeat appropriate lighting/occlusion checks; measure detection cadence and source age separately. Stop capture, verify camera `Stopped`, and leave Home at `Start live session` after evidence collection. |

## Concrete defects and decisions to resolve next

1. `ShellLiveDetect.RunLiveDetectAsync` requires `!vision.Uncertain` as well as clear layout for event candidates. `VisionAnalyzer` sets broad uncertainty when cue or black is absent. A scratch therefore cannot currently produce a candidate even when the visible layout itself is reliable. Earlier cue-absent colour flicker is a real negative control: dropping the broad gate alone would not establish correct classification.
2. `LiveDetectCountCandidateTracker` silently resets its baseline on clear count increases or mixed increases/decreases. It exposes only decrease candidates, so there is no explicit ball-return or re-rack report. Stable supported current inventory must survive this transition without carrying stale events into a new frame.
3. Count decreases alone do not identify a pocket or establish a physical pot. Use the existing source-time observation/tracking seams and saved before/after evidence, and retain uncertainty where a hand covers a ball. Avoid creating a second canonical match store to solve display needs.
4. The old holdout overlay is invalid as a geometric review image: its handwritten inverse-Y coefficient caused a 15.39–61.09px downward shift. It is preserved alongside the corrected outputs. Follow the [pipeline notes](live-detect-pipeline.md#centre-overlay-discrepancy-resolved-29-october-review) and the committed comparison script when generating future reviews.

## Evidence locations and commands

- Holdout and corrected reviews: `work/production/evidence/o08-holdout-20261029/raw-session-e3c02bb9dc4542948c829a512630041a/`.
- Five player clips and v21 exact replay: `work/production/evidence/shot-samples-20261027/current-v21-five-clips-20261029/`. The earlier red disappearance under a hand/cue is an occlusion negative, not confirmed red-pot evidence. Two yellow decreases were advisory; no canonical state changes were made.
- Four-light replay: `work/production/evidence/o08-current-device-check-20261029/v21-lighting-replay/`.
- Reproduce centre comparison: `./scripts/Compare-ManualVisionCentres.ps1 -EvidenceDirectory work/production/evidence/o08-holdout-20261029/raw-session-e3c02bb9dc4542948c829a512630041a -Width 800` (repeat at 1000).

Update this tracker after each concrete implementation or evidence result. Commit focused changes often. Treat green builds, corpus checks, a one-frame location score and physical event acceptance as different kinds of evidence. Keep capture stopped for offline replay/builds/waits, and keep Cast/TV untouched unless the user requests it.
