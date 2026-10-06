# R01 — Match & Table State completion report

6 October 2026. R01.01-R01.07 are implemented in small checked commits. This delivers the state core and minimal application integration; full rules, scoring UI and perception tracking remain downstream work. The [brief](r01-match-table-state-brief.md) defines the mission and the [plan](r01-match-table-state-plan.md) records its chunks.

## 1. Library and responsibility

`src/SkyEye.GameState` is a pure `net10.0` C# library. It owns an immutable canonical match/frame/table snapshot, meaningful ordered events and deterministic transitions. It has no project/package dependency on Android, CameraX/Media3, OpenCV, Cast, GPT/Sky, UI or networking, and no clocks, timers or workers. It uses BCL immutable collections and JSON metadata, with no event-sourcing framework.

`GameSession` is an immutable proposal: accepting an input returns a replacement session or a duplicate/stale/rejected/conflicting result. The application persists a replacement before publishing it. Consumers read `GameSnapshot`; they cannot change the owning session by editing a view-state field.

## 2. Core types

| Area | Implemented types and behavior |
|---|---|
| Input envelope | `GameInputEnvelope`: input ID, match/frame identity, timestamp, optional source/PTS, provenance and confidence. `EvidenceSource` identifies camera, timeline, generation, calibration and connection. PTS is microseconds within that source timeline. |
| Match/frame commands | `StartMatch`, `StartFrame`; two distinct players and a frame target. Current-player/visit, declared groups/foul/winner and frame score are represented. One active match is supported; broader session management remains G01. |
| Evidence | `TableObservation`, `MotionStarted/Stopped`, `ShotStarted/Ended`, `TableSettled`, `BallObserved/Moved/Missing/Appeared`, `PossiblePocketEvent`, explicit `SelectSource`. `PossibleCollision` and `PossibleCushionContact` are reserved typed seams and currently return unsupported rather than adjudicating contact. |
| Corrections | `PlayerCorrection` / `SkyCorrection` wrap `AssignColour`, `ChangeTurn`, `SetFoul`, `SetBallPresence` and `SetFrameWinner`. Pot amendments can reference the original event. Other correction commands address the current frame/visit; general historical undo is G05/R02. |
| Events | Match/frame started, source selected, table observed/settled, shot started/ended/resolved, individual ball evidence, pocket approached, ball potted, colour assigned, turn changed, foul recorded, frame won and state corrected. Cue-ball pots use the pot event's `CueBall` flag; match winner is derived from declared frame wins/target. |
| Canonical state | `GameSnapshot` contains `MatchState`, `FrameState`, `TableState` and current `ShotState`; `KnownBall`, normalized `TablePosition` and `Fact<T>` retain supporting metadata. Remaining counts are nullable when evidence is uncertain or balls are missing. Black/ball presence and motion have explicit unknown states. |

## 3. Reducer and transitions

`GameReducer.Rebuild` reduces the append-only event history in authoritative sequence order. Envelopes retain schema/version, sequence, event/input IDs, fingerprint and evidence metadata. Duplicate accepted IDs cannot append or execute again; changed payloads with the same ID conflict. Older timestamps/PTS and repeated physical table frames cannot overwrite current evidence. Another source must be selected explicitly; a new connection can restart analysis generation while invalidating live-table continuity.

A pot requires a known before ball, pocket approach and missing after ball on a clear settled table after the shot ends. For this conservative slice, supplied identity and pocket confidence must be at least 0.8; player-confirmed identity can supply the before fact without invented detector confidence. Pot confidence retains the weakest supplied support. Conflicting pockets, obstruction, unresolved association and disappearance alone cannot become a confident pot. This threshold is a bounded inference policy, not a calibrated probability or complete pool rule.

Raw observations can contain up to 64 imperfect detections. More than 16 cannot create a canonical 17-ball table: the evidence is retained with unresolved identity and uncertainty. No automatic group, turn, foul or winner follows a pot. New visits/shots reset current foul knowledge rather than carrying a previous declared foul into a new shot.

The history limit is 10,000 events; transitions containing observation/pot/resolution events refuse atomically if the complete group cannot fit. No history is silently forgotten. Archive/multi-session policy remains a follow-up.

## 4. Provenance and corrections

Facts distinguish `Observed`, `Inferred`, `PlayerConfirmed` and `PlayerCorrected`, with optional confidence and evidence IDs. A Yellow/Inferred/0.78 fact becomes player-confirmed when the player declares "I'm yellows". `SkyCorrection` remains inferred and cannot revoke a committed pot. Perception-origin application commands cannot impersonate a player correction.

A disputed pot appends a `StateCorrectedEvent` referencing the original pot. Reduction replaces that fact at its original sequence position, repairs ball/count/visit state and retains the original evidence. Later genuine shots still reduce afterward, so correcting an earlier pot cannot erase a later real pot. Same-shot machine loss cannot override the player's ball correction; a subsequent shot can change that ball again. Winner amendments replace a frame's declared winner rather than counting it twice.

