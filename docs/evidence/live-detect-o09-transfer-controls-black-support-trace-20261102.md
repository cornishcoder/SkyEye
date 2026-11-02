# O09 transfer-control black support and seed trace — 2 November 2026

## Result

**`O09-TRANSFER-NEW-CONTROLS-BLACK-SUPPORT-TRACE`: diagnostics complete 3/3; baseline black recovery remains 0/3.** Current-source, receipt-matched full-audit traces were collected for reviewed black-present frames 288, 1691 and 2831. All three still returned 15/16 balls with B absent and `uncertain=true` at width 800. The traces identify two related gates: (a) boundary support rejects seeds near B in frames 288 and 2831; (b) default seed enumeration excludes the label-near peaks in frame 1691 before ranking.

| Frame | Seed evidence near mapped reviewed B | Current decision | Output |
|---:|---|---|---|
| 288 | Peaks at `(755,1164)` and `(757,1207.5)`, 22.5 and 26.0 analysis pixels from the mapped point; both seed probes classified black | Rejected at 18/32 and 19/32 supported sectors, below the configured 21/32 minimum | 15 balls; B absent; uncertain |
| 1691 | Default trace has no fitted seed near B. The dark-mask trace finds local qualified peaks at component indices 133–135, at `(737,1205)`, `(760,1208)` and `(748,1158)`, with peak radii 12.42, 9.96 and 20.54 px | The default component-index cap is 64, so these later components do not reach ranking. The isolated all-seed arm puts `(748,1158)` at rank 12; its fit has 18/32 support and is rejected | 15 balls in both default and all-seed outputs; B absent; uncertain |
| 2831 | Peaks at `(758,1207.5)`, `(735,1186.5)`, `(735,1184)` and `(736,1190)`, 26.5, 10.5, 10.2 and 10.9 analysis pixels from the mapped point; all four seed-centered probes classified black | Three fits are rejected at 18/32–19/32 support; one nearby fit is rejected by the final classification check | 15 balls; B absent; uncertain |

Distances are in the width-800 analysis grid after applying the receipt-bound calibration transform. They are trace-targeting measurements, not a numeric bound on the user-reviewed labels. The source labels remain unchanged, and no numeric centre-review uncertainty was assigned. Peak radius is the dark-mask distance-transform radius, not a reviewed ball radius.

## Isolated seed-enumeration ablation

**`O09-TRANSFER-NEW-CONTROLS-BLACK-SEED-COVERAGE-1691`: PASS, label-local seed coverage traced.** The width-800 dark-mask trace uses the independently reviewed B point `(1224,152)` in source pixels as an evaluation-only anchor, mapped to `(746.1,1185.2)`. It records 134 minimum-radius-qualified local peaks and a hard component-index limit of 64. Three qualified peaks near the anchor have component indices 133–135; the production sparse-black enumeration stops before them. In the recorded expected-radius region, 1,186 of 1,463 pixels survive the dark threshold/opening, and the maximum distance-transform radius is 17.23 px against an expected analysis radius of 21.6 px. These values describe the trace, not a new classifier threshold.

**`O09-TRANSFER-NEW-CONTROLS-BLACK-SEED-ENUMERATION-ABLATION`: no recovery, 0/1.** A single diagnostic run changed only `SparseBlackEnumerateAllSeeds=true`; radius-first ranking and the 12-fit cap remained unchanged. It expanded the reported seed pool from 38 to 108 at width 800 (111 at width 1000). The reviewed-point candidate reached rank 12, but its fit remained below the boundary-support requirement at 18/32. The complete ball and circle outputs, counts and uncertainty were byte-for-byte equal after JSON serialization to the default run at both widths. This isolates the seed-coverage effect on this frame; it is not a detector candidate or a safety/accuracy result, and the ablation was not run on negative frames.

The analyzer setting identity is an intentional mismatch for this diagnostic arm (`configurationMatchesPrimaryWidth=false`, `configurationSettingsMatchPrimaryWidth=true`) because the single predeclared diagnostic ablation is included in the identity. It is not scored with the seven-frame baseline and is not represented as a receipt-exact run. The 3-frame support traces have both configuration match flags true; `audit=1` only enables telemetry. No production code, detector threshold or live state changed.

## Provenance and artifacts

All runs used the installed isolated `uk.co.skyeye.visionchecks` Release APK SHA-256 `e405c1cb1d3dce9d58eb53eab314229d7bd8728136242867eb6627f2caa44e62`, source checkout `da161891c8a0f813249994f912428f3efd8f9b09` including fix `432c0a13142173eb7774f451ef714cc15717ec91`, and the same reviewed source frames as [the Pixel baseline report](live-detect-o09-transfer-controls-pixel-baseline-replay-20261102.md). Width 800, extent `.65`, radius-first ranking and other receipt settings were held fixed for the support traces. Runs were serial and used saved frames only.

