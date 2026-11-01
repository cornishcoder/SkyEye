# Frame 978 Development corpus, version 1

This is a one-frame, black-absent Development baseline fixture. It is not a matched control, Holdout, detector acceptance result, or physical-centre accuracy proof. The only accepted corpus import is corpus-v1; an earlier partial importer attempt is retained separately in corpus/ as an unsuccessful work trace.

## Source and review

- Source frame: corpus-v1/978/978.png, 1920×1080, source frame ID 978, PTS 48,922,017,388µs, timeline 2ea48e0c35cd474eb627acf970a7eec5, generation 3.
- Source PNG SHA-256: b368348c63d924f5c7834e8e849b7287225ab525b470bdc3b85f7a8278cbbe70.
- Raw source receipt SHA-256: 653e1ea4bc6c927ba592ad796a6c084e49103c27003d13e5d23d0c98a3144034.
- User review record SHA-256: 4f8a2c1139b6e007e4a4789b1e6a9592b4d880d042772a709b36b4ff210b2fc1.
- The user confirmed all 15 displayed assistant-draft centres: cue 1, red 7, yellow 7, black absent. Taom chalk is excluded. Draft uncertainty remains roughly 4–7 source pixels; the review was conditioned on the displayed draft and set no numeric tolerance.
- A stable corpus-local sampleId (code65-frame-978-v1) was added in the derived receipt because the raw receipt did not contain one. It does not replace frame ID, PTS, timeline, generation, calibration ID, or source hash. Derived receipt SHA-256: 12910e79eaf9d5189a7c31bd572bbe9fd5636d6ca905e39da1d58746b5b10b52.

## Transform and uncertainty

The source-image coordinates use x right/y down and are normalized by 1920×1080. Ordered corners are top-left, top-right, bottom-right, bottom-left; the existing SkyEye.Core.TableCalibration.Map maps them to table-2x1 coordinates (0,0), (2,0), (2,1), (0,1). The exact matrix, source labels, mapped labels, inverse round-trip results, and calibration provenance are in source/source-to-table-transform.json (SHA-256 e0677e9ddd2f1108c5480e3f9334f2d2835dddabacb306a1c6e378b2f50f0d89). Round-trip points are in source/source-roundtrip-points.json (SHA-256 9e8cfe87df7839dfd0e454ccc4ba63762664e4313a7061257cb6ebc2ced7179a); maximum round-trip error is 3.41e-13 source pixels.

The corners were copied from Code64 source receipt 715 on the same fixed mount. Code65 retained its calibration ID but not its corners, so geometry-derived scores remain diagnostic. The 128-sample transformed uncertainty envelope reaches 0.0097943 table units. The proposed 0.25R accuracy bound is 0.00675 (R=0.027), so the current rough review uncertainty is too large to certify that bound. The overlay at source/source-to-table-overlay.png checks label mapping only; it is not detector output or physical cloth-plane ground truth.

## Imported corpus and frozen contract

- Corpus ID: o08-code65-frame978-development-v1.
- Sequence: code65-frame978-v1, split Development, purpose StillRegression.
- Manifest SHA-256: 09d383902f0cd2c271a555f1ed41be7016d958dd7d251eb2b5b0affbb4366950.
- Frozen split lock SHA-256: 49f8e735f49669b860cd0447a9db53fc4292b1194d957a76246b4504a2f93e76.
- Frozen acceptance-contract SHA-256: 1574245e516431ea3087c9202875d4b26c17701f4d424457f722f987ead47563.
- Imported index SHA-256: 21539c943d635e92ef802ca0ebab83614446a560e5b85f88e915ca3fefc92723.
- Imported labels SHA-256: 10276ef090853643e6be8167afe05299120e8526d33c3cdefe8e4ef7fcad20022.
- The O08 workbench validated one real StillRegression sample, zero Holdout samples, and accuracyGateReady=false.
- acceptance-contract.json is frozen before tuning/Holdout evaluation. Its 0.010 frame 978 matching tolerance is only for the Development baseline and is not the 0.25R acceptance accuracy limit.

The frame 978 baseline milestone is complete; see [the hash-bound two-arm score](../../../docs/evidence/frame978-screening-baseline-20261101.md). Current-source width-800 `.65` and captured-settings `.4` are separate exports and Development scores at explicit tolerance `.010`. Each scored the one reviewed sample 15/15 with zero false positives/misses and an empty unscored list. The `.4` arm matches the recorded analyzer profile; `.65` is deliberately a packed-colour configuration delta. Both outputs remain `uncertain=true`, the corpus has no Holdout, and review uncertainty is too large to certify the proposed `0.25R` bound.

## Current device and replay gate

Pixel 11 ADB is reachable as 66181FDDV001HS; installed Production package `uk.co.skyeye.companion` remains version 0.8.59 / code67. A Release build from `1c19f1c` was installed in place for timing diagnosis; installed APK SHA-256 is `872f3494843be309b147f13bf56d238f59097a9dc21cbb02abf9f90eac66c187`, UID/private storage were retained, and the previous APK is `work/production/backups/20261101-102028/before.apk`. The mounted-camera package was not updated.

