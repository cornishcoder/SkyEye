# Corrected history, analytics, smart replay and practice roadmap

Updated 9 October 2026. These consumer missions remain planned at their recorded scope. The [settled-state architecture](temporal-tracking-design.md) now makes O03-O05 the primary automatic match path and O10 an independent optional replay/trajectory path. Preserve existing declared-history delivery and evidence; this update is documentation only.

SkyEye should remember how players perform over time, find meaningful moments automatically, and turn real matches into better practice. Adapt the supplied analytics/practice/replay brief to SkyEye's existing state, scene and media architecture. No external product implementation is a dependency.

## Ownership and existing missions

```text
Settled A/B reconciliation or explicit player declarations
    -> accepted shot evidence / corrected SkyEye.GameState events
    -> corrected match and shot history
    -> rebuildable Shot Record projection
    -> analytics / search / replay / drills / Sky tools

Completed shot + settled A/B + bounded media -> O10 replay model
    -> validated SkyEye.Physics fitting / optional trajectory consumers
    -> labelled simulation / Shot Lab / supported comparison
```

`SkyEye.GameState` remains authoritative. High-rate tracking samples stay in bounded evidence/media storage, referenced by accepted events; they do not become the match ledger. Durable analytics indexes are rebuildable projections with a canonical journal revision, not independent truth. Corrections/undo must repair affected records, aggregates, query results and playlist membership, including downstream visit/run/player attribution. Immutable exports retain their revision and can be regenerated; already-shared files cannot change automatically.

| Existing mission | Extension here; retain its ID and ownership |
|---|---|
| R01; G01-G05; declared R04/R02 | Authoritative identity, match/frame/visit history, outcomes, corrections and rules. Extend typed seams only for missing accepted facts; never build a statistics ledger beside them |
| O01-O05 | Activity/settlement, group/count physical outcomes, frozen context and reviewed canonical admission. No continuous identity/trajectory prerequisite; O06 audio remains optional corroboration |
| O10 | Asynchronous post-shot replay/trajectory model with coverage, contact evidence and uncertainty; optional analytics/coaching enrichment, never a match ledger |
| C01/C02 | Structured geometric explanations and evidence-bounded replay coaching. Consume difficulty/history and V04 comparison; no second coaching engine |
| C03 | Keep basic session statistics and summaries here; its delivered frame-summary subset is not complete shot statistics |
| T03-T05; T08-T12; T06/T07 | Existing clip/recent-window playback, source geometry, compositor, correction, annotation history and export. Smart replay selects media; it does not record another stream |
| V01-V04 | Shared semantic HUD, geometric/physics predictions and actual-versus-predicted presentation; retain V04 for comparison |
| PH01-PH03 | Physics model, evidence fitting, reconstructed replay and counterfactuals; retain their validation gates |
| A02-A05 | Real voice integration and shared command/tools. Add history/practice tool consumers when their missions exist, not speculative capabilities now |

New IDs use unused AN (Analytics), SR (Smart Replay), PR (Practice), C04 and PH04 series slots. Existing IDs are preserved.

## Priority and dependency order

| Priority | Delivery slice | Why / prerequisites |
|---|---|---|
| P0 — current | O01 match corpus + O08 reliability + O02 foundations → O03 → O04 → O05 → R04/R02 | Reliable settled comparisons and reviewed match consequences; voice shares typed review/controller seams and retains its separate acceptance |
| P1 — first history value | AN01.01 -> C03.01 + SR01.01 + AN06.01 | Partial records from corrected declared/accepted events, frame summaries, bounded search and CSV/JSON export. No perfect tracking or physics prerequisite; unknown outcomes stay unknown |
| P1 — optional analysis foundations | O10 retained-media reconstruction → V04/C02/PH02/PH03 | Independent trajectory/coverage/cost evaluation. A failed replay model does not invalidate accepted match outcomes |
| P2 — richer match value | AN02, AN03, SR02, SR04, then AN04 | Versioned geometry, comparison, semantic replay playlists and selected-shot annotated export. SR02/SR04 can start for declared shots if source/media mapping exists; automatic categories wait for O05 |
| P2 — practice slice | PR01 -> PR02 -> PR03 -> PR04 | Definition + recognized setup + a declared attempt + retained progress before automatic scoring. Reuse reliable table recognition; auto attempts require O03-O05 |
| P3 — later consumers | SR03, C04; PH01 -> PH02/PH03 and PH04; V03/V04 | Reels after playlist value; personalized practice after enough corrected metrics. Physics branch follows trustworthy evidence and capability validation; it does not block basic statistics |
| P4 — low | AN05 local rating | Only after stable local identity, sufficient results and correction-safe history; no social infrastructure |

