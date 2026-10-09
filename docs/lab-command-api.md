# SkyEye Lab command API

Updated 9 October 2026. This is a development-only control surface for driving the same Lab actions as the app UI and dashboard scenario page.

## Connect and inspect

The performance build listens on Pixel loopback port `8769`. The PowerShell helper temporarily forwards laptop port `18768` through ADB; it does not expose this command server on Wi-Fi.

```powershell
.\scripts\Invoke-LabCommand.ps1 -CompanionSerial 66181FDDV001HS -Command commands
.\scripts\Invoke-LabCommand.ps1 -CompanionSerial 66181FDDV001HS -Command status
.\scripts\Invoke-LabCommand.ps1 -CompanionSerial 66181FDDV001HS -Command scenario
```

The HTTP endpoints are:

| Method and path | Purpose |
| --- | --- |
| `GET /ping` | Confirms the performance-enabled Lab package is running. |
| `GET /control/commands` | Returns the active command names. |
| `GET /control/status` | Returns app, camera, current/editable table geometry, canonical GameState/match, dashboard, diagnostics, scene, Sky, TV and clip state. It reports whether a Sky key is configured but never returns saved credentials. |
| `GET /control/scenario` | Reads the separate disposable scenario workspace. |
| `GET /control/label-frame` | Returns the captured JPEG and metadata when a fresh frame is waiting for manual labels. Detector labels are not included. |
| `POST /control/command` | Runs one named command with JSON `arguments`. |

Example request:

```json
{"command":"match.start","arguments":{"firstPlayer":"Alice","secondPlayer":"Bob","target":3}}
```

Commands that change the canonical app state accept optional `expectedVersion`; it defaults to the current app version. Scenario commands similarly accept optional `scenarioId` and `expectedVersion`, which default to the current scenario. A stale version is rejected rather than silently overwriting newer state. Read status again after each state change.

App and camera controls require the Pixel unlocked with SkyEye Lab in the foreground. Pure scenario reset/event/rule/review commands and scenario status can run independently; live scenario capture requires the app active. Native Android prompts (permissions, Cast picker, key-entry dialog, share sheet) still need a human screen choice. The API accepts key setup/removal actions but never returns the key.

## Command catalog

`GET /control/commands` is authoritative for the installed Lab build. Optional arguments are in brackets.

| Area | Commands and arguments |
| --- | --- |
| Camera | `camera.connect [address]`; `camera.discover`; `camera.select [address]` or `[name]`; `camera.claim-host`; `camera.release-host`; `camera.capture.start`, `.pause`, `.resume`, `.stop`. Capture changes affect the camera recording. |
| Session and source | `session.start-live`, `.start-dark`, `.watch-live`, `.end`; `view.fixture`; `view.local-clip id`. `session.end` follows the UI behavior and stops capture when host control allows it. |
| Dashboard and vision | `dashboard.start`, `.stop`; `diagnostics.start`, `.stop`; `vision.observe`, `.verify`; `vision.motion.start [hz=30, allowUnconfirmed=false]`; `vision.motion.stop`. Motion cadence is 15, 30 or 60 Hz. |
| Label frames | `vision.label.capture`, `.save`, `.discard`, `.finish`, `.remove-last`, `.clear`, `.confirm-empty [confirmed=true]`, `.confirm-all [confirmed=true]`; `.colour value`, `.visibility value`, `.lighting value`, `.motion value`, `.notes [value]`, `.scene-tag tag [selected=true]`; `.box x1 y1 x2 y2`; `.rename currentId id`. Box coordinates are normalized to the image (0–1). Fetch the image with `label-frame`. |
| Table | `table.calibrate`, `.correct`, `.apply`, `.cancel`, `.group group`; `table.corner.add point`, `.move index point`, `.undo`, `.reset`; `table.ball.select index`, `.kind kind`, `.add-mode`, `.place point`, `.delete`. Points are `{x,y}` in normalized image coordinates. |
| Live match | `match.start firstPlayer secondPlayer target`; `.next-frame player`; `.turn player`; `.colours [player, colour]`; `.foul foul`; `.winner frame player`; `.undo inputId`; `.finish`; `.declare-shot match frame facts [corrects]`. `facts` uses the `DeclaredShot` fields described below. |
| Scenario workspace | `scenario.reset [preset]`; `.command action [playerOne, playerTwo, target, playerId, colour, ballId, x, y, confidence, foul, settled, uncertain, shotId, pocket]`; `.review proposalId choiceId`; `.rules legality [redsPotted, yellowsPotted, blackPotted, cueBallPotted, cueBallOffTable, firstContact, callRequired, calledGroup, redsBefore, yellowsBefore, break, legalBreak]`; `.live.start`, `.live.samples sessionId throughSequence`, `.live.stop`. Presets: `empty`, `clean-rack`, `mid-match`. Scenario events include match/frame/turn/group/foul/winner/undo, ball observed/moved/missing, table state and shot/pocket events. Use the IDs and current version returned by `scenario` for proposal decisions. |
| Shot choices | `shot.choices.show [target=Both]`; `shot.choices.cancel-check`; `shot.cue-path tableVersion id`. |
| Presentation | `presentation.display target mode`; `.pin-shot target tableVersion id`; `.show-shot target tableVersion number`; `.annotation target tableVersion id [annotations]`; `.clear-annotations [target=Both]`; `.table-correction target enabled`; `.guides.toggle`. Enums accept their C# names or camel-case forms. |
| Clips, replay and TV | `clips.load`; `.download id`; `.play id`; `.share id`; `.play-tv id`; `.cancel`; `replay.recent.request [seconds=30]`, `.watch`; `tv.choose`, `.play-live`, `.stop`; `.replay.control request action [seconds]` where action is `Pause`, `Resume` or `Seek`. Clip IDs come from the returned app state/clip list. |
| Sky and app UI | `sky.key.setup`, `.key.remove`, `.start`, `.mute.toggle`, `.end`; `scene.check.toggle`; `checks.annotation-journal`, `.annotated-export`; `ui.navigate screen` (`home`, `settings`, `clips`, `match`); `ui.controls.open`, `.close`, `.reveal`; `ui.notice.dismiss`; `ui.more-controls`. |

`DeclaredShot` accepts `Break`, `Legality`, `RedsPotted`, `YellowsPotted`, `BlackPotted`, `CueBallPotted`, `CueBallOffTable`, `LegalBreak`, `FirstContact`, `CallRequired`, `CalledGroup`, `RedsBefore`, and `YellowsBefore`. Enum arguments can be strings. For example:

```powershell
.\scripts\Invoke-LabCommand.ps1 -CompanionSerial 66181FDDV001HS -Command scenario.reset -Arguments @{ preset = 'clean-rack' }
.\scripts\Invoke-LabCommand.ps1 -CompanionSerial 66181FDDV001HS -Command scenario.command -Arguments @{ action = 'ball-missing'; ballId = 'yellow-1' }
.\scripts\Invoke-LabCommand.ps1 -CompanionSerial 66181FDDV001HS -Command scenario
.\scripts\Invoke-LabCommand.ps1 -CompanionSerial 66181FDDV001HS -Command vision.label.capture
.\scripts\Invoke-LabCommand.ps1 -CompanionSerial 66181FDDV001HS -Command label-frame
```

Resetting the scenario clears its disposable scenario history. The live match is separate and unchanged. See the [live table testing routine](live-table-testing-routine.md) for the matching on-screen workflow and end-to-end confirmation steps.
