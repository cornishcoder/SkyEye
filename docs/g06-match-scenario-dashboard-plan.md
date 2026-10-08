# G06 — Dashboard Match and Tracking Scenario Lab

8 October 2026 · G06.01a, G06.02a and typed G06.02b controls are installed. G06.03a is Release-built and installed. Live readback shows 16 clear diagnostic labels, while the production table tracker has no current settled identified layout; the live match's stored positions are stale. G06.03b adds a three-frame, explicitly accepted baseline in the isolated scenario when the labelled colour counts match. The live match remains unchanged. Full O03/O04 tracking, pocket evidence, G06.01b history verification and Sky voice review remain open.

## Goal

Show the current match, frame, score and table state beside the live vision diagnostics. Let the user create, reset, edit and replay match scenarios from the laptop to check state transitions and tracking behavior without disturbing a real match.

The current dashboard remains the read-only live view at its existing address and page. The Scenario Lab has a separate browser page and address, with a separate Lab scenario session and journal. Both paths use `SkyEye.GameState` and its typed inputs, event reducer, version checks and correction/undo semantics; the scenario path never writes to the active match journal.

## Dashboard views

### Live match

Present a readable summary of the current canonical snapshot:

- Every `GameSnapshot` area: match and archive history; frames and derived scores; current frame/turn/visit/groups/foul/result/pots/rules; table inventory, motion/source/settlement/uncertainty; and active shot, before-state, approaches, ended/resolved status.
- Ball identity, colour, presence, table position, provenance and confidence. Keep unknown or conflicting facts visible as uncertain; do not fill them from expected counts.
- Source and calibration identity, last-settled/source time, freshness, and `GameSnapshot.Version`.
- A bounded event history showing observed, inferred, player-confirmed, player-corrected and scenario-injected inputs distinctly, including correction/undo links and the resulting revision.
- Existing live image, detector labels, candidate diagnostics and sample history remain on the unchanged live page. Match controls and history belong on the separate scenario page and must not displace the full-width camera frame.

The live view is read-only. It must not dispatch match edits to the production coordinator or active match journal.

### Scenario Lab

Provide a clearly selected scenario workspace with controls to:

- Start/reset a scenario and choose a blank state or a built-in example (clean rack, open table, assigned groups, mid-visit, uncertain/missing ball, corrected pot, foul, frame result, or multi-frame match score).
- Set up players and target, start/select a frame, change the current player, advance the visit through typed turn/shot inputs, assign/open colours, declare foul/result, finish a match, and correct or undo supported decisions.
- Add/edit/remove scenario balls with identity, colour, normalized table position, presence, confidence and explicit scenario provenance; set table motion/settlement/uncertainty and source-time metadata.
- Step through a timestamped scenario sequence of table observations, shot/motion boundaries and supported tracking evidence. Where O03–O05 processing is available, replay it through those real proposal/admission seams and show proposed versus accepted events.
- Review evidence before it enters the scenario journal. Missing-ball, movement, pocket-approach, shot-end and table-state inputs create a proposed GameState transition with its event provenance and outcome. Accept, reject, or keep missing-ball evidence while leaving the table uncertain; the reducer's inferred pot is not recorded until accepted.
- After a settled shot, compare the shot's before-state with the final inventory. If balls moved and no pot was recorded, open the rules review automatically. The user can declare shot facts with compact controls, evaluate them with the existing International Eightball rules engine, and accept its proposed position, keep the current state, edit the facts or choose a player turn manually. The proposed rules event and next state remain separate from the current scenario until selected.
- Save or reload named scenario presets locally in the Lab, and inspect the event history and state after each step.

Scenario controls must cover every canonical state category. Direct fields are edited through the supported typed commands; state that is derived from history (such as visit count, pots, frame score, match score, archive history and shot resolution) is reached by stepping the corresponding typed input sequence. Derived values stay derived: change frame results through typed frame-winner commands and let match score recompute; do not expose an independent score textbox that can disagree with the frame ledger. Commands use the scenario snapshot version and unique operation IDs so stale or duplicated browser requests cannot silently change the scenario twice.

All injected or replayed scenario data must carry a visible Scenario/Test badge separate from the domain's `Observed`, `Inferred`, `PlayerConfirmed` and `PlayerCorrected` provenance. It cannot masquerade as a camera observation, change live detector output, or enter the reviewed O01 corpus unless a user separately labels and exports it through the existing review flow.

## Delivery chunks

