# O08 remaining issues and repair plan

7 October 2026. Original audit of `752b27d` and retained real development captures. The findings below preserve the pre-repair diagnosis. The subsequent v8 repairs and their results are recorded in [the implementation report](o08-observation-reliability-report.md#repair-follow-up-7-october-2026): geometry, reference recovery, body motion coverage, typed failures and request-window scoring are implemented. The six-frame native development replay now gives six clear detections and 16-ball consensus. Live playback stability, reviewed geometry and physical acceptance remain open.

The main blockers are unstable ball geometry, geometry-dependent black/cue classification, and insufficient motion evidence. Image resolution alone does not explain the observed failures. Source acquisition and the evaluation harness also need work. The fixes below retain the existing C# vision pipeline, transient verification owner and canonical GameState boundary.

## Confirmed findings

### 1. The motion verifier can label moving pixels still

`VerificationPixels` ignores each pixel difference of twelve grayscale levels or less. Its `CoverageValid` requires forty horizontal gradients somewhere in the perimeter plus an initial geometry check; it does not establish that the playable interior or each possible body is observable. `TableVerificationWorker` reduces the source to 480×270, warps to 200×400 and discards colour. A typical detected radius of 25–30 in the 1000-pixel-wide detector becomes about 5–6 pixels here.

A new synthetic diagnostic calls the production `VerificationPixels` directly. A radius-five body of brightness 110 moves one pixel per sample across a brightness-100 interior, with static textured perimeter pixels. The first result is unknown; all five subsequent results are `Still`, `CoverageValid=true`, with zero reported local/perimeter/illumination change. Total displacement is five pixels, half the body diameter. The fixed anchor does not rescue motion whose contrast stays below the pixel threshold.

This demonstrates a component blind spot, **not an observed end-to-end false Ready on the mounted table**. Detection consensus may independently refuse. The earlier user-assisted slow-roll requests were also refused, with no independently labelled motion intervals.

**Fix:** combine whole-playable-area colour/edge change coverage with bounded, sufficiently resolved body/uncertain-region displacement measurements. Reuse the existing forward/backward flow primitives, while measuring possible bodies separately from static cloth so a background-dominated median cannot hide movement. Check both adjacent samples and the fixed anchor. Derive noise and displacement limits from labelled development clips; insufficient interior/body evidence is unknown. Use ordered static references to check pose, and keep changed calibration invalid even if registration succeeds. Validate rail/pocket regions separately; the current perimeter also includes areas that balls can occupy.

**Proof:** regress the synthetic low-contrast case, then use independently labelled slow rolls, cumulative drift, same-luminance different-colour movement, rail balls, intervening movement, exposure flicker, obstruction and camera nudges. Report the complete publication result as well as the motion result.

### 2. Circle geometry causes real stationary-layout refusals

Both refused requests in `final-v4-live.json` contain a radius-ratio failure under the current 1.25 consensus bound:

| Request suffix | Candidate | Fitted radii | Ratio | Centre displacement, table widths |
| --- | --- | --- | --- | --- |
| `8c57` | red | 31.13 → 22.16 | 1.405 | 0.00910 |
| `4ca` | red | 34.25 → 24.11 | 1.421 | 0.00990 |
| `4ca` | yellow | 26.58 → 34.25 | 1.289 | 0.00758 |

Every full detection in the second refused request was individually clear. Both requests' final detections were clear. The exact retained evidence therefore identifies a cross-frame geometry veto, not merely a missing final colour. These are fitted circles in the rectified image, not changing physical ball sizes or manually established centre errors.

`HoughCircles` operates on a thresholded/morphologically processed cloth mask. Its fitted boundary can include shadow or select only part of a body. The earlier normalized branch also fitted the black's cream patch. The generic connected-component refiner was already tried and reduced replay availability from 3/6 to 2/6; enabling that experiment is not an established fix.

**Fix:** retain Hough as a proposal generator, then refine ambiguous candidates against current local body/edge evidence. Use a calibrated radius prior, radial edge support across sectors, centre residuals and explicit shadow/cloth exclusion. Keep merged/touching/partially hidden bodies unresolved. Record proposal geometry separately from refined geometry and uncertainty. A prior must not create a missing ball. Validate centres against independently marked body centres and radii before changing the consensus tolerance.

### 3. Black/cue classification is coupled to the unstable circle

In the six-frame native v4 replay, the black is recognized in three frames and unresolved in three. Recognized fits have radii 25.28–26.71 and dark-rim fractions 0.458–0.465. Unresolved fits have radii 29.70–29.96 and dark-rim fractions 0.409–0.412. The recovery classifier requires a dark-rim fraction of at least 0.45. Its sampling annulus is defined by the fitted circle, so a different circle changes the classification evidence. The baseline initially labels both the real cue and black as cue, preventing unique-cue normalization.

There is a second source-confirmed availability issue: the first sample sets `appearance.Frozen=true` even when no valid reference was found. Subsequent frames restore that invalid frozen status, so the burst cannot acquire a newly valid reference. Its frequency has not been measured.

**Fix:** stabilize body geometry before sampling colour; distinguish a central cream patch with a supported dark surrounding body from the spotted cue. Inspect sector coverage and raw plus consistently normalized appearance. Establish a supported reference during a bounded bootstrap phase, then freeze it; invalid initialization must remain an explicit acquisition state. If the reference changes, restart affected evidence within the existing deadline or refuse. Evaluate missing/ambiguous cue references explicitly. Tune on multiple black/cue orientations and lighting sessions. Lowering 0.45 just to pass these six related frames would not establish correctness.

### 4. Recovery does not repair an earlier geometry outlier

`VerificationConsensus.Resolve` returns null immediately for any matched historical radius ratio above 1.25. A sixth clean frame cannot undo that veto. The first frame also supplies every association anchor and the required body count. Initial misses/extras therefore cannot be repaired merely by collecting a clean final frame. Existing middle-dropout handling is narrower than the roadmap's targeted recovery requirement.

**Fix:** return structured consensus conflicts identifying sample, candidate and failed criterion. Use those conflicts to trigger bounded local remeasurement while the relevant current pixels are owned. Distinguish an unreliable geometric estimate from contradictory physical evidence. Aggregate only supported estimates, while retaining required start/end support, current evidence for every published body, full-area extra-body checks and independent motion proof. If a valid interval cannot be established within four seconds, retain the refusal. Do not simply delete the radius gate or carry an absent body forward.

**Proof:** first/middle/final bad fits, a transient missed body, persistent missing body, extra circle and touching/merged bodies, with detector-call and deadline bounds. A final clean detection alone is insufficient.

### 5. Playback failure is real; its initiating cause is not established

Final v7 requests acquired zero verification samples. The implementation report records `playing=false` / `no-playing-texture`; this is a source availability failure before CV. It provides no final-v7 detector result and does not prove O08 caused playback to stop.

The Java copy layer already has detailed refusal reasons, but acquisition eventually throws a generic error. `RunVerificationAsync` catches every non-cancellation exception as `FrameUnavailable`, including failures in the worker or optional capture callback. Thus a detector or capture failure can be presented as a live-video problem.

**Fix:** preserve typed acquisition failures and bounded internal exception diagnostics by stage; record playback state, play intent, suppression/error, surface availability, generation, frame progress and copy refusal around the request. Check live readiness before starting the evidence window and route recovery through existing live controls, invalidating old evidence when the source changes. First reproduce why playback stopped; do not assume a decoder or camera fix from the current receipts. Keep unknown ages explicitly unavailable in diagnostics rather than displaying a small numeric age for a request with no frame.

### 6. The acceptance tooling and evidence are incomplete

The corpus can now store/validate still and negative sequences, and `DetectorBaseline` scores per-frame detections. The workbench does not yet score complete verification request windows against labelled conditions, including ready/refusal, deadline, source age, geometry and unsafe readiness. The native JPEG instrumentation is a branch ablation, not that evaluator.

**Fix:** add a request-window evaluator using the same verification decision path, locked parameters and source metadata, with explicit live-only metrics separated from offline replay. Split sessions before tuning; independently review centres/radii/visibility and motion/obstruction intervals. Preserve short originals as well as selected decoded samples. Freeze baseline resource scenarios before optimizing.

The existing plan requires at least two lighting strata, 100 non-overlapping supported requests per stratum, five layouts, two held-out sessions, and 100 unsafe/source/control windows spanning the required categories. Report correlated sessions honestly. Phone-plus-corrected-TV p95 frame gaps/latency, native resource retention, actual one-tap/cancel/correction/source controls and guarded normal delivery remain physical gates.

## Image-quality conclusion

The inspected 1920×1080 derivative clearly shows the bodies, large black cream patch, spotted cue and pronounced directional shadows. Live detection consumes decoded pixels directly; saved JPEG replay adds another encoding stage, so its failure rate is not interchangeable with live performance. No source-versus-decoded focus, compression, clipping or edge-resolution benchmark exists yet.

The evidence supports fixing the software's boundary selection and evidence reduction first. It does not establish that the camera is inadequate, nor does visual inspection certify image quality. Capture paired lossless decoded crops and source footage where available, measure native pixels per ball, edge spread/clipping and stability, and test exposure/focus controls only if those measurements justify it. Increasing output resolution alone will not repair selecting the wrong edge.

## Recommended execution order

1. Preserve the new reproductions as regression cases; add specific consensus/acquisition diagnostics and the request-window evaluator.
2. Implement and validate motion coverage/displacement first, because a clear colour result must not mask unsupported stillness.
3. Repair geometry, then black/cue sampling and valid reference bootstrap; evaluate each change separately on frozen development material.
4. Add targeted conflict recovery and diagnose live playback readiness using the improved telemetry.
5. Freeze parameters and run untouched sessions plus the phone/TV resource and interaction matrix; deliver the normal build only after acceptance.

## Audit reproduction

`python work/o08/diagnose_remaining.py` extracts the recorded radius conflicts and black-rim evidence. Its output is `work/o08/remaining-diagnosis.json`; this is an artifact audit, not a replacement for production consensus or truth labels.

`dotnet run --project work/o08/audit-probe/audit-probe.csproj` invokes the current production pixel verifier on the synthetic moving body. Output is retained in `work/o08/motion-blindspot.json`. The probe built and ran successfully. No app runtime files, device state or camera configuration were changed by this audit; no new full-suite or physical acceptance claim is made.
