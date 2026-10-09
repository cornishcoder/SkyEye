# G06 — Dashboard Match and Tracking Scenario Lab

Updated 9 October 2026. G06.01a/02a/02b and G06.03a-d are implemented within their recorded Lab scope. Commit `853c3f6` adds stable-layout shot inference across same-source obstruction/sample gaps; current pages are dashboard v10 and scenario view 4. It still requires unique per-ball association, records absence as Missing until confirmation, and has no completed aggregate reconciliation/production-admission acceptance. The redesign below is planned; earlier install evidence remains in the progress log.

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
- Step through a timestamped scenario sequence of table observations, shot/motion boundaries and supported tracking evidence. Where O03–O05 reconciliation is available, replay it through those real proposal/admission seams and show proposed versus accepted events.
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
| G06.02a | Separate Scenario Lab page, isolated journal, reset and presets | The existing live dashboard page/address stay as they are. A separate Lab address shows a compact read-only live summary beside the isolated scenario's match score, players, frame ledger, table inventory and event history. Detailed camera evidence is expandable; test provenance stays visible. The default new test starts from the current live match; blank, clean-rack and mid-match remain alternate presets that reset only the isolated scenario. |
| G06.02b | Typed controls and user-reviewed event proposals | Match setup/target, frame/turn/groups/foul/result/finish/undo, ball add/edit/move/missing, table settle/certainty, and shot/pocket steps go through typed GameState inputs. Tracking evidence produces a revision-fenced preview with accept/reject alternatives; a likely pot also offers a keep-missing/uncertain path. |
| G06.03a | Isolated camera review bridge | Copy the canonical live match history into a new scenario journal. Sequential diagnostic samples retain source, calibration, frame and PTS identity. Wait for three stable samples before proposing a changed layout; an uncertain detector result or ambiguous identity can only be recorded as uncertain evidence. Accept/reject stays in the scenario journal. No production match write is possible through this bridge, and disappearance alone does not infer a pot. Release build and Pixel Lab install/launch succeeded. |
| G06.03b | Scenario camera baseline | When live positions are stale, unknown or uncertain, allow isolated review with an active frame, no unresolved shot, the same camera source and supported detections no greater than the copied inventory by colour. Require three consecutive clear stable samples, then present a player-confirmed `TableObservation` proposal in the isolated scenario. Accepting marks absent balls Missing. When exactly one red/yellow is absent, offer a separate explicit player-confirmed Potted-presence choice; it does not establish the shot, pocket, shooter, score or turn. Excess/unsupported detections remain blocked. The live journal is never written. Release build and in-place Pixel Lab update succeeded; Lab UID 10646 is retained. |
| G06.03c | Stable-layout shot inference | Keep the last accepted clear layout across temporary obstruction, stale frames and sample gaps from the same camera/calibration source. Once three clear stable samples agree, compare the new layout with that baseline. If identities associate uniquely and any ball moved or is absent, offer a low-confidence inferred shot even when the obstruction hid its start; show bounded motion-time intervals only when adjacent source-PTS windows are available and each is no more than 1.5 seconds wide; exact timing remains unproven. Missing balls stay Missing until the user confirms a pot. Source/calibration changes reset the baseline. This one-second Lab aid does not require a transient `Moving` sample and is not O03 live tracking. |
| G06.03d | Per-ball before/after review evidence | For uniquely associated camera proposals, show each ball's previous and current normalized table position with its detector confidence inside the collapsed Evidence detail. Baselines and unresolved identities show current positions only. This makes the identity and movement claim inspectable; it is not a trajectory or a durable live track. |
| G06.03 | Settled-state physical outcomes and rule proposals | Planned: O03 frozen activity context and verified A/B → O04 group/count/presence proposals → O05 reviewed admission/correction. Use canonical GameState and the existing rules evaluator with explicit unavailable rule facts. Continuous tracks and exact pockets are not prerequisites; O10 enriches replay separately. Current unique-association Lab aid does not complete this path. |
| G06.03e | Aggregate outcome and incomplete-ruling review (planned) | Show before/after red/yellow counts, cue/black presence, supported layout change and uncertainty. Offer removal/pot confirmation, reject/correct, and questions for unavailable rule facts. Same-colour identity can remain unresolved. Fence frozen game context, source discontinuity and stale browser choices; prove canonical aggregate/per-ball consistency and reload in the isolated journal before production O05 adoption. |

## Planned reconciliation scenarios

Exercise a legal yellow pot, no-removal layout change, multiple removals, cue/black absence, a one-minute player/cue obstruction, same-colour crossings, ball replacement/rerack, source interruption, missed/multiple activity windows and a player/frame/version change while review is pending. Separate observed removal from confirmed pot and rules consequences; default missing contact/legality to unresolved rather than legal. Accept/reject/correct and reload must agree with canonical state, including aggregate facts with unresolved physical identity. O10 failure must not disable these scenarios. These are future acceptance cases, not completed tests.

## Verification and release boundary

- Pure checks cover each preset, reset, reducer rebuild, frame/match score derivation, correction/undo, duplicate and stale commands, and malformed or out-of-order source times.
- Dashboard checks cover the live read-only boundary, scenario-only write boundary (enabled only while the Lab scenario workspace is selected), revision fencing across refresh/session changes, event-history rendering and laptop-width usability.
- Rules review exercises the implemented International Eightball profile with user-declared facts. It does not infer first contact, legality, called-shot status, or complete official rulings from the current ball detector; those remain explicit facts until vision and referee inputs support them.
- Live camera review requires a fresh clear diagnostic frame, an active frame, no unresolved shot, the same camera/calibration source and supported detected colours within the copied inventory. Stale, unknown or uncertain saved positions force a baseline proposal, which requires three clear/stable samples in the same diagnostic session. Accepting sets observed positions and absent balls Missing; for one absent red/yellow, the user can explicitly set its presence to Potted. Neither choice infers the shot facts or rules outcome. During later review, temporary obstruction, stale frames or sample-sequence gaps clear only the in-progress consensus, not the last accepted layout. Three subsequent clear/stable samples allow a before/after comparison. A uniquely associated moved or missing layout proposes a likely inferred shot even if movement began while the player blocked the camera; source-time windows only refine timing when available. Missing balls still need explicit pot confirmation. Fast shots, same-colour crossings and precise pocket evidence remain unsupported.
- A Pixel Lab build is installed in place, preserving the existing Lab package identity and private O09 captures. The laptop dashboard exercises a declared scenario and a tracking replay while the real live match remains unchanged.
- Scenario results are functional checks for state transitions and implemented tracking seams. They do not establish detector accuracy, real-shot tracking quality, rules correctness beyond the exercised slice, or O08 lighting/release acceptance.

## Ownership and dependencies

G06 consumes canonical state and supported inputs from R01/G01–G05. The next planned review path depends on O03 activity/settlement, O04 count/presence reconciliation and O05 admission. O10 trajectory replay is optional and cannot block core scenario match testing. Existing installed unique-association behavior is preserved until those chunks land. The dashboard stays on the fixed-port local-Wi-Fi path; scenario writes remain confined to its disposable journal.
