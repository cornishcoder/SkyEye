# What D3 would change if it could act: possible pots on 11 November (offline)

**Target test:** `D3-POT-VETO-ANALYSIS`. **Question:** across today's device runs, how many of Live Detect's
"possible pot" events would D3's evidence contradict, and would it contradict any real pot? Offline replay of
the pulled logs; no capture, no product change.

New tool: `dotnet run --project tests/SkyEye.AgentUi.Checks -- --pot-veto [--detail] LOG.jsonl ...` replays
each run with D3 evidence (schema 15) through the current reducer and policy and, for every `possible_pot`,
reports (a) whether D3 holds or infers that colour within the next 40 passes, and (b) whether the colour's
count dropped while the table was still (every pass Settled since the colour was last at its old count).

## Result

| Run | Time | Live Detect | Truth | D3 inference (U2) | Still-table drop |
|---|---|---|---|---|---|
| `3a434fbb` | 16:35:02 | pot black | **false**: black on the table (16:36 screenshot) | none | **yes** (motion at most 0.0012) |
| `3a434fbb` | 16:36:07 | pot black | **false**: same black | none | **yes** (motion 0) |
| `5a519dc2` | 20:33:54 | pot red | **real**: Richard's break pot | none | no (motion 0.02 to 0.11) |
| `e7276502` | 21:40:18 | pot black | **false**: cushion black flickering | none | **yes** (motion at most 0.0024) |
| `e7276502` | 21:40:26 | pot yellow | unverified | none | no (motion during the shot) |
| `e7276502` | 21:40:43 | pot black | **false**: black placed on the cushion | **infer_black at the cushion** | no (hand moving) |

- 4 of the 6 possible pots are false black pots. U2 alone contradicts 1; **a still-table rule would
  contradict the other 3**. Together: 4 of 4 false black pots, 0 of 1 real pot, the unverified yellow left
  standing.
- **U1 (hold) cannot do this job as designed.** At the moment a possible pot settles, the reducer accepts it
  as a new baseline without the ball, and D3's chain re-anchors to that baseline (length 0 in every case
  above), so there is no missing baseline ball left to hold.

## Proposed rule (not built): pot without motion

A count decrease of a colour is not a pot when every pass from the last pass at the old count to the pot was
`Settled` (no table motion): a ball cannot leave the table without moving. In shadow it would be a proposal
`pot_without_motion` beside U2's `infer_black`.

Risk before it may act: the motion detector calls the table still below 0.0025 of its area changing, and one
ball is about 0.001 to 0.002, so **a single ball creeping alone into a pocket might not count as motion**.
The 21:40:18 false drop measured 0.0024 (a hand arriving), just under the threshold; true pots today measured
0.02 or more, but all involved several moving balls. Needed: the motion fraction of lone slow pots, from the
T4 clips or a short staged session, and a veto threshold set below them.

## Follow-up the same evening: the still-table signal is not enough on its own

One ball as a fraction of the table bed is 0.00115; a ball that moves more than a diameter changes about
0.0023 (0.0034 with its shadow), against the 0.0025 still threshold. So "the table stayed Settled" cannot
rule out a single ball rolling slowly into a pocket, and a rule built only on it could cancel a real pot.

A sturdier test, "nothing changed where the ball last stood", was added to `--pot-veto`: it looks for the
next settled-change comparison spanning the drop and checks the change grid at the ball's last detected
position. Result: for the real red pot and the yellow it correctly reports "changed at last position"
(never contradicts them), but for the three still-table drops there is **no comparison to look at**,
because D3 only compares after motion has settled.

**Proposed design:** when a possible pot arrives while the table is still, have D3 run a comparison on
demand (reference window from before the drop against the current settled window) and check the ball's
last position. A ball that left must have changed that spot whatever the motion threshold says; a detection
drop leaves it unchanged. This needs a processor change (on-demand comparison) and is not built.

## Limits

Six events, three runs, one evening. The two 16:3x false pots come from the black missed in a touching group
after the break (see `d3-chain-fix-device-check3.md`); 0.8.67's packed-black rules changed that case, so a
re-run would differ. Software unchanged; detector inventory 0/3 and physical D1/D2/E 0/3 carried forward.
