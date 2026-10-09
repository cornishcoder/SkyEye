# Settled-state reconciliation, game outcomes and post-shot trajectories

Updated 9 October 2026 from the supplied **SkyEye — Reframe Game Understanding Around Settled-State Reconciliation** brief. Implementation has started with the frozen O03 context and pure O04.02 settled-count comparison in `SkyEye.GameState`. This first domain slice builds, but camera/activity orchestration, detector integration, review, journal admission and device acceptance remain open. It supersedes the earlier continuous-tracking dependency model. Preserve this file path and existing mission IDs for established links.

> **Settled-state reconciliation tells SkyEye what happened. Post-shot trajectory analysis explains how it happened.**
>
> **The match must remain usable even when trajectory reconstruction is unavailable or uncertain.**

Here, “what happened” means supported count/presence/layout changes and reviewed outcomes. It does not mean that two still images prove a complete referee ruling.

## Priority and preserved foundations

The core delivery path is **O01 match corpus/evaluation + O08 reliable settled observation → O02 source/PTS and bounded activity foundations → O03 Activity & Settlement Boundary → O04 Settled-State Reconciliation & Physical Outcome Proposals → O05 Outcome Review, Admission & Canonical Game Integration → R04/R02 rules consumers**. O09 remains a bounded detector comparison where reliability evidence warrants it. O01 collection and offline O04 contract work can proceed alongside O02/O03; full replay tracking accuracy does not block core match delivery. The next implementation slice is specified below.

**O10 — Post-shot Trajectory Reconstruction & Replay Model** is a new, previously unused mission ID. It supplies optional asynchronous enrichment to V04/C02/PH02/PH03, advanced replay, Shot Lab and evidence-backed statistics. It is outside the authoritative match dependency chain. Source pairing, relevant playback/resource safeguards and held-out evidence remain required for whichever path is being released.

| Existing foundation | Status retained | Role in this destination |
|---|---|---|
| R01 `SkyEye.GameState`, ordered journal, provenance and deterministic reduction | Delivered within the R01 report's scope | Canonical match/frame/table truth; additive typed seams only where required. Do not replace R01 or create a second ledger. |
| `AppCoordinator` / `SubmitGameInput` | Existing serialized admission boundary | Source/version/ownership fencing, idempotency, persistence before publish; raw CV never writes directly. |
| O08 / `VisionAnalyzer` / `TableAnalysisSession` / `TableTracker` | Verification and obstruction foundations delivered; wider reliability acceptance open | Trustworthy settled A/B observations; clear-table eligibility is distinct from whether the inventory permits shot advice. |
| O02.01-O02.04 | Pairing, scoped receipts and bounded feed foundations delivered within reports | Actual pixels/source PTS, one active plus one replaceable latest frame, activity/settlement cues and media alignment. O02.05 resources/recovery and O02.06 signal acceptance remain open. |
| O04.01a-c | Diagnostic import, offline association evaluator and sequence viewer implemented; real evaluation open | Preserve tooling and IDs; optional positional association and O10 experiments. No group/count dependency on durable red/yellow IDs. |
| G06 / commit `853c3f6` | Isolated Lab layout proposals installed; physical acceptance incomplete | Three-sample before/after aid, still requiring unique identity association; missing remains Missing. It is not the new aggregate outcome or production admission model. |
| R04/R02, Sky and shared media/scene infrastructure | Retain recorded delivery limits | Rules consume accepted facts; Sky is a typed controller/consumer. Reuse recording, replay, annotation and correction seams. |

Earlier captures, failures, resource measurements and release gates remain evidence at their original scope. No old tracking benchmark becomes a passed reconciliation test because the roadmap changed.

## Authoritative game path

```text
trustworthy SETTLED TABLE A + canonical game context
    → activity window (possibly obstructed)
    → clear-table / settlement verification
    → trustworthy SETTLED TABLE B
    → bounded before/after reconciliation
    → physical outcome proposal
    → review / confirmation / correction and fenced admission
    → SkyEye.GameState accepted physical facts
    → rules evaluator + known/unknown rule facts
    → reviewed turn / group / foul / frame consequences
```

