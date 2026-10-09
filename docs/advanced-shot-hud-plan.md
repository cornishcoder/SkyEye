# Game-style shot HUD and cue-ball outcome roadmap

Updated 9 October 2026. The [settled-state architecture](temporal-tracking-design.md) supersedes earlier tracking dependencies. V01/V02 retain their recorded delivery; V03/V04 and the dependency changes here remain planned. O03-O05 maintain core game outcomes, and O10 supplies optional post-shot trajectories for rich replay/coaching. This documentation update adds no runtime or device acceptance.

## Product intent

Make SkyEye's shot guidance feel like a polished pool-game or augmented-reality coaching HUD, with **predicted cue-ball travel after contact and likely finish as important as the object-ball potting line**. Keep live footage prominent and the real table easy to see. Use restrained translucent corridors, readable markers and distinct paths; avoid arcade clutter, neon overload and diagnostic-looking hairlines.

The cue route before impact, projected route after impact and estimated resting region are separate semantic elements. A direction estimate alone cannot establish travel distance or a final position. Uncertainty is part of the scene, not a reason to draw a falsely exact endpoint.

## Placement and ownership

| Mission | Dependencies and earliest useful work | Owns / excludes |
|---|---|---|
| V01 — Game-Style Shot HUD | Existing T01/T02 scene and commands, T13/T14 independent presentation/rendering, valid current shot candidates; T11/T12 history/export integration | Geometry-only presentation and semantic contract extension. No new tracking or physics; no dependency on completion of O01-O05 |
| V02 — Simple Cue-Ball Projection | V01 contract plus trustworthy calibrated collision geometry | Explicitly assumed post-contact stun/tangent direction. No realistic pace/spin/friction prediction; finish distance/region only when defensible inputs exist |
| V03 — Physics-Backed Cue-Ball Outcome | V02 contract and validated PH01 capabilities; PH01 retains measured-model/capability gates; O10 informs later fitting | Adapter/presentation for SkyEye.Physics results. No second physics engine; capability-specific validation before pace/spin/cushion/finish claims |
| V04 — Predicted vs Actual | V02 or V03 frozen pre-shot prediction plus a trustworthy O10 post-shot replay model, accepted shot identity and source alignment | Comparison presentation and evidence-grounded C02 explanations. O10 owns asynchronous reconstruction; PH02 owns fitting, PH03 owns counterfactuals/reconstructed replay |

R01's Match & Table State core is delivered and remains authoritative for frame/turn/ball identity and corrections. Where available, selections use its current legal-target/state versions; the geometry-only branch adds no gate to basic R01/manual match delivery. Predictions never become observed game events or rules decisions.

V01/V02 may be scheduled alongside appropriate presentation work because current candidate geometry already exists. They do not displace O01-O05, close T13/T14's outstanding physical acceptance, or require every TV/endurance gate before offline design/fixtures. Live HUD acceptance must verify the relevant source, projection, clear/expiry and media-resource gates. Accurate observed comparison requires O10 evidence; accurate prediction remains PH-owned.

## Extend one semantic scene

Reuse `ShotAnnotations`, `SurfacePresentation`, `ShotSceneProjection`, `TableScene`, native phone/TV renderers and the existing coordinator. Today's annotations already select incoming cue/object paths, target, ghost, pocket and label; extend them without reinterpreting an old incoming `CueBallPath` as a post-impact prediction. Keep existing command/version/freshness admission. A conceptual `ShotOverlay` belongs within this shared scene/annotation model, not a separate HUD store or renderer.

Planned vocabulary, independently selectable where useful:

| Element | Meaning / source |
|---|---|
| AimCorridor | Translucent aiming band from validated candidate geometry; width is not automatically a confidence interval |
| TargetBall | Selected object-ball identity/location |
| GhostBall / ContactPoint | Geometric ghost centre and separately derived ball-to-ball contact point; do not confuse the two |
| ObjectBallPath | Candidate potting/bank route, later prediction or observed route with provenance |
| CueBallIncomingPath | Cue route before object-ball contact |
| CueBallProjectedPath | Post-contact route with explicit geometric/physics provenance and assumptions |
| CueBallFinishZone | Estimated resting region: centre, radius or ellipse/axes/orientation, confidence meaning and model provenance |
| CushionContacts / PocketTarget | Planned, predicted or observed cushion/pocket markers, with their role distinguished |
| Pace / Spin | Declared/assumed/modelled inputs or supported observed estimates; never inferred merely from a displayed line |
| ShotLabel / Confidence | Candidate number/rank, provenance and uncertainty; rank/heuristic score is not a pot probability |

