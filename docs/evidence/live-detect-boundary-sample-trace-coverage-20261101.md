# Boundary sample and pocket-sector trace coverage — 1 November 2026

Target `BOUNDARY-SAMPLE-REJECTION-TRACE` asks whether a reviewed pocket/cushion candidate fails because the background ring or rounded sample bounds are invalid, or because visible exterior sectors lack boundary support. This is an artifact audit only; no analyzer replay was run.

## Reusable evidence

| Evidence | Existing result | What it answers |
| --- | --- | --- |
| Code65 frame 1179, source pixel SHA-256 `0ce1efbdfdb940d04c0021356a527ab5ce45312334aa8bf988eb84362fac7301`, PTS `48928717966` µs | User confirmed one proposal-conditioned visible red point. Its nonduplicate red proposal sampled red at the raw centre, then fit 20/32 sectors (`0.625`) and was rejected below the unchanged 24/32 gate. | Identifies insufficient support as the rejection stage for that candidate. The retained report does not include the per-sector evidence or sample-bounds/background-sample failure counters needed to distinguish why support is low. It is not a complete reviewed inventory. |
| Code67 frame 554 cue path | Existing joint trace reports 24/32 supported, 8/32 unexplained; neighbor geometry explains 10/12 unsupported sectors at width 800 but 5/14 at width 1000 in the separate distance analysis. | Shows width-dependent unexplained sectors; it does not establish calibrated pocket-sector credit or sample-bounds rejection on a reviewed cue centre. |
| Code70 frame 1365 boundary probes | Existing records show cue and black support below acceptance on several paths; black seed support remains under the 21/32 gate. | Confirms incomplete support, but the black centre is unresolved and the frame label set is still a draft. |

The Code65 raw-proposal report is retained at `work/production/evidence/local-raw-proposal-gates-20261031/exact-profile-negative-replay-20261101/runs/rawclneg800-004647/o08-replay-proof-rawclneg800-004647-rawproposals.json` (SHA-256 `f19910e93a8b3a12eb38f18a1ede2362f267959e9655875f756c0d147d2099e7`). Its candidate audit stores the support fraction and rejection decision but not sector evidence (`sectorEvidenceSampleCount` and supported-sector indices are null). The summary is also recorded in [the Code65 exact-profile report](code65-raw-proposal-exact-profile-20261101.md).

## Decision

Do not repeat frame 1179 or the existing 1365/554 replays with unchanged code and inputs. The desired distinction is still unmeasured: saved results expose fit support and some joint-neighbor evidence, but not a consistent per-sector account of invalid background samples, rounded bounds, exterior rejection, and pocket-facing sectors for the same reviewed candidate.

Next offline work should add a diagnostics-only trace at the existing boundary-refinement seam, then run it once on a receipt-bound reviewed candidate and one matched empty-pocket/rail control. Record source commit, pixel/receipt hashes, candidate ID, all rejection reasons, and sector indices. Keep the support and readiness gates unchanged. A physical matched-cushion comparison remains dependent on stable adequate lighting and user-staged controls.

This audit changes no pass/fail denominator. Current metrics remain software `3/3`, focused motion `20/20`, production detector inventory `0/3`, and D1/D2/E `0/3` proven.
