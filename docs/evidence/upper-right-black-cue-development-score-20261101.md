# Upper-right black/cue Development replay — 1 November 2026

This report scores the user-reviewed four-step upper-right control group against the frozen Development corpus, then checks the same two replay arms across all ten v4 Development frames. It is a spatial still-image comparison. It does not pass D1, D2, or E.

## Frozen source set and review

Corpus `live-detect-20261101-black-cue-development-v4` contains ten real `StillRegression` frames in three Development setup groups: lower-left (678, 844), lower-right (1049, 461, 477, 375), and upper-right (697, 229, 206, 190). All remain Development; the split lock contains zero Holdout frames. The new upper-right four-step sequence stays together in `live-detect-20261101-upper-right-controls`. The user confirmed each single-ball change, hands-clear staging, the visible Y/R/C/B markers and expected empty black/cue locations, and an approximate ±3–4 source-pixel centre estimate. This is a visual uncertainty estimate, not a numeric label tolerance or a reviewed ball radius.

Upper-right labels bind to calibration `668fa30f00ef49bdad941348ee886152`, 1920×1080 source pixels and the copied receipt corners. The overlay maps detections back through the stored `SkyEye.Core.TableCalibration` matrix; its inverse round-trip error over the six visual-check frames is `6.92e-13` source pixels. The user's ±3–4 source-pixel estimate maps to at most `.00560261` table units over the 62 visible upper-right labels; this is uncertainty, not measured detector accuracy. Frame 229 has a measured brightness shift of unknown cause despite the user's report that physical lighting was unchanged, so it is excluded from matched-light claims. The user also clarified that one red ball moved before this group; that separate layout is fixed across the four upper-right controls.

## Controlled replay and scoring

Both arms use the same ten exact PNG/receipt pairs, device `66181FDDV001HS` (Pixel 11 Pro XL), isolated APK `uk.co.skyeye.visionchecks` SHA-256 `4e6de239d97ca98306ac1f2af2cc7b7e7d6589c194d6d4808775f0a4fa828d77`, and `screening` mode. Source checkout was HEAD `50c321ca7a999d1ce287b13f53a9148d2328e50f`; analyzer code is unchanged from `484f2c5`. Each one-frame replay retained the first measured analyzer call, used `independentFrames=false`, and was serialized across the shared Pixel. Every run manifest records camera `/status` `Stopped` before and after.

The baseline uses the captured width-800 packed-colour boundary search extent `.65` (`CaptureConfigurationExact=true`, 10/10 runs). Its recorded/replay analyzer configuration identity is `6D4E4F3E2FC28BBA1A56C637F6783E1D743E082D7807362324344A102FEFA414`. The candidate replays the same captured `.65` frames with only `VisionSettings.PackedColourBoundarySearchExtentFraction` changed to `.50` at width 800 (`CaptureConfigurationExact=false`, expected for this declared offline experiment); its replay identity is `3795ECFCE2E45324D17913440C4E56FE1C2E4FD25F12E9B1C26918276FA00F5D`, while the recorded identity remains the baseline identity. All other recorded/replay settings match after restoring that one candidate value to `.65`; all runs remain `screening`. Baseline and candidate source PNG and receipt hashes match for every frame. The paired comparison is therefore on identical pixels, not a claim that `.50` was captured live.

Workbench `baseline` used the predeclared `.010` table-2x1 matching tolerance separately for each arm. It scored all ten labelled samples with no unscored IDs:

| Frame | Control | Visible | Current `.65` TP/FP/miss | Candidate `.50` TP/FP/miss | Current p95 | Candidate p95 | Uncertainty `.65` / `.50` |
| ---: | --- | ---: | ---: | ---: | ---: | ---: | --- |
| 678 | Lower-left black+cue present | 16 | 15/1/1 | 15/1/1 | `.00823673` | `.00818877` | false / false |
| 844 | Lower-left black absent, cue present | 15 | 15/0/0 | 14/1/1 | `.00824324` | `.00819074` | true / true |
| 1049 | Lower-right black+cue present | 16 | 15/1/1 | 16/0/0 | `.00837616` | `.00832782` | false / false |
| 461 | Lower-right black absent, cue present | 15 | 15/0/0 | 15/0/0 | `.00541348` | `.00652929` | true / true |
| 477 | Lower-right black present, cue absent | 15 | 15/0/0 | 15/0/0 | `.00682183` | `.00666563` | true / true |
| 375 | Lower-right black+cue restored | 16 | 16/0/0 | 16/0/0 | `.00996385` | `.00996385` | false / false |
| 697 | Upper-right black+cue present | 16 | 15/1/1 | 16/0/0 | `.00965366` | `.00612271` | false / false |
| 229 | Upper-right black absent, cue present | 15 | 15/0/0 | 15/0/0 | `.00606170` | `.00606170` | true / true |
| 206 | Upper-right black present, cue absent | 15 | 15/0/0 | 15/0/0 | `.00878676` | `.00846391` | true / true |
| 190 | Upper-right black+cue restored | 16 | 16/0/0 | 16/0/0 | `.00823342` | `.00828416` | false / false |

