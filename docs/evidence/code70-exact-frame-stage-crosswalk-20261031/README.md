# Code70 exact-frame cue and black stage crosswalk — 31 October 2026

This offline diagnostic aligns the six saved Code70 pocket frames with the physical Live Detect rows and a later width-800 cue-probe replay. It exposes where the replay path lost the black and cue while keeping source identity, physical output, and probe output distinct.

## Result

The physical Code70 run has 151 frame checks: 147 reported 14/16 balls and four reported 13/16. All 151 were uncertain, 146 were layout-uncertain, and none was obstructed. The run produced no trusted baseline, table events, or clips. Recalculated p50/p95 values are 338/395 ms detector time, 397/491 ms full-pass time, and 825/894 ms source age. Processing time and source age are separate; there is no matched before/after live timing trial.

All six source frames were matched by frame ID, source PTS, and PNG SHA-256 to exactly one row from physical run `f9dd4379-cfc7-4977-8533-4cbfdd4356a5`. The physical rows and the offline replay each contain 7 red and 7 yellow circles, with no black or cue. All six replay observations remain uncertain; five are layout-uncertain and none is obstructed.

The replay stage audit reports:

- The sparse-black search saw 119 ball-sized seeds and ranked 72 across six searches, with a per-search fit cap of 12. Of those 72 ranked probes, 58 had no boundary fit, 11 had insufficient boundary support, and three failed classification. No black was accepted.
- All six cue-recovery records report no unique first-pass cue and zero neutral-body candidates. One sparse-black dark-edge probe was classified as cue, but that did not produce an accepted cue.
- Hough telemetry has 50 `rejected-duplicate-before-fit` decisions; packed-colour telemetry has one duplicate-peak rejection. These are candidate-stage rejections, not measured duplicate-ball false positives.
- The ordinary Hough audit also records 262 cloth rejections, 29 no-fit rejections, eight insufficient-support rejections, and 29 margin rejections across 378 audited Hough proposals.

The [crosswalk CSV](code70-cue-stage-crosswalk.csv) contains each exact source tuple, physical row, replay result, configuration identity, per-frame uncertainty, and stage counts. The [candidate trace CSV](code70-cue-candidate-trace.csv) preserves all 802 candidate-audit rows, including proposal/fitted coordinates, support, classification, decision, report observation index, and final uncertainty for each source frame.

Across the six reports, packed-colour telemetry records 43 red and 50 yellow peaks. It accepts 42 red and 42 yellow peaks, rejects one red peak as a duplicate peak and eight yellow peaks by classification, and adds 84 strong isolated red/yellow bodies. The Hough, sparse-black, cue-recovery, and duplicate-stage outcomes are summarized above and are available per candidate in the trace. The exact frame and PTS list with PNG hashes is also retained in the [control ledger](../live-detect-control-ledger-20261031.md).

All 378 Hough audit rows end in a rejection: 262 cloth, 29 no-boundary-fit, eight insufficient-support, 29 margin, and 50 duplicate-before-fit. The sparse-black path also records 12 dark-body candidates rejected by pocket exclusion. These decisions and fitted/proposed coordinates are preserved per row; no reviewed centres are available to score the unmatched proposals as false positives.

## Provenance and reproduction

The source frames are from the daylight, mounted-camera Code70 layout listed in the control ledger: black and cue near the lower-left pocket, with other balls around pockets and cushions. Their calibration scope is timeline `76ec0f8c622045378494b0c250198ce6`, generation 6, calibration `27a80900d5fb4cd983a91c6a367389f0`.

The crosswalk script verifies the replay report SHA-256 against its manifest, each report sample against its indexed receipt and PNG bytes, and each physical row against run ID, frame ID, PTS, pixel hash, timeline, generation, and calibration. The report observations do not carry frame IDs or PTS, so the script pairs each report sample with `fullAnalysis800.observations` by array index after checking equal counts. The index is recorded in the CSV; this positional association is not an independent per-observation identity check.

Reproduce from the repository root:

```powershell
pwsh -NoProfile -File scripts/Export-Code70CueProbeCrosswalk.ps1
```

