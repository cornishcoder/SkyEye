# Dim hand/arm obstruction controls (11 November 2026)

Follows [low-chroma cloth](obstruction-low-chroma-cloth.md), which found that the T1 manifest had only three
hand/arm controls and that one of them (#3, `hand-2s`) is caught by accident.

**Target test:** `OBSTRUCTION-HAND-CONTROLS` (new). **Before:** 3 hand/arm controls, all obstructed, one only
through a cushion shadow band. **Intended:** enough dim hand/arm frames to check that today's three
obstruction changes (`-obstruction-shadow60`, `-packed-local-unknown25`, `-obstruction-chroma14`) lose no
real obstruction, and a baseline for later obstruction work.

## Where the frames come from

- The T1 manifest has almost none: of its 283 non-Holdout dim and mid frames, reviewed on contact sheets,
  only two show a hand (one is control #3). It is mostly staged still layouts.
- The code57 event clips (`work/production/evidence/code57-readout-recovery-20261029/event-clips`, 10 clips,
  about 160 s of play, 29 October evening) show hands, arms, cue and the player's head and shoulders over
  the cloth. Sampled every 2 s, reviewed on a contact sheet, 31 frames picked and decoded with OpenCV.
- Table V 59 to 76 (the manifest's dim stratum is below 80). The cloth is blue-grey, not warm-tinted.
- No Holdout source (Holdout rows are exactly the files under folders named holdout).
- Corners: the session's calibration `83bd5f29` was not saved with corners; the current-mount corner set
  fits this session's pockets and cushions (checked on an overlay) and is used.
- The clip behind control #3 (`live-detect-pocket-lip-20261030/landscape-export.mp4`) is an app export with
  a different framing and an overlay, so it cannot be used with known corners.

Fixture: `tests/fixtures/detection-corpus/obstruction-hand-controls.json` (role `obstruction-control`);
frames under `work/production/evidence/obstruction-hand-controls-20261111/` (git-ignored, hashed in the
fixture).

## Result (desktop detector, newly executed)

| Expect | Frames | Obstructed before today's 3 changes | Obstructed now (production) |
|---|---|---|---|
| obstructed (hand/arm/body over the cloth) | 16 | 14 | **14** |
| edge (bridge hand at the cushion), not scored | 12 | 3 | 3 |
| cue-only, not scored | 3 | 1 | 1 |

No frame changes either way, and 0 balls change. So today's three obstruction changes lose no real hand or
arm here.

The two missed real controls, k7 and k57, are size misses, not cloth-colour misses: no non-cloth component
reaches the 96 x 96 px minimum (k7: hand at the cushion beside the cue; k57: motion-blurred arm mostly off
the playing surface). This predates today's changes.

Software suite not rerun: no source change (fixture, README and docs only). Carried forward from `1690ce0`:
build 0 errors, both suites pass, parity 4/4.

## What this does not cover

The weak case found earlier, a hand on **warm-tinted** dim cloth whose hue estimate makes skin read as
cloth (control #3), is not represented: these clips have blue-grey cloth. A warm-light hand clip with known
corners is still needed before changing the obstruction cloth-hue estimate.

## Next

1. Find a warm-light hand capture with known corners among saved sessions; otherwise it needs a short
   capture at the table (your call: it is new staging).
2. Black recall for the black-less racks (group A and #10 to #16).
3. Companion build with today's obstruction changes and the D3 chain-fix device check.

Detector inventory 0/3 and physical gates D1/D2/E 0/3 carried forward. Not run on the device.
