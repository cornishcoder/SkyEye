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

## Defect found

`chainIntact` was false on every pass, so the shadow rules produced **no proposals** (not even for the break).
Suspected cause: `LiveDetectSettledChangePolicy` restarts its chain whenever the reducer's
`StableTableChangeTracker` snapshot `BaselineSourceTimeUs` changes. If that field moves on ordinary matching
passes, the chain is wiped constantly. Not yet confirmed or fixed. The synthetic checks and the golden test
did not catch it because they hold the baseline time fixed between passes.

## Not done

- Confirm and fix the chain anchor; add a check with a moving `BaselineSourceTimeUs`; replay this run's logged
  evidence through the fixed policy.
- Review the break's raster sheet (`rasters-sheet.jpg`) against the balls that moved, and run `--golden-shadow`
  on the pulled log.

Detector inventory 0/3 and physical gates D1/D2/E 0/3 carried forward.
