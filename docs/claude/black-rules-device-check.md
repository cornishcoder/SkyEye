# Device check: packed-black rules on 0.8.67 (11 November 2026, evening)

Follows [black-absent dim rack control](black-absent-dim-rack-control.md). Companion 0.8.67 / code 75 (commit
`652f992`, containing the dark-body check and the adjacent-ball rule, both on), installed in place
(previous APK `work/production/backups/20261111-201557/before.apk`, camera package unchanged). Evidence:
`work/production/evidence/black-rules-device-check-20261111/`.

**Target test:** `BLACK-TOUCHING-GROUPS` on the device: rack then break. **Before:** afternoon run
`3a434fbb` (0.8.66) had 105 of 206 rack frames obstructed and the cue often unfound; after the break 225
frames obstructed with the black missed in a touching group. **Outcome: PASS for this run.**

## Run `5a519dc2`

- Start live; reconnect guard after the reinstall; **Confirm camera has not moved** (Richard confirmed the
  mount earlier this evening; only balls moved since). Start live detect 20:32:47 on a full rack with the
  black (restored after the black-absent control).
- Richard broke at about 20:33:45 and **potted a red off the break** (his report).
- Stopped once settled: Live Detect stopped, then End session; camera `/status` `Stopped` revision 27, Home
  **Camera ready / Start live session**. About 2 minutes of capture; no Cast or TV.

| Check | Result |
|---|---|
| Rack (155 passes) | `rack_detected` and `settled_layout_baseline` at 20:32:49; **155/155** read 7 red, 7 yellow, black, cue; `clear_unchanged`; **0 obstructed** |
| Break | obstructed only 20:33:46 to 20:33:52 (20 passes, Richard at the table breaking) |
| Pot | `possible_pot` "red detections 7 to 6" at 20:33:54; `possible_pot_settled` at 20:33:58: 6 red, 7 yellow, 1 black, 1 cue. **Matches the red Richard potted** |
| Settled after break | clear and unchanged to the stop (last pass 20:34:30: 6, 7, 1, 1) |
| D3 shadow | 275/275 frames with evidence; median 15.1 ms, p95 24.2 ms; chain intact throughout (length 1 and 2 on the rack, then length 1 again after the pot, re-anchored to the new settled baseline); one `unexplained_change` at (1.455, 0.914) cushion, not applied |
| Live Detect unchanged by D3 | `--golden-shadow` passes on the pulled log (868 frames, 86 table events identical) |

## Reading

Compared with the afternoon run on 0.8.66 at a similar evening light, the rack phase went from 105/206
obstructed to 0/155 and the post-break table settled clear with the correct inventory instead of staying
obstructed. One run, one layout, one light level: it shows the rules work live here, not that every
post-break touching group is solved. No source frames were saved, so the passes cannot be replayed exactly.

Software carried forward from `ef66dd1` (build 0 errors, both suites pass, parity 4/4). Physical: this
device check PASS (newly executed); D1/D2/E 0/3 and detector inventory 0/3 carried forward.
