# Live table testing routine

Updated 9 October 2026. This routine makes Pixel/Lab checks repeatable without treating a successful command, camera connection or rendered dashboard as proof that the detector or game-state result is correct. It records two ways to operate the same workflow: the development command API and the app UI. Every meaningful scenario is exercised through both paths, and state written through one path is read back through the other.

### This room's camera setup

The ceiling-mounted Pixel 10 runs SkyEye Camera and stays armed, powered from power banks. The Pixel 11 is the SkyEye companion/Lab and host. For normal test runs, leave the camera phone and its capture service alone; do not re-arm or restart it just because the companion or dashboard needs attention. A foreground Lab screen or an old dashboard tab does not prove capture is active. Check the camera and fresh-frame status first. If the Lab command API is missing, repair/reinstall the Lab in place and then recheck camera capture; the camera app is a separate package.

## Before each run

See the [command API reference](lab-command-api.md) for the full command catalog, required arguments and endpoint details.

For a game-tracking change, run `dotnet run --project tests/SkyEye.Checks -- --game-tracking` first. This selects the journal, observation-to-game, match, rules and bounded-verification regressions without unrelated rendering/transport checks. Run the full harness separately and report any remaining failure; neither command proves physical detection accuracy.

1. Confirm the mounted Pixel 10 is still reachable on the room network and the Pixel 11 companion is available. Read the camera address from SkyEye Settings; do not copy host credentials into commands, logs or evidence.
2. Confirm camera capture and calibration before touching session controls. The command API can start, pause, resume and stop capture, so check the requested command before running it. Treat `session.end` and `camera.capture.stop` as recording controls; routine diagnostics should not restart the mounted camera.
3. Keep the table, camera mount and lighting steady for a stationary baseline. Record any sunlight/blind pattern, table-light state, obstruction or camera restart as a separate test condition.
4. Keep the laptop dashboard page open at the exact URL shown by SkyEye. The app displays the dashboard asset version in that URL; do not hard-code an old `?dashboard=` value.

## Path A — command API

The API exists only in the opt-in **Hybrid Lab Performance** build. It listens on the Pixel's loopback address at port 8769. The laptop reaches it through ADB port forwarding on local port 18768; it is not exposed to Wi-Fi. `GET /control/commands` lists the supported app actions. `GET /control/status` returns current state, and `POST /control/command` accepts a command plus an optional JSON `arguments` object. The API exposes the app's normal controls, including capture and match changes; use it with the same care as tapping those controls on the phone.

Build and install the opted-in Lab when needed:

```powershell
.\scripts\Install-HybridLab.ps1 -CompanionSerial 66181FDDV001HS -Configuration Release -Performance
```

This restarts/replaces the Lab package, not the separate camera app. Recheck camera capture after installation before beginning a measurement run.

Use the helper to discover commands, read state and start the workflow:

```powershell
.\scripts\Invoke-LabCommand.ps1 -CompanionSerial 66181FDDV001HS -Command commands
.\scripts\Invoke-LabCommand.ps1 -CompanionSerial 66181FDDV001HS -Command status
.\scripts\Invoke-LabCommand.ps1 -CompanionSerial 66181FDDV001HS -Command scenario
.\scripts\Invoke-LabCommand.ps1 -CompanionSerial 66181FDDV001HS -Command camera.connect
.\scripts\Invoke-LabCommand.ps1 -CompanionSerial 66181FDDV001HS -Command camera.claim-host
.\scripts\Invoke-LabCommand.ps1 -CompanionSerial 66181FDDV001HS -Command session.start-live
.\scripts\Invoke-LabCommand.ps1 -CompanionSerial 66181FDDV001HS -Command dashboard.start
.\scripts\Invoke-LabCommand.ps1 -CompanionSerial 66181FDDV001HS -Command diagnostics.start
.\scripts\Invoke-LabCommand.ps1 -CompanionSerial 66181FDDV001HS -Command status
```

