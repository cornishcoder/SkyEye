# Manual match management — delivery report

6 October 2026. The [evening plan](manual-match-plan.md) extends R01 with usable declared match management. The manual portions of G01-G05 and frame-result summaries from C03 are implemented. Complete/versioned pool rules, automated outcomes, voice tools and TV score presentation remain separate.

## Delivered

- Phone setup for two named players and a frames-to-win target; the first player starts the first frame. Declare the next frame's starter without resetting match score.
- A phone-only score HUD, current player/group/status and race target. The HUD remains available with controls hidden; it belongs to the phone controls, not the encoded TV scene.
- Explicit turn and complementary red/yellow declarations, open table and foul/no-foul controls. Shot advice follows the current player's declared colours; old group preferences cannot overwrite an active match. Automatic black eligibility and foul entitlements remain unimplemented.
- Frame winner buttons and corrections to earlier frames. Correcting frame one does not overwrite the current frame. Repeated winner requests cannot count another win.
- Undo of supported player decisions: groups, turn, foul, winner and ball corrections. Original events stay in history. Reduction skips the undone decision while preserving subsequent frames and declarations. Match/frame creation, source evidence, match finishing and undo itself are not undoable; previous archived matches are read-only in this slice.
- Explicit match finish, a fresh match with its own zero score and retained previous-match summaries. Summaries show recorded frame results/coverage; they do not invent pot/miss statistics. Starting a replacement active unfinished match is refused.
- Existing journal recovery resumes player names, scores, current frame, groups and history. Old schema-1 R01 events reconstruct the new ledger; new event kinds extend that same codec. Live observation freshness still resets on recovery. Bounds remain 10,000 events/16 MiB; archive history is retained in that bounded journal, without compaction/deletion.

## Ownership and implementation

`SkyEye.GameState` remains authoritative. `MatchState.Frames`, `Finished/FinishedAt` and `GameSnapshot.Archives` derive from events. `FinishMatch`, `DeclareColours` and `UndoDecision` add explicit transitions. Earlier-frame winner declarations update that frame's ledger and score; they retain the latest frame's state.

`AppCoordinator` publishes `MatchView`, a read-only projection with score/status, frame rows, previous-match summaries and the last undoable decision. `SubmitGameInput.ExpectedGameVersion` fences rendered match controls independently of camera polling. Without a camera connection, explicit local Touch/Sky player declarations can manage a private match; connected guests remain blocked and perception/source evidence still requires camera host control. Source IDs and duplicate admission remain unchanged.

`ShellMatch`, `MatchControls` and `MatchHud` use that command surface. No UI score store exists. Setup/result/history controls appear before camera controls in the sheet. Starting a replacement match focuses its heading so the shortened form cannot strand the user below the new controls. The normal legacy group selector is hidden for an active match, and its command path refuses conflicting overrides.

## Verification

Domain scenarios cover Rich winning two declared frames, correcting frame one to Mike, reconstructing 1-1, preventing silent replacement, explicit finish and retaining the old match. Further cases cover complementary groups, undo restoring open table, repeat-undo refusal, turn undo preserving later declared facts and undoing an earlier award without deleting a later frame.

Core fixtures cover local setup, game-version staleness, connected-guest refusal, player/group advice filtering, legacy override refusal and read-only score/undo projection. Existing journal corruption, conflicting owner, failed-write, source/frame/cancellation and restart-freshness cases continue to pass. Domain/core/Agent UI suites pass. Sequential Lab, baseline and production Release builds pass with zero errors and existing binding/native warnings.

The [Pixel 11 phone proof](evidence/manual-match-phone.json) uses the separate trimmed Release Lab package. Through actual touch controls: enter Rich/Mike, race to three; declare Rich reds, change to Mike/yellows, use foul controls; award two frames to Rich; correct frame one to Mike (1-1); undo it (2-0); reapply it; force-stop/relaunch and inspect the recovered 1-1 HUD. Finish that match and start Alice/Bob (0-0), then inspect retained Rich/Mike 1-1 history. The restored HUD screenshot was inspected.

That scenario requested no camera connection, observation, shot, TV playback or voice/key operation. The Lab journal contains the test matches; the production match journal is not seeded with these examples. Physical shot tracking, TV score pixels, complete rules and broad accessibility/endurance acceptance are not claimed.

## Deployment and remaining work

The normal production Release is installed after the phone-only Lab check. The guarded installer verifies matching signing and retained UID/private storage. [Production proof](evidence/manual-match-production.json) records the APK hash and empty match setup controls; the Rich/Mike and Alice/Bob demonstrations remain in Lab. The phone is left at the normal match setup controls, ready for the player's own match. Camera/table/TV acceptance was not exercised during this delivery.

G01 retains full rules-profile work; G02 retains rule-derived black/foul eligibility. G05 retains conversational tools and general historical correction/undo across archived matches. C03 retains validated shot statistics/coaching. A02-A05 voice remains deferred. T13/T14 acceptance and T06/T07 replay work remain open. No table or TV testing was added to this manual delivery.
