# O08 — Reliable mounted-camera table observation

**9 October dependency clarification:** O08 supplies trustworthy settled observations A/B for [O03–O05 reconciliation](temporal-tracking-design.md). Observation quality must be distinguished from cue/black inventory and advice eligibility: a verified absent cue/black can be physical outcome evidence while advice remains unavailable. This semantic integration is planned, not newly implemented; preserve all delivered reliability results and open gates.

8 October 2026 · **Runtime foundations implemented; physical reliability/release acceptance incomplete. P0 release blocker for dependable automatic shot advice.** The user reports that vision is “borderline unusable” in current pool-room lighting; this is severity feedback, not a measured error rate. Epic: **Mounted-Camera Observation Reliability**. The [implementation/evidence report](o08-observation-reliability-report.md) records bounded verification, actual Pixel interaction, failed development variants and remaining gates. The [O09 learned detector spike](o09-learned-ball-detection-spike.md) compares detector proposals after O01/O08.01 data readiness; O08 retains ownership of safe current observations and release acceptance. This plan and the provisional 8 October illumination/hue changes do not establish accuracy. Planning baseline: `5453d0b` and the code inspected on 7 October. Preserve other missions and their recorded evidence.

## Outcome and release decision

A player with a mounted camera and a clear, stationary table presses **Show candidate shots** once. SkyEye performs a short, bounded verification, establishes a trustworthy current layout and displays eligible advice. A separate **Confirm balls** action is not part of normal readiness: a fresh, clear, supported layout that passes the stillness window becomes ready automatically. If verification cannot establish that layout, it identifies the problem and the useful next action. The player does not have to time repeated requests or certify a correct automatic result by hand.

Repeatable failure under supported clear/still conditions blocks claims of dependable automatic shot advice. Keep declared match/rules, manual correction and delivered media usable while fixing observation reliability. The release gate is correct recognition with safe rejection of motion/uncertainty, not a higher rate of green “ready” messages.

## Sequential lighting diagnostics

An opt-in sequential diagnostics mode is implemented in the current Hybrid source. With an active calibrated live table, it samples a fresh frame about once per second, checks frame/source progression, and retains a rolling maximum of 60 per-frame detector records. The table controls show the latest eight records; Sky can start, read and stop the same session, receiving the latest three records. These diagnostic observations do not enter `TableTracker`, confirmed table state or `GameState`. The controls are bounded by active host/live-session and calibration requirements, and stop when the source or calibration changes.

This is a useful first-pass debug view. The rolling “raw dump” is structured detector output, not source imagery. A separate **Capture fresh frame to label** action now requests one fresh decoded image, hides detector output on-screen and from Sky while the frame is open, and lets a person mark ball boxes, classes, visibility, lighting, motion and scene conditions. Saving is explicit; images/receipts use a 40 MiB/120-frame local cap and stay out of confirmed table/GameState. Finished hand-labelled sessions import through O01 with their original camera recording-session ID and frozen split assignments. See the [O09 comparison plan](o09-learned-ball-detection-spike.md).

**Priority:** complete O08.01/O01 labelled session-separated baseline → O09 A/B/C detector comparison → remaining O08 held-out, safety, resource and guarded-release gates → resume A03.01/A05.01 spoken shot-advice parity. Existing A02 voice availability and declared-history consumers can proceed independently; dependable automatic shot advice and its spoken parity wait for the P0 gate. Collect the O08 subset of O01 evidence and required O02.05 readback/resource measurements during this mission. The 9 October settled-state direction uses O01 core-match evidence and relevant O02 activity/source/resource gates before production O03 → O04 → O05 acceptance; persistent real-time identity/trajectory accuracy is no longer a prerequisite. O10 replay metrics are separate. Existing T13/T14 acceptance, V01/V02 physical coverage and V03/V04 physics/trajectory prerequisites stay open.

O08 uses O01 workbench and O02 atomic decoded-frame foundations already delivered within their limits. It does not wait for a fully accepted moving-ball tracker, and it does not complete that tracker. Numeric mission IDs do not imply that O08 depends on O03–O07.

## Inspection findings and hypotheses at the planning baseline