Do not require every element in the first implementation. Unsupported/unavailable elements are absent, and command receipts/Sky context explain why. No fabricated pace, spin, cushion bounce, confidence percentage or finish zone to fill an otherwise empty HUD.

The planned prediction payload carries shot/candidate identity, frame/table/source/calibration versions, source timestamp/clock, prediction creation time, model/version, input assumptions, supported capabilities, path roles and uncertainty/unsupported reasons. Distinguish **Geometric**, **Physics-predicted** and **Observed** visibly; retain PH02/O10's additional **Inferred/Reconstructed/Simulated** enrichment labels (not assumed to be existing GameState enum values) for fits and counterfactuals. Confidence metadata states whether it is a heuristic, measurement bound or calibrated probability. A cosmetic fade must not imply calibrated statistical accuracy.

Store semantic geometry with declared coordinate space/units and calibration version. Current `TableScene` uses normalized original-image points: project table-space candidate/physics geometry into that existing render contract with the historical/current transform as appropriate. Do not put SI-unit physics positions directly into normalized image fields. Corridors, paths, markers and ellipse boundaries follow the same perspective correction, while text stays upright. Keep scene size, lifetime and resource bounds.

Version the existing scene/journal schema as elements are added, preserve old recordings and define unsupported-element behavior. PH results replace the simple prediction producer through an adapter to these same roles; changing the producer must not require a presentation rewrite.

## Bounded missions

### V01 — Game-Style Shot HUD

Use current direct/bank/kick candidate geometry to improve aim corridors, target/pocket markers, ghost/contact visuals and clearly differentiated incoming cue/object paths. Add restrained shot numbering/ranking and richer TV styling. Show cushion markers only where existing candidate segments actually establish them. Geometry confidence and obstruction/freshness gates remain intact. No post-contact prediction is implied by the incoming cue line.

**Smallest useful first vertical slice — V01.01:** one fresh, eligible direct-shot candidate from an explicitly requested observation. Extend typed scene roles for a translucent aim corridor and derived contact marker; retain target/pocket, ghost and incoming cue/object paths. Select it through the existing touch and Sky-compatible command path. The phone uses minimal labels, TV adds the candidate number and honest geometry provenance. Independently enable/clear the elements/surfaces, persist the source-timed scene in the existing journal and prove historical replay/export alignment. Clear, expiry or changed source/layout returns to clean live footage. No new physics, projected post-contact path or finish region in this slice.

Follow with V01's remaining bank/kick markers, TV layout and useful element combinations in bounded chunks. Future pace/spin/confidence panels may be designed here, but show them only when supported data exists. Programmatic Sky-compatible commands prove command parity; actual A02-A05 voice is now the next priority and requires its own real-session acceptance.

### V02 — Simple Cue-Ball Projection

Add an explicitly assumed geometric post-contact cue direction for supported collisions, first a labelled **Projected stun line**. Distinguish incoming path, contact and post-contact tangent. Specify applicable assumptions, such as an idealized stun collision; **Centre-ball estimate** may be used only for a model whose assumptions actually justify that label. Centre-ball aim alone does not establish sliding/rolling state or guarantee stun at impact.

The UI and Sky expose model limitations and assumptions, for example “That white-ball route assumes a stun shot.” Do not claim to account for measured pace, top/back/side spin, sliding/rolling transition, cloth friction, throw, cushion response or pocket jaws. Unsupported full-ball/near-degenerate or multi-contact cases return a bounded/unknown result rather than an arbitrary direction. A finite on-screen direction segment denotes direction only; its end must not resemble a predicted resting point.

Default to direction-only guidance. Add an approximate finish region only in a separately validated case with defensible travel-distance inputs, with provenance and broad uncertainty; otherwise “Show me the finish” reports that a finish estimate is unavailable. Never invent a stopping distance from collision geometry alone.

### V03 — Physics-Backed Cue-Ball Outcome

Consume `SkyEye.Physics` outputs through the shared semantic model. Progress from geometry collision → simple post-contact direction → pace/travel distance → top/back spin → side spin/cushion response → table-specific calibrated physics → probabilistic finish zone. Gate each capability against PH01 validation; completion of its first rolling/collision slice does not establish all spin/jaw models. PH02 fitting can later refine supported parameters from observed evidence.

Show input pace/spin as declared, assumed or fitted, and disclose table/model calibration. Prefer an uncertain resting region over an exact endpoint: centre plus radius/ellipse, confidence interpretation and model/assumption metadata. As measured uncertainty decreases the region may shrink. Unsupported interactions remain explicit and wider/unknown outcomes remain visible.

