# Code65 exact-profile dim negative raw-proposal replay — 1 November 2026

## Scope

This is a diagnostic replay of the retained dim, black-absent Code65 pocket frames through the captured width-800 analyzer profile. It corrects the execution-profile mismatch in the earlier `rawclneg6-164655` raw-proposal replay. The six frames are repeated observations from one separate capture session and lighting condition; they are not a same-light pair with the Code70 black-present frames, independently reviewed labels, or six independent trials.

No production detector setting or normal `Analyze` path changed. The raw-proposal entry point is diagnostic-only and did not write canonical `GameState`. No live capture was started.

## Inputs and analyzer identity

The source receipts all bind to mounted-camera timeline `2ea48e0c35cd474eb627acf970a7eec5`, generation 3, calibration `6d304dd864f84c8486ee30844fe685b1`, and report detection requested. The table gives source PTS in microseconds and full SHA-256 values for the PNG and receipt.

| Frame | Source PTS (µs) | PNG SHA-256 | Receipt SHA-256 |
| ---: | ---: | --- | --- |
| [978](../../work/production/evidence/code65-source-frame-check-20261031/exact-replay-input/978.json) | 48922017388 | `b368348c63d924f5c7834e8e849b7287225ab525b470bdc3b85f7a8278cbbe70` | `653e1ea4bc6c927ba592ad796a6c084e49103c27003d13e5d23d0c98a3144034` |
| [1072](../../work/production/evidence/code65-source-frame-check-20261031/exact-replay-input/1072.json) | 48925150988 | `e398d50ff10d521060a8a460dde4d6b4196d94fde3cf893fb21d975efdd531e8` | `ea4ce6b0c206d74b557398b8f051d38edd376d341479c54686fc3a911957d44d` |
| [1105](../../work/production/evidence/code65-source-frame-check-20261031/exact-replay-input/1105.json) | 48926251088 | `e6f733f4f5e7e06fb97b700515fbe9bfb2a98f709a11a18ee9ae7b0b748936ef` | `68fe8e1653888831ca95e52da0e2893154fb7a23d6427b30402136aca9af58fd` |
| [1150](../../work/production/evidence/code65-source-frame-check-20261031/exact-replay-input/1150.json) | 48927751222 | `e4531ab11b7c74910770aa0f046994bb0babbe8597cf6df4387a0780d6643eda` | `177884273991ffec2a64b4a8f1a446140485bf807f2c6b71a1cb5909c9a34f65` |
| [1179](../../work/production/evidence/code65-source-frame-check-20261031/exact-replay-input/1179.json) | 48928717966 | `0ce1efbdfdb940d04c0021356a527ab5ce45312334aa8bf988eb84362fac7301` | `fca4a67f270caba01d04af620d40beb84eaae7ed69a10e772b86bbba5a8c00ca` |
| [1198](../../work/production/evidence/code65-source-frame-check-20261031/exact-replay-input/1198.json) | 48929351355 | `e74e17f41921769b32b0b3de8492df8074fa4b720e542122496aed2e47fca634` | `69d51814cc5b8620af88448bac8295a469d5863bdde22a2cb895fcb88e7ea4c3` |

Every PNG, receipt, proposal file, and proposal-to-PNG source hash matched the replay manifest/report. The recorded and replay analyzer identities are identical: `body-priority-packed-colour-peaks-v25-neutralceiling94-edge23-pocket48-sparse20-review75-redyellow-black-radius13-support21`, width 800, packed-colour boundary extent `.4`. Other captured settings include pocket exclusion radius 48, Hough support `.75`, sparse-colour support `.625`, sparse-black extent `1.3`, sparse-black support `.65625`, radius-first sparse-black ranking enabled, and illumination normalization off. Both baseline and proposal-assisted measurements report analysis width 800; no 1000px fallback occurred. The manifest's separate `packedColourBoundarySearchExtentFraction=.65` describes the unused width-1000 diagnostic analyzer; the raw-proposal analyzer used the width-800 `.4` setting.