| Finding in the current code | Consequence and planned response |
| --- | --- |
| `TableTracker.SameLayout` requires equal counts and matches only the same colour, checking the previous sample and fixed anchor within 0.015 table units | A missed circle or label flip can restart settlement without physical movement. Separate motion evidence from recognition quality; keep the fixed-anchor protection against cumulative drift. Table units are dimensionless, not metres. |
| A unique cue reference enables normalization and a second detection pass with a different Hough vote threshold (defaults 15→14 at analysis width 1000) | Reference availability changes the processing branch. Measure branch/gain/count changes on a sequence before attributing the observed failures to them. Stabilize processing within a verification burst. |
| `ConstrainInventory` marks every label in an oversubscribed colour group unknown | A spurious candidate can cause widespread ambiguity. Preserve inventory as a rejection/review constraint; localize the conflicting evidence without inventing labels or enforcing a full rack. |
| `uncertain` and physical obstruction both contribute to the table's `Occluded` flag | A recognition failure is difficult to distinguish from obstruction. Add explicit reason/quality information while preserving conservative legacy eligibility. |
| `TableAnalysisSession` constructs `TableTracker(15)`; the tracker default is six seconds; confirmation/correction freshness is six seconds; the UI tells the player to repeat within six seconds | These are different policies currently described inconsistently. Audit and name each timing policy; share configuration/formatting and fix the UI/docs. Two-second minimum settlement span remains the initial baseline, subject to source-time evidence. |
| The live frame adapter admits an age estimate up to six seconds, and the session passes an age-derived UTC timestamp into the tracker | Audit freshness at the final advice decision. Use exact native frame identity/source PTS for continuity and span; a successful copy alone does not establish sufficiently current advice. |
| The existing diagnostic motion feed starts only from a confirmed settled layout | Its bootstrap dependency prevents it from deciding initial stillness. Reuse bounded ROI/flow primitives under a separate short verification mode which can start before a confirmed layout exists. |
| Detection IDs are regenerated from colour/order | Match observations spatially inside the burst. Do not interpret these IDs as durable ball identities or create canonical GameState identities from ordering. |

These are code findings and plausible contributors, not measured causal attribution. The [live HUD report](v01-v02-hud-report.md) and [live evidence](evidence/v01-hud-live-acceptance.json) establish presentation behavior and intermittent count/colour variation; their successful samples do not establish detector accuracy or a reliability rate.

Code anchors: [tracker](../src/SkyEye.Core/Analysis/TableTracker.cs), [analysis owner](../src/SkyEye.Core/Analysis/TableAnalysisSession.cs), [detector](../src/SkyEye.Vision.Android/VisionAnalyzer.cs), [classifier](../src/SkyEye.Core/Analysis/BallClassifier.cs), [frame adapter](../src/SkyEye.Companion/PlayerTableFrameSource.cs), [motion startup](../src/SkyEye.Companion.Hybrid/ShellMotion.cs), [native ROI processor](../src/SkyEye.Vision.Android/RoiMotionProcessor.cs), [shot controls](../src/SkyEye.Companion.Hybrid/Components/ShotControls.razor).

## Scope and ownership

- Retain the C# OpenCV/Hough/colour pipeline and its calibrated geometry. Improve its measured stability and targeted recovery before considering O07 detector replacement.
- Keep one transient observation owner in `TableAnalysisSession`, one typed coordinator, one canonical `SkyEye.GameState` and the existing shared phone/TV scene/journal. Burst association is transient evidence, not another game ledger or general tracking service.
- Extend the observation contract additively. Preserve existing single-observation semantics, source/version fences, operation deduplication, host authority, player correction and old recorded scene/journal compatibility.
- A single-frame **Observe table once** remains available. **Show candidate shots / Verify table for advice** explicitly selects bounded verification; do not silently turn every old `IncludeChoices` or observation request into a burst.
- Preserve unknown/occluded/missing distinctions. No single disappearance becomes a pot, scratch, score or durable removal; automatic outcome proposals remain O04/O05 work.
- Candidate selection/preset/clear uses the verified catalogue and does not rerun perception. Expiry, source/layout invalidation and shiny-only TV policy remain authoritative.
- Camera capture remains governed by existing commands. Optional exposure/white-balance experiments require an independently scoped camera change with restoration; ordinary verification only reads decoded pixels.

## Verification contract and state

