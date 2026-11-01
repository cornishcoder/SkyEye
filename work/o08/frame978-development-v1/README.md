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
- Imported index SHA-256: 21539c943d635e92ef802ca0ebab83614446a560e5b85f88e915ca3fefc92723.
- Imported labels SHA-256: 10276ef090853643e6be8167afe05299120e8526d33c3cdefe8e4ef7fcad20022.
- The O08 workbench validated one real StillRegression sample, zero Holdout samples, and accuracyGateReady=false.
- acceptance-contract.json is frozen before tuning/Holdout evaluation. Its 0.010 frame 978 matching tolerance is only for the Development baseline and is not the 0.25R acceptance accuracy limit.

No detector export, baseline score, or unscored-sample report exists yet. The score must enumerate the sample IDs the scorer could not score. Replay output must match report mode screening; the source receipt's historical captured configuration is width 800 with packed-colour boundary extent 0.4, while current source initializes extent 0.65. These are separate arms. The complete receipt configuration, including radius-first sparse-black ranking, must be explicit for historical reproduction.

## Current device and replay gate

Pixel 11 ADB is reachable as 66181FDDV001HS; installed Production package uk.co.skyeye.companion is version 0.8.59 / code 67, with installed base APK SHA-256 76f4b22037df10679fc07a2200dfbcb8d26499046cdd53e809974ff150b71720. This is a read-only installation identity check, not acceptance for current source. The companion is on Home with “Connect your camera”, “CAM —”, “Start live” (off), and Cast disconnected. Current camera /status timed out, therefore capture state is unverified, not Stopped. No capture was started, and no Cast/TV action occurred.

Do not build, install, or replay until /status reports Stopped and Home shows “Start live session”. Then build the isolated Vision Device replay APK from the tested source, run frame 978 serially on the Pixel, produce the compatible screening export, and score the one-frame Development manifest with the explicit 0.010 tolerance. Keep the exact historical 0.4 reproduction and current-source 0.65 baseline separate; record APK/configuration hashes, device, warm-up, batch size, source PTS, and analyzer-to-observation mapping. The scorer reports stored-result quality; it does not itself establish physical accuracy, readiness, Holdout performance, or D1/D2/E.

## Next evidence

After the baseline milestone, establish suitable playable lighting before asking for any bounded live evidence. Collect the matched same-camera/same-light visible-black and black-absent pair and cue-present/cue-absent pair, with non-target balls unchanged, at the same pocket and calibration. Keep each recording/setup group entirely in Development or Holdout, review centres and absence locations before scoring, and stop capture at the first safe boundary with current /status and Home readback. The full acceptance coverage, D1/D2/E requirements, denominators, timing meanings, phone scope, and activity list are frozen in corpus-v1/acceptance-contract.json.