Across all ten Development stills, current `.65` matched 152/155 visible labels (3 false positives, 3 misses); candidate `.50` matched 153/155 (2 false positives, 2 misses). Nearest-rank p95 matched-centre error was `.00823342` for `.65` and `.00818877` for `.50`; both exceed the frozen `.00675` geometry proposal. No radii were reviewed, so radius-normalized error is unavailable. Candidate `.50` corrects the reviewed lower-right Y3 miss in frame 1049 and the R5 miss in frame 697, while introducing a Y3 miss and extra yellow candidate in lower-left frame 844. Frame 678's pocket-black mismatch remains in both arms and is label-limited by its rough source-point uncertainty. Thus the full v4 result does not establish a no-regression candidate.

On the four upper-right frames alone, current `.65` matched 61/62 visible labels with one R5 miss/extra in frame 697; candidate `.50` matched all 62 with no other miss or extra. Both arms preserve the exact same broad uncertainty flags. Candidate detections contain no black in black-absent frame 229 and no cue in cue-absent frame 206. Candidate upper-right nearest-rank p95 is `.00741136`, still above `.00675`. The narrow upper-right spatial/absence subcriteria pass; the candidate is not promoted because the broader Development set contains the frame-844 regression and the required pocket-review clause is unavailable.

The upper-right analyzer-call wall times, in milliseconds, were current `.65` / candidate `.50`: frame 697 `780.214 / 1113.132`; 229 `1161.965 / 1013.132`; 206 `837.734 / 717.957`; 190 `824.437 / 826.280`. Across all ten frames, offline-time p50/p95 was `860.035 / 1161.965ms` for `.65` and `959.449 / 1286.156ms` for `.50`. These are single offline analyzer calls, not live request latency or source age, and do not support a performance claim.

No boundary/pocket audit was enabled in either arm (`boundaryAudit=false`); the paired captured receipts also contain no boundary-audit records. Pocket-review status is therefore unavailable, not passed. The controls still confirm the expected visible/absent inventory labels at the reviewed points.

## Images

The first sheet compares current `.65` and candidate `.50` detections against the reviewed source centres. Green rings are reviewed visible centres, orange dashed rings are expected empty locations, cyan marks current `.65`, and magenta marks candidate `.50`.

![Upper-right Development replay comparison](../../work/o08/live-detect-black-pair-v1/physical-controls/visuals/upper-right-v4-current-065-vs-candidate-050-source-overlay.png)

The second sheet shows the cross-layout Y3 tradeoff: `.50` misses the reviewed Y3 point in frame 844 but matches it in frame 1049.

![Cross-layout Y3 comparison](../../work/o08/live-detect-black-pair-v1/physical-controls/visuals/cross-layout-y3-v4-current-065-vs-candidate-050.png)

## D1, D2 and E status

- **D1 — not passed.** The v4 validator reports ten real still regressions, zero timed-shot sequences, zero verification sequences, zero Holdout samples and `accuracyGateReady=false`. Three correlated setup groups do not meet the frozen held-out layout/session/light coverage. Both replay arms exceed the `.00675` centre-error proposal, and the `.50` all-Development result has a new Y3 miss/extra on frame 844.
- **D2 — not run to acceptance.** A fresh read-only package pull confirms Production companion `0.8.59` / code67, APK SHA-256 `0751393c12afbbad838522c9a69f2a8ea8c97369ae794b3fc47c2c248d9e1a55`, source `1c19f1c`. The installed isolated VisionChecks APK remains the `.50` replay package above. The companion `MainActivity` is foreground, and its fresh accessibility tree reports **Camera ready**, **Start live session**, and Live Detect unavailable while stopped. Two fresh ADB screenshots show the app area black outside the status/navigation bars, so visible Home rendering is unverified; no control was tapped. The hierarchy also retains the old `Unsettled (SampleGap)` and `Sample saved · 31.4s clip` messages; these are historical, not current readiness. No live freshness, 30-second stationary verification, stale-source, obstruction or false-Ready windows were measured on the installed build.
- **E — not started.** No shot/event/clip sequence was attempted. The mounted-camera clip count remains 139, and the physical controls are still-image spatial evidence only.