Maintain independent dimensions: **motion** (`unknown`, `still`, `moving`), **layout quality** (`unknown`, `clear`, `ambiguous`, `obstructed`) and **freshness/source validity**. These are proposed semantic values, not an instruction to replace all existing public enums. Add reason metadata and map conservatively into current `Ready`, `Unsettled`, `Unclear` until consumers migrate. `Ready` requires still + clear + valid/current source and an eligible layout. Unknown motion never implies still.

"Confirm balls" is a UI workflow to remove, not a new confidence shortcut. A successful bounded still/clear verification supplies readiness automatically; manual correction remains available when the layout is wrong. Movement evidence may trigger a shot/event review, but cannot by itself establish which balls moved, were potted, or how the canonical match state changes. After motion ends, require a fresh clear settled layout, compare it with the prior trusted layout, and send a typed proposal through the O03/O04/O05 evidence and acceptance path.

| Observable result | Meaning and next action |
| --- | --- |
| Checking table | One active bounded request; allow cancellation; show useful progress without publishing partial advice |
| Ready | A current, eligible, verified layout; generate candidates once and retain source/version/expiry identity |
| Table still; detection unclear | Motion evidence is adequate but one or more ball detections/labels are unresolved; show the affected region and offer retry/review |
| Balls moving | Positive local displacement/change evidence; wait and request again |
| Table obstructed | A hand, cue/body or object prevents adequate visibility; clear the view |
| Lighting changed | Photometric evidence exceeds the verified normalization range; reacquire a reference or explain the unsupported condition |
| Camera/calibration changed | Pose/shape/reference checks fail; clear old advice and request calibration validation/review |
| Live frame unavailable or too old | Acquisition, age, mapping or continuity failed; reconnect/resume live and retry |
| Cancelled/superseded | No late table/catalogue publication from the previous operation |

Each result carries the operation ID; camera/source/timeline; analysis and decoder generation; calibration/configuration identity; source-PTS interval; independent frame count and gaps; final receipt/age estimate and clock-mapping quality; motion/quality/reason codes; associated detections and their evidence ages; elapsed acquisition/CV/verification time. Keep native pixels and credentials outside agent/Razor state. Feature scores are not calibrated probabilities.

One verification operation freezes its source, geometry, analysis settings, target group and relevant game/version references at admission. Duplicate operation IDs reuse the existing result/in-flight work; overlapping incompatible requests cancel or supersede explicitly. Editing, group/game changes affecting advice, loss of authority, source/timeline/decoder/calibration changes and navigation away invalidate late publication. Follow the existing coordinator's concurrency rules rather than rejecting the operation because its own progress updated a projection.

Verification establishes the snapshot's readiness at publication. It does not prove that the table stays still for the subsequent catalogue TTL. Clear obsolete advice while a replacement check is pending and on known invalidation; do not extend expiry using old evidence. Continuous post-publication movement detection remains the O02/O03 observation-session scope, with its own resource and coverage gates.

## Sampling, source time and resource limits

Initial design target: a roughly two-second evidence span, four to six independent fresh samples, completion within three seconds on supported clear/still scenarios, and a hard bounded timeout proposed at four seconds. Final cadence/full-analysis count must be frozen from O08.01/O08.02 measurements. A timeout returns an honest reason; it does not accept a partial layout or extend indefinitely.

Use strict increasing native frame IDs and source PTS in the same scope. A duplicated/frozen image receipt cannot establish the span. Use a monotonic local clock for elapsed budget and source PTS for temporal evidence; UTC timestamps remain receipt/UI metadata. Define and test separate policies for minimum still span, maximum gap between motion samples, final-frame age, clock-mapping age/quality, legacy observation gaps, correction freshness and catalogue TTL. Never substitute a six- or fifteen-second catalogue/legacy limit for continuous evidence inside a short burst.

Proposed final frame age at the publication decision is at most 1.5 seconds, with a trustworthy source-clock mapping; freeze the actual supported budget from measured input latency. Mapping uncertainty or stale clock samples must fail freshness. Source-time arithmetic does not make the clock mapping exact. Report age distribution and mapping limitations alongside the three-second UX target; do not pass by analyzing delayed video quickly.