| Chunk | Deliverable | Acceptance |
|---|---|---|
| G06.01a | Readable live match and state summary | Show derived match score, players/groups/current player, frame ledger and archives, plus canonical ball position/presence/provenance and source/motion/shot facts from the current `MatchView` and `GameSnapshot`. Live dashboard requests cannot mutate the match. |
| G06.01b | Bounded evidence and event history | On the same revision, event/correction history agrees with the canonical journal; uncertainty and stale source time stay explicit; refresh or a new diagnostic session does not show stale state. Live dashboard requests remain read-only. |
| G06.02a | Separate Scenario Lab page, isolated journal, reset and presets | The existing live dashboard page/address stay as they are. A separate Lab address shows a read-only live match/camera summary beside the isolated scenario's match score, players, frame ledger, table inventory, event history and clearly badged test provenance. Blank, clean-rack and mid-match presets can reset only the isolated scenario. |
| G06.02b | Typed controls and user-reviewed event proposals | Match setup/target, frame/turn/groups/foul/result/finish/undo, ball add/edit/move/missing, table settle/certainty, and shot/pocket steps go through typed GameState inputs. Tracking evidence produces a revision-fenced preview with accept/reject alternatives; a likely pot also offers a keep-missing/uncertain path. |
| G06.03a | Isolated camera review bridge | Copy the canonical live match history into a new scenario journal. Sequential diagnostic samples retain source, calibration, frame and PTS identity. Wait for three stable samples before proposing a changed layout; an uncertain detector result or ambiguous identity can only be recorded as uncertain evidence. Accept/reject stays in the scenario journal. No production match write is possible through this bridge, and disappearance alone does not infer a pot. Release build and Pixel Lab install/launch succeeded. |
| G06.03b | Scenario camera baseline | When the fresh detector layout no longer matches saved live positions, allow review only when the active match is settled and certain and per-colour counts agree. Require three consecutive clear stable samples, then present a player-confirmed `TableObservation` proposal in the isolated scenario. Accepting sets its starting positions without creating a shot, turn change, score or pot. A count/colour mismatch remains blocked for manual inventory correction. The live journal is never written. |
| G06.03 | Tracking evidence, rule proposals and result comparison | Complete source-time track association, shot start/settling evidence and reviewed before/after reconciliation from O03–O05. The existing International Eightball evaluator receives user-declared shot facts plus canonical GameState and proposes a new rules position. Accept, keep, revise facts or manually choose a turn; only the chosen option is journaled. The current one-second layout bridge is a Lab workflow aid, not full temporal tracking or a detector-accuracy claim. Sky voice does not yet ask or resolve these proposals; the review DTO and choice endpoint provide the shared seam for that follow-up. |

## Verification and release boundary

- Pure checks cover each preset, reset, reducer rebuild, frame/match score derivation, correction/undo, duplicate and stale commands, and malformed or out-of-order source times.
- Dashboard checks cover the live read-only boundary, scenario-only write boundary (enabled only while the Lab scenario workspace is selected), revision fencing across refresh/session changes, event-history rendering and laptop-width usability.
- Rules review exercises the implemented International Eightball profile with user-declared facts. It does not infer first contact, legality, called-shot status, or complete official rulings from the current ball detector; those remain explicit facts until vision and referee inputs support them.
- Live camera review requires a fresh, settled, certain active match, a fresh clear diagnostic frame, and matching per-colour counts. If the saved positions differ from the camera, three consecutive clear/stable frames produce a user-reviewed baseline proposal in the isolated scenario; no shot, turn change, score or pot is inferred. Later changed layouts require three stable samples and produce provisional scenario events. Review is fenced to one diagnostic session. Fast shots, same-colour crossings and precise pocket evidence remain unsupported.
- A Pixel Lab build is installed in place, preserving the existing Lab package identity and private O09 captures. The laptop dashboard exercises a declared scenario and a tracking replay while the real live match remains unchanged.
- Scenario results are functional checks for state transitions and implemented tracking seams. They do not establish detector accuracy, real-shot tracking quality, rules correctness beyond the exercised slice, or O08 lighting/release acceptance.

## Ownership and dependencies

G06 consumes canonical state and supported inputs from R01/G01–G05. It is a Lab-facing consumer, not a second game ledger or a new set of rules. Tracking replay depends on the relevant O03/O04/O05 stage being available; this dashboard does not claim those stages are complete. The vision dashboard remains opt-in and on the existing fixed-port, local-Wi-Fi path. Any new write operation is scoped to the disposable Lab scenario session, never the live match.
