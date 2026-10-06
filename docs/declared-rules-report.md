# Declared-shot rules — R04/R02 report

6 October 2026. A low-light-safe rules slice is implemented, checked and installed in the normal production app. The [bounded plan](declared-rules-plan.md) records scope; this delivery does not establish autonomous referee accuracy.

## Profile and supported decisions

Profile `wepf-international-2c-2023-05-v1` deliberately uses the repo's existing [International Eightball Revision 2c, May 2023 primary rulebook](https://www.wepf.org/docs/International%20Eightball%20Rules%20(Revision%202c)%20-%20A4%20Booklet.pdf), verified during this implementation. It is version-pinned, not advertised as the latest competition revision.

Supported clause families: 3/7 black completion; 4h/i/j break groups, black re-spotting and cue-placement exceptions; 6a open-table group/call context; 6b/c/d continuation and loss of turn; 6e standard fouls; 6f declared frame-loss and early/foul black. Unknown legality, required-call context, contact needed for group selection and pre-shot counts remain unresolved. Contradictory contact/count declarations are refused. Illegal break/re-rack and break frame-loss require manual review.

The player declares pots/cue events, aggregate legality, optional contact/call facts and before-counts. No unseen first contact or cushion contact is inferred. A black decision needs confirmed eligibility before the shot; a dim/missing detection cannot supply it. Counts can be carried from prior declarations or supplied explicitly; table perception remains separate.

## Architecture and recovery

`SkyEye.Core.Rules.InternationalEightball` is a pure downstream consumer of canonical GameState plus `DeclaredShot`. It returns profile-stamped `RulePosition` effects with clause references, target, next player, placement entitlement, pending reason and possible winner.

`DeclareShotOutcome` joins the serialized application command surface. Local declared matches work without a camera connection; connected-guest restrictions still apply. Perception-origin commands cannot claim player legality or inject a ruling. Rendered game versions, durable duplicate IDs and changed-payload conflicts remain fenced. Pending decisions block legal-choice requests.

`SkyEye.GameState` contains typed declaration/ruling records, not the rules evaluator. Its reducer applies `RulesShotRecordedEvent` effects and maintains canonical player/groups/visit/foul/winner plus the recorded rules position. Player-confirmed group facts retain their provenance. A rules-derived winner updates the existing frame ledger, not another score store. Rulings invalidate live layout freshness.

The latest effective shot can be replaced. The consumer finds its original before-state, evaluates the replacement facts, and appends a referencing event. Reduction suppresses the replaced interpretation. Undoing the replacement restores the original; undoing a shot restores its prior state. Events and profile/clause references survive the existing checksummed journal and source-generated codec. Corrections across later play remain deferred; the UI exposes replacement only while the last effective event is that frame's shot ruling.

## Phone workflow

**Controls → Match → Declare or correct a shot** exposes the declared form and latest-shot replacement. The phone shows reason, target, remaining-count knowledge, one-visit cue-placement entitlement, black re-spot requirement and clause references. The HUD shows pending status and cue placement when applicable. Each fresh form starts with unresolved legality; no camera/TV operation is triggered by recording a declaration.

The [actual Pixel 11 Lab proof](evidence/declared-rules-phone.json) records a standard foul from Alice to Bob, anywhere ball-in-hand/one visit, then replacement with a legal miss evaluated from Alice's original before-state. Bob remains next with no placement entitlement. Force-stop/relaunch reconstructs that corrected decision and its clause references. The form/HUD screenshot was inspected. Test data stayed in the separate Lab journal; production is not seeded with it.

## Checks and delivery

Pure scenario checks cover break pots/open groups, black re-spot/black-only break, cue-pot versus off-table placement, illegal break, unresolved legality/calls, mixed-group first contact, combinations, misses/opponent-only pots, standard foul, confirmed last-group clearance, unknown black eligibility, early/same-shot/foul/legal black and contradictory declarations.

Application checks cover recorded effects, target filtering, original-before-state correction, undo restoration, durable duplicates/conflicts, stale version/source authority, unresolved decisions and deterministic journal restart. Domain/core/Agent UI suites and sequential Lab/baseline/normal production Release builds pass with zero errors; existing packaging/binding warnings remain.

The signed normal Release is updated in place with UID/private storage retained. Empty production match setup is verified, with demo players confined to Lab. [Normal delivery proof](evidence/declared-rules-production.json) records the source/APK identity. No physical shot, low-light detector tuning, TV playback test or voice/key use was part of acceptance.

## Deferred

This is a declared-outcome assisted referee. O-series reliable tracking/contact evidence, full tournament procedure/lag/clocks/interference/stalemates, automatic re-rack handling, older-shot correction across subsequent play, alternate/current rule profiles, broad accessibility/endurance and TV score pixels remain open. The concurrent temporal-tracking roadmap priority is preserved; this rules consumer is ready for reviewed outcomes rather than raw dim-frame guesses.