Use low-resolution playable-area changes and a bounded number of local motion samples; schedule expensive full detection at selected anchors/recovery points, not each video frame. Reuse one-active/one-latest ownership and metadata-only fresh-frame polling from O02. Dispose frame/ROI resources promptly; never retain a queue of full bitmaps throughout the burst. Coalesced requests share useful work and cancellation semantics, without several hidden detector loops.

Actual native readback cost may dominate even if processing uses small ROIs. Measure complete acquisition, conversion, native copies, detector/flow time, allocations and UI/video frame gaps at the mounted source's actual resolution. A controlled 640×360 fixture or nominal cadence does not close the real-source resource gate.

## Stillness and mounted-camera health

Bootstrap stillness from current calibrated playable pixels before relying on recognised ball colours. Combine robust global change evidence with coverage of possible ball regions, including uncertain and newly appearing bodies. Known-layout ROIs alone can miss an unrecognised ball or a ball outside its old region.

Use static rail/cloth reference regions to estimate image registration/pose change and broad illumination change. Distinguish a global camera shift from local ball movement, but invalidate the old calibration/advice when the mount moves; compensation must not silently legitimize a changed pose. An unsupported/failed registration is unknown, not a zero-motion result.

Compare against both recent samples and the burst's fixed anchor. Noise-calibrated displacement/change thresholds must detect the labelled slow-roll cases; smoothing cannot allow cumulative motion to creep through. Forward/backward feature checks, valid-point coverage and image-difference coverage provide quality evidence. Too few tracks, a uniform region, failed flow or an obscured perimeter cannot prove stillness. Test exposure flicker compensation against actual small ball movement so that normalization does not remove that movement.

Reuse [existing ROI primitives](o02-motion-feed-report.md) and OpenCV's [sparse optical-flow API](https://docs.opencv.org/4.5.5/dc/d6b/group__video__track.html) where useful. The existing feed's fixed regions/high-cadence diagnostic startup is not the product verification design; bootstrap, recentering/coverage and lower bounded sampling need their own measured checks. This API reference supplies mechanics, not accuracy evidence.

Validate compatible stored calibration with cheap observed reference checks when connecting and before advice if health is doubtful. Metadata compatibility alone cannot prove an unmoved camera. Avoid redetecting/overwriting corners on every frame. If pose is incompatible or confidence inadequate, use the existing calibration-review path. Missing calibration has a clear result and cannot produce a guessed rectangle.

## Photometric stability and detection recovery

The [8 October cue body/geometry candidate](cue-ball-detection-recovery-plan.md) now separates ordinary colour correction from proposal location, adds raw neutral-body recovery and validates false cue identities against current boundaries. Native development checks and the guarded Lab update are recorded there. This is incremental O08.04/O08.05 evidence, not completion of physical or Holdout acceptance; temporal extension follows the measured live result.

1. Record raw/normalized detection branches, gain/reference values, cloth estimates, Hough/contour candidates, rejected candidates and colour evidence for representative sequences.
2. Freeze one validated normalization/settings snapshot within each burst. Establish its reference from adequate neutral/cue evidence across samples; refuse conflicting references. Detect changes outside that reference's support and end/restart verification explicitly. Bound any reuse between bursts by source, lighting health and age.
3. Inspect auto-exposure/white-balance/focus capabilities on the actual camera. Evaluate locking only after stabilization, behind an optional scoped configuration with restoration and unsupported-device fallback. Do not promise lock support or require a camera rebuild for the first baseline.
4. Constrain size/shape using calibrated table geometry and observed ball-radius distributions. Combine current Hough candidates with bounded local evidence for dark/partially shadowed balls, especially black/cue, cushions and pocket lips. Validate candidate deduplication and touching-ball separation. A radius/contour prior cannot manufacture a ball.
5. Preserve inventory as evidence against impossible labels. Resolve a conflicting candidate locally only when appearance/geometry evidence supports it; otherwise retain ambiguity. Never force sixteen balls or a full red/yellow count: legitimately potted balls are normal.

All threshold/branch changes require an ablation against frozen development sequences and an untouched session holdout. Keep source/frame hashes and the raw baseline; distinguish gain stabilization, geometry association and detector improvements in the report.

## Short-window association and layout consensus

Match candidate bodies across samples by calibrated spatial distance, radius/shape and appearance evidence. Colour is supporting evidence, not a prerequisite to comparing motion. Use a deterministic bounded one-to-one association; handle competing assignments and merged/split circles as unresolved. Preserve raw detections separately from the consensus result.