The run used the isolated Vision Checks APK with SHA-256 `39ee43d25d85016e47f1ed83c31e75657caf1a9b89e5bb0327bf32f7f94c369a`, built at repository HEAD `65eedc7`. The report has `configurationComparison=exact-match`, `configurationMatchesPrimary=true`, `configurationSettingsMatchPrimary=true`, `diagnosticOnly=true`, and `canonicalStateChanged=false`.

## Results

Baseline and proposal-assisted final counts matched on every frame. In the table, B/A means baseline / proposal-assisted. The source proposal labels are unreviewed and are not ground truth.

| Frame | Source PTS (µs) | Baseline = assisted inventory | Uncertain B/A | Layout uncertain B/A |
| ---: | ---: | --- | --- | --- |
| 978 | 48922017388 | 15: cue 1, red 7, yellow 7 | true / true | false / false |
| 1072 | 48925150988 | 15: cue 1, red 7, yellow 7 | true / true | false / false |
| 1105 | 48926251088 | 15: cue 1, red 7, yellow 7 | true / true | false / true |
| 1150 | 48927751222 | 15: cue 1, red 7, yellow 7 | true / true | false / false |
| **1179** | **48928717966** | **14: cue 1, red 6, yellow 7** | **true / true** | **false / true** |
| 1198 | 48929351355 | 15: cue 1, red 7, yellow 7 | true / true | false / false |

The report contains 92 external proposals: 6 cue, 44 red, and 42 yellow; it supplies no black proposal. Their recorded decisions are 84 duplicate-before-fit rejections, 5 accepted proposals, 1 insufficient-boundary-support rejection, and 2 table-margin rejections. The analyzer's separate sparse-black audit made 12 boundary-fit attempts per frame; those attempts had no fit, with one additional pocket-exclusion rejection. No black circle was output. This is a limited empty-pocket false-positive diagnostic, not a supplied false-black-proposal test.

All baseline and proposal-assisted outputs remained uncertain. The proposal-assisted `layoutUncertain` flag was true on frames 1105 and 1179 while the baseline flag was false on all six. Frame 1179's 14-ball output is an observed detector count, not a measured recall miss: the centres are unreviewed. The earlier `.65` run reported 15 in every frame but used a width-1000 raw-proposal path and was marked configuration-mismatched; it is not a matched comparison with this width-800 `.4` run.

Six-frame offline analyzer wall times were p50/p95 1,042/1,484 ms for baseline and 1,227/1,933 ms for proposal-assisted analysis. Here p50 is the median and nearest-rank p95 is the maximum of six samples. The full instrumentation phase took 16.180 s; the runner took 20.410 s including setup and transfers. These are offline diagnostic timings; live detector latency and source age were not measured.

The camera `/status` readback was `Stopped` before and after, revision 94, battery 21%, 139 clips, and 126006 completed frames; these values were unchanged. The replay used the isolated Vision Checks package and did not update the mounted-camera package.

## Artifacts and next gate

- [Replay report](../../work/production/evidence/local-raw-proposal-gates-20261031/exact-profile-negative-replay-20261101/runs/rawclneg800-004647/o08-replay-proof-rawclneg800-004647-rawproposals.json), SHA-256 `f19910e93a8b3a12eb38f18a1ede2362f267959e9655875f756c0d147d2099e7`.
- [Replay manifest](../../work/production/evidence/local-raw-proposal-gates-20261031/exact-profile-negative-replay-20261101/runs/rawclneg800-004647/manifest.json), computed SHA-256 `a3fc0045f925a4bb9eaecafebbbc0375e89ac15c2eb684e9aa5bf0896cd0036a`.

The six dim frames are useful as a separate-session negative diagnostic, but they do not supply the missing matched black-present/absent pair, cue-present/absent pair, reviewed centres, or a held-out lighting result. The user reported that current lighting was too dark to play, so no new capture was attempted. Resume with the same-light lower-left-pocket layout and review the source centres when lighting supports reliable placement. The held-out inventory gate and subsequent event/clip gate remain open.
