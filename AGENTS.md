# SkyEye repository guidance

## Physical camera and companion sessions

- Keep the mounted Pixel 10 camera capture stopped unless an explicitly authorized task is actively collecting live evidence. Capture uses camera battery after the Pixel 11 companion is backgrounded or closed.
- Run only the bounded live capture needed for the current evidence step. At the first safe boundary, stop capture before long builds, offline replay, waiting, pauses, or task completion. Do not stop an atomic evidence capture mid-sequence.
- Use the companion Home action `End recording session` to stop. Verify the camera's `/status` endpoint reports `state: "Stopped"`; closing or backgrounding the companion is not a stop. Then leave the companion at Home showing `Start live session`.
- Preserve saved clips and app data. Retain host ownership only as the existing flow does; release host or voice resources through supported controls when the task calls for it. Do not add automatic lifecycle behavior unless requested.
- Restart live capture only when the authorized task needs new live frames, using the normal non-Cast Home action. A match is not required for perception stillness checks; never start one only to inspect detection.
- Do not interact with Cast or the TV while the user is watching. Do not update the mounted camera package as part of Pixel 11 companion work unless explicitly requested.

For the full accessibility and device procedure, see [docs/device-control-guide.md](docs/device-control-guide.md).