Aggregate supported centre estimates robustly and combine colour observations with explicit evidence thresholds. Require observations at the start and end of the supported still interval, sufficient independent appearances/coverage and a current final verification. Initial vote/support thresholds are hypotheses to measure, not arbitrary guarantees from majority voting. Correlated errors across several similar frames can produce a confidently wrong consensus.

One dropout triggers targeted current-region rechecking. A previously supported ball can be retained as a hypothesis with age/missing evidence, but not silently promoted into a fresh clear layout. Final `Ready` requires current supporting visual evidence for every included ball, a clear search for unresolved extra bodies and motion/occlusion clearance. Persistent missing/extra/ambiguous bodies return unclear and clear affected advice. A one-time miss is neither movement nor a pot.

Temporary same-source occlusion should retain the last trusted full layout as an explicitly stale/occluded reference, even when a player or cue blocks the table for up to a minute. Do not refresh its observation time, expose it as advice-ready, or interpret hidden balls as potted. Once clear, compare the new layout with the saved anchor and collect a fresh still span before restoring readiness. A changed layout becomes a reviewable candidate; a source or calibration change starts a new scope.

A previously reviewed layout can seed search regions and reduce expensive re-detection while stillness and source/lighting/pose health remain verified. Freshness is explicit; do not refresh the timestamp of unobserved geometry. Player correction remains authoritative within its valid scope, and automatic work cannot overwrite it after a version change. Remove or alter canonical balls only through existing accepted evidence/correction seams. Burst IDs remain separate from O04 track IDs and nullable durable ball identities.

## Corpus, labels and measurable acceptance

Extend the [O01 workbench](observation-corpus-guide.md), not a second evaluation store. Its current holdout validator rejects non-shot purposes. Add an explicit reviewed real still-verification/negative-verification holdout path with schema/compatibility tests before claiming an O08 holdout result; do not label still clips as shots to bypass validation. O01's real timed-shot count and shot-accuracy readiness remain separate.

Collect short original sequences once, preserving actual source timestamps, pixel receipts/hashes, scope/calibration, camera/resolution/settings, lighting/reference state and operation windows. Labels include visible/occluded/unknown/absent bodies, centres/radii/colours, still/moving/obstructed/lighting/pose intervals, review/annotator information and uncertainty. Mark centres before seeing detector output; review identity/count boundaries. Do not treat potted/absent balls as a failed full-rack count.

Split whole recording sessions, including adjacent windows/crops/re-encodes, before tuning. Hash checks alone cannot detect related-session leakage. Freeze corpus and parameters with the report; new labels/tuning require new versions. Repeating one still image or overlapping windows is not a set of independent trials. Repetition is useful separately for nondeterminism/soak checks.

| Coverage stratum | Required cases |
| --- | --- |
| Supported still layouts | Full and partial racks; several ball distributions; black number patch and spotted cue orientations; central, rail-adjacent and pocket-lip balls; touching/close balls; normal and warm supported lighting |
| Recognition stress | Shadows/reflections/compression/exposure changes; single transient misses/extra circles; colour flips; merged/split detections; missing cue/black and ambiguous labels |
| Unsafe-to-publish negatives | Slow roll and cumulative drift; ball moves between detector anchors; hand/cue/body obstruction; ball lifted/replaced; camera nudge/shape change; appearance of a previously unrecognised body |
| Source/control negatives | Duplicate/frozen frames, out-of-order PTS, clock/source-age failure, decoder restart, disconnect/reconnect, authority loss, correction/group change, cancellation and duplicate operations |
| Legitimate changed layouts | Real removal after motion with reviewed before/after truth, partial racks and explicit correction; unsupported outcome inference remains pending |
| Coexistence | Live phone only; corrected/branded TV together with phone; background/navigation/cancel; repeated checks at actual resolution |

### Proposed gates to freeze before tuning

These are future targets, not results. O08.01 records supported conditions and label precision; O08.08 publishes all denominators, failures and per-stratum/per-session rates. Include a descriptive binomial interval for request rates, explicitly flag within-session correlation and avoid treating non-overlapping windows as statistically independent merely because their pixels differ. Use session-level uncertainty analysis when enough sessions exist. A limited zero-error set is not a universal safety guarantee.

