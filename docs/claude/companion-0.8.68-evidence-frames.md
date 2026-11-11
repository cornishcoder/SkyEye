# Companion 0.8.68: source frames at the moments that matter (11 November 2026)

Prepares the staged top-cushion black session (live evidence for D3 U1/U2). Carries the D3 region-centre fix
(`18a1880`) and a change to which source frames Live Detect saves.

**Target test:** `LIVE-EVIDENCE-FRAME-BUDGET` (new). **Before:** none of today's live runs could be replayed
exactly around the shot. **Intended:** frames saved at each settled-change comparison, so the next session
can be replayed on the desktop.

## Why no frames were saved around the shot

Frames were saved, but `LiveDetectFrameEvidenceCapture` allowed **6 per run** (initial, then every 3 s, or
on any quality/inventory change), and every run spent them in its first 4 to 15 seconds:

| Run | The 6 saved frames |
|---|---|
| `755281db`, `7c151fba`, `5a519dc2` | initial + five 3 s samples, all before the shot or break |
| `3a434fbb` | initial + five quality/inventory flips within 4 s of the start |

## Change

- Up to **12** frames per run (the store's 96-file / 128 MB caps still apply).
- Interval samples only at the run's start: at most **2** after the initial frame.
- Quality and inventory changes, and interval samples, stop **4** slots early; those slots are kept for
  **`settled-change`**: a frame saved on every pass that produces a new settled-change comparison.
- `ShellLiveDetect` now runs the settled-change observer first in the frame callback and passes "new
  comparison" to the capture policy.

New AgentUi check: with quality flicker every pass, exactly 7 change frames then 4 `settled-change` frames
(12 in all); a quiet table takes only the initial frame and two interval samples. The first version let
interval samples use the reserved slots; the check caught it.

## Verification (newly executed)

`Build-SkyEye.ps1 -Target All` 0 errors; `SkyEye.Checks` and `SkyEye.AgentUi.Checks` pass; Layout03 parity
4/4; `--golden-shadow` passes on both device log pulls (717 and 868 frames identical). Companion 0.8.68 /
code 76 installed in place on the Pixel 11 (storage retained; previous APK
`work/production/backups/20261111-213554/before.apk`; camera package unchanged); camera `/status`
`Stopped` revision 28. Not yet run live.

## Next: staged top-cushion black session

Live Detect on a layout with the black against the top cushion near the middle (where it is missed, so the
baseline reports it not seen); one or two shots that leave it alone (U1 should hold it); then one where it
moves to a visible spot (departure support). About 5 minutes of capture; frames now saved at each
comparison.

Detector inventory 0/3 and physical gates D1/D2/E 0/3 carried forward.
