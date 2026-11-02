# Warm frame266 direct1000 timing screen — 2 November 2026

`O09-WARM-LAYOUT01-DIRECT1000-TIMING-SCREEN`: benchmark executed, narrow inventory parity PASS8/8; promotion HOLD. Isolated Vision Device Release build succeeded0 errors/26 warnings. Replay mode is opt-in ScreenOnly + TimingComparison: one equal warm-up per arm, four measured analyses per arm, alternating order, same saved PNG/receipt and calibration. No live capture or companion installation. Runtime includes saved-image decode and analysis; it is not live worker cadence or source freshness.

Current width800+fallback elapsed milliseconds:1873.8145,1691.5499,1841.4952,1749.8625; median1795.67885. Direct1000:1192.8815,1170.3909,1195.7055,1356.2681; median1194.2935. Median reduction33.49%. Four samples per arm cannot establish sustained p95 or a production latency bound. Both arms returned16 balls, clear uncertainty flag, in4/4 measured runs per arm. Numeric centre-label accuracy and full geometry equivalence are not yet scored. This is one rack Development frame, not full/reduced/negative coverage or D1/D2.

The opt-in harness runs separate analyzers with diagnostic audit flags disabled for the timed arms; the ordinary screen result remains separately generated. Production configuration is unchanged. Original live worker time and this saved-image benchmark have different endpoints and may not be treated as a before/after speedup claim.

Artifacts: `work/o09/development-v3/warm-batch-20261102/timing-screen/warm266timing-210413/`. Manifest binds source/receipt and installed isolated APK. Source hash is the reviewed frame266 hash. Next test: normalized geometry parity of all measured outputs, then broaden the same measured comparison to existing reviewed full/reduced/negative Development controls before any candidate decision. Do not repeat frame266 timing unchanged.

Metrics remain captured13/24; reviewed4/12; lighting0/2; D1/D2/E0/3. Camera verified Stopped revision61 before replay. No thresholds relaxed.

## Geometry parity follow-up

`O09-WARM266-DIRECT1000-GEOMETRY-PARITY`: PASS8/8 outputs,16 circle identities each. Comparing published normalized table2x1 centres/radii by ball ID against the first current800 output: maximum centre delta2.220446049250313e-16; maximum radius delta3.469446951953614e-18. Classifications and uncertain/layoutUncertain/obstructed flags agree throughout. Values are floating-point rescaling differences. This checks repeatability and cross-arm output equivalence on one saved frame, not reviewed-label accuracy. No replay rerun. Full rows retained in timing-screen/warm266timing-210413/geometry-parity.json.

Next target `O09-DIRECT1000-EXISTING-CONTROL-SCREEN`: preselect saved full/reduced and absent controls with exact receipts, then run the same bounded timing arms serially. Keep every changed detection and unsafe/negative output visible. Production width800 and fallback remain unchanged; no D1/D2 acceptance follows from parity.

## Timing harness provenance guard

O09-TIMING-SCREEN-PROVENANCE-GUARD PASS2/2 invalid-scope checks: TimingComparison without ScreenOnly and with multiple frames each fail before any device setup. New manifests explicitly record enabled timing comparison,1 warm-up/4 measured per arm,alternating order and saved-image wall-time endpoint. Earlier immutable manifests lacked this explicit object; their reports retain complete trial records, so they remain evidence with that limitation. No old manifest rewritten. Host-only guard change; no replay or Android rebuild needed.