| Gate | Initial acceptance proposal |
| --- | --- |
| Correct timely layout | At least 99% of supported clear/still independent requests return an entirely correct eligible layout within three seconds; incorrect `Ready` counts as a failure, as do timeout/retry/correction requirements |
| Evaluation size | At least 100 non-overlapping request windows per supported lighting stratum, at least five distinct layouts and two held-out recording sessions; use at least two lighting strata. Every required supported case is reported separately; do not hide a failing case in an aggregate |
| Geometric accuracy | Freeze a radius-relative centre tolerance compatible with reviewed label precision. Initial target: p95 centre error ≤0.25 labelled ball radius in calibrated/image geometry with an explicit transform; report worst errors and pocket/rail cases, not only the average |
| Stable unchanged layout | At least thirty seconds of repeated verification on each representative unchanged layout, with no false count/colour changes in published `Ready` results. Report uncertain intervals/retries as availability failures rather than hiding them |
| Unsafe readiness | Zero observed false `Ready` results in at least 100 independent held-out unsafe/source/control windows spanning every negative category. An invalidating event blocks late publication; a safe new operation may recover after conditions genuinely clear |
| Dropout recovery | A transient miss receives bounded rechecking; all `Ready` balls have current visual support. Persistent disappearance/occlusion/extra bodies never become a fresh accepted unchanged layout through stale carry-forward |
| Clock and freshness | Increasing scoped receipt IDs/PTS, adequate sampling coverage, explicit final-frame age/mapping quality and source-time still span. Duplicate frames, clock failure and interrupted spans cannot pass |
| Resource/playback | One active/latest ownership, bounded work and prompt cancellation/disposal; verify actual resolution. Initial gate: ≤10% relative regression in p95 phone/TV frame-gap and latency metrics versus the same baseline scenario, no new stall >1 second, no monotonic retained-native-resource growth after warm-up/repeated checks |
| User interaction | One actual tap produces result or specific reason; no separate ball-confirmation tap or timed second tap. Preset/select/clear does not increase detector calls. Sky/Touch typed parity passes; real voice audio/tool acceptance remains A02/A03 |

Choose resource sample sizes and the final source-age/coverage thresholds from the baseline before optimization. Record absolute values as well as relative changes; a poor baseline cannot legitimize poor playback. If a proposed gate is infeasible, document the measured blocker and revise supported scope explicitly before release. Never lower a safety gate merely to obtain a passing status.

Keep media bounded: short originals plus selected ROI/frame evidence and JSON receipts, referenced by immutable hash. Establish a per-run storage budget and retention manifest. Store large media under the existing corpus/work locations; commit small labels/configuration/reports. Do not add continuous recordings to Git or automatically delete the user's existing videos/APKs.

## Delivery chunks and verification

Runtime foundations for O08.01–O08.06 are implemented, with controlled checks and bounded real development/tap evidence. Their full physical acceptance is **open**; O08.07–O08.09 remain incomplete. The [report](o08-observation-reliability-report.md) retains failures and missing denominators. Each chunk produces a small reviewable implementation and evidence artifact; split Core/native/UI/corpus changes into further commits when needed. Commit after each meaningful verified change. Dependencies and failure results are recorded rather than marking the whole mission complete after a build.