Continuous analysis answers only whether activity began, remains active, appears to have stopped, or the view is too obstructed/uncertain to verify settlement. Hands, cue, body, shadows, rapid balls and crossings do not require complete ball tracks. Retain useful frames/media for later analysis while holding the last trusted layout as historical evidence.

Same-source obstruction may last a minute or longer. It must not expire into a new rack, missing-ball conclusion or forced manual confirmation. Wait for a clear view and new bounded settlement verification; surface waiting/uncertain state if it cannot finish. Do not present the retained baseline as a current sighting. A source/timeline/generation/calibration discontinuity invalidates automatic reconciliation across that boundary; preserve evidence for review and establish a new baseline explicitly. An ordinary diagnostic sampling gap within a proven continuous source may lose precise shot timing without invalidating A/B geometry. Unproven continuity is not assumed.

If activity onset is missed, a verified layout change can propose an activity/shot window with unknown start time. It cannot invent a precise start timestamp. If several shots, a ball replacement or a rerack could have happened during the blind interval, require review rather than attribute everything to one shot.

## O03 — Activity & Settlement Boundary (in progress)

Conceptual states: `Settled → ActivityDetected → Active → WaitingForClearTable → Settling → SettledCandidate`, with `Uncertain` for unsafe or discontinuous evidence. These are planned semantics, not a claim that current runtime enums implement them. Renewed activity returns to Active; renewed obstruction waits for clear evidence. Quiet pixels alone cannot establish settlement or an empty table.

- **O03.01:** source-time activity window plus immutable shot-start context and pre-roll reference; bounded cheap activity monitoring using O02 primitives.
- **O03.02:** hand/cue/body obstruction hold, including at least a one-minute case; preserve A and avoid premature removal/turn proposals.
- **O03.03:** trigger O08 bounded clear/still verification after quiet; publish a settled candidate only after verification. Measure latency from actual settlement and from clear-view availability separately.
- **O03.04:** missed onset, duplicate/backward timestamps, interruption/reset and multiple-shot ambiguity; retain available pre/post media boundaries and explicit unknown timing.

Capture context at window start: MatchId, FrameId, GameVersion, CurrentPlayer, CurrentVisit, Groups, OpenTable, rules profile/version, trusted table A, and source/timeline/generation/calibration identity. If onset is missed, use a provably compatible baseline context and mark timing uncertain; otherwise require user resolution. An incompatible canonical revision while analysis is pending requires re-review, never silent application to the new player or frame. No identity/trajectory completion requirement belongs to O03.

**Implemented contract slice:** `FrozenGameContext` captures the canonical match/frame/version/player/visit/group/open-table/rules view. `ActivityWindowContext` holds that immutable context, trusted settled A and an optional source-time activity onset; a missing onset remains unknown. `TryBegin` only captures a clear settled baseline with complete counts and source PTS. This defines the boundary without yet detecting activity, retaining media or orchestrating obstruction/settlement.

## O04 — Settled-State Reconciliation & Physical Outcome Proposals (in progress)

A bounded pure reconciler, named `TableStateReconciler` only if that fits existing seams, takes **settled A + settled B + frozen game context** and emits a **proposed physical outcome** with evidence references and uncertainty. Raw samples and inferred outcomes remain outside the canonical ledger until admission.

| Supported comparison | Proposed fact | Still unresolved |
|---|---|---|
| 7 yellows before, 6 after; reliable equal-source observations | One yellow removed from the table | Exact physical yellow, pocket, cause and legality unless independently supported/confirmed |
| Cue present before, absent from a complete clear after-table | Cue removal / possible scratch | Whether potted, lifted or otherwise removed; contact-level foul facts |
| Black present before, absent after | Black removal | Frame result and legality belong to rules/review |
| Equal counts but supported changed arrangement | Activity with no ball removed | Whether it was a legal shot, safety, miss or manual movement; turn decision needs rules facts |
| Counts increase, rack-like arrangement or incompatible observations | Presence/rack proposal or uncertain outcome | No automatic rerack, new frame or reversal of prior pots |