R01 is delivered in this checkout, so no active R01 work is displaced. Keep T13/T14 and X01/X02 media/recovery/endurance gates open; address prerequisites when a selected slice relies on them. V01/V02 remain independently useful geometry presentation work. This ordering ranks outcomes, not a requirement to finish every AN mission before continuing observation. Historical next-step notes do not override the current settled-state match priority.

## What current evidence can support

| Evidence tier | Useful scope | Still missing / gated |
|---|---|---|
| Delivered declared match history | Players within a match, frame winners/results, session score, supported declared turn/group/foul/outcome facts and corrections | Not a complete per-shot census; past declared-shot association may be incomplete. Cross-session player identity requires AN03 identity reconciliation |
| Delivered settled geometry and candidate planner | Supported distances, cut angle, obstruction/path geometry, direct/bank/kick classification for a chosen valid candidate | A suggestion is not proof of the attempted target/pocket. AN02 freezes the chosen context; no pace/spin/finish or human probability claim |
| Delivered recorded-media/scene infrastructure | Source-clock clips and historical correction/annotations where retained; selection/export can reuse T03-T05/T08-T12 | Shot-to-media references are not yet a delivered projection. Expired recent windows cannot be promised as durable replays; historical gaps retain honest fallback |
| O03-O05 accepted physical outcomes (planned) | Automatic activity windows, settled A/B count/presence reconciliation and reviewed outcomes with coverage | First contact, exact pocket, legality and ordering may remain unresolved; a no-removal shot does not establish miss versus safety or intent |
| O10 replay model (planned optional) | Source-aligned observed/reconstructed trajectories, collision/cushion/pocket evidence where supported | Coverage, identity and contact uncertainty remain explicit; failure affects enrichment only |
| Validated physics + measured observations | Supported Shot Lab parameters, fitting, positional/contact/pace comparisons | Capability-specific fidelity, measurement bounds and table calibration; no numerical coaching or spin reconstruction from unsupported inputs |

All metric/query responses distinguish **available**, **partial** and **not yet measurable**, with eligible count, total/unknown count, scope, canonical revision and reason. Null is not zero. Use explicit time semantics: source PTS for media/elapsed shot duration, recorded session date for date search; wall-clock declarations alone are not measured shot times.

## Analytics missions

### AN01 — durable, queryable Shot Record projection (P1)

**Depends on:** R01 corrected ledger and declared match identity; O03-O05 for automatic physical outcomes and O10 only for optional trajectory/contact fields. Own a pure C# deterministic projection plus companion-owned rebuildable persistence/index outside Android/UI domain logic.

**AN01.01:** project one completed declared/accepted shot with stable shot/match/frame IDs, nullable player, before/after references where recorded, result/unknown status, event references, provenance and canonical revision. Do not turn an unrelated foul correction into a shot or manufacture missing past shots. Extend accepted domain facts in a bounded versioned chunk where the ledger lacks association; do not scrape UI text. Follow with incremental/restart rebuilding and query paging before trajectory enrichment.

Grow optional fields by capability: visit; date and source timestamps; target ball/group and intended pocket where declared/selected; pot/miss/safety/foul/unknown and potted balls/actual pocket; shot type; versioned difficulty; cue start/finish; trajectories/cushion/collision evidence references; video window/reference; confidence type, review/corrections and unresolved reasons. Preserve intended versus observed pocket and observed versus inferred facts. Store evidence references, not the high-rate trajectory stream in GameState.

**Gate:** correcting an earlier pot, player/turn or winner and undoing a declaration rebuilds identical records after restart, repairs dependent attribution, and invalidates affected aggregates/search/playlist caches. Rejected proposals and duplicate events produce no extra completed shots; incomplete sessions remain partial.

### C03 — basic session statistics and summary extension (P1)

**Depends on:** R01/G history for existing frame summaries; AN01 for shot aggregates. Preserve C03's delivered frame-summary slice. **C03.01:** two-player session summary of recorded frame/match results plus shot/pot/foul totals only where facts are associated and supported. Add miss count and pot percentage only with explicit attempted-pot outcomes and an honest denominator. Define pot percentage as successful eligible pot attempts / known eligible pot attempts; separately show unknown and unrecorded coverage. Multi-ball pots count balls separately from successful shot attempts.

