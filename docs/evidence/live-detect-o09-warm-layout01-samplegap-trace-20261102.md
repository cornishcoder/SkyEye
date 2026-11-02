# Warm Layout01 SampleGap trace — 2 November 2026

Target `O09-WARM-LAYOUT01-SAMPLEGAP-TRACE`: rejection-path trace complete; latency cause unresolved.

The observed Home outcome was SampleGap after 1/5 samples for operation 9a3c2da3-3ac4-4bb4-a5b6-458a3a44a2a7. TableAnalysisVerification checks source PTS separation after acquisition and freshness/clock/source validation, before worker analysis or evidence capture for the rejected frame. ObservationPolicy.MaximumVerificationGapSeconds is 0.8. Thus the outcome establishes a source-time gap greater than 800 ms, provided the installed implementation matches the inspected path; the exact rejected gap is unavailable in the pulled receipt.

Only accepted frame266's receipt is retained locally. It records clock sample age16 ms, uncertainty50.709 ms, and vision elapsed655.909 ms. It does not store the rejected frame or the per-stage acquisition, cadence wait, evidence callback, total processing and source-gap breakdown. Vision elapsed alone cannot identify the source of the gap or be summed with unrelated timing values. No evidence proves storage, acquisition or analysis was the sole cause. No gate or threshold was changed and no capture repeated.

Next independent diagnostic action: retrieve the matching historical verification report through the existing read-only companion snapshot, if still retained. Match the full operation ID before using its rejected-frame timings. If no matching report survives, preserve this limitation and collect those existing telemetry fields during the next independently necessary capture; do not repeat this layout solely for timing.

Raw review provenance: 16 manual raw-image draft centres and crop sheet were generated before receipt inspection exposed its embedded detector output. The drafts remain unchanged and unaccepted. The user requested a new-tab view rather than approving centres. Browser policy blocked the local-file tab; a direct local image link was supplied. No accepted labels or radius values were written. The existing detector output is unreviewed and is not scored here.

Metrics unchanged: captured states13/24, reviewed anchors3/12, measured lighting0/2, inventory acceptance0/3, D1/D2/E0/3. Physical table closed for night. Last verified camera state Stopped revision61, clips139; no device action occurred in this offline trace.

Draft-centre SHA-256: `8b7982c7bd1197394b7368b3d8b40c83ddb159dcd5619619aae0163fb00a06dc`.

## Matching historical timing readback

`O09-WARM-LAYOUT01-HISTORICAL-TIMING-READBACK`: PASS matching operation/source identities and exact source-gap arithmetic. Existing dashboard port8770 was read through temporary ADB forward18770, without starting capture. Full report is retained under warm-batch-20261102/historical-verification-readback.json; historical=true, isCurrent=false.

Accepted frame266 source PTS34661355877; rejected frame290 PTS34662162944; delta807.067 ms exceeds the800 ms limit by7.067 ms. Accepted acquisition13.2855 ms, processing789.3193 ms, worker781.8095 ms, evidence callback3.2277 ms, review encoding0 ms. First clock refresh130.9315 ms occurred before the first accepted source sample and must not be added to the subsequent source-gap budget. Rejected acquisition6.4694 ms, polling0.4511 ms, cadence wait0 ms, clock refresh0 ms. Rejected frame age474.0812 ms; clock sample age813 ms; uncertainty50.709 ms. The saved first frame's detector elapsed655.909 ms is nested inside worker time, not an additional stage to sum.

One attempt indicates worker analysis consumes most of the source-gap margin; it does not isolate which worker stage dominates or prove a sustained bottleneck. The3.2277 ms callback does not measure asynchronous compression/write completion. Do not attribute the gap to PNG compression from this report or relax the800 ms gate. Next offline diagnostic: inspect the receipt-bound worker configuration and existing per-stage diagnostics for an avoidable repeated pass, without rerunning the known-failing capture. Detector output on this single frame is16/16, uncertainty=false; it remains unscored spatial output and not a readiness pass.

Counts unchanged: captured13/24; reviewed4/12; lighting0/2; D1/D2/E0/3. No new capture, inference or test-suite run.

Historical report SHA-256: `a270abf9c19d0925d1389d2fefa4667ab83048635cd4c583ea68d88bb9aa3209`.

## Receipt-bound worker-stage trace

`O09-WARM-LAYOUT01-WORKER-STAGE-TRACE`: PASS branch identification. Receipt configuration is width800 with packed-colour extent0.65. Its packedClusterFallback records primary width800, incomplete cluster,15 balls,312.9029 ms; fallback width1000,16 balls,343.0061 ms, recovered=true. Sum655.909 ms matches reported detector elapsed exactly. Worker781.8095 ms leaves125.9005 ms outside reported detector elapsed; this remainder is not individually profiled. Source path AnalyzePixels runs AnalyzePixelsCore twice when the primary packed cluster is incomplete, then preserves normalized circle coordinates via rescaling. VerificationWorker also validates geometry on its first frame and creates motion pixels; those are required tasks, not proven redundant work.

The second pass recovered an actual missing detector output. Removing it without replacement would trade accuracy for speed. No source change is justified from this single observation. Next measured experiment: exact saved-frame direct-width1000 versus current width800-plus-fallback, on the same Pixel runtime with matched warm-up and output comparison; include both timing endpoints, preserve current production configuration and all gates. A positive timing screen requires additional full/reduced and negative controls before promotion. Existing historical mixed-batch timings must not substitute for the matched run. No replay or timing benchmark has run in this trace.