| Chunk | Concrete result | Dependencies and check |
| --- | --- | --- |
| O08.01 — Establish the baseline | Structured reason/change/branch telemetry; audit timing/freshness policies; versioned still/negative corpus support and frozen development/holdout manifest; baseline report and supported-condition declaration | Existing O01/O02; verify the opt-in sequential diagnostics view with actual changing light; record each rejected/missed/changed circle and current ready/latency rate; add bounded, reviewed source-frame capture and freeze session boundaries for the linked O09 comparison |
| O08.02 — Bound verification ownership | Explicit single/burst mode, scoped operation result/progress, monotonic deadline, fresh native sample admission and named timing policy | O08.01; fake-clock/source tests for duplicates/gaps/out-of-order/source changes/cancel/late completion/disposal/coalescing. One-frame callers retain their contract |
| O08.03 — Bootstrap stillness | Motion/quality separation; calibrated global/local change checks, anchor drift protection and registration/illumination quality; no settled-layout startup prerequisite | O08.02; frozen slow-roll/obstruction/exposure/camera-move/textureless sequences and native measurements. Unknown coverage cannot produce still |
| O08.04 — Stabilize appearance | Per-burst reference/settings consistency, measured classifier/inventory conflict handling and bounded local circle recovery | O08.01/O08.02; branch/gain/threshold ablations on development cases; explicit uncertain labels and no forced inventory. Preserve untouched holdout; compare the current pipeline and any provisional illumination/hue branch against the same O09 baseline frames |
| O08.05 — Form a verified layout | Deterministic bounded association and robust position/colour consensus; targeted dropout recheck; current-support/final-age checks; reviewed-layout seed rules | O08.03/O08.04; persistent miss/extra/merge/split/colour-flip/cumulative drift/correction cases. No stale ball becomes current evidence or canonical removal |
| O08.06 — Deliver one-action UX | Shared typed verification route, single-tap candidate flow, truthful progress/reasons/review/retry/cancel; candidate generation once after verified result | O08.05; real Blazor/typed coordinator checks and actual Pixel tap. Touch/programmatic Sky-origin parity, scoped catalogue/clear/expiry, source and group changes; voice transport remains separate |
| O08.07 — Prove mounted health and coexistence | Cheap pose/lighting-health validation; optional supported camera controls only if justified; actual-resolution acquisition/CPU/native memory/video resource report | O08.03–O08.06; connected mounted source, phone-only and corrected TV comparisons, cancellation/navigation/repeated-check runs. Conditional camera experiment has capability record and restoration proof; keep wider O02.05/T14 gates open |
| O08.08 — Freeze and evaluate | Reproducible build/config/corpus-lock report, blind session holdout results, per-case denominators/uncertainty, failed-window evidence and ablations | All prior chunks; all proposed safety/correctness/latency/freshness gates and meaningful regression suites. Changed parameters require a new version and retained previous report |
| O08.09 — Guarded normal delivery | Signed normal Release, protected in-place migration, end-to-end live one-tap acceptance and updated roadmap/report | O08.08; signer/package/version/UID/private-storage safeguards, installed hash, normal startup, phone/TV scene behavior. Remove opt-in diagnostics/owned forwards; preserve capture/key/clip/journal data |

**Next evidence session:** finish O08.01 telemetry/policy audit and corpus/schema support, then collect and label a bounded real baseline including the closed-blind sunlight pattern. Freeze session splits before threshold or model tuning. Run O09 only when the comparable data and holdout manifest are ready. O08.02 and later work continue against the reviewed failure taxonomy and timing policy. Corpus review can proceed offline; physical input, actual-resolution resource and final UX gates require the mounted camera/Pixel, with TV needed for O08.07/O08.09 coexistence only.

## Risks, rollback and completion record

The principal risk is making a wrong layout appear stable through correlated votes, stale carry-forward, overly broad normalization or permissive motion thresholds. Keep raw evidence, conservative unresolved outcomes, fixed-anchor checks and untouched holdouts. Extra samples can reduce availability through resource contention; limit work and measure complete native acquisition. Exposure locking can worsen changing light or be unsupported; retain a measured fallback. Pose checks can fail under occlusion; disclose uncertainty instead of replacing stored geometry.

Retain single-observation/review functionality as a recovery route, with its truthful gates. Allow bounded verification to be disabled through an explicit scoped implementation feature/configuration while debugging; disabling it does not constitute reliability acceptance. Do not auto-fall back to guessed balls or original live-TV presentation. Keep persisted settings/old commands and recorded scenes compatible. Restore any experimental camera setting and close temporary diagnostics after tests.

Completion report must identify commits/builds, devices and actual dimensions; corpus/config hashes and session split; measured accuracy/availability/latency/geometry and resource tables; every false-ready/unsafe failure; manual interventions; supported lighting/layout limits; actual phone/TV/normal-install evidence and remaining gates. Record staged status honestly: runtime implemented, controlled checks passed, physical gate open, or accepted within explicit supported conditions.

This planning update performs no runtime edits, device/camera operations, builds, paid voice calls or new accuracy measurements. The 99% and timing/resource values above are acceptance proposals. O08 does not establish automatic refereeing, ball identity through crossings, physical stun/finish prediction or the wider O01/O02/O03–O05/T13/T14 claims.
