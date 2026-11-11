# D3 staged cushion-black session: U2 infers the black where Live Detect calls a pot (11 November 2026)

Follows [D3 shadow proposal review](d3-shadow-proposal-review.md) and [0.8.68](companion-0.8.68-evidence-frames.md).
Companion 0.8.68 / code 76. Evidence: `work/production/evidence/d3-cushion-black-session-20261111/`
(`after/` log pull with raster sidecars and source frames, `rasters-sheet.jpg`).

**Target test:** `D3-CUSHION-BLACK-SESSION`. **Before:** U1 to U4 had never fired live. **Intended:** live
evidence on the case D3 exists for: the black on a cushion where the single-frame detector misses it.

## Session (physical, newly executed; staged by Richard)

Run `e7276502`, started 21:39:03 after the reconnect guard (mount confirmed unmoved this evening). Layout:
6 reds, 7 yellows, cue, black against the end cushion at about (1.92, 0.50). Then Richard: (1) a shot not
touching the black; (2) moved the black by hand to the open table; (3) put it back on the cushion. Stopped
21:41:10 (Live Detect stopped, End session); camera `/status` `Stopped` revision 31, Home **Camera ready /
Start live session**. About 2 minutes of capture; no Cast or TV.

## Result

| Moment | Live Detect (reducer) | D3 settled change (shadow) |
|---|---|---|
| Baseline 21:39:05 | 6 reds, 7 yellows, **black not seen**, cue | black `not_seen` |
| 21:40:15 to :18 (black flickers into view) | `baseline_corrected`, then **possible pot, black 1 to 0** (false) | no proposal |
| Step 1 shot, comparison 11 (21:40:28) | possible pot, yellow 7 to 6 at 21:40:26 (unverified) | 4 ball-sized regions, all explained: **no proposal** |
| Step 2, black to open table, comparison 12 (21:40:38) | balls returned: black 1 | departure at the cushion (dark 0.48 to 0.18), dark arrival at (1.181, 0.592); `unexplained_change` at the cushion (the departure of a black Live Detect never saw) |
| Step 3, black back on cushion, comparison 13 (21:40:47) | **possible pot, black 1 to 0** at 21:40:43 (false: it was placed, not potted) | **`infer_black` at (1.923, 0.521), recheck pending; black state "inferred"** (correct, within 0.025 of where it stood) |

So on the case that matters, Live Detect said "possible pot" and D3's shadow evidence said "the black is on
the cushion here". U2 fired once, correctly; no false `infer_black`. The `unexplained_change` at step 2 is
technically right (an unseen ball left) but not useful; it could become `departure_support` for a not-seen
ball in a later rule change.

Per-pass cost: median 15.7 ms, p95 29.3 ms, max 130.9 ms; 322/322 passes with evidence.

## Frame saving: budget still spent too early

All 12 source frames were saved by 21:39:33, before step 1: 5 change frames from the black flickering on
the cushion, 2 interval frames, and 4 `settled-change` frames on comparisons 2 to 5, which held only thin
sub-ball regions from the same flicker. The D3 raster pairs for comparisons 11 to 13 were saved (sidecars),
so the D3 side can be replayed; full-resolution detector frames for those moments were not. Fix: take a
`settled-change` frame only when the comparison has a ball-sized region, and keep flicker from spending
change slots.

## Frame budget fixed in 0.8.69 (later the same evening)

- A `settled-change` frame is taken only when the comparison found a **ball-sized** region (the flicker
  comparisons held only thin regions).
- Change-triggered frames (quality or inventory) are at least **5 s apart**.

Replaying this session's 322 passes through the new policy (offline): 11 frames, of which 5 are spaced
flicker/interval frames before the shots and **3 are the comparisons that mattered** (21:40:28 shot,
21:40:38 black to the open table, 21:40:47 black back on the cushion). New AgentUi check: inventory flicker
every 0.5 s for 10 s takes two change frames.

Verification (newly executed): `Build-SkyEye.ps1 -Target All` 0 errors; `SkyEye.Checks` and
`SkyEye.AgentUi.Checks` pass; Layout03 parity 4/4; golden-shadow passes on this session's log (764 frames).
Companion 0.8.69 / code 77 installed in place (storage retained; previous APK
`work/production/backups/20261111-215947/before.apk`; camera package unchanged); camera `Stopped`
revision 31. Not yet run live.

## Limits

One staged layout, one light level, one inference. U1 (hold) and U3 (departure support) still have not
fired live: this layout had the black missing from the baseline, so there was nothing to hold. The black
was on an end cushion, not the (1.48, 0.067) spot of the labelled misses.

Software carried forward from `566195a`. Detector inventory 0/3 and physical gates D1/D2/E 0/3 carried
forward.