Possible result vocabulary includes `BallGroupCountChanged`, `CueBallRemoved`, `BlackRemoved`, `BallPresenceChanged`, `NoBallRemoved`, `RackLikeLayoutObserved` and `TableOutcomeUncertain`. These are design examples, not existing API promises. Pocket identity is optional and never fabricated to satisfy a current input contract. One intermediate missing detection or an obstructed B supports no removal conclusion.

- **O04.02 — Contract and group/count reconciliation:** validate complete clear observations and scope; reconcile red/yellow counts and unique cue/black presence independently of optional physical track identity. Separate detector quality from advice eligibility so a legitimate cue/black absence can be represented without pretending the image is unreliable.
- **O04.03 — Ambiguity and context fences:** distinguish removal from confirmed pot, handling/replacement/rack and missed activity; reject source discontinuity and incompatible game revisions. Same-colour crossings may leave identities unresolved while counts are still supported.
- **O04.04 — Supported layout changes:** compare position sets with measured stationary noise in calibrated geometry; infer changed arrangement without assigning arbitrary durable identities. Exact per-ball movement is optional. No geometry or match result is manufactured to fill inventory limits.
- **O04.05 — Corpus evaluation and G06 presentation:** show compact before/after counts, cue/black presence, supported movement and unknowns, with actionable review choices and held-out error reporting.

Equal counts initially mean no observed net removal, not proof that nothing was briefly removed and replaced. `NoBallRemoved` requires a compatible single activity window with no unresolved handling/replacement evidence. Identical A/B counts do not establish whether a shot occurred. Supported activity or a player declaration can establish a no-removal shot even when the arrangement returns to its prior state; absent both activity and a layout change, do not fabricate a shot. Multiple shots or manual handling in an unseen interval remain unresolved.

Preserve calibrated coordinate conventions: core analysis uses a 2:1 table; GameState uses normalized positions. Restore the physical aspect for distance comparisons, retain historical calibration, and never treat normalized units as metres. Geometry-specific facts need the actual versioned bounds and ball dimensions.

A ruleset inventory maximum is an upper bound, not an expected current count. The planned domain group/count fact must be distinct from a detection ID, shot-local track ID or durable physical ball identity. The existing per-ball model and approach-plus-absence pot path do not already supply a general identity-free outcome contract. O04/O05 must plan and prove a narrow additive, versioned R01 input/event/reducer extension for accepted aggregate facts, correction and migration. Never select an arbitrary Y3 to satisfy a per-ball API or keep independent canonical counts alongside contradictory ball records. Unresolved individual membership remains explicit; projections must agree after correction/reload.

**Implemented contract slice:** `SettledTableCounts`, `SettledTableEvidence`, `BallCountChange` and `SettledStateReconciler` compare complete red/yellow/cue/black counts only. The result requires matching camera, timeline, generation, connection and calibration, plus increasing source PTS. It reports removals/additions without calling a removal a pot or changing canonical state. Direct construction supports future detector evidence whose same-colour identities are unresolved; capture from the current `GameSnapshot` is available when its per-ball projection is complete. This is not yet the additive accepted GameState event/input or a production detector adapter.

## O05 — Outcome Review, Admission & Canonical Game Integration (planned)

- **O05.01:** stage O04 results with A/B references, frozen context, supported/weak/ambiguous evidence, unavailable facts and choices. Preserve the original evidence when a player confirms or corrects a conclusion.
- **O05.02:** use existing typed input seams where sufficient; add the bounded aggregate outcome seam above where not. Prove ordering, serialization, deduplication, source/version fences and deterministic reduction. Do not submit a complete committing inference chain before the review decision.
- **O05.03:** admit accepted outcomes through `SubmitGameInput` and `AppCoordinator`, persist before publish, retain immutable evidence references and reject stale proposals. Historical evidence is referenced from a current admissible event rather than backdated across the journal watermark.
- **O05.04:** reject, amend, undo, reload and rebuild; invalidate dependent rules/replay/analytics projections when corrections change their inputs. Player-confirmed/corrected facts outrank machine inference for that fact. G06 exercises this path in its isolated journal before production admission is enabled.

