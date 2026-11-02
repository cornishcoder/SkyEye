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

The user reviewed the four selected frames and replied “Looks correct.” This accepts the visible centres and expected-empty locations for these four frames; it does not accept numeric centre tolerance or the provisional radius guides. Do not copy the previous train Layout03 labels into this validation group. The user has asked for no more daylight capture; no further live collection is planned in this cycle.

The separate [user-reviewed centre-label record](../../work/o09/development-v3/reviews/layout03-validation-control-20261102/user-reviewed-centre-labels.json) binds each selected source hash and records only centres and presence/absence; its SHA-256 is `e3f348bd22b14bb696f7f79ff7c9631c3c14b6fd7266155663e39eed25037ab6`. Radius labels remain provisional and are omitted from the frozen centre record.

## Fixed-patch image-condition check

Five fixed, ball-free 80×50 source-pixel cloth patches were compared using raw RGB pixels only. Luminance is `0.2126R + 0.7152G + 0.0722B`; frame 2083 is the reference. No numeric same-light acceptance threshold was predeclared.

| State/frame | Patch luminance deltas from frame 2083 (P1…P5) | Median absolute delta | Maximum absolute delta |
| --- | --- | ---: | ---: |
| Both-present / 2083 | 0.00, 0.00, 0.00, 0.00, 0.00 | 0.00 | 0.00 |
| Black-absent / 957 | −9.29, −7.29, −7.51, −5.86, −9.29 | 7.51 | 9.29 |
| Cue-absent / 962 | −12.22, −6.29, −5.29, −5.86, −9.29 | 6.29 | 12.22 |
| Both-restored / 2263 | −18.73, −0.01, +6.49, −10.44, −8.93 | 8.93 | 18.73 |

The source-patch shifts mean a same-light paired effect is **not established**. The cause is not assigned: daylight drift, camera exposure, or another imaging change may contribute. Keep the four reviewed frames as per-frame Development data, but do not make a causal matched-light black/cue claim from this sequence.

- [Fixed-patch map](../../work/o09/development-v3/reviews/layout03-validation-control-20261102/fixed-cloth-patch-map.png), SHA-256 `87389bbdc7c7c9c69338e70b21fb53f63acb5bca9a7a8035e83635564299bc73`
- [Photometry JSON](../../work/o09/development-v3/reviews/layout03-validation-control-20261102/fixed-cloth-patch-photometry.json), SHA-256 `8719d3ab7f36aa0a89d76017c84677533c545428073de5c53763018f9b7ed6ab`

## Safe boundary and metrics

After the bounded capture, Pixel 11 Home showed `Start live session`; camera `/status` was `Stopped`, revision 57, completedFrames 57125, `clipCount=139`. The token-free readback is at `work/o09/development-v3/device-state/layout03-control-20261102/state4-poststop-safe-readback.json`. The camera is left stopped.

`SkyEye.Checks` remains **69/69 named groups PASS, carried forward; not rerun**. No source code or detector settings changed. The D2 acceptance counts remain **0/200** supported clear/still request windows, **0/100** held-out unsafe/source/control windows, and **0** 30-second soaks. This capture changes none of those denominators. D1, D2, and E remain **0/3 proven**. Physical shot/event/clip testing remains gated behind D1 and D2.

Next targeted item: `O09-DEVELOPMENT-V3-CORPUS-ALIGNMENT`; retain the four frames as separately labelled Development samples and preserve the unmatched photometry status.

## O09-DEVELOPMENT-V3-CORPUS-ALIGNMENT

The raw-only centre/existence review is frozen in the source-bound user review record (SHA-256 `e3f348bd22b14bb696f7f79ff7c9631c3c14b6fd7266155663e39eed25037ab6`). `O09-DEVELOPMENT-V3-CORPUS-ALIGNMENT` passes **6/6 preflight assertions**. The result binds the exact v3 contract and split-plan hashes, planned setup group `o09-v3-normal-validation-layout-03`, role `validation`, stratum `normal-pool-room`, layout `normal-validation-layout-03`, selected-frame roster/counts, receipt/pixel hashes and dimensions, and identical calibration, analyzer, configuration and frame-source identities. Holdout was not opened; no official corpus or split lock changed.

This is partial alignment only. The complete 12-layout Development export remains not ready; 12 of 24 staged state identities have a capture and 13 planned acquisitions remain. The four-state photometric comparison did not establish same-light conditions, and the reviewed record does not review radii or set numeric centre uncertainty. Receipt metadata says detection was requested for 2/4 selected frames; no detector measurements were read during this preflight. The follow-on `O09-NORMAL-VALIDATION-LAYOUT03-SPATIAL-BASELINE` is complete; see its [score and transform report](live-detect-o09-normal-validation-layout03-spatial-baseline-20261102.md). Do not use that result as a matched-light control or readiness result.

Preflight result: ignored `work/o09/development-v3/alignment/layout03-validation-alignment-preflight.json`; SHA-256 `F1B5DADAB674BDB980BDC87CD4C5753B8C5E585C348784698981A3D07444E6E9`. The checker source is in ignored `work/o09/development-v3/alignment/layout03_validation_preflight.py`. `SkyEye.Checks` 69/69 is carried forward; not rerun because no production source changed.

## Follow-on spatial baseline result

`O09-NORMAL-VALIDATION-LAYOUT03-SPATIAL-BASELINE` scored all four reviewed frames against current-source width-800/`.65` outputs: **62 visible labels, 59 TP, 0 FP, 3 FN; 100% precision, 95.16% recall**. The black was missed in all 3 present frames; all 4 outputs remained uncertain. A receipt-bound `TableCalibration.Map`/`Unmap` audit checked 64 visible/expected-empty points and inverse-projected 59 detections for source overlays. Its maximum transform round-trip residual was `5.1e-13` source pixels; no reviewed centre tolerance was set. The four states are still not photometrically matched, and this does not pass inventory acceptance, D1, D2, or E. The phone remains unnecessary for the next offline action. Exact next test: **`O09-LAYOUT03-EXACT-STAGE-FAILURE-TRACE`**, using the saved frames and unchanged thresholds. Full method, per-state scores, hashes, safe-state readback, and harness failures are in the [baseline report](live-detect-o09-normal-validation-layout03-spatial-baseline-20261102.md).

## Additional raw review selection

O09-NORMAL-VALIDATION-UNREVIEWED-FRAME-SELECTION:12/12 source PNG hashes match receipts across Layout01/02 (6 each). Lower-median source PTS selects frame1587 from Layout01 and frame31 from Layout02, independent of detector results. Source/receipt hashes and IDs retained in work/o09/development-v3/reviews/normal-validation-layout01-02-raw-selection-20261102/selection-manifest.json. Raw contact sheet generated and inspected; no centres/radii accepted or detector measurements opened. Next prepare raw-only point/crop review packs for both frames. Counts unchanged:13/24 captured,4/12 reviewed,lighting0/2,D1/D2/E0/3.

## Layout01/02 accepted centre review

User replied Looks correct to displayed frame1587 and frame31 raw crop sheets:31/31 visible centres accepted. Separate reviewed records retain original draft SHA and source SHA; radii/numeric tolerance unset. No detector measurements opened before review. Reviewed anchors advance4/12 to6/12; captured13/24 unchanged. Frame1587 cue absent; frame31 all16 present. Next source-bound C# transform/group alignment and scoring; radius guide review remains required for export.
