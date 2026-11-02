# Warm Layout01 SampleGap trace — 2 November 2026

Target `O09-WARM-LAYOUT01-SAMPLEGAP-TRACE`: rejection-path trace complete; latency cause unresolved.

The observed Home outcome was SampleGap after 1/5 samples for operation 9a3c2da3-3ac4-4bb4-a5b6-458a3a44a2a7. TableAnalysisVerification checks source PTS separation after acquisition and freshness/clock/source validation, before worker analysis or evidence capture for the rejected frame. ObservationPolicy.MaximumVerificationGapSeconds is 0.8. Thus the outcome establishes a source-time gap greater than 800 ms, provided the installed implementation matches the inspected path; the exact rejected gap is unavailable in the pulled receipt.

Only accepted frame266's receipt is retained locally. It records clock sample age16 ms, uncertainty50.709 ms, and vision elapsed655.909 ms. It does not store the rejected frame or the per-stage acquisition, cadence wait, evidence callback, total processing and source-gap breakdown. Vision elapsed alone cannot identify the source of the gap or be summed with unrelated timing values. No evidence proves storage, acquisition or analysis was the sole cause. No gate or threshold was changed and no capture repeated.

Next independent diagnostic action: retrieve the matching historical verification report through the existing read-only companion snapshot, if still retained. Match the full operation ID before using its rejected-frame timings. If no matching report survives, preserve this limitation and collect those existing telemetry fields during the next independently necessary capture; do not repeat this layout solely for timing.

Raw review provenance: 16 manual raw-image draft centres and crop sheet were generated before receipt inspection exposed its embedded detector output. The drafts remain unchanged and unaccepted. The user requested a new-tab view rather than approving centres. Browser policy blocked the local-file tab; a direct local image link was supplied. No accepted labels or radius values were written. The existing detector output is unreviewed and is not scored here.

Metrics unchanged: captured states13/24, reviewed anchors3/12, measured lighting0/2, inventory acceptance0/3, D1/D2/E0/3. Physical table closed for night. Last verified camera state Stopped revision61, clips139; no device action occurred in this offline trace.

Draft-centre SHA-256: `8b7982c7bd1197394b7368b3d8b40c83ddb159dcd5619619aae0163fb00a06dc`.