Existing reserved/unsupported contact inputs do not become supported through this plan. O10 contact proposals need an explicit validated seam and O05 review before any canonical use; high-rate samples stay outside the ordered match journal.

First delivery uses explicit accept/reject/correct. Any later automatic acceptance policy is a separate measured gate; a heuristic score is not a calibrated probability. R01's existing pocket-approach/absence inference remains supported at its delivered scope, but O05 must prevent premature commits and must not fabricate pocket evidence to encode a count change.

## Rules and Sky responsibilities

Accepted physical outcome (for example one yellow removed, cue and black remain) is separate from “Rich continues.” R04/R02 evaluate accepted/confirmed shot facts against the frozen player/visit/groups and the active rules profile/version. A removal becomes a pot outcome only with appropriate evidence or explicit confirmation. “No supported foul” is not proof that a shot was legal.

First contact, cushion-after-contact, contact/pot ordering, brief off-table-and-return events and exact pocket often remain Unknown/Pending/Unresolved from A/B alone. Hold the dependent ruling, or obtain explicit player facts. A no-removal layout change can propose a turn change after legal-shot facts are resolved; it cannot declare a miss or foul merely from motion. Sky and the scenario page use the same typed review choices. Neither owns match truth. Existing declared rules support and reference profile remain unchanged by this documentation decision.

## O10 — Post-shot Trajectory Reconstruction & Replay Model (planned, optional)

```text
completed shot + settled A/B + bounded existing-media window
    → asynchronous forward/backward/bidirectional analysis
    → semantic ShotReplayModel with gaps and evidence provenance
    → replay graphics / coaching / V04 comparison / PH02 fitting
```

- **O10.01:** reference existing pre-roll/activity/settlement/post-roll media by source PTS, shot, calibration, checksum and canonical revision. Define byte/duration/job/retention limits and cancellation before enabling; record truncated/expired windows honestly. Reuse current recording/replay ownership; no second recorder.
- **O10.02:** reconstruct paths using measured combinations of detector/association, optical flow, forward/backward tracking from known endpoints, optional filtering and local contact re-analysis. Existing O04.01 tooling supports experiments. Processing may take longer than real time; bound its queue and resource use so live match updates continue.
- **O10.03:** emit a reusable semantic replay model with ShotId, source timeline, start/end intervals, calibration/model/revision, optional ball and cue trajectories, collision/cushion/pocket events, observed and reconstructed evidence, confidence type, gaps and unavailable reasons. Exact schema/type names remain a design choice.
- **O10.04:** align/render the model through the shared scene and historical annotation infrastructure on real-video and clean top-down replay; later 3D, cue paths, contact/pocket graphics, highlights, Shot Lab, statistics and coaching consume supported fields.
- **O10.05:** held-out trajectory/contact evaluation, resource/processing-time budgets, correction/revision invalidation and graceful partial/unavailable fallback. A failed reconstruction never invalidates an accepted match outcome. Later useful evidence can create a new review proposal through O05, never silently rewrite history.

Distinguish **Observed**, **Inferred**, **Reconstructed** and **Simulated** in the enrichment contract/UI; this is not a claim that all are existing GameState enum values. Keep high-rate paths outside the match journal and retain durable references only under a defined retention policy.

## Physics, HUD and audio consumers

**PH01** retains a pure deterministic C# physics model with measured SI geometry and capability-specific validation. It consumes immutable state/evidence; GameState never depends on Physics, OpenCV or a simulator. Detailed fitting needs O10 rather than a production live persistent tracker.

**PH02:** settled A/B + bounded shot media → O10 trajectories/contact intervals → validated PH01 best-fit reconstruction, with residuals, uncertainty, model version and non-identifiable speed/spin left unknown. **PH03:** validated reconstruction → labelled replay/what-if/coaching, including softer/stun/screw/side experiments where supported. Neither can override observed or corrected canonical facts.