Follow with longest/average run using corrected visit/turn boundaries, average shot time from comparable source-time boundaries, average difficulty from AN02-scored eligible shots, and separately supported scratches. Define eligibility, run-end treatment and fouls explicitly in each metric; incomplete visits are labelled. Match wins require recorded completed results, not an invented result for a stopped session.

**Gate:** one correction changes totals/runs/summary consistently; zero eligible attempts shows not measurable rather than 0% or division by zero. Missing trajectories or intent do not become misses, safeties, scratches or lost position. Fixtures compare declared/partial and accepted-complete coverage.

### AN02 — versioned geometric Shot Difficulty (P2)

**Depends on:** current validated candidate geometry/C01 path semantics; AN01 only for historical attachment. **AN02.01:** freeze a valid selected direct-shot context and expose deterministic cue-object/object-pocket distances, cut angle, clearance and pocket geometry with model/version, units, assumptions and unsupported reasons. Reuse `ShotPlanner`/`ShotMath` geometric difficulty and ranking rather than inventing a parallel planner; establish a documented stable scale and adapters.

Follow with supported bank/kick/cushion factors. An unselected suggestion is not the attempted shot, and unknown target/intent leaves historical difficulty absent. Historical values retain their original model version; optional rescoring produces an explicitly labelled version. Later player calibration belongs to AN04, not this first geometric model.

**Gate:** deterministic ordering, obstruction/unsupported cases and historical version explanation pass. Display geometric difficulty, never human pot probability. Calibration cannot silently rewrite past averages.

### AN03 — player/session/head-to-head comparison (P2)

**Depends on:** AN01, C03 and corrected R01/G identity; AN02 for difficulty views. **AN03.01:** compare two players in one session using common metric definitions and coverage. Follow with an explicit local identity/alias reconciliation across sessions and historical opponent/frame/shot comparisons. Matching a display name alone is insufficient. Difficulty-adjusted comparison first shows geometry distributions/bands with coverage; personal expected performance requires AN04 calibration.

**Gate:** no hard-coded UI or player names; correction/identity reconciliation updates the same projections. Comparisons show scope/sample size and unknowns; unequal/incompatible coverage cannot produce an unsupported claim about who played better.

### AN04 — trends and observed personal success (P2/P3)

**Depends on:** AN01-AN03, C03 and stable cross-session identity. **AN04.01:** trend available session/shot metrics over a selected date range. Follow with success by supported distance/cut/pocket/type, bank/double/kick, opponent and session, plus foul/scratch rate. Position success/error requires a preserved intended finish region and sufficiently accurate actual finish; cue finish alone does not prove positional intent.

Later fit player-specific expected make probability from eligible corrected attempts, with minimum sample sizes, held-out/time-separated validation, calibration/model versions and uncertainty. Sparse categories remain unavailable; no fabricated personal probability. Recompute affected windows after corrections.

**Gate:** coverage/sample thresholds are visible and correction-safe; geometry averages and personal probabilities remain distinct. Sky can explain higher difficulty without claiming an unvalidated adjusted probability.

### AN05 — local rating and household/club standings (P4)

**Depends on:** stable AN03 local identity and corrected completed match results/C03. **AN05.01:** optional versioned Elo-like local match rating with chronological deterministic replay, provisional status and rules for draws/unknown results. Difficulty/expected-performance inputs wait for AN04 validation.

**Gate:** corrected winner or identity rebuilds all later ratings consistently. No public profiles, followers, feeds or global leaderboard; local standings only.

### AN06 — corrected CSV/JSON data export (P1)

**Depends on:** AN01 and C03; AN02/temporal evidence only for their optional columns. **AN06.01:** export a bounded selected session/shot range with schema/revision, IDs, date/source time, nullable result/player/difficulty, before/after positions, intended/actual pocket, type/foul, cue start/finish, provenance/correction and evidence references. Include definitions/units and coverage; unsupported values are explicit null/empty with reason. Export canonical projections, not detector internals or raw tracking samples.

**Gate:** CSV escaping and JSON round-trip preserve IDs, unknowns and revision; corrected re-export agrees with UI/tool queries. Useful for debugging/evaluation/datasets without treating player declarations as independently measured labels.

## Smart Replay missions

### SR01 — deterministic shot search/filter projection (P1)

**Depends on:** AN01; AN02 for difficulty and AN03 for historical opponent/identity filters. **SR01.01:** bounded/paged player/session/date/result/group/foul filters shared by touch and typed Sky tools. Follow with known target/pocket, difficulty range, type, distance and bank/double/kick filters. Explicitly version category thresholds (for example long/short); unsupported filters return not measurable, not an empty set suggesting no matching shots.

