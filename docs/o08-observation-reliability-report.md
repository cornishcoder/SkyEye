# O08 implementation and evidence

7 October 2026. Baseline `5453d0b`. Physical reliability acceptance remains open.

## Baseline and policy audit (O08.01)

Legacy observations keep their single-frame contract: fifteen-second maximum observation gap, two-second settlement span, six-second confirmation/correction/advice freshness, fifteen-second catalogue lifetime. `ObservationPolicy` names these independently. Legacy settlement compares colour/count and fixed spatial anchors; telemetry now reports spatial compatibility separately from label compatibility. It does not call recognition variation physical motion.

Recognition telemetry preserves raw/final counts, the processing branch, gains, cloth hue, pre-inventory labels and warnings. Physical obstruction and ambiguous detection have separate reason/quality fields, with conservative legacy `Unclear` compatibility. No detector threshold or inventory behavior is tuned by this change.

O01 schema version 1 gains explicit `StillVerification` and `NegativeVerification` purposes. Reviewed real holdouts require source timing, increasing scoped receipts, frame labels and confirmed condition intervals. Existing shot counts and accuracy readiness stay separate; recording-session and source-hash split fences remain. Contract tests exercise the schema; their constructed metadata is not a real corpus.

Provisional burst configuration: two-second minimum source span, five to six samples, 0.5-second cadence, 0.8-second maximum gap, four-second deadline, 1.5-second final age and 2.5-second maximum source-clock sample age. These are conservative implementation limits from the plan, **not measured supported-condition guarantees**. Mounted resolution, readback cost and clock uncertainty must be measured before normal delivery.

The connected Pixel is `66181FDDV001HS` (Pixel 11 Pro XL). A read-only camera status check reported active capture and advancing completed-frame/source counters. This establishes connectivity only. No new labelled mounted sequences, frozen development/holdout split or baseline request-rate denominator is available yet. Existing live HUD evidence is presentation evidence and cannot substitute for detector truth.

## Acceptance still required

Freeze a real corpus with blind session holdouts, annotate centres/counts/colours before seeing detector results, run branch/consensus ablations, and report all proposed O08 denominators and unsafe cases. Measure acquisition/native allocations/playback at actual source resolution with phone and corrected TV. Normal installation follows O08.08 acceptance using the existing guarded installer; no normal Release update is authorized by a passing synthetic check alone. The broader O01/O02.05/T13/T14 and V03/V04 gates remain open.