**V04/C02:** frozen V02/V03 prediction (or explicit PH04 simulation) + source-aligned O10 replay model + accepted shot/layout references. V04 presents supported actual-versus-predicted path/contact/cushion/finish comparisons; C02 explains only evidence-backed differences. Partial reconstruction means partial/unavailable replay/coaching detail, while the match remains usable. Reuse shared phone/TV scene, T11/T12 history/export and T07 transforms. PH02 fitting is optional for directly observed comparisons.

**O06** retains optional timestamped audio corroboration after source-clock alignment and feed availability are established. Audio alone does not adjudicate outcomes; absent/unsynchronized audio does not block match reconciliation. **O07/O09** retain detector comparison scope and licensing/held-out gates. No new detector migration is implied.

## First authoritative vertical slice and acceptance

1. Obtain trustworthy table A; record Rich current, yellows, visit, frame/game version and rules profile.
2. Start an activity window, tolerate the player/cue remaining over the table, then verify clear settled B.
3. Reconcile 7→6 yellows with unchanged reds, cue and black; propose one yellow removed with optional identity/pocket left unknown.
4. Review/confirm the physical pot where supported, admit once through the canonical boundary, and ask for any unavailable facts required by the rules.
5. R04/R02 propose whether Rich continues; accept the consequence only with sufficient rule facts.
6. Correct the removal/pot or shooter, then reload/reduce the same history and obtain the same inventory, player, visit and score projections.

A separate replay slice takes this completed shot's retained media → O10 analysis → replay model → annotated trajectory on TV. It does not gate the authoritative slice.

O01 keeps session-grouped Development/Holdout splits and two evaluation categories. The existing ≥60 real sequences / ≥20 held-out starting corpus target is retained as planned, not achieved; expand it to cover the matrix. Freeze labels, thresholds and denominators before evaluation. Repeatedly consulted holdouts become development data. Existing association tooling does not yet implement the aggregate evaluator described here.

| Core match gate | Planned acceptance / measurement |
|---|---|
| Settled A/B correctness | Correct group/count differences, false-removal proposals and missed removals; cue/black presence accuracy; full-table visibility and uncertain rate, stratified by lighting and obstruction. Initial supported removal proposal targets: precision ≥0.95, recall ≥0.90; these do not authorize automatic admission. |
| Activity/settlement | No premature settled admission in unsafe/moving/obstructed cases; report start misses/false triggers and settlement latency p50/p95 from true stillness and from clear-view availability. Initial candidate target: trustworthy B within 3 seconds of a clear stationary view; this is proposed, not a guaranteed timeout-to-ready. |
| Continuity/context | No reconciliation across source/timeline/calibration discontinuity or incompatible player/frame/version. Test a minute-plus obstruction, missed onset, renewed movement during settling, multiple shots and manual ball handling. |
| Canonical safety | Zero confident wrong canonical game-state mutations from weak visual evidence in the held-out/challenge set; report denominators, uncertainty and all errors. This is an acceptance gate, not a universal guarantee. No inferred contact/legality from missing evidence. |
| Admission/correction | A real observed physical outcome reaches review; accept once, reject without unchanged recurrence, correct/undo and reload deterministically. Group/count facts remain consistent with unresolved individual identities; old journal fixtures remain compatible. |
| Runtime/resources | Preserve exact pixel/PTS receipts, bounded active/latest ownership, master footage and relevant T13/T14 safeguards. Measure acquisition/worker latency, activity cadence, memory/CPU/GPU/battery/thermal and at least 30-minute recovery/endurance against disabled baseline. Set budgets from O02 measurements before enabling; real-time trajectory quality is not a match gate. |

The core matrix includes legal pot, no-removal shot, multiple removals, cue scratch/replacement, black removal, rack/reset, same-colour crossings, near-pocket balls, detection dropout, striped sunlight, shadow/hand/cue/body obstruction, long obstruction, slow/fast activity, missed onset and source interruption. Required rule facts are labelled separately from visually observable facts.