The input replay is `work/production/evidence/code70-daylight-pocket-capture-20261031/cue-probe-replays/cueprobe6-110025/`. Its manifest SHA-256 is `97bba6b88585ec96e141769d181b548865521aff317173ee343c9a6a72bdac04`; its report SHA-256 is `4c510ad68fc1f426501e39cea2b8e4a24156de13e4cfcb05a72282cba39551c9`. The physical `live-detect.jsonl` SHA-256 is `ef1aa6996ade5e5698974cc971fb2e92cda22d9884497f371666e719c4f2b947`.

## Two-frame hot-stage profile context

The retained `code70sparseprof-111516` screen-only profile uses frames 1365 and 1425 from this source set, device `66181FDDV001HS`, and width 800. Its manifest SHA-256 is `00cf372d6b355824ecb33e8f6073972c5e4f838bf654fe403f6995bc95c54291`; report SHA-256 is `11f66a554dd9844b7c0cdcff3d5c552317b5e8dd831c104faa249fac8203fd56`; APK SHA-256 is `9ab5c1f8a1bc317781f0dac73aec135b2c6b72633e5fa6a2ce2386bd3e077b60`. The manifest records the camera `Stopped` before and after this historical probe. Both frames still returned 7 reds and 7 yellows, no black or cue, and `uncertain=true`.

For these two frames, the screen-only instrumentation reported means over two calls: packed-cluster proposals 761 ms, sparse-black mask/seed extraction 20 ms, sparse-black fit/classification 577 ms, sparse-black recovery 600 ms, Hough candidate processing 199 ms, and circle detection 1022 ms. The sparse-black ranked limit was 12 per frame and the profiler counted 24 boundary refinements; the candidate audit on the same exact frames shows no accepted black. These are diagnostic stage measurements, not a latency distribution. Timer stages are nested, so their values must not be added together.

This profile is `screen-only` with candidate audits disabled and uses a different APK from the probe-only candidate trace. It has no recorded warm-up treatment, and its `.65` packed-colour extent does not match the physical `.4` receipt. It localizes a potentially expensive bounded stage on two frames, but supports neither an optimization claim nor a detector change. Use the matched, reviewed positive/negative holdout before changing the fit budget or candidate ranking.

## Limits and interpretation

The probe used mode `probe-only`, device `66181FDDV001HS`, APK SHA-256 `a376cdce9a2e5b43b4d7f62c3ccfb98e54c6f9269df302c7daca5dd6d80ecaa6`, width 800, and the normal perspective-rectified detection path. Both replay configuration comparison flags are false. The physical receipt records packed-colour boundary extent `.4`; this probe used `.65`. Other recorded settings include illumination normalization off, pocket exclusion 48 px, Hough support `.75`, sparse-colour support `.625`, sparse-black extent `1.3`, sparse-black support `.65625`, and candidate cap 12. The algorithm identity string matches, but settings do not. Treat the replay as diagnostic, not an exact-settings comparison or detector accuracy result.

Offline analyzer-call times range from 2185 to 2719 ms (median 2387 ms; nearest-rank p95 2719 ms). They are from a separate device replay path with mismatched settings and do not compare to physical Live Detect timing or source age. No reviewed centres are bound to these frames, so recall, false-positive rate, duplicate-ball rate, and centre error cannot be scored. The pocket arrangement also does not provide the requested independently visible, non-overlapping black and cue centres. No detector setting changed.

The candidate trace records source scope, calibration, replay settings, audit positions, candidate geometry, decisions and final uncertainty. It does not hash intermediate warped-image, HSV-mask or other preprocessing rasters, so it is a decision trace bound to exact source bytes and settings rather than a byte-level record of every intermediate image.

This is the calibrated, rectified detector path. The separate [raw-versus-rectified fixed-fit audit](../raw-perspective-boundary-support-20261031/README.md) resamples existing candidate fits and does not generate raw-space candidates. Neither diagnostic resolves the matched-control gap. The user reported that it is too dark to play; no new capture was started, and the held-out inventory and event/clip gates remain open.
