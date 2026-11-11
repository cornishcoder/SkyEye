# D3 on-device shadow check: result (11 November 2026)

Plan: `d3-device-check-plan.md`. Companion 0.8.63 / code 71 (commit c44669f), APK SHA-256
`5B962279DE92CC565332866CC442B946983648F83566B26C5BBBD0C4D9E7F657`, installed in place on Pixel 11
`66181FDDV001HS` with UID and private storage retained; previous APK
`work/production/backups/20261111-123231/before.apk`. Evidence: `work/production/evidence/d3-shadow-check-20261111/`.

## What happened

- First start: Live Detect was disabled by the calibration-reconnect guard. Capture was stopped and
  `/status` confirmed `Stopped` before asking Richard. He confirmed the mount had not moved; after the restart,
  Settings → **Confirm camera has not moved**, then Live Detect.
- Run `755281db-547e-4f68-9178-5a4887af1ff1`: still table, a hand pass (control), then a break with no pot.
- Stopped with **Stop live detect** and **End session**; camera `/status` `Stopped` (revision 5), Home
  **Camera ready / Start live**. About 4 minutes of capture in total. No Cast or TV.

## Results (physical, newly executed)

| Check | Result |
|---|---|
| Evidence produced | 344/344 frames carry `settledChange`; Moving 32, Settling 30, Settled 282; no unavailable passes |
| Cost per pass (Pixel) | median 14.7 ms, p95 22.3 ms, max 162 ms (the break's comparison); guard not tripped |
| Memory while running | total PSS 564 MB (graphics 220 MB, native heap 49 MB); no D3-off figure to compare |
| Hand-pass control (seq 2) | 0 ball-sized regions; one jaw region rejected as a rail crossing |
| Break (seq 3) | 9 ball-sized + 2 oversize regions; one raster pair saved (71 KB, 74 KB) |
| Later comparisons (seq 4 to 6) | 0 regions |
| Decoder restart | processor reset to a new epoch, as designed |
| Live Detect behaviour | its own events normal: rack, baseline, obstruction, movement, no pot observed, event clip |
| Mode line | Live Detect activity screen: "Settled-change evidence: shadow (not used) · 5 comparison(s) this run"; Home unchanged |

## Defect found and fixed (`D3-CHAIN-ANCHOR-FIX`)

On the device run (`ae5abbe`), `chainIntact` was false on every pass, so the shadow rules produced **no proposals** (not even for the break). Inspection of `work/production/evidence/d3-shadow-check-20261111/after/live-detect/live-detect.jsonl` showed `StableTableChangeTracker.BaselineSourceTimeUs` did **not** move on ordinary passes; instead, three concrete timing/state mismatches between `StableTableChangeTracker` (3 clear frames, ~0.7 s) and `SettledChangeProcessor` (`SettledSegmentTracker`, ≥ 3 still frames spanning ≥ 1.2 s after a prior frame for motion) broke the chain:

1. **Initial `NoReference` (`seq 1`) broke the chain.** At run start (Frame 2362) and after decoder recovery (Frame 72), the 3-frame layout baseline forms ~1.1 s before `SettledSegmentTracker` reaches 1.2 s of stillness and emits `seq 1` (`Status = NoReference`, establishing the initial reference window). `LiveDetectSettledChangePolicy.Link` treated every `Status != Compared` as breaking the chain instead of only `NoReference` after motion (`Sequence > 1` or `chain.Count > 0`, design 2.3).
2. **`BaselineSourceTimeUs` is sample 1 ($s_1$) of the 3-sample baseline probe, 1–2 passes earlier than `ReferenceSegmentStartUs` ($s_2$ or $s_3$).** On `seq 2` (hand pass, Frame 4029), `BaselineSourceTimeUs` was Frame 2332 ($s_1$, where `motionFraction` was `null`) while `ReferenceSegmentStartUs` started on Frame 2353 ($s_2$). On `seq 4` (post-break stillness, Frame 4483), `BaselineSourceTimeUs` was Frame 4328 ($s_1$, while balls were decelerating within `SameLayout` tolerance) while pixel stillness started on Frame 4348 ($s_3$, where `BaselineAdvanced` fired). Tracking the baseline's confirmation window `[anchorStartUs, anchorEndUs]` ($[s_1, s_3]$, widened without clearing `chain` if `BaselineSourceTimeUs` moves while `BaselineAdvanced` is false) aligns the first link's reference segment with the baseline view.
3. **Shot transition comparison (`seq 3`) arrives 3 passes *after* `BaselineAdvanced`.** On the break, `StableTableChangeTracker` promoted the post-break baseline $B_2$ on Frame 4348 (3 clear frames), whereas `SettledChangeProcessor` finished its 1.2 s settle window 3 passes later on Frame 4384 and emitted `seq 3` (comparing the pre-break segment $S_1$ to the post-break window $S_2$). Because `BaselineAdvanced` had already fired at Frame 4348, `Link(seq 3)` rejected `seq 3` and set `chainBroken = true`, poisoning `seq 4..6`. In addition, while `pass.State` is `Moving` or `Settling`, `chainIntact` must be suspended so pre-shot links do not emit false `hold` proposals on moving balls before the shot comparison arrives.

**Fix (`LiveDetectSettledChangePolicy.cs`, `LiveDetectTableEventTracker.cs`, `LiveDetectSettledChangeChecks.cs`):**
- `NoReference` at `Sequence == 1` with `chain.Count == 0` records the epoch without setting `chainBroken`.
- `LiveDetectTableEventTracker` passes `BaselineConfirmedSourceTimeUs` ($s_3$) alongside `BaselineSourceTimeUs` ($s_1$), and resets the policy on `mediaGenerationChanged`. `LiveDetectSettledChangePolicy` anchors to `[anchorStartUs, anchorEndUs]` and widens it without wiping `chain` if `BaselineSourceTimeUs` moves while `BaselineAdvanced` is false.
- When a transition comparison (`ReferenceSegmentEndUs < anchorStartUs && CurrentWindowEndUs >= anchorStartUs`) arrives from an intact previous baseline, it is accepted as a transition link (`chainIntact = true, chainLength = 1`, enabling U2/U5 inference and explaining pre/post-shot ball regions) and is absorbed/replaced when the first comparison starting in the new baseline's segment (`seq 4`) arrives.
- `chainIntact` requires `pass.State == SettledChangeState.Settled`.

## Raster review (`rasters-sheet.jpg`, `seq 3` break) and golden-shadow replay

- **Break raster pair (`seq 3`, Frame 4384):** 11 regions total (9 ball-sized green circles, 2 oversize grey circles in `work/production/evidence/d3-shadow-check-20261111/rasters-sheet.jpg`):
  - **2 oversize rejected regions:** `(1.508, 0.479)` (`area = 16.69` balls, the triangle rack footprint where the pack broke apart and 4 balls remain) and `(1.082, 0.382)` (`area = 5.17` balls, merged arrival of 3 closely grouped balls — yellow, red, cue — in mid-table).
  - **8 ball-sized arrival regions:** every one matches one of the 8 isolated scattered post-break balls within `Near` (`0.0432` table units = $1.6R$, distances `0.009..0.043`): reds at `(1.084, 0.249)`, `(1.878, 0.252)`, `(1.525, 0.187)`, `(0.983, 0.215)`, `(1.907, 0.827)` and yellows at `(1.611, 0.770)`, `(1.682, 0.662)`, `(1.730, 0.497)`.
  - **1 ball-sized departure region:** `(0.515, 0.471)` (`Direction = Departure`, `white 0.06 -> 0.69`), the cue ball's departure from baulk. The pre-break cue ball was detected at `(0.467, 0.488)` (`d = 0.0503 = 1.86R > 1.6R` because the cue ball's bright highlight and cloth shadow shifted the region centroid by ~3 raster px), so it produces 1 shadow `unexplained_change` proposal (`applied = false`).
- **Golden shadow & logged replay (`--golden-shadow work/production/evidence/d3-shadow-check-20261111/after/live-detect/live-detect.jsonl`):**
  - `PASS golden shadow live-detect.jsonl: 4 runs, 717 frames, 24 table events identical with and without shadow evidence`
  - Replaying run `755281db-547e-4f68-9178-5a4887af1ff1`'s 344 logged frames through the fixed policy gives **77 intact settled passes** across all 5 comparisons:
    - `seq 1` (initial reference): `intact = false, len = 0, proposals = 0`
    - `seq 2` (hand pass): `intact = true, len = 1, proposals = 0`
    - `seq 3` (break): `intact = true, len = 1, proposals = 1` (`unexplained_change@(0.515,0.471)`)
    - `seq 4` (post-break stillness): `intact = true, len = 1, proposals = 0`
    - `seq 5` (post-break stillness): `intact = true, len = 2, proposals = 0`
    - `seq 6` (post-break stillness): `intact = true, len = 3, proposals = 0`
    - `seq 1` (after decoder recovery): `intact = false, len = 0, proposals = 0`

Detector inventory 0/3 and physical gates D1/D2/E 0/3 carried forward.
