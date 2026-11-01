# Opposite-side mid-cushion cue control — 1 November 2026

## Scope

This is a Development-only control of the same mid-cushion black position with the cue moved to the opposite side at similar separation. The user reported that only the cue moved; the black, other balls, chalk and physical lighting stayed fixed. The source frames show the black and cue separated, with chalk on the rail. No production threshold or detector code changed.

The Home verifier saved six frames and reported `Unsettled (TimedOut)`. Detection was requested on frames 959, 999 and 1035; the other samples were motion-only. Home also reported `Sample saved · 31.4s clip · 5s target timing unverified`. The mounted-camera `/status` clip count remained 139 before and after this operation; saved app data and frames were preserved. Capture was stopped through Home **End session** before replay.

## Capture results

| Frame / source PTS (µs) | Detection | Live inventory result | PNG SHA-256 | Receipt SHA-256 |
| --- | --- | --- | --- | --- |
| 959 / 27819764400 | Requested | 16: red 7, yellow 7, cue 1, black 1; `uncertain=false`; 299.27 ms | `a1fbd2386be1d6ca40d01ab7800f3b567f42ba76d629eb8dbc9dfd6792c48953` | `12700661c3c7e5a31b902f21f2ce9bf47dad99e7ed0642a134b924b186f1410a` |
| 982 / 27820531133 | Motion-only | No live detector result | `72316b92f9bf95133a11e52e485d8df5069a01c828917202a215aa5c5fd45251` | `b7aeecaf250df5497c9a7f2848ed391a1fb365001c5f95afce2f3ec57164fc14` |
| 999 / 27821097844 | Requested | 15: red 7, yellow 7, cue 1, black 0; `uncertain=true`; 253.26 ms | `2bc28f5762b76ab1b29a94a9742256bddde28d074b174c947811830451578a48` | `ca3d240f07024ab1cc6312b5bb9efba1abf0eb7b2a6886c69c02a9c1c233eac7` |
| 1018 / 27821731233 | Motion-only | No live detector result | `ce1ccbc84c21e9785897dd9a6a78f43ffa8b6c6fab4f62a56ee98de6754f646e` | `d3a7dbacc9ff114c8a5c32fea9af4c9270dd8e69ed7ec94430f0f997a75782bb` |
| 1035 / 27822297955 | Requested | 15: red 7, yellow 7, cue 1, black 0; `uncertain=true`; 282.64 ms | `617c3753d4ff2d4f5c2653f38d3f91b452bde53f2a2b9ad66fa056d996622b1e` | `0864bf39fe93c86cf841939f234ff81e257a6df114e6e47a1a1018dce92808db` |
| 1057 / 27823031344 | Motion-only | No live detector result | `8ac1887819bb5f0c65a66c10209d92c355f320284437416ebfaf05969cfe657a` | `dd8acfc46bf11013783ac7f465c1ed0d06dd7ee409a4d309c677aef241154e10` |

All receipts bind the same operation `87b15e58-4e33-483e-b2c2-9e8f299b481f`, timeline `ad683a2f494b4b94a5981c7140d38a2f`, calibration `00a2e8e6d0eb4c90bec72c9bfea9c28f`, and identical ordered calibration corners. The image and receipt hashes are checked again in the [machine summary](../../work/o08/live-detect-black-pair-v1/physical-controls/stage-replays/oppositecue-20261101/opposite-cue-stage-replay-summary-current-source-20261101.json), SHA-256 `dcd65994a8f0c892778cfa8fd2393442f389700563feb84e74e18695cd0d5c54`.

## Exact current-source replay

The six saved PNG/receipt pairs were replayed serially in `full-audit` mode on checkout HEAD `4cea11a4c4a8027fb0d7a7761153075489f43afe`. VisionAnalyzer source commit was `484f2c593c2582519cd9637216e34da362976814`; replay instrumentation commit was `8e2edd43854c3b387f99fbdbecb04516ab4a5bcc`. VisionChecks APK SHA-256 was `9b9e1658b781a8773bf3fc3e70a54f889e7132ad5fb600224c6963b6cb754be3`. Recorded and replay analyzer identities match exactly at width 800 and packed-colour extent `.65`. Sparse-black extent remains `1.3`, minimum support `21/32`, radius-first ranking, dark-body value ceiling 40, pocket exclusion radius 48, and illumination normalization off.