| Frame | Pixel SHA-256 | Receipt SHA-256 | Diagnostic report SHA-256 |
|---:|---|---|---|
| 288 | `815fbaeef9224ee3dcf457be27260256fd1d0ec52875beb9b39c9ed92ef99474` | `3cca9a9102f93ca63d51cbcee3b73e5c741e79e9cb24216beb42919eb045a384` | `25421ab061d8bf51bc2193c37fc93f6161e8c71604d1b0fc0d0eba23615bf600` |
| 1691 | `fc1ad7873fa6e35d539b6b28f5fafd1fa7840691221db6cbe7408b29e1e58e3d` | `4b814a03414a002afdda1f742e61bad425d4bfa7b85e1981a07f483569f3914b` | `f4e8f7c9c6facb8c96ac6a590b6b2c28d032b96f91e5d784b6f37859e4111558` |
| 2831 | `d59148155e7c45c1a3b1d6e8804e95b5e5375b61fded7f82efcc5a4637b1eb6f` | `4142fc835b5bfbba64b67c45f9a2b872ef7029e92784b04cd3f6469256a7aa09` | `3d5259373c9077ec01cdc97d8cc6c913e7d08d1d58af0d9e1fe568318dbb9277` |

- 1691 reviewed-label black trace report: `work/o09/transfer-new-controls-pixel-baseline-replay-20261102/runs-diagnostic/1691-coverage/o08-replay-proof-xfer1691coverage-20261102-blacktrace.json`, SHA-256 `5fbc2cc2bcb36505b21e4c06b31b96118c0517635f47dd7c9f257cf72c96e536`.
- 1691 all-seed diagnostic report: `work/o09/transfer-new-controls-pixel-baseline-replay-20261102/runs-diagnostic/1691-allseed/o08-replay-proof-xfer1691allseed-20261102.json`, SHA-256 `fe09a5fdecb14a31ccc298d6371a7a37633dc2cb08cfcd8ad90e79d3ac41ecd9`.
- 1691 trace overlays: `work/o09/transfer-new-controls-pixel-baseline-replay-20261102/runs-diagnostic/1691-coverage/1691-800-dark-mask-overlay.png` (SHA-256 `969f7e7286ab84c516d48a9dc4e226581acccad9e43749491bc0e4c87e7c85cf`) and `1691-800-distance-window.png` (SHA-256 `ef8eff6bda802de999e68afedaf7a00f5a89795bbbfbdcab80622b767b8624aa`).
- Token-free post-trace device readback: `work/o09/device-state/transfer-controls-black-trace-postcheck-20261102.json` (SHA-256 `2120c805c9815080f7fd75f978dae083dac563987bd362f3b5b517266e9aa0ca`).

The first report pull checked the input subdirectory, while the instrumentation writes its report in the isolated package's external-files root; the report was located and pulled without rerunning the frame. The black-trace images were stored one level below the run directory and were pulled after locating that subdirectory; no replay was repeated. One local PowerShell summary-format command failed, then the retained JSON was read with a corrected formatter. These retrieval/formatting errors did not alter test inputs or results.

## Device state and gates

At `2026-11-02T22:37:49.135034Z`, camera `/status` was **Stopped**, revision62, timeline `f5d2618b269749e780a00f2550ba42ad`, 58,052 completed frames, `clipCount=139`, zero viewers and no host. Battery was 100%. A fresh Pixel Home accessibility readback showed **Start live session**, **Sample collection off**, and Live Detect unavailable while stopped. The visible last verifier message was historical `Unsettled (SampleGap)` at 20:00:53 UTC. No capture or clip was created. The isolated verification package stored new replay inputs and reports under unique external-files run folders; production companion data was not changed or cleared. No Cast/TV interaction, production companion update or mounted-camera update occurred.

This work narrows the black-miss mechanism but does not change the accepted baseline (106/109 spatial matches, 0 FP, 3 FN; inventory complete 4/7), add a negative-control ablation, or meet stillness/readiness. D1/D2/E remain 0/3 proven; D2 remains 0/200 supported request windows, 0/100 held-out unsafe windows and 0 soak runs; lighting strata remain 0/2. Next offline check: **`O09-CALIBRATION-RECONNECT-PRESERVATION-CHECK`** before another matched physical batch. Next physical collection remains **`O09-DEVELOPMENT-REMAINDER-BATCH`** when the table and stable playable warm light are available.