After that install, one Home **Start live** action started camera timeline `4c1802ca0c294b0bbd9ec950bed670ff`, but the native player decoded zero frames, the camera reported zero viewers, and **Capture verifier frames** was disabled. No verifier pixels or receipts were saved. The Home **End recording session** action stopped the attempt; `/status` is `Stopped`, revision15, 8,642 completed frames, `clipCount=139`, zero viewers. [The after-stop screenshot](device-state/pixel11-after-instrumented-prerequisite-stop-20261101.png) shows **Start live**, Live Detect off and Cast disconnected. The earlier cue-restored screenshot remains available at `device-state/pixel11-cue-restored-after-stop.png`.

The isolated replay, compatible screening exports and one-frame Development scores are retained under `device-replays/`; they prove stored-result scoring only. The four physical controls are separate from this corpus and are indexed in [the attempt report](../../../docs/evidence/live-detect-bounded-control-attempts-20261101.md). A raw-only approximate centre draft is preserved at `physical-controls/raw-only-review-draft-20261101.json`, with the [full-table review sheet](physical-controls/review-contact-sheet-20261101.png), [lower-left crops](physical-controls/lower-left-review-crops-20261101.png), and [separate user-reviewed label record](physical-controls/user-reviewed-centres-20261101.json). The original raw-only draft stays unchanged; review confirms approximate points without setting a numeric tolerance. The [photometric ROI report](physical-controls/photometric-roi-check-20261101.json) records a frame-455 chroma shift. Source commit `1c19f1c` adds per-phase accepted-frame timings and pre-analysis rejected-frame timing; focused `SkyEye.Checks` pass. The installed package includes this diagnostic code, but the player received zero frames and the camera reported zero viewers, so no instrumented verifier result exists. Diagnose the phone stream path and decoder source while capture is stopped before another live attempt. Do not change the 800ms guard from these runs. The scorer and controls do not establish held-out accuracy, readiness, D2, or D1/D2/E.

## Latest phone stream load — 1 November

After installing `438696d` with Media3 load diagnostics, one Home **Start live** action produced timeline `c1a726f97fde4f119c8ce8a1da26f4e6`, one server viewer, and the visible table image in `device-state/pixel11-stream-metrics-active-20261101.png`. The redacted load event completed with 137,435,896 bytes over 173,187ms. Home **End recording session** stopped the run; final status is `Stopped`, revision 19, 13,836 frames, 139 clips, zero viewers. Home shows **Start live session**, Live Detect off. The approximately 173-second session exceeded the intended short diagnostic. No verifier frame or clip was created. This run confirms a successful restarted stream but does not diagnose the previous zero-viewer result; see the [detailed record](../../../docs/evidence/phone-stream-load-diagnostic-20261101.md).

## Separate lower-left black present/absent Development pair — 1 November

Frames 678 (black present, cue present) and 844 (black absent, cue present) were imported into a separate `../live-detect-black-pair-v1/corpus-v2/` using receipt-bound source-pixel/table-2x1 mapping, then scored from current-source width-800 `.65` exact-configuration exports. At explicit Development tolerance `.010`, frame 844 scored 15/15 and frame 678 scored 15/16: the black centre and prediction differ by `.010999613` table units, while the approximate reviewed point has `+/-15–20 px` uncertainty and is about 7.85 source pixels from the candidate. This is a label-limited spatial mismatch, not a confirmed detector miss. The pair is separate from this frame-978 corpus, remains one matched Development setup group with zero Holdout samples, and made no detector-threshold change. See the [hash-bound score report](../../../docs/evidence/lower-left-black-pair-development-score-20261101.md). A fresh post-replay status read at 10:58:04 UTC confirmed `Stopped`, revision 19, 13,836 completed frames, 139 clips and zero viewers; [the redacted status](device-state/pixel10-status-current-safe-20261101.json) and [current Home screenshot](device-state/pixel11-current-home-recheck-20261101.png) remain in this directory.

## Next evidence

The user confirmed suitable lighting for the 1 November lower-left sequence. All four single-ball control states were attempted, but verifier timing prevented a settled five-sample result. The user confirmed the displayed centres and black-absence point; the lower-left black/pocket estimate remains low confidence and no numeric tolerance is set. Frame 455 has similar cloth-patch luminance but a consistent red-down/blue-up shift, so image-colour matching remains unresolved. One restarted companion session has now decoded/rendered the stream, though the prior zero-viewer startup remains unexplained and the successful diagnostic ran about 173 seconds. The current camera status is `Stopped` and Pixel 11 Home shows **Start live session**. After the next hands-clear lower-right pocket group is staged, capture the bounded matched controls with capture stopped between layout changes; then collect other distinct Development groups and independent Holdout groups with frozen assignments and reviewed centres. Keep every restart and layout change in its entire setup group on one split side. The full acceptance coverage, D1/D2/E requirements, denominators, timing meanings, phone scope, and activity list are frozen in corpus-v1/acceptance-contract.json.
