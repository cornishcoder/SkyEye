# Declared-shot rules — R04/R02 delivery

6 October 2026. The five chunks are implemented and verified within the declared-outcome slice. See [completion report](declared-rules-report.md) and [phone proof](evidence/declared-rules-phone.json). Low-light work uses player declarations; no CV detector or TV score feature is claimed.

The existing project reference is pinned to [International Eightball Revision 2c, May 2023](https://www.wepf.org/docs/International%20Eightball%20Rules%20(Revision%202c)%20-%20A4%20Booklet.pdf), checked against the primary rulebook today. Profile ID: `wepf-international-2c-2023-05-v1`. This is an explicit versioned implementation, not a claim to the latest competition rules.

Bounded chunks: (1) pure rules consumer and source-linked fixtures; (2) recorded interpretations, latest-shot correction/undo and deterministic recovery; (3) shared application admission, legal-target/entitlement projection; (4) phone declaration/review/history controls; (5) scenario/build/phone checks, normal delivery and glow-up updates.

Supported clauses: 3/7 separate legal black; 4h/i/j break groups/black/cue exceptions; 6a group/call context; 6b/c/d continuation/combinations/loss of turn; 6e declared standard fouls; 6f declared frame-loss and early/foul black. Break legality, complete shot legality, group-call need and counts are explicit declarations. Unknown facts stay pending. No timed foul, contact/cushion detector, lag adjudication, re-rack workflow or automatic referee accuracy is claimed.

Keep `SkyEye.GameState` platform/rules-engine independent. `SkyEye.Core.Rules` consumes canonical state and declared facts; the application records an explicit ruling with profile/clause references. The game reducer applies recorded effects. Corrections replace the latest effective declared shot, evaluated from its original before-state; undo restores previous history without external field edits. Older shot corrections across later play remain a separate extension.

## Planned automatic-outcome integration — 9 October 2026

R04/R02 remain downstream of accepted O04/O05 settled-state physical outcomes and frozen shot-start player/visit/groups/rules context. The declared rules delivery above is preserved. A supported yellow removal is separate from a confirmed pot and from a legal continuation; no visible foul is not evidence of legality. Unknown first contact, cushion-after-contact, ordering and pocket facts stay unresolved until supported or declared. Group/count outcomes must not require arbitrary red/yellow physical identities. Corrections use the canonical journal and rebuild dependent consequences. Optional O10 trajectories can later supply evidence through review without becoming required for a useful match. No automatic rules integration is implemented by this update.
