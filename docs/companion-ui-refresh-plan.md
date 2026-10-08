# Companion home, settings, clips and dark sessions

Updated 7 October 2026. This bounded companion UX slice reduces the resting screen to the actions used during play while preserving the existing native camera, capture, clips, Sky and `SkyEye.GameState` paths.

## Home

Keep the home focused on a bottom panel with six chunky icon-only actions: Settings, Sky, Live session, Dark session, Clips and Match. All six controls fit on screen without scrolling. Their accessible names remain available to screen readers. Sky's icon becomes Hang up while a conversation is active. An active recording replaces the two start-session icons with End session. Match opens the existing match controls.

Show the current session and table-analysis capability in plain language. Availability is derived from the active live session, capture/host state and table calibration; it is not inferred from a voice connection or a stationary-looking screen.

## Dark sessions and Sky

A dark session uses the existing camera host and capture pipeline to record video. It leaves camera preview and image-driven analysis, motion tracking and shot physics inactive. Sky remains available to save clips and update the match when players state events in conversation. Those declarations flow through the existing command path into canonical `SkyEye.GameState`; Sky must not present a declared event as an image observation or invent a pot, foul, shot result or winner.

Expose `sessionMode`, `physicsAvailable` and a readable `physicsStatus` through camera/table state reads. In a dark session, table reads explain the limitation, shot-advice and observation requests return an explicit unavailable result, and observation/presentation commands cannot start analysis. In a live session, report physics as available only when the current capture and calibration prerequisites are satisfied.

## Clips and settings

The Clips screen groups camera clips and clips saved on this phone, with refresh and the existing play/share/TV actions. Recent replay stays in the same screen and uses the existing bounded replay assets. It is not a second recording or clip catalogue. Further organization should use canonical match/frame metadata and existing media references when available; unknown associations remain unknown.

Settings groups camera/recording, table analysis/shot display, TV/Chromecast and Sky voice/conversation. Advanced camera controls and Lab checks stay secondary. Personal voice credentials remain in their current protected native storage and do not enter the Razor projection.

## Launcher icon

Use the user-supplied `icon.png` as the Hybrid companion's adaptive-icon foreground over the app's dark background. Preserve the existing package, activity, signing and service identities. The 7 October production update was installed in place on Pixel 11 Pro XL; the guarded installer confirmed the same signer, UID and private-storage identity and did not change the camera package. The launcher mask was visually checked on the device.

## Status and remaining acceptance

Implemented in the Hybrid source: focused home, Settings/Clips/Match screens, Sky hang-up toggle, live/dark/end session actions, dark-mode analysis gates, truthful physics fields for Sky reads, clip grouping and the supplied launcher icon. Agent UI checks and the production Release build pass. The first installed Pixel smoke check confirms that the refreshed home renders and the icon appears correctly.

When the table is available, verify on device:

1. Confirm all six icon actions are visible in the bottom panel without scrolling, including on a shorter portrait viewport.
2. Start/end Live and confirm the calibrated table enables image analysis only while live capture is available.
3. Start/end Dark and confirm capture continues, Sky remains usable, clips can be saved and played, and physics/observation/shot requests report unavailable.
4. Use clear player conversation to update a match, then confirm the existing Match screen and canonical history agree.
5. Open Clips, play/share a clip and open the Settings groups; confirm navigation does not stop an active recording.

Controlled tests establish command gating, canonical state use and tool reporting; they do not establish the physical camera/clip flow. O08 held-out accuracy, unsafe-motion windows, geometry and physical image acceptance remain separate open gates in the [glow-up plan](glow-up-plan.md).