**Gate:** natural-language requests become validated typed filters over known facts; ambiguous player/session/shot references are clarified. Unknown intent/group/position is not silently excluded as a measured negative. Corrections change query membership at the current revision.

### SR02 — semantic pot/miss/scratch playlists (P2)

**Depends on:** SR01, AN01 source-window references and T03-T05/T08-T12 media services. **SR02.01:** play existing retained media windows for all known pots or misses in one session; keep ordered shot IDs, source timeline/PTS and small pre/post margins. No combined MP4 required. Resolve to saved footage where possible; bounded recent replay is usable only while available. Follow with supported long pots/doubles/scratches and best-five selection using a documented geometry/result criterion and coverage, not invented shot quality.

**Gate:** each item seeks the correct historical shot, survives corrections, and shows missing/expired/unrecorded footage. Playlist selection does not create another recording pipeline or permanently save every shot by default.

### SR03 — condensed automatic highlight reel (P3)

**Depends on:** SR02 and validated existing T09-T12 export/compositor lifecycle. **SR03.01:** render a short selected playlist to one verified MP4, removing between-shot dead time with explicit margins and source map. Follow with optional highlights selection criteria; reuse audio/timing/orientation, cancellation, atomic publication and resource limits.

**Gate:** source/shot order and historical scene alignment match the playlist; cancellation cannot publish a partial reel. Corrections mark prior derivatives stale and regeneration uses the new revision; originals remain available.

### SR04 — selected-shot annotated export (P2)

**Depends on:** AN01 source/history references, T08-T12 compositor/journals and SR01 selection. **SR04.01:** export one selected historical shot with the actually retained score/target/pocket/result context and historical scene. Support additional paths/finish/comparison through V missions only when recorded/measured evidence exists. Explicitly label later analysis as retrospective and preserve its model/revision rather than pretending it was displayed during play.

**Gate:** old clips never receive today's table geometry or annotations. Missing historical scene produces a documented plain/partial export. Predicted and observed paths remain distinct; physics/finish overlays wait for their capability gates.

## Practice / Drills epic

### PR01 — executable drill definitions (P2)

**Depends on:** existing normalized table coordinates/shared commands; reuse AN02 for optional difficulty. **PR01.01:** one straight-pot drill definition with versioned required ball positions, target/pocket, success criteria, attempt count and scoring. Follow with X, spot-shot, positional, cushion/bank and cue-control definitions only as their criteria are measurable. Parameterize table orientation/ball radius and tolerances; definitions are domain data, not static coaching cards.

**Gate:** definitions validate geometry, targets and supported criteria; touch and Sky refer to the same drill ID/version. A positional drill cannot claim automatic success without measurable finish criteria.

### PR02 — recognized drill setup and guidance (P2)

**Depends on:** PR01, valid calibrated settled-table recognition/correction and shared semantic scene/commands; A03/A05 for spoken guidance. **PR02.01:** match one drill layout to corrected recognized positions with table-coordinate tolerances, highlighting the ball and direction to adjust. Start explicitly when verified or player-confirmed; uncertainty prompts review. Follow with orientation/assignment handling and concise "move that yellow" guidance using stable references.

**Gate:** small allowed placement error passes, out-of-tolerance/ambiguous or stale layout does not; recalibration invalidates old setup. No pixel-perfect placement requirement or silent wrong-ball instruction.

### PR03 — attempt tracking and scoring (P2)

**Depends on:** PR01/PR02, AN01 and canonical accepted practice context; O03-O05 for automatic attempts/outcomes. **PR03.01:** start/end one declared attempt, record known success/miss/unknown and correction, then reset/setup for the next attempt. Introduce a bounded practice-mode/context seam in GameState where needed; do not fabricate competitive frames or a parallel outcome ledger. Drill definitions can be separate assets; accepted attempt facts use the canonical event/correction path.

Follow with supported automatic success, measured cue finish/time/difficulty and consistency. Miss direction requires measured target/path/aim; a missing pot alone does not reveal left/right. Cue finish and positional score require recognized/accepted positions; unknowns remain unscored.

**Gate:** duplicate/uncertain attempts never count twice/as failures, corrections repair scoring after restart, and match/practice mode transitions preserve their intended context.

### PR04 — drill history and progress (P2)

