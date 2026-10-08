# G06 — Dashboard Match and Tracking Scenario Lab

8 October 2026 · G06.01a is implemented and installed; G06.01b and the isolated scenario workspace remain open. This expands the existing laptop vision dashboard so match tracking can be inspected and exercised against the canonical `SkyEye.GameState` model.

## Goal

Show the current match, frame, score and table state beside the live vision diagnostics. Let the user create, reset, edit and replay match scenarios from the laptop to check state transitions and tracking behavior without disturbing a real match.

The current dashboard remains the read-only live view. Scenario controls operate on a separate Lab scenario session and journal. Both paths use `SkyEye.GameState` and its typed inputs, event reducer, version checks and correction/undo semantics; the scenario path never writes to the active match journal.

## Dashboard views

### Live match

Present a readable summary of the current canonical snapshot:

- Every `GameSnapshot` area: match and archive history; frames and derived scores; current frame/turn/visit/groups/foul/result/pots/rules; table inventory, motion/source/settlement/uncertainty; and active shot, before-state, approaches, ended/resolved status.
- Ball identity, colour, presence, table position, provenance and confidence. Keep unknown or conflicting facts visible as uncertain; do not fill them from expected counts.
- Source and calibration identity, last-settled/source time, freshness, and `GameSnapshot.Version`.
- A bounded event history showing observed, inferred, player-confirmed, player-corrected and scenario-injected inputs distinctly, including correction/undo links and the resulting revision.
- Existing live image, detector labels, candidate diagnostics and sample history remain available. Match status must not displace the full-width camera frame or make the laptop layout hard to use.

The live view is read-only. It must not dispatch match edits to the production coordinator or active match journal.

### Scenario Lab

Provide a clearly selected scenario workspace with controls to:

- Start/reset a scenario and choose a blank state or a built-in example (clean rack, open table, assigned groups, mid-visit, uncertain/missing ball, corrected pot, foul, frame result, or multi-frame match score).
- Set up players and target, start/select a frame, change the current player, advance the visit through typed turn/shot inputs, assign/open colours, declare foul/result, finish a match, and correct or undo supported decisions.
- Add/edit/remove scenario balls with identity, colour, normalized table position, presence, confidence and explicit scenario provenance; set table motion/settlement/uncertainty and source-time metadata.
- Step through a timestamped scenario sequence of table observations, shot/motion boundaries and supported tracking evidence. Where O03–O05 processing is available, replay it through those real proposal/admission seams and show proposed versus accepted events.
- Save or reload named scenario presets locally in the Lab, and inspect the event history and state after each step.

Scenario controls must cover every canonical state category. Direct fields are edited through the supported typed commands; state that is derived from history (such as visit count, pots, frame score, match score, archive history and shot resolution) is reached by stepping the corresponding typed input sequence. Derived values stay derived: change frame results through typed frame-winner commands and let match score recompute; do not expose an independent score textbox that can disagree with the frame ledger. Commands use the scenario snapshot version and unique operation IDs so stale or duplicated browser requests cannot silently change the scenario twice.

All injected or replayed scenario data must carry a visible Scenario/Test badge separate from the domain's `Observed`, `Inferred`, `PlayerConfirmed` and `PlayerCorrected` provenance. It cannot masquerade as a camera observation, change live detector output, or enter the reviewed O01 corpus unless a user separately labels and exports it through the existing review flow.

## Delivery chunks

| Chunk | Deliverable | Acceptance |
|---|---|---|
| G06.01a | Readable live match and state summary | Show derived match score, players/groups/current player, frame ledger and archives, plus canonical ball position/presence/provenance and source/motion/shot facts from the current `MatchView` and `GameSnapshot`. Live dashboard requests cannot mutate the match. |
| G06.01b | Bounded evidence and event history | On the same revision, event/correction history agrees with the canonical journal; uncertainty and stale source time stay explicit; refresh or a new diagnostic session does not show stale state. Live dashboard requests remain read-only. |
| G06.02 | Isolated scenario session, reset, presets and typed state controls | Reset returns only the scenario to its selected baseline. Player/frame/score/correction/undo and ball/table edits go through supported inputs and the same reducer; stale versions and duplicate operation IDs are rejected. Scenario state never appears in the production match journal. |
| G06.03 | Steppable tracking evidence scenarios and result comparison | A repeatable source-timed scenario can be stepped, reset and replayed. Before/after snapshots, proposed/accepted events, uncertainty, provenance and score changes are visible. Replay calls the implemented O03–O05 seams; unimplemented tracking stages are clearly marked unavailable rather than simulated as successful. |

## Verification and release boundary

- Pure checks cover each preset, reset, reducer rebuild, frame/match score derivation, correction/undo, duplicate and stale commands, and malformed or out-of-order source times.
- Dashboard checks cover the live read-only boundary, scenario-only write boundary (enabled only while the Lab scenario workspace is selected), revision fencing across refresh/session changes, event-history rendering and laptop-width usability.
- A Pixel Lab build is installed in place, preserving the existing Lab package identity and private O09 captures. The laptop dashboard exercises a declared scenario and a tracking replay while the real live match remains unchanged.
- Scenario results are functional checks for state transitions and implemented tracking seams. They do not establish detector accuracy, real-shot tracking quality, rules correctness beyond the exercised slice, or O08 lighting/release acceptance.

## Ownership and dependencies

G06 consumes canonical state and supported inputs from R01/G01–G05. It is a Lab-facing consumer, not a second game ledger or a new set of rules. Tracking replay depends on the relevant O03/O04/O05 stage being available; this dashboard does not claim those stages are complete. The vision dashboard remains opt-in and on the existing fixed-port, local-Wi-Fi path. Any new write operation is scoped to the disposable Lab scenario session, never the live match.
