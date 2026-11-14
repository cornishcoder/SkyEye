# D3 chain-anchor fix: device check, second attempt (11 November 2026, evening)

Follows [the first attempt](d3-chain-fix-device-check.md), which false obstruction blocked, and today's three
obstruction fixes ([shadow tolerance](obstruction-shadow-tolerance.md),
[local unknowns](packed-cluster-local-unknowns.md), [low-chroma cloth](obstruction-low-chroma-cloth.md)).
Evidence: `work/production/evidence/d3-chain-fix-check2-20261111/` (`before/` and `after/` log pulls,
`logcat.txt`).

**Target test:** `D3-CHAIN-ANCHOR-FIX` on the device. **Before:** not exercised live (no baseline formed).
**Outcome:** **not exercised: Live Detect stopped on its third pass with an `ArgumentException`**, caused by
a D3 shadow logging bug, now fixed in 0.8.66 / code 74. The detector side did what the fixes intended on
the two passes it ran.

## Run `65ccfdec` on companion 0.8.65 / code 73

- First Start live (15:54): Live Detect unavailable because of the calibration reconnect guard. Capture
  stopped (End recording session, `/status` `Stopped` revision 13) and Richard asked; he confirmed the mount
  had not moved. Capture about 30 s.
- Second Start live, **Confirm camera has not moved** (Settings, Table awareness), Start live detect at
  15:57:46. The table was not racked: balls spread from the afternoon break.
- Two passes logged (15:57:47 and :48): 15 balls (7 red, 7 yellow, cue, black not seen), **not obstructed**,
  layout certain, colours uncertain only for the missing black.
- Third pass (frame 2264): Live Detect stopped, "analysis error (ArgumentException)". It is the outer
  handler (no retry), so the error came after the detector, not in it.
- Stopped at once: End recording session, `/status` `Stopped` revision 15, Home **Camera ready / Start live
  session**. About 1 minute of capture in all. No Cast or TV.

## Cause

With the black not seen in the baseline, the settled-change shadow policy (D3 slice 3, `7fc50e1`) adds a
`not_seen` ball state with `NaN` table coordinates. The Live Detect log writes the frame record with
`System.Text.Json` web defaults, which throw `ArgumentException` on `NaN`, and the exception escaped the run
loop. It never happened before because no earlier device run formed a baseline with the black not seen
(the afternoon run was obstructed throughout). Offline replay (`--golden-shadow`) recomputes the shadow
but does not serialise it, so it could not catch this.

## Fix (0.8.66 / code 74)

- `LiveDetectSettledBallState` coordinates are now nullable; a not-seen ball has `null` position.
- New check in `SkyEye.AgentUi.Checks` (U5): the policy's not-seen state is serialised with the log's JSON
  options and read back with null coordinates. It failed on the old code (serialisation threw) and passes
  now.
- Software, newly executed: `Build-SkyEye.ps1 -Target All` 0 errors; `SkyEye.Checks` and
  `SkyEye.AgentUi.Checks` pass; `--golden-shadow` passes on both of today's pulled device logs (4 runs, 766
  frames; 5 runs, 768 frames). No detector change, so parity and detector scores carry forward.
- Installed in place: storage retained, previous APK `work/production/backups/20261111-161803/before.apk`,
  camera package unchanged; camera `Stopped` revision 17, Home **Start live session**.

## Next

Repeat the check on 0.8.66: rack, Start live (confirm the mount if asked), Start live detect, about 30 s
still, break, wait to settle, stop.

Detector inventory 0/3 and physical gates D1/D2/E 0/3 carried forward.
