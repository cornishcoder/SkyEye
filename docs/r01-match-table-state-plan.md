# R01 — Establish the Match & Table State Core

6 October 2026 · Match & Table State epic · R01.01-R01.07 implemented and verified. See the [completion report](r01-match-table-state-report.md) for actual APIs, device evidence and intentional limits.

The [user brief](r01-match-table-state-brief.md) is canonical. This plan makes it deliverable in small checked chunks. R01 establishes an authoritative understanding of a game unfolding over time. Replay, rules, scoring, coaching, shot reconstruction and future 3D simulation are consumers of that model.

## Scope and ownership

Create `src/SkyEye.GameState/SkyEye.GameState.csproj`, initially targeting the repository's platform-independent `net10.0` lane, and add it to the solution. Use plain C# records, explicit commands/events and a deterministic reducer. No Android, CameraX/Media3, OpenCV, Cast, GPT/Sky, UI or networking references; no timers, background workers, brokers or generic event-sourcing framework.

The library owns canonical match/frame/table state and transitions. `SkyEye.Core` and companion adapters translate existing evidence, serialize admission and publish read-only projections. Companion private storage owns journal I/O. External consumers cannot mutate arbitrary game fields. Application state references the domain snapshot rather than maintaining a competing game ledger.

Keep existing media, analysis and presentation behavior intact. Do not implement complete pool rules, automatic scoring, voice transport, a new detector, replay reconstruction or 3D simulation in this slice.

## Existing integration seams

These are present in the checkout; they are evidence sources, not an existing durable game model:

- `SkyEye.Core.Application.TableObservation` / `ObserveTable` and `TableObservationEffect` carry source scope, analysis/media generation, timeline, capture time, source PTS, freshness, outcome and table version. This application observation currently carries no ball list. The adapter must pair it with the matching `TableAnalysisSession` snapshot, never whichever snapshot happens to be latest.
- `TableAnalysisSession`, its `VisionResult` and `Analysis.TableState` expose detected balls/positions, uncertainty, obstruction and settlement. `TableTracker` remains a perception/settlement helper; it does not become the authoritative match reducer.
- `AppCoordinator` already serializes commands and rejects stale/duplicate operations; `SkyEyeState` exposes application projections. Add only game-input admission and the immutable game projection needed for this slice.
- T13 provides requested decoded-frame observations; T14 independently composes TV and journals source time. Game input preserves source/timeline identity and timestamps without depending on either media implementation or cadence.

Existing ball detections must not be assumed to have durable cross-shot identity. Use explicit domain ball identities in the scenario and a conservative adapter association seam; ambiguous same-colour association remains unresolved. Domain positions use an explicitly documented normalized table coordinate space, with calibration identity/version carried in evidence. Do not import CV point/ball types into the library.

## Bounded delivery chunks

Each chunk includes meaningful scenario checks. The following sequence has landed in checked commits; the completion report describes the resulting APIs and scoped acceptance.

| Chunk | Result | Acceptance |
|---|---|---|
| R01.01 | Add the pure library and scenario-test entry point; define minimal typed input/event/state contracts and explicit unknown states. | Pure .NET checks run without Android or video. Start a two-player match/frame with Rich current, an open table and a settled known layout. Confirm no forbidden dependency or timers. |
| R01.02 | Implement an ordered append-only history and pure reducer for frame start, table observation/settlement and shot start/end. | Reducing the same history produces identical state/version; duplicates do not append twice; stale/wrong-source input cannot overwrite current facts; interrupted timelines invalidate table freshness without erasing the match. |
| R01.03 | Interpret the minimal yellow-pot scenario, retaining evidence references, confidence and provenance. | Before/shot/after plus known pocket-approach evidence produces one inferred `BallPotted`; table counts/presence and current visit reflect the outcome. Disappearance alone, obstruction or ambiguous identity stays uncertain; no automatic group, score, foul or winner. |
| R01.04 | Add typed player confirmation/correction commands and amendment events. | "I'm yellows" outranks inferred assignment; "that red stayed up" repairs ball presence/counts; a disputed pot correction repairs the scenario. Lower-authority stale evidence cannot undo it. Replaying the amended history gives the corrected state. |
| R01.05 | Persist/reload the small versioned journal through a companion-side file adapter, outside the domain. | Process-restart fixture reconstructs the same match/frame/history without double-counting. Atomic publication and invalid/truncated journal handling are explicit. Old observations never become fresh live facts on recovery. |
| R01.06 | Integrate existing admitted settled observations and the typed correction seam through the application coordinator; expose canonical game state. | Matching source/generation/table snapshots translate correctly; source replacement/cancellation cannot publish stale game evidence. Preserve requested-only perception, candidate commands and media flows. Build affected app lanes sequentially and run relevant existing checks. |
| R01.07 | Finish the readable vertical scenario, integrated acceptance and completion report. | Full shot/pot/correction/rebuild story, stale/out-of-order, duplicate, uncertainty and precedence cases pass. Verify real observation-to-state integration on available devices; distinguish injected shot/pocket signals from actual detector evidence. Report the seven brief deliverables and update glow-up progress with observed versus untested limits. |