| Replay trajectory gate (O10 only) | Planned acceptance / measurement |
|---|---|
| Coverage and geometry | Trajectory coverage and visible-sample recall, centre error, gaps and track recovery; inherited candidate targets ≥90% visible recall and p95 centre error ≤0.5 measured ball diameter for declared supported cases. |
| Identity and contacts | ID switches, ambiguous association, collision/cushion/pocket accuracy and ordering intervals. No silently asserted identity swap; unresolved alternatives remain visible. |
| Cost and availability | Processing time, queue/retention bounds and resources; fail or cancel a reconstruction while proving that match admission/correction still works. |

Old 30/60 Hz tracker cadence and ≤250 ms boundary targets remain useful optional O02/O10 experiments; they no longer block count/presence match maintenance by themselves.

## Migration audit and implementation order

| Previous dependency assumption | Revised owner / document |
|---|---|
| Global priorities and first slice require tracked pot/miss | `glow-up-plan.md`: O03/O04/O05 core path; O10 optional branch |
| O04.01 live association must precede game outcomes | This design: retain a-c delivery evidence; d/e optional; start O04.02 contract/count work |
| O02 must validate persistent moving-ball tracking | `o02-frame-pairing-plan.md`: pairing/activity/settlement budget; detailed trajectory acceptance under O10 |
| G06 completion waits for source-time tracks/pocket paths | `g06-match-scenario-dashboard-plan.md`: planned group/count proposal and canonical review seam; current unique-ID Lab limitation stays explicit |
| V04/C02 and PH02/PH03 consume O04/O05 trajectories | HUD and analytics/replay plans: O10 model plus accepted shot context; physics separate |
| O01 has one combined tracking/game release gate | Corpus guide and this design: separate match and replay labels/metrics |
| Delivered R01 story includes a pocket approach | R01 plan: preserve historical delivered story; additive aggregate-outcome integration is future O04/O05 work |
| Historical progress/reports prescribe previous next work | Retain dated measurements; add current-direction notices and current progress entry rather than rewriting results |

Next implementation: connect O08 clear/still observations to the count evidence contract and implement the O03 activity/obstruction/settlement lifecycle. Then add the O04 review proposal, G06 presentation, O05 canonical aggregate input/event/reducer with corrections/reload, and R04/R02 rule consequences. Collect O01 match evidence and resolve required O08/O02 reliability/resource blockers alongside these bounded chunks. O10 follows separately. The first domain contract builds; no camera or device integration has been changed.

## O04.01 — Inventory-aware association from last-seen positions

**Historical association slice, retained under its existing ID.** O04.01a-c tooling remains delivered within its recorded limits. The association algorithms and metrics below now support optional positional evidence and O10 trajectory experiments; they are not prerequisites for O04 group/count reconciliation. The new primary O04 implementation starts at O04.02.

**Original goal:** use recent, source-timed ball positions and the supported game's maximum inventory to associate detections across frames, distinguish stationary balls from plausible movement, and reduce duplicate, shadow and sunlight-pattern detections. This is the first bounded O04 association slice; it does not add a second game ledger or change the detector by itself.

**Inventory semantics:** distinguish the ruleset's maximum ball counts, a clear settled layout observed in this session, and per-frame sightings. A maximum is a hard upper bound, not an exact expected count. Only an accepted/reviewed event may change canonical in-play or pocketed state. A ball missing from a frame remains unseen/occluded/lost/uncertain according to evidence; its old position is never emitted as a current observation and absence alone does not prove a pot.

**Track record:** maintain session-scoped `TrackId`, optional verified `BallIdentity`, last confirmed calibrated table position, last-seen source PTS, velocity estimate when timing and matches support it, appearance/class evidence, association quality, motion state, visibility/loss state and uncertainty reason. Keep detection IDs, track IDs and GameState ball identity distinct. Expire or invalidate tracks on source, generation, calibration or timeline discontinuity. Do not give visually indistinguishable same-colour balls durable identities unless the sequence supports a unique, reviewed mapping.

