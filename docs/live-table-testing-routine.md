# Live table testing routine

Updated 9 October 2026. This routine makes Pixel/Lab checks repeatable without treating a successful command, camera connection or rendered dashboard as proof that the detector or game-state result is correct. It records two ways to operate the same workflow: the development command API and the app UI. Every meaningful scenario is exercised through both paths, and state written through one path is read back through the other.

## Before each run

See the [command API reference](lab-command-api.md) for the full command catalog, required arguments and endpoint details.

For a game-tracking change, run `dotnet run --project tests/SkyEye.Checks -- --game-tracking` first. This selects the journal, observation-to-game, match, rules and bounded-verification regressions without unrelated rendering/transport checks. Run the full harness separately and report any remaining failure; neither command proves physical detection accuracy.

1. Confirm the Pixel 11 is authorized over ADB and the camera is reachable on the room network. Read the camera address from SkyEye Settings; do not copy host credentials into commands, logs or evidence.
2. Confirm the camera address and calibration. The command API can start, pause, resume and stop capture, so check the requested command before running it. Treat `session.end` and `camera.capture.stop` as recording controls.
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

1. In SkyEye Settings, check the saved camera address and tap **Connect camera** if the camera is not reachable.
2. If host control is not held, tap **Become host** while the camera is active.
3. Return to the home panel and choose **Start live session**. The UI may start camera capture when it is inactive; confirm that behavior from the camera status before a measurement run.
4. Open **Live vision dashboard** from Settings, or use the displayed address on the laptop.

The Pixel display is part of the verification: confirm the live table is actually visible and the app reports the intended live session. A dashboard API response alone does not prove that the phone's viewer or the rendered page is usable.

## End-to-end confirmation for both paths

1. **Command pass:** issue the target action through `/control/command`; then read `/control/status` and the relevant dashboard endpoint. Confirm the transition and record the returned app version, camera/session state, and match/table projection.
2. **Screen confirmation:** inspect the Pixel itself. Confirm the intended page, live image, visible status and any editor/label/match result. An HTTP success alone does not prove the user-facing view updated.
3. **UI pass:** use the corresponding on-screen control for the same kind of action, using a disposable test match or undo/reset when the action changes state. Then read `/control/status` and the dashboard again. Confirm the UI action produced the expected API and dashboard state. This checks command-to-screen and screen-to-state behavior.
4. On the laptop, verify the dashboard renders the current camera frame and labelled table. Read `GET /api/snapshot`: `active` must be true, `totalSamples` must increase over time, the latest frame sequence/ID and source time must advance, and camera/calibration identifiers must remain stable during a still test. Check uncertainty/warnings and ball count; do not infer accuracy from the count alone.
5. Check `GET /api/live-match` for the canonical table/match projection. Record whether the projected table is current and whether game-state errors or pending review are present. The scenario page is a separate, disposable test workspace and is not evidence that the live match updated.
   For the starting-baseline slice, leave the calibrated live table to check itself. Startup retries bounded verification up to three times, two seconds apart, after decoder warmup failures; a new active frame also requests its starting check. Confirm canonical `groupCounts`, clear/settled state and `lastSettled` have the current source/calibration, with no change to player/visit/pots/score. Dashboard v14 renders aggregate counts; Sky's `get_match_state` returns the same counts in `table.inventory`. Later changed counts must hold A for review. Do not call `vision.verify` to claim that automatic startup passed; record any explicit retry separately.
6. Capture a stationary baseline for 30–60 seconds with nobody leaning over the cloth. Preserve the summary and failures. A sample gap, source-generation change, camera restart, movement or lighting change ends that baseline; start a new labelled run rather than stitching the intervals together.
7. For a movement run, save a unique artifact with `scripts/Capture-O02LiveMotion.ps1`. Use a 30–60 second bounded capture, take one clearly timed shot, and write down the actual result (balls moved/potted and approximate time). Do not leave the run active while changing lighting unless lighting change is the explicit condition. The script stops only its diagnostic motion feed; it does not stop camera recording.
8. Inspect the dashboard again after the shot: confirm a fresh frame and ball layout, check whether a game event/proposal appeared, and compare the canonical live match with the physical result. A frame-count change or one disappearing ball is not by itself a confirmed pot.

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
