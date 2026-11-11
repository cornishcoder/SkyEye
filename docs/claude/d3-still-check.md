# D3 still check: was anything changed where the ball stood? (11 November 2026)

Follows [the pot-veto analysis](d3-pot-veto-analysis.md): 3 of today's 4 false black pots happened while the
table was still, where D3 had no comparison to look at, and "the table read Settled" alone is unsafe because
one moving ball sits near the motion threshold.

**Target test:** `D3-STILL-CHECK`. **Intended:** on a count drop while the table is still, compare the table
now with the table when it settled, and say whether the missing ball's spot changed. Shadow only.

## Design

- `SettledChangeProcessor` keeps the settled window from the moment the still segment began (the anchor; one
  extra raster, about 1.1 MB). `RequestStillCheck()` asks for one comparison on a later pass that is still
  `Settled` with a full window after the anchor; the request is dropped if motion starts (the normal
  comparison then covers it) or a new segment begins. The result is `SettledChangePass.StillCheck`
  (`SettledStillCheck`: anchor and current window times, the comparison). It is **not** chain evidence and does
  not change the chain.
- `ShellLiveDetect` asks for it when the D3 state is `Settled` and the reducer shows a count decrease starting
  (`inventory_change_pending`) or a `possible_pot`. Cost: one comparison (about 35 to 130 ms on the device so
  far), only on such passes. Its rasters are saved as sidecars (`still-anchor`, `still-current`) and the check
  is logged as `settledChange.stillCheck`.
- Policy rule **U6** (shadow): for each baseline ball missing from a trusted current layout, no change in the
  still check's grid or regions at its spot gives `pot_contradicted`; a change gives `still_check_changed`;
  a gated check (lighting drift) gives `still_check_gated`. Nothing is applied.

## Verification (newly executed)

Desktop self-check (`settled-change-selfcheck`, staged mid-cushion frames 542 and 288):

| Case | Result |
|---|---|
| Still table, request, same view | 1 still check, 0 ball-sized changes |
| Black and cue appear with no moving frames | motion seen (0.0062), request dropped, normal comparison made |
| **One ball vanishes** (288 with the black's dark pixels replaced by 542's cloth) | motion read **Settled** (0.0014, under 0.0025); the still check found a thin change region at the black (389, 614 against 390, 615) and the change grid is set there, so U6 would say `still_check_changed`, not contradict the pot |

That last case is exactly the lone-pot risk from the analysis: the motion detector misses it, the still
check does not.

AgentUi checks (new): no change at the missing black gives `pot_contradicted`; a change gives
`still_check_changed` and no contradiction; a gated check contradicts nothing; no missing ball, no output;
the still check is logged.

Software: `Build-SkyEye.ps1 -Target All` 0 errors; `SkyEye.Checks` and `SkyEye.AgentUi.Checks` pass; D3 self-check
PASS; Layout03 parity 4/4; golden-shadow passes on all three device log pulls (Live Detect output identical).

## Not yet known

Whether it fires on the device as intended: today's logs cannot replay it (it needs live rasters). Expected
on the next run with flicker or a dropped black: a `pot_contradicted` where Live Detect logs a false pot on a
still table. Not built for the device.

Detector inventory 0/3 and physical gates D1/D2/E 0/3 carried forward.