**Association:** retain the raw candidate detections and their feature scores. Generate one-to-one detection-to-track assignments with gates for elapsed source time, plausible displacement, appearance/class compatibility and table geometry. Use the ruleset maximum to reject globally impossible excess assignments only when candidate evidence permits; never force a low-quality candidate to fill an inventory slot. If more than one assignment remains plausible, surface ambiguity and keep the track unresolved. A stale track can seed a local search or explain a possible movement, but cannot count as visible.

**Movement:** compare associated, fresh positions in calibrated table coordinates over mapped source time. Establish stationary noise and a movement deadband from labelled stationary/light-change sequences, then use hysteresis for `stationary`, `moving`, `settling` and `uncertain`. Illumination change, camera interruption, weak ROI texture, occlusion, crossing, a large source-time gap or calibration change must reduce confidence or trigger reacquisition rather than manufacture movement or continuity. O03 remains the owner of shot boundaries and settling lifecycle.

**Delivery steps:**

1. **O04.01a — Freeze the data contract and cases [diagnostic shot import implemented; real collection open].** The hand-labelling flow retains the detector result paired with each exact frame while hiding it from the annotator; editable stable ball IDs support reviewed identity across samples. `import-diagnostic-shots` maps labels through the saved calibration into `table-2x1` and writes detector candidates separately for each frozen split. Manually saved samples are about one per second, so they support settled layouts, lighting changes and slow movement but do not establish fast-shot recall. Use O01's identity/visibility labels and O09/O08 lighting samples; add reviewed labels only where the current corpus cannot distinguish a real ball from a false candidate or record a ball's last visible position. Include blind-pattern transitions, stationary balls under changing light, missed/extra detections, slow movement, temporary hiding, crossing same-colour balls, pocket approach and source/calibration resets. Freeze session-grouped development/holdout splits before tuning.
2. **O04.01b — Deterministic offline association [evaluator implemented; real evaluation open].** The `SkyEye.Observation.Workbench association` command consumes one frozen Development or Holdout detector output and uses calibrated table coordinates, source PTS, last-seen gated matching, a configurable ruleset inventory ceiling, explicit ambiguity abstention and separate predicted/ground-truth movement thresholds. It reports per-session/per-tag detector misses/extras, track continuity/identity changes, inventory conflicts, movement confusion and coverage. Replay the same candidate with different association/movement parameters on Development; hold ground-truth movement thresholds fixed. The checked-in seed has no real timed shots, so accuracy/threshold conclusions remain open.
3. **O04.01c — Diagnostic presentation [offline viewer implemented].** The opt-in sequential view of current candidates and tracks shows track labels, last-seen age/position, current matched or predicted position, motion state, association quality and unresolved alternatives. The workbench writes a standalone HTML viewer for one selected sequence, with checksummed raw JPEGs beside a calibrated table diagram, frame stepping and source-time display. This view is read-only; it is not a live overlay and cannot edit labels. The viewer is capped at 600 frames and 64 MiB of source images. The raw image has no table overlay because the corpus does not retain the required pixel-to-table transform.
4. **O04.01d — Bounded live association feed [deferred/optional].** Preserve this reserved ID without claiming delivery. Reconsider only if measured activity gating or O10 reconstruction benefits from live association. It is no longer a prerequisite for O03/O04/O05 core match completion. Existing frame ownership/source/resource requirements still apply if enabled.
5. **O04.01e — Association consumer integration [planned/optional].** Supported positions may enrich reconciliation and replay evidence. Count/presence outcomes must work without assigning every same-colour ball a durable identity. O10 owns complete trajectories/contact reconstruction; O05 owns admission.

**Association-only acceptance (not a core match release gate):** replay is deterministic for a fixed recording/configuration; every reported sighting points to a current source frame; no predicted/stale point is presented as observed; the inventory maximum never fabricates missing balls; no silently committed identity swap occurs in the crossing/loss challenge set; ambiguity and reacquisition are explicit. Report recall, precision, ID switches/losses, position error and false movement by lighting/motion case on untouched real sessions. Retain the existing proposed O-series target of visible-sample recall ≥90% and centre error p95 ≤0.5 measured ball diameter for supported cases, with zero silently committed identity swaps; these remain targets, not achieved results. Do not claim improvement until O01/O08 held-out evidence and O02.05 device cost gates pass.