### V04 — Predicted vs Actual

Freeze the selected prediction, inputs, model/version, source/time and pre-shot scene before the shot. Compare against source-aligned O10 observed/reconstructed cue trajectory, contact/cushion evidence and finish region after trustworthy settled reconciliation. Do not recompute today's prediction and present it as what SkyEye predicted then. Preserve both versions across review/correction; gaps, identity ambiguity and clock uncertainty remain visible.

Use distinct predicted/observed styles and compare only supported quantities. C02 may explain thinner contact or extra travel only when measured evidence supports it; do not produce a “20 cm further” claim without calibrated units and adequate coverage. PH02 supplies fitted explanations with residuals; PH03 supplies later what-if scenarios. A supported geometric prediction can be compared before full physics fitting exists, with its assumptions still shown. Trajectory comparison depends on measured O10 reconstruction quality. Missing/partial reconstruction leaves comparison and C02 coaching partial or unavailable; it never blocks accepted match state or rules review. Two settled layouts alone do not establish contact order or a path.

## Phone, TV and Sky interactions

Pixel 11 defaults to the selected target, ghost/contact, object route, projected cue route and finish zone when available. Keep labels minimal. TV may add shot number/rank, provenance/confidence, pace/contact/spin, alternative paths and a white-finish region. Density presets choose typed elements on each surface; selecting full TV analysis does not alter the phone's presentation.

| Request | Semantic intent |
|---|---|
| “Just show me the line” | Minimal aim/path preset on the requested surface |
| “Where will the white go?” | CueBallIncomingPath + ContactPoint + CueBallProjectedPath with model assumptions |
| “Show me the finish” | CueBallFinishZone if supported; otherwise explain missing travel prediction |
| “Show the full shot” | All useful, supported elements for that shot/surface |
| “Put the full analysis on the TV” / “Keep my phone clean” | Independent TV analysis / phone live or minimal preset |
| “What if I screw it?” / “Show me with a bit of right side” | Later PH03 counterfactual input change, gated on validated top/back/side capabilities |

Sky selects shot identity, surface and semantic element set through the existing show-shot/show-annotation commands; deterministic application code derives and renders graphics. A conceptual `show_shot(surface: TV, shotId: shot3, overlays: [AimCorridor, GhostBall, ObjectBallPath, CueBallProjectedPath, CueBallFinishZone, Pace, Confidence])` describes intent, not a new parallel tool API or permission for model-drawn pixels. Touch dispatches the same commands and produces the same state/receipts. Presentation changes over cached scenes do not trigger perception or physics recomputation.

## Replay and acceptance

Use the same shot annotations for live video, recorded overlays, clean 2D reconstruction, future 3D reconstruction, comparison and coaching. T11 records the scene/assumptions/selected prediction at source time; T12 bakes that historical content into exported footage. T07 transforms historical video and overlays together for freeze/zoom. Reconstruction may use table-space semantic geometry, but must preserve prediction/observation identity and provenance. Recorded playback never substitutes current analysis, current corners or a later model for the historical scene.

Future delivery gates apply to every supported overlay:

- Graphics match real semantic state and reject stale shot/table/source/calibration references; unknown data stays unknown.
- Paths, corridors, contact and finish regions stay registered under phone/TV perspective correction, rotation, replay seek and freeze/zoom. The same scene has matching geometry/provenance on both surfaces despite intentional density differences.
- Sky-compatible and touch commands yield identical presentation state; independent phone/TV show/clear works, and delayed results cannot resurrect cleared/expired overlays.
- Clean live footage returns reliably after clear/expiry, retaining configured table correction and branding. Live video remains visible; no periodic perception, unnecessary encoding, unbounded scene or resource growth is added.
- Historical scene/model/assumptions survive journal reload and replay/export at the correct source PTS. Old recordings remain readable and changed geometry does not borrow current calibration.
- Cue paths visibly encode Geometric, Physics-predicted or Observed provenance. Uncertainty style and text agree; widths/dashing/fade cannot imply full physics or a calibrated probability absent supporting metadata.
- V02 verifies supported collision geometry, degenerate/unsupported cases and direction-only semantics. V03 verifies each consumed PH capability and finish uncertainty. V04 verifies frozen prediction versus observed evidence, missing intervals and corrected evidence before coaching claims.
- Physical Pixel 11/Chromecast checks establish readability, alignment, independent density and clear behavior for the delivered slice. Fixture/build success does not close physical acceptance or inherited T13/T14 gates.

All gates above are future requirements. This roadmap update performs no runtime implementation, new build or device test.