| Frame | Saved-frame replay | Black audit at expected position |
| --- | --- | --- |
| 959 | 16/16; `uncertain=false` | Dark-body black accepted with 22/32 boundary support |
| 982 | 15/16; black absent; `uncertain=true` | No dark-body black candidate near the expected position |
| 999 | 15/16; black absent; `uncertain=true` | No dark-body black candidate near the expected position |
| 1018 | 16/16; `uncertain=false` | Dark-body black accepted with 22/32 boundary support |
| 1035 | 15/16; black absent; `uncertain=true` | No dark-body black candidate near the expected position |
| 1057 | 16/16; `uncertain=false` | Dark-body black accepted with 23/32 boundary support |

Only the three detection-requested capture frames count as live detector observations: one correct full inventory and two uncertain black misses. The other three replay results are saved-frame diagnostics, not live requests. Across the six frames there are no red/yellow count errors. This does not establish a settled inventory or Ready state.

In the earlier same-side cue-positive control, the current-source replay missed black in all three requested frames; the nearest sparse-black fits had 18–19/32 support. The opposite-side run has one requested hit with 22/32 dark-body support and two misses with no nearby dark-body candidate. However, the source images are not a matched photometric comparison. Four fixed cloth regions versus the earlier restored frame 288 changed by `−13.45`, `−6.52`, `−12.66`, and `+0.92` median luminance units in frame 959. The other new frames show a similar regional shift. This uneven display-referred change can reflect lighting or camera processing; it makes a same-light directional comparison invalid. Do not infer that moving the cue caused the different fit, and do not tune from this small correlated group.

The separate [fixed-patch comparison](../../work/o08/live-detect-black-pair-v1/physical-controls/photometric-roi-check-opposite-cue-20261101.json), SHA-256 `a21a71a0e99257de484e01b68bc2519c11f9b53c1bcc86c5571ad9ffdf1151d5`, preserves all six patch measurements and the interpretation limit. The exact replay report SHA-256 is `53926043c2125aa752de8ee9e2e216c93c58c1182a448f459b967209f0aa5107`; its manifest SHA-256 is `7c8f5c8f669d28ece4079edc1f0619fa401ebf275d999099c4dedda7a1af55150`.

## Visual review and device boundary

The [six-frame overlay sheet](../../work/o08/live-detect-black-pair-v1/physical-controls/review-overlays/opposite-cue-review-v1/opposite-cue-six-frame-review-sheet.png), SHA-256 `87df8ca3e84994076ea2e5a0b00eb1157325b80b5a9859e86da0984994dee6d3`, and [enlarged black/cue crops](../../work/o08/live-detect-black-pair-v1/physical-controls/review-overlays/opposite-cue-review-v1/opposite-cue-black-cue-crops.png), SHA-256 `3cad6e408215dd961a8da9e0d68a926b5bbc64412384a2afb3c50de00beda377`, display current-frame replay centres and expected black placeholders. The [per-frame draft manifest](../../work/o08/live-detect-black-pair-v1/physical-controls/review-overlays/opposite-cue-review-v1/labels-draft-manifest.json), SHA-256 `6695df1d6a2110369c7190073c2eee3e556df99122e21a6bf6e96798fd139e94`, binds each point to its own image, receipt and overlay. `B?` marks only the expected black location on frames where replay had no black output; it is not a detector result. The label review is pending. No numeric centre tolerance, centre-error score, or physical cloth-plane accuracy claim is made.

At `2026-11-01T16:35:03Z`, fresh camera `/status` reported `Stopped`, revision 57, timeline `ad683a2f494b4b94a5981c7140d38a2f`, 32,706 completed frames, 139 clips, zero viewers and 65% battery. Pixel 11 Home visibly showed **Camera ready** and **Start live**. The saved status SHA-256 is `000729685a79449d4ab1cb39d0ab3b2dfbd1dd55a7aad52e533e94b37e91e3214`; the Home screenshot SHA-256 is `879bd4bf91db69a0fc77012fd801111f277a36fe045d021a606b0e02b7e6d5e0`.

This exploratory run does not pass D1, D2, or E. Keep the capture stopped. A same-light cue-side comparison requires new frames whose image patches match the paired control, or a separately declared lighting/camera-processing group.
