# Cue ball detection recovery

8 October 2026 · **P0 incremental detector work; room-lighting acceptance open.** This work continues O08 current-frame recovery alongside the O09 learned-detector comparison. It preserves the C# vision pipeline and canonical GameState.

## Evidence and suspected failure

The user's stationary-table dashboard samples 50–73 contain five frames with fewer than the expected 16 balls. The user confirms that the cue ball remains visible throughout. These counts establish intermittent incomplete layouts, but do not identify which detection stage failed. They are one reported sequence, not a held-out accuracy estimate.

The ordinary detector can recognise one cue ball, derive colour gains from it, and then replace all proposals with a second detection on the colour-corrected image. A valid cue ball can be lost during that replacement or subsequent classification. The absence of the no-reference warning in the reported failing samples points to this path, but paired stage telemetry must confirm it. Local illumination gain extrema alone cannot establish cue ball brightness or contrast.

## Implementation order

1. **Preserve fresh cue geometry through colour correction.** Retain a unique first-pass cue ball only when its full body has current-image boundary support at the calibrated ball size. Associate it with the second pass without creating duplicates. Recover a missing proposal; do not choose an identity from the inventory or overwrite contradictory classification. Record first-pass, normalized-pass and final cue counts, current-image recovery and rejection reason.
2. **Independent neutral-body search.** When the initial pass finds no cue ball, propose compact neutral bodies with brightness above their local background, calibrated diameter, roundness/fill and raw-image boundary support. Check body and rim appearance to exclude the black ball's number patch. Bound proposal count and processing cost. Colour normalization is not a prerequisite for finding this body. Add empty-cloth, stripes, dark ball with cream patch and exposure regression cases.
3. **Fresh recovery within temporal verification.** Preserve O08's existing separate-frame burst consensus. A last-seen position may later seed a local search through the existing tracking seam, but cannot supply an observed ball. Require support in the newest frame; motion, source/calibration changes and obstruction invalidate reuse. Keep per-frame diagnostics available. Any additional stable dashboard view must identify observed, tracked and uncertain evidence distinctly. This stage follows the measured result of stages 1–2; do not interlace images or assume misses are independent.
4. **Continue O09.** Collect completely labelled, session-separated ordinary/dim/bright/blind-sunlight frames and compare current, learned and hybrid candidates on identical pixels. Freeze configuration/provenance for each run. The first two partial saved label frames remain ineligible. [O09 plan](o09-learned-ball-detection-spike.md).

## Acceptance and delivery

Expose a settings switch for controlled before/after replay, with distinct analyzer configuration identity. Exercise the native detector on the connected Pixel in the isolated vision-check package, using saved real fixtures and synthetic positive/negative images. Check cue presence, duplicate/conflicting classifications, false cue balls on empty/striped cloth and number patches, and processing cost. Synthetic variants and saved development replays cannot establish actual sunlight reliability.

Publish all failures and denominators. Update SkyEye Lab in place after build/checks; preserve its package/signing identity, UID and private storage. The user's live stationary-table run must demonstrate whether dropouts improve. O08 safety, fresh-final support, physical acceptance and held-out gates remain open until measured. Commit each bounded change and synchronize the [progress](glow-up-progress.md) and [roadmap](glow-up-plan.md).