Use the existing executable-check style or a small dedicated pure .NET test project; choose the simplest fit during R01.01. Tests tell the scenario rather than mirroring implementation.

## Domain contract and transition decisions

**Inputs:** carry stable evidence/command IDs, match/frame identity, source/timeline generation, timestamps and provenance. Support minimal `TableObservation`, shot/motion start/end, settlement and correction inputs first. Provide small typed seams for ball observed/moved/missing/appeared and possible collision/cushion/pocket evidence. Do not implement every detector or infer detector capability from an API name. Document a source-time convention and adapter conversion; never compare PTS from unrelated timelines.

**State:** represent players, frame score/target/current frame; current player/visit, open/assigned colours, reds/yellows remaining, black and foul/visit status; known balls with positions where available, moving/settled and last settled observation. Unknown is explicit. Derive counts from resolved ball facts instead of independently editable totals. R01 can record declared group/foul/result facts without automatically adjudicating full rules.

**Events:** store meaningful accepted transitions such as match/frame started, table observed/settled, shot started/ended, ball/cue-ball potted and state corrected. Group/turn/foul/frame/match outcomes use the same event seam as supported commands/consumers arrive; do not fabricate automatic decisions to populate it. Envelopes retain schema version, sequence, event ID, evidence references/time and provenance. Keep video frames and high-rate samples outside the durable game journal.

**Ordering:** admission assigns an authoritative sequence; the reducer applies that order. Track duplicate evidence/command IDs and the accepted source-time watermark per relevant scope. Reject stale observations, or retain them separately as non-mutating evidence if useful; do not silently reorder committed events. Historical corrections append now and reference the fact/event they amend. Source/timeline changes require explicit admission and invalidate perception continuity.

**Observation versus interpretation:** a missing yellow and a pocket approach are evidence; `BallPotted` is an inference linking that evidence. A missing detection alone is not proof. Keep uncertainty available to consumers and defer automatic score/turn/group decisions to rules missions. No complete rules-profile implementation is required to record the minimal table transition.

**Confidence/provenance:** distinguish `Observed`, `Inferred`, `PlayerConfirmed` and `PlayerCorrected`. Confidence is optional and meaningful where supplied; do not invent numerical detector certainty. Player confirmation/correction outranks machine inference for the fact it resolves. Scope precedence to the fact/shot/frame so a later genuine shot can still change a corrected ball's state. Preserve original evidence and explanatory history.

**Corrections:** accept typed commands for group/current player, foul, ball presence and frame result as needed. "No, that wasn't a foul", "I'm yellows", "His turn", "That red stayed up" and "That's my frame" need straightforward domain seams. Prove them with command fixtures where no corresponding detector exists; GPT intent parsing and UI remain later consumers. Avoid arbitrary property patches.

**Persistence and reduction:** admission/interpretation produces events; a pure reducer produces immutable canonical state. Persist accepted events before publishing an acknowledged durable transition. Recover by reducing the same history; report invalid storage rather than inventing a score. Revalidate live table freshness at the application boundary after restart. No domain clock, disk or networking dependency is needed.

## Required vertical story

1. Start a two-player frame with Rich current and an open table.
2. Establish a timestamped settled layout with known yellow Y3 and pocket coordinates.
3. Admit `ShotStarted`, then evidence that Y3 disappeared and its trajectory approached the bottom-right pocket.
4. Admit `ShotEnded` / `TableSettled`; record one evidence-backed inferred `BallPotted(Y3, BottomRight)`.
5. Update ball presence, remaining yellows and the visit's recorded outcome, retaining ordered history. Keep rules-dependent group/turn/score conclusions unknown or declared.
6. Apply a player correction that Y3 stayed up; append `StateCorrected` referencing the inferred pot and restore relevant table/visit facts.
7. Rebuild from the corrected history, including a persisted/reloaded copy, and obtain the same canonical state.

Also demonstrate an uncertain machine group fact (for example Yellow/Inferred/0.78 in a fixture) becoming player-confirmed. Repeat with duplicate signals, out-of-order/stale observations, occlusion and source changes. The original inference and correction remain explainable.

## Downstream work and completion

G01-G05 extend match management, scoring, durable history and user-facing undo using this model. O01-O05 supply validated motion/tracking/contact evidence. R04 retains the previous R01 rules-outcome intent; R02 retains black/frame checks and history corrections, and R03 retains quiet-mode hints. Rules use the same correction/reduction path. T-series replay/media retains its IDs and scope; future shot reconstruction/3D replay consumes events plus source-referenced evidence. C-series coaching/statistics consumes corrected history.

R01 needs no API key. A02-A05 remain deferred. The new priority does not close outstanding T13/T14 acceptance or introduce timer-driven perception. Full rules, first-contact adjudication, exhaustive detectors, conversational correction parsing, score HUD/statistics, replay/3D and physics remain deferred.

The [completion report](r01-match-table-state-report.md) records the library's responsibility, core types, reducer/transition design, provenance/corrections, demonstrated scenario, integration points and deferrals. The [glow-up plan](glow-up-plan.md) and [progress](glow-up-progress.md) reflect the delivered slice and remaining consumer work.