**Depends on:** PR03, AN01/C03 metric machinery and stable AN03 identity for cross-session progress. **PR04.01:** corrected history and known-success/eligible-attempt summary for one drill version. Follow with comparable time/finish/consistency trends and touch/Sky "how many did I make?" queries.

**Gate:** definition/version/tolerance changes are explicit; incomparable drill attempts are not merged. Unknown/partial attempts and corrections use the same coverage semantics as match analytics.

### C04 — Sky-generated evidence-based practice sessions (P3)

**Depends on:** mature C03/AN04 metrics, PR01-PR04 usable drills and A02-A05 tools. **C04.01:** map one sufficiently supported weakness to a bounded timed plan using validated drill IDs, measurable goals and an explanation with eligible sample counts. Follow with mixed twenty-minute sessions and feedback from completed practice.

**Gate:** Sky queries corrected metrics, explains why each drill was chosen and executes the same practice tools as touch. Sparse/unsupported positional or spin evidence yields an honest limitation/default player-selected drill, never an LLM diagnosis from conversation memory.

## Shot Lab / Physics and comparison

### PH04 — load a real shot into Shot Lab (P3)

**Depends on:** AN01 pre-shot layout/shot selection, validated PH01 capabilities and existing PH03 counterfactual seam; PH02 for fitted observed parameters. **PH04.01:** load one selected real miss (or other known shot) with its recorded pre-shot layout, cue/target, provenance and any supported observed parameters into the existing `SkyEye.Physics` request model. Missing speed/spin/aim stays unknown; explicitly entered assumptions remain separate from measured/fitted values.

Follow with direction/power and validated top/stun/screw/left/right parameter experiments; unsupported controls remain unavailable. PH01 owns simulation, PH02 reconstruction and PH03 alternative reruns/replay; PH04 adds the history-to-lab interaction and parameter workspace, not another simulator. Preserve the accepted real record when creating simulated alternatives.

**Gate:** "load my last miss" resolves a deterministic corrected shot ID, recreates its historical layout and reports unavailable footage/parameters. Supported simulations are repeatable/versioned; simulated outcomes never become observed events or rules evidence.

### V04 / C02 / PH02-PH03 — actual versus simulated (retain existing missions)

**Depends on:** frozen pre-shot V02/V03 prediction or explicit PH04 simulation, source-aligned O10 post-shot trajectories/replay model and matching AN01 shot/layout/calibration; PH02/PH03 for fitted/counterfactual comparisons. Keep V04 as the shared comparison presentation and C02 as the explanation consumer. O10 may finish later or fail: expose partial/unavailable replay detail without blocking the match. PH02 fits settled A/B plus O10 evidence; PH03 consumes validated reconstruction. Observed, Inferred, Reconstructed and Simulated remain distinguishable.

Compare supported contact point, object/cue lines, cushion contacts, final cue position and pace with residuals/coverage. Geometric direction-only comparison can precede complete physics; pace/spin/finish claims require validated capabilities. Numerical claims such as "2 degrees thicker" require independently measured accuracy and uncertainty narrow enough to support that resolution; otherwise show a qualitative/unknown result. No new comparison engine or metric may substitute simulation for observed reality.

## Typed Sky tools and acceptance

Future tool shapes are planning contracts, not registrations: query statistics(scope, metrics); search shots(typed filters, page); play playlist(shot IDs, target surface); export shot/range; start/setup/record drill; query practice progress; build practice plan; load Shot Lab(shot ID); simulate supported parameters. Each result includes canonical revision, eligibility/coverage, provenance and availability; mutations use existing host/version/operation-ID guards and the shared executor.

"How did I play tonight?", "What was my pot percentage?" and "Who played better?" use C03/AN03/AN04. "Show my missed yellows", "all my doubles against Mike" and "best five shots" use SR01/SR02 supported filters and selection definitions. "Shots where I lost position" waits for known intended and actual finish evidence. "Set up the X drill", "how many did I make?" and "twenty minutes based on tonight" use PR/C04. "Load that miss into Shot Lab" and "what if softer?" use PH04/PH03. Ambiguous references are clarified; unsupported requests return the missing capability.

The first history gate is a declared shot -> corrected AN01 record -> C03 summary and SR01 query -> correction -> changed results -> restart -> identical projections. Extend it to a source-backed SR02 playlist and one PR03 declared attempt before expanding automatic observation. Domain/query/metric logic stays pure C# where practical; Android/UI owns media, persistence and presentation adapters. No runtime code, builds, API calls or device testing are part of this planning update.
