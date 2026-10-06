# Manual match management — evening delivery

6 October 2026. All five chunks implemented for declared match play without table or TV testing. See the [completion report](manual-match-report.md) and [phone proof](evidence/manual-match-phone.json). Use existing G01-G05/C03 work without claiming a complete rules profile, automatic scoring or voice integration.

| Chunk | Deliverable | Verification |
|---|---|---|
| 1 | Ordered frame ledger, correction of earlier winners, explicit match finish and retained completed-match history | Rich wins two frames; correct the first to Mike; reconstruct 1-1. A new match retains the previous ledger. |
| 2 | Declared complementary groups/open table and supported decision undo | Turn/group/foul/winner/ball corrections replay correctly; undo preserves later frames and original history. |
| 3 | Read-only match projection and local manual commands, with game-version admission | Local declared matches work before camera connection; connected guests cannot mutate the host game. Source evidence remains host-gated. Restart/conflicting saves/stale controls cannot fabricate state. |
| 4 | Phone setup, score HUD, frame/turn/group/result/correction controls and history summaries | Phone UI uses the canonical command surface; no separate score store, inference or TV score rendering. |
| 5 | Domain/core/UI/build checks, phone-only declared scenario and normal delivery | Setup, declare frames, correct/undo, restart/resume, finish/archive. Update glow-up docs and record device versus fixture limits. |

Persistence keeps schema-1 events and reconstructs new projections from old R01 journals; new event kinds extend the same codec. Stored raw perception does not become current on restart. History remains bounded to 10,000 events/16 MiB; this slice retains completed matches, without compaction or deletion policy.

Keep camera capture and TV flows independent. Phone-only match UI verification can use the Lab package and local declared input. No camera observation, pot detector, physical shot, TV test or voice/key use is needed for the manual acceptance story. Complete/versioned rules, automatic black eligibility, historical contact adjudication, general undo across replaced matches and shot statistics remain downstream.