## Artifact binding and device state

| Artifact | SHA-256 |
| --- | --- |
| v4 manifest | `7ad75f41c3f452d7fd71685c363ea334eb129d2fafc2d557ea27998a07edb277` |
| v4 split lock (freeze output byte-matches saved lock) | `49b3c3bcc01fdea4d0cfc6989e3f70814077e8db3a762921739a5d941a3bfd2e` |
| v4 acceptance contract (copied frozen contract) | `1574245e516431ea3087c9202875d4b26c17701f4d424457f722f987ead47563` |
| v4 validator output | `d252f35a139b3132e7e6ea874ba6b0d2081cff4bcd186e8d4bde647a4c2833ec` |
| source-to-table transform | `874428e8cddeff2833402dcddadbdade2a181153b40b71a17b7bfd6765821584` |
| current `.65` merged export manifest / detections / score | `448ee331c93b4023cdc179a81424f37ff3da382cefb6993ceb9b279a5ebb13ca` / `8c3bf68cced3aee9c76ff90fcf7cbe31a6608e17907038db75c66589abf2fabe` / `ce616897fe4d1be9523c2a2c33dfe3cbb29683415edb6e4ec7dabc0c4f4f118c` |
| candidate `.50` merged export manifest / detections / score | `7068af995203683210b492e115c5bb25cbcb1d84a0450a0352cc4f30a97b8081` / `d9ef9b8cdb2af74ba70c2a4fcdd44c0342c6114ec373bcb0a1a248ce8b53d704` / `c4131bf707540c1534dc9e212869294a516fb7291f2fd901bbc2d01e752cc68f` |
| upper-right reviewed-centre record | `7e310b0b1b48b0ae83e9b54c58ccada84742e9fb52eebbb0fd274eef0ace3dd1` |
| upper-right capture record | `8f7b3c352720c1c141c9774afe0d36edab5f7719380afccc3ff861522dcf7bb9` |
| upper-right photometric check | `7b19c928512a3c9928540047a6a2edb7b3414fbd03e5d67b9ec852ac83cfaa3b` |
| upper-right replay comparison image | `85873aef739f4d199fbaa58075417f476af5db44b2f781dbccf0f4a74abac3ed` |
| cross-layout Y3 comparison image | `92514311a8f9c167326522ddd99aab6085e2eec527490a2b141edf9ec6256b90` |
| final token-free camera status / installed-package readback JSON | `8ec97b3fbc3b596d8709c5eb1f01afa16fd5887859e2d45c8fc33566a635a3c3` / `e98ffe46ae4ea90f91592f8522b7149285ad04307f6cae9117a38e9c5d3087e4` |
| final Pixel screenshot / fresh UI hierarchy | `609f5cd8abe7842b1eed9fc289464e8cca9749ff85a0b6d45601564b7adb5ba1` / `673a27f992eca67149a9b96a18d5a55899423064b0f54cb14d17036a8893c5f6` |

Fresh mounted-camera `/status` at `2026-11-01T12:29:42Z` reported `Stopped`, revision 37, timeline `96142ab0f1ef4127a81e4f4ebad79126`, 22,028 completed frames, 139 clips, zero viewers and 100% battery. The host field reported Pixel 11 Pro XL generation 4 with `expiresAt=12:30:09Z`; no release action was requested or issued. The companion `MainActivity` was foreground and its accessibility tree reported **Start live session**, but two fresh screenshots showed a black app surface; the visible Home state is not claimed. The pulled screenshot SHA-256 is `609f5cd8abe7842b1eed9fc289464e8cca9749ff85a0b6d45601564b7adb5ba1`; hierarchy SHA-256 is `673a27f992eca67149a9b96a18d5a55899423064b0f54cb14d17036a8893c5f6`. No capture, Production companion or camera-package update, Cast/TV action or clip deletion occurred. The serialized replay runner used `adb install -r` to reinstall the same isolated VisionChecks APK for each saved-frame screen; its installed hash still matches the recorded `4e6de2…` APK.

The corpus validator and freeze verification were rerun. Validation returned ten StillRegression sequences, zero real shots, zero verification sequences, zero Holdout and `accuracyGateReady=false`; the new freeze result exactly matches the saved split lock. No tests or detector-source changes were made. Production settings remain `.65`.

**Next:** add a distinct cushion/rail Development group with a matched black/cue present/absent sequence and a nearby chalk/shadow negative. Keep the current `.50` run as an archived experimental arm, trace the frame-844 Y3 regression before any further geometry adjustment, then obtain the required independent Holdout groups only after Development labels/configuration are frozen. Keep capture stopped between all table changes and while offline work runs.