Commands cover camera connection/discovery/host/capture; live and dark sessions; dashboard and diagnostics; one-shot vision checks and motion feed; labelled-frame capture and annotation; table calibration/correction and editor actions; canonical match/frame/turn/colour/foul/winner/undo/shot facts; phone/TV presentation; clips and recent replay; Sky voice; app controls; and the separate scenario workspace (reset, simulated events, proposal decisions, rule facts and live camera review). `scenario` reads that workspace; `label-frame` fetches the current fresh labelling image after `vision.label.capture`. Pass command arguments as a hashtable:

```powershell
.\scripts\Invoke-LabCommand.ps1 -CompanionSerial 66181FDDV001HS -Command match.start -Arguments @{ firstPlayer = 'Alice'; secondPlayer = 'Bob'; target = 3 }
.\scripts\Invoke-LabCommand.ps1 -CompanionSerial 66181FDDV001HS -Command match.turn -Arguments @{ player = 'Bob' }
.\scripts\Invoke-LabCommand.ps1 -CompanionSerial 66181FDDV001HS -Command scenario.reset -Arguments @{ preset = 'clean-rack' }
.\scripts\Invoke-LabCommand.ps1 -CompanionSerial 66181FDDV001HS -Command scenario.command -Arguments @{ action = 'ball-missing'; ballId = 'yellow-1' }
.\scripts\Invoke-LabCommand.ps1 -CompanionSerial 66181FDDV001HS -Command scenario
.\scripts\Invoke-LabCommand.ps1 -CompanionSerial 66181FDDV001HS -Command vision.label.box -Arguments @{ x1 = 0.31; y1 = 0.42; x2 = 0.36; y2 = 0.48 }
```

Commands that open an Android permission prompt, Cast picker, key-entry dialog or share sheet can start that native UI, but the system prompt still needs its on-screen choice. App controls require the Pixel unlocked with SkyEye Lab in the foreground. The scenario workspace remains separately readable through `scenario`. The status endpoint reports app state without returning saved credentials. Avoid putting credentials in command arguments or saved logs.

## Path B — app UI

1. In SkyEye Settings, check the saved camera address and tap **Connect camera** if the mounted camera is not reachable. Verify the status reports capture active and calibrated.
2. If host control is not held, tap **Become host** while the camera is active.
3. Start a new live session on the companion when required. Verify the session mode and fresh frames; do not restart the camera app as part of normal test setup.
4. Open **Live vision dashboard** from Settings, or use the displayed address on the laptop. Start diagnostics if sampling is not already active.

The Pixel display is part of the verification: confirm the live table is actually visible and the app reports the intended live session. A dashboard API response alone does not prove that the phone's viewer or the rendered page is usable.

## End-to-end confirmation for both paths

1. **Command pass:** issue the target action through `/control/command`; then read `/control/status` and the relevant dashboard endpoint. Confirm the transition and record the returned app version, camera/session state, and match/table projection.
2. **Screen confirmation:** inspect the Pixel itself. Confirm the intended page, live image, visible status and any editor/label/match result. An HTTP success alone does not prove the user-facing view updated.
3. **UI pass:** use the corresponding on-screen control for the same kind of action, using a disposable test match or undo/reset when the action changes state. Then read `/control/status` and the dashboard again. Confirm the UI action produced the expected API and dashboard state. This checks command-to-screen and screen-to-state behavior.
4. On the laptop, verify the dashboard renders the current camera frame and labelled table. Read `GET /api/snapshot`: `active` must be true, `totalSamples` must increase over time, the latest frame sequence/ID and source time must advance, and camera/calibration identifiers must remain stable during a still test. Check uncertainty/warnings and ball count; do not infer accuracy from the count alone.
5. Check `GET /api/live-match` for the canonical table/match projection. Record whether the projected table is current and whether game-state errors or pending review are present. The scenario page is a separate, disposable test workspace and is not evidence that the live match updated.
   For the starting-baseline slice, leave the calibrated live table to check itself. Startup allows three batches of three bounded checks, two seconds apart within a batch and at least fifteen seconds between batches; a new active frame also requests its starting check. A matching trusted A disables bootstrap retries. Confirm canonical `groupCounts`, clear/settled state and `lastSettled` have the current source/calibration, with no change to player/visit/pots/score. Dashboard and Sky should report the same aggregate inventory. For a changed layout, require the implemented clear/still verification and inspect the resulting live match; do not treat one detector frame as a pot. Do not call `vision.verify` to claim that automatic startup passed; record any explicit retry separately.