## 5. Demonstrated scenario and evidence

The readable pure checks start Rich/Mike on an open settled table, start a shot, admit Y3/bottom-right pocket evidence, end/settle the shot, record one inferred pot, update the table and visit, append a player correction that Y3 stayed up, then reconstruct the corrected state. Checks also cover weak/contradictory inference, duplicate/conflicting evidence, source/PTS/frame staleness, explicit unknown facts, precedence, later-shot preservation, restart and atomic capacity refusal.

The full coordinator fixture runs the same declared shot/pot/correction through `SubmitGameInput`, persists it and reconstructs the canonical application snapshot. Existing frame acquisition supplies the initial settled observation; later shot/pocket signals in this test are declared tracker evidence.

Pixel 11's trimmed Release check passed the [saved device proof](evidence/r01-device-game-state.json): an actual decoded-live-texture observation retained source PTS `41787204766`, 15 detections and explicit uncertainty through journal reload. It did not award any game outcome from that uncertain frame. A separate declared device scenario recorded one inferred pot at confidence 0.9, changed remaining yellows from 0 back to 1 after correction, removed the visit pot, rebuilt its ten-event history identically and recognized the durable duplicate. Production match history was untouched and camera capture stayed Active.

Earlier actual frames produced 17 circles; that led to the excess-detection regression above. The device check also exposed reflection-only JSON serialization of immutable fact dictionaries after trimming. `GameJsonContext` and `GameJournalJsonContext` now supply explicit metadata; the checked Release device then passed snapshot/journal reconstruction. Desktop builds alone did not establish that compatibility.

Verification: pure game checks, the full existing core/HTTP/media/state suite and Agent UI checks pass. Sequential baseline and production Release builds have zero errors, with existing binding/native packaging warnings. Deterministic file checks cover checksums, unknown schema, truncation, concurrent owner/prefix conflicts and failed-save publication. They do not claim arbitrary power-loss/storage-hardware recovery.

## 6. Application integration and storage

Both companion hosts initialize a private `game-state` journal. `AppCoordinator` serializes `SubmitGameInput`, requires host control, checks application version and domain operation IDs, saves events and then publishes `SkyEyeState.Game`. Storage failure retains the previous canonical state. An unreadable journal reports a game error and refuses further game mutations rather than inventing a score.

`TableObservationEffect` captures immutable ball evidence from the exact matching `TableAnalysisSession` snapshot, carrying calibration/source/timestamp and converting the application's 2:1 geometry to a normalized rectangle. `GameObservationAdapter` retains identities only across a uniquely associated unchanged layout. Moving/crossing/changed-count layouts remain unresolved for later tracking. Unknown CV colour remains unknown. Results admitted before a frame replacement cannot seed the new frame.

The owning application saves schema-1/checksummed events using a flushed temporary file and atomic replacement, bounded to 16 MiB. An exclusive file lock and append-only prefix comparison prevent competing owners from overwriting history. Recovery rebuilds game facts but sets live freshness false. `HasFreshGameTable(now)` also checks the six-second observation age; the domain itself has no timer. JSON consumers use `GameJson.WriteState` for the explicit snapshot metadata.

`ShellSession.ExecuteApplicationCommandAsync` is the public integration seam. Match entry and conversational correction UI are deferred; this mission establishes their typed commands and authoritative state. Existing candidate selection, native geometry, media and requested-only perception remain separate consumers. The opt-in `/game-check` is compiled only with `SkyEyePerformance=true`, binds to loopback and uses separate test journals.

Deployment: the final normal production Release is installed in place, with signing certificate, UID and private storage verified. [Restoration evidence](evidence/r01-normal-release-restored.json) records its source commit/APK hash, absence of the diagnostic endpoint, Active capture and host control. The normal controls restored stored phone correction and connected Chromecast; receiver-backed UI reports **TV playing rectangular table**, with the background TV service foreground. The phone screenshot was inspected with live footage and controls closed. Physical TV pixels, motion smoothness and endurance were not separately reconfirmed during R01; their broader T13/T14 gates remain open. No permanent clip, capture stop or voice/key test was requested.

## 7. Intentional deferrals

Complete/versioned pool rules, automatic scoring/adjudication, robust cross-shot ball identity, continuous motion/contact/pocket detectors, conversational intent parsing, full match controls/HUD/undo, statistics/coaching, replay reconstruction/3D and cue-ball physics remain downstream. No voice/API key was used. G/O/R04/R02/R03/C work consumes this state/event model rather than creating another ledger.

T13/T14's remaining physical latency/endurance/network/audio/annotation gates remain open; R01 does not close them. Actual pot detection from a physical shot and a complete player conversation were not exercised: the device pot/correction story used explicitly declared evidence. The observation adapter's actual uncertain frame and the declared domain scenario are separate evidence.
