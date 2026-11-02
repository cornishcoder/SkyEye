# O09 Development lighting expansion capture — 2 November 2026

## Target and scope

Target: `O09-DEVELOPMENT-LAYOUT-LIGHTING-EXPANSION`, including the normal-validation Layout03 black/cue control sequence. This is Development evidence only. It does not establish D1, D2, stillness acceptance, or play readiness.

Checkout: `4216ba8` (`master`) at the start of this evidence cycle. Installed companion provenance carried forward from today's device check: Production 0.8.62/code70, APK SHA-256 `B601D0943503E9FB77DD2145C46A875776EA1B6FFCDF520B05895171BD7BB88E`. The mounted camera package was not changed.

The user described the normal-pool-room condition as daylight without direct glare and held the non-target balls and camera/lighting arrangement fixed while staging. The user later said daylight shots should stop for today. Lighting metadata and image-patch equivalence are not yet measured, so these states are not claimed as a verified same-light comparison.

## Bounded operation results

Each Home action captured one bounded operation. No operation is treated as a D2 request-window pass.

| State | Operation ID | Valid raw image/receipt pairs | Home verifier result |
| --- | --- | ---: | --- |
| Normal/train Layout01 replacement rack | `86287db27b984c3da4978a24bcf77707` | 5/5 | `Ready` |
| Normal/validation Layout01, cue absent | `3eac0dd5b97f43b480ed06a954589f4e` | 6/6 | `Unclear (DetectionAmbiguous)` |
| Normal/validation Layout02 spread | `008a3905cd164a5db413886ab73a344d` | 6/6 | `Capture incomplete · no matching table result` |
| Normal/validation Layout03, both present | `8f9d667a1e7342cfb4208c4eb3c0aef5` | 5/5 | `Unclear (DetectionAmbiguous)` |
| Normal/validation Layout03, black absent | `b9ee27aca5c944e3a7ce5d2d320fd7a7` | 5/5 | `Unclear (DetectionAmbiguous)` |
| Normal/validation Layout03, cue absent | `60411e14c26848739fa1d9a5c3619582` | 7/7 | `Unclear (DetectionAmbiguous)` |
| Normal/validation Layout03, both restored | `d72ad7021a294b308ff22ff5bbcff67b` | 7/7 | `Unclear (TimedOut)` |
| **Total** | **7 operations** | **41/41** | **1 Ready, 5 Unclear, 1 incomplete** |

For all 41 pairs, integrity was checked from only `operationId`, `indexedFrame.frameId`, `sampleId`, `sourceTimeUs`, `pixelFile`, and `pixelSha256`; each local PNG SHA-256 matched its receipt. Detector measurements were not opened during this audit. Operation result labels are the visible Home summaries, not detector scores.

The four-state Layout03 sequence is 24/24 raw pairs across four operations. Its four verifier summaries contain no stillness pass: three are `DetectionAmbiguous` and the restored state is `TimedOut`. The restored-state operation saved frames 2219, 2234, 2248, 2263, 2278, 2293, and 2309; all seven PNG hashes matched.

## Raw-only review draft

The lower-median `indexedFrame.sourceTimeUs` images were selected independently per Layout03 operation: frame 2083 (both present), 957 (black absent), 962 (cue absent), and 2263 (both restored). A source-pixel-only overlay and per-frame crop sheets were generated from those PNGs. Red/yellow centres were fitted independently in each selected image; black/cue crosses use raw-pixel circle proposals, and B?/C? are expected-empty points only. Apparent-radius guides are provisional. No detector receipt measurements were read for these drafts.

- [Four-frame raw-only overlay](../../work/o09/development-v3/reviews/layout03-validation-control-20261102/layout03-four-state-raw-only-overlays.png)
- [Both-present crops](../../work/o09/development-v3/reviews/layout03-validation-control-20261102/layout03-both-present-raw-only-crops.png)
- [Black-absent crops](../../work/o09/development-v3/reviews/layout03-validation-control-20261102/layout03-black-absent-raw-only-crops.png)
- [Cue-absent crops](../../work/o09/development-v3/reviews/layout03-validation-control-20261102/layout03-cue-absent-raw-only-crops.png)
- [Both-restored crops](../../work/o09/development-v3/reviews/layout03-validation-control-20261102/layout03-both-restored-raw-only-crops.png)
- [Raw-only review manifest](../../work/o09/development-v3/reviews/layout03-validation-control-20261102/raw-only-review-manifest.json), SHA-256 `43D0208353C3427651C1AC83F12575070E4A8F73FCB3AF3EAEA4D4D522DBB864`

These centres and radii remain drafts until the per-frame images are reviewed. Do not copy the previous train Layout03 labels into this validation group. The user has asked for no more daylight capture; no further live collection is planned in this cycle.

## Safe boundary and metrics

After the bounded capture, Pixel 11 Home showed `Start live session`; camera `/status` was `Stopped`, revision 57, completedFrames 57125, `clipCount=139`. The token-free readback is at `work/o09/development-v3/device-state/layout03-control-20261102/state4-poststop-safe-readback.json`. The camera is left stopped.

`SkyEye.Checks` remains **69/69 named groups PASS, carried forward; not rerun**. No source code or detector settings changed. The D2 acceptance counts remain **0/200** supported clear/still request windows, **0/100** held-out unsafe/source/control windows, and **0** 30-second soaks. This capture changes none of those denominators. D1, D2, and E remain **0/3 proven**. Physical shot/event/clip testing remains gated behind D1 and D2.

Next targeted item: `O09-NORMAL-VALIDATION-LAYOUT03-RAW-LABEL-REVIEW` on frames 2083, 957, 962, and 2263.