6. Capture a stationary baseline for 30–60 seconds with nobody leaning over the cloth. Preserve the summary and failures. A sample gap, source-generation change, camera restart, movement or lighting change ends that baseline; start a new labelled run rather than stitching the intervals together.
7. For a movement run, save a unique artifact with `scripts/Capture-O02LiveMotion.ps1`. Use a 30–60 second bounded capture, take one clearly timed shot, and write down the actual result (balls moved/potted and approximate time). Do not leave the run active while changing lighting unless lighting change is the explicit condition. The script stops only its diagnostic motion feed; it does not stop camera recording.
8. Inspect the dashboard again after the shot: confirm fresh, clear settled samples, compare the canonical live match with the physical result, and record any proposed or applied event. A stable red/yellow count decrease can support a pot inference under the current policy, but one physical run does not establish general reliability or identify a pocket. With unchanged counts, do not invent a turn change: verify that the cue ball and layout are clearly classified and that a settled before/after comparison exists.

## Physical test notes — 9 October 2026

- **Yellow pot:** The clear settled baseline had 7 reds, 7 yellows, 1 cue ball and 1 black (16 total). After the user-reported yellow pot, clear samples settled at 7 reds, 6 yellows, 1 cue ball and 1 black (15 total). The live match updated Bob to yellows and kept his turn; frame score stayed 0–0. This is one successful physical case, not a reliability estimate.
- **Same-count follow-up:** The user reported a likely missed yellow attempt. Recent samples stayed at 15 total; cue-ball reference classification intermittently became unavailable, then recovered in later clear samples. The canonical state remained Bob's turn on yellows, with no turn-change event. The old comparison had no usable before/after layout, so it could not establish whether the cue ball physically moved.
- **Same-count implementation, 9 October:** The installed build now establishes a stable same-source colour-position baseline from three clear frames, then requires three matching clear frames of a changed arrangement before starting bounded stillness verification. It proposes “shot likely · no pot observed” and asks the player to choose legal miss, foul, loss of turn or no shot. Vision does not infer legality or foul. This has not yet been confirmed on a physical shot. After reinstall, reopen the calibrated live table and leave the dashboard running; verify the asset address ends in `?view=9`, then make a clear no-pot shot with the balls visibly changing places. Check that the event appears only after the table settles, ball counts stay unchanged, and no-pot/foul choices update the scenario turn and group as expected. Record the actual user-declared result separately from camera evidence.
- **Evidence boundary:** These observations came from live camera/dashboard frames. Room audio, including the ball return, was not available to the diagnostic agent.

Example bounded motion run after the API reports a live, calibrated session:

```powershell
.\scripts\Capture-O02LiveMotion.ps1 -CompanionSerial 66181FDDV001HS -OutputPath .\work\o02\live-run-20261009T1245.json -DurationSeconds 30 -Hz 30 -AllowUnconfirmedDiagnosticSeed
```

The unconfirmed seed option is for development diagnostics only; the script first requests a fresh table observation and refuses to start if that candidate is obstructed or outside the supported 1–16 ball range. Use a new output path for every run.

## Results to retain

- Entry path: command API or app UI; Lab build/configuration and dashboard version.
- Camera reachability/capture/host state, calibration ID, live session/viewer state, and first/last frame IDs/source times.
- Detector sample count, ball count, uncertainty/warnings, lighting condition, obstruction, and any dashboard/service interruption.
- Motion capture JSON for shot runs, with a short human note about the physical action and its time.
- A screenshot of the final Pixel view and laptop dashboard when validating a UI change. Keep screenshot/smoke evidence distinct from reviewed detector truth or physical accuracy acceptance.

Do not report a successful install, HTTP response, still table, or one clear detection as movement accuracy or held-out reliability. For release evidence, follow the reviewed corpus and gate requirements in the [O08 plan](o08-mounted-table-reliability-plan.md) and [O01 corpus guide](observation-corpus-guide.md).