## Reference review and reuse register

Reviewed upstream code/README/licence trees on 6 October 2026, pinned below. Recommendations are design choices for SkyEye, not claims these projects have validated SkyEye's hardware or English-pool accuracy. No external source is copied in this documentation update.

| Reference and pinned revision | Small useful concept and limits | Licence/reuse disposition |
|---|---|---|
| [Pool-Table-Tracker](https://github.com/sofi-vega/Pool-Table-Tracker/tree/14dbbcb559ecd24ba492ba0916ba08b8c919302d) | `Pool-Tracker.py` uses Shi-Tomasi points/pyramidal Lucas-Kanade flow and cue-contour direction estimates. Its cue-ball-centred visual tracking is inspiration for ROI flow, not a ready durable multi-ball association solution. Start O02 with independently implemented ROI measurements/flow status checks; cue direction is a later optional O10 cue-strike aid, not proof of first contact. Avoid its interactive sliders/render loop. | [MIT](https://github.com/sofi-vega/Pool-Table-Tracker/blob/14dbbcb559ecd24ba492ba0916ba08b8c919302d/LICENSE), copyright Sofia Vega 2025. If copying substantial code, retain copyright and permission notice. Prefer small independent C# equivalents. |
| [Pooltool](https://github.com/ekiefl/pooltool/tree/81040e931facada508bbab2e53ab820295bbd90e) | Review `evolution/event_based`, `physics/evolve` and selectable `physics/resolve` models: next-event scheduling, sliding/rolling friction, ball collisions and linear/circular cushion/pocket geometry. PH01 begins with event ordering and a simple collision/rolling slice; jaws/spin models require separate validation. No Python/rendering dependency in SkyEye. | [Apache-2.0](https://github.com/ekiefl/pooltool/blob/81040e931facada508bbab2e53ab820295bbd90e/LICENSE.txt). Direct reuse needs licence, relevant attribution/copyright and modified-file notices; preserve applicable NOTICE content if present. No standalone NOTICE was found in the inspected tree. Check file/dependency/asset terms before any import. |
| [abeven/pool-visualizer](https://github.com/abeven/pool-visualizer/tree/54ad3cc6aabb4947ba8bc96b7d357ae9809da09b) | This is the selected match for the brief's unqualified repository name. `src/physics/engine.js` chooses the next transition/collision/pocket event, evolves balls, then resolves the event. Useful compact event-loop reference. README says its physics was ported from Pooltool; do not treat advertised exactness or cloth presets as measured truth. | README declares MIT, but no standalone licence was found and GitHub reports no detected licence. Pooltool-derived provenance needs reconciliation with upstream Apache notices. **Behavioural reference only; do not copy source until rights/notices are clear.** |
| [Four-Ball_Billiards_ThreeJS](https://github.com/skoo1/Four-Ball_Billiards_ThreeJS/tree/036052d6edc05122f7806ac0f38e3c9ed7213801) | `physics.py` separates rendering from spin/throw/friction/cushion and elevated cue models; useful later headless model/testing ideas. Its physics advances with `dt`; do not confuse it with the preferred event-driven scheduler. Carom dimensions/coefficients and advertised speed are not English-pool validation. Masse/jump remain late research, no Three.js/UI port. | [MIT](https://github.com/skoo1/Four-Ball_Billiards_ThreeJS/blob/036052d6edc05122f7806ac0f38e3c9ed7213801/LICENSE), copyright Musculoskeletal BioDynamics Lab @ KAIST 2026. Preserve copyright/permission notice for substantial reuse and audit per-file/dependency terms. |

Before copying any source, recheck the exact revision/file licence and SkyEye's distribution requirements; record repository/revision/files, original notices, modifications, and copied/adapted versus independently implemented status in a checked-in reuse register. Prefer compatible MIT/Apache sources after that check. Ambiguous reuse remains reference-only. Do not import recordings, models or renderer assets merely because the root source licence is permissive.
