# SkyEye repository guidance

## Physical camera and companion sessions

- Keep the mounted Pixel 10 camera capture stopped unless an explicitly authorized task is actively collecting live evidence. Capture uses camera battery after the Pixel 11 companion is backgrounded or closed.
- Run only the bounded live capture needed for the current evidence step. At the first safe boundary, stop capture before long builds, offline replay, waiting, pauses, or task completion. Do not stop an atomic evidence capture mid-sequence.
- Use the companion Home action `End recording session` to stop. Verify the camera's `/status` endpoint reports `state: "Stopped"`; closing or backgrounding the companion is not a stop. Then leave the companion at Home showing `Start live session`.
- Preserve saved clips and app data. Retain host ownership only as the existing flow does; release host or voice resources through supported controls when the task calls for it. Do not add automatic lifecycle behavior unless requested.
- Restart live capture only when the authorized task needs new live frames, using the normal non-Cast Home action. A match is not required for perception stillness checks; never start one only to inspect detection.
- Do not interact with Cast or the TV while the user is watching. Do not update the mounted camera package as part of Pixel 11 companion work unless explicitly requested.

For the full accessibility and device procedure, see [docs/device-control-guide.md](docs/device-control-guide.md).

## Companion shortcut and automation usability

- Keep the companion straightforward for people and Codex to operate. For routine development actions, add a direct, clearly named Home shortcut when it removes repeated navigation; preserve a compact non-scrolling Home, accessible names, and visible working/result states without exposing implementation clutter.
- Shortcuts must reuse the existing command and verification path, must not start live capture implicitly, and must not create parallel readiness or canonical state.
- Keep the menu panel pinned by default. Offer a clearly named, accessible pin/unpin control and remember the user's choice in private app preferences; deliberate unpinning may restore the existing five-second fade while video plays.
- Document every shortcut in [docs/device-control-guide.md](docs/device-control-guide.md): accessible name and state labels, exact code handler, prerequisites, whether it starts capture, visible result, and post-action readback.
- Keep actionable WebView controls above the native video hit surface. Verify a shortcut from the current visible screen while live video is active; accessibility hierarchy alone does not prove it can be tapped.
- Keep the stop-when-idle rule above: after a bounded live check, stop capture and confirm `Stopped` and Home `Start live session` before waiting, building, or finishing.

## Live Detect progress reporting

Before each work cycle, name the specific test/case/gate being worked on, its current result and the intended observable improvement. Refer back to that same test when reporting findings. At every commit point update the Live Detect plan/evidence ledger and report current software-suite, detector-case and physical-gate metrics separately, identifying carried-forward versus newly executed results. Preserve failures and untested status; never combine unlike denominators or treat counts as geometric acceptance. Include the commit and next target test.
