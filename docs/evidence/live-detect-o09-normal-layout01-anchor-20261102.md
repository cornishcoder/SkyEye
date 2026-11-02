# O09 normal/train layout-01 anchor — 2 November 2026

## Result

`O09-NORMAL-TRAIN-LAYOUT-01-RAW-RECEIPT` **PASS 6/6 integrity checks** for one saved source image. The physical verifier operation itself **FAILED**: Home reported `Saved 1 of 5 required verifier frames (maximum 7). Table result: Unsettled (SampleGap).` It was stopped at that result boundary. The one hash-valid image cannot establish a five-sample stationary result, readiness, a soak, or a request/unsafe-window count. The user later replied “Looks correct” after seeing detector-generated crosses; that is recorded as **proposal-conditioned visual confirmation only**, not an independent label or score.

No retry is planned solely to satisfy the verifier's five-sample minimum. The existing pair is retained for provenance and proposal-conditioned review, but it is excluded from independent training labels and centre-accuracy scoring because detector output was shown before review. The next spatial candidate will use a different layout and a raw-only, blind-first annotation pass.

## Staging and light metadata

The user staged the planned `o09-v3-normal-train-layout-01` / `normal-train-layout-01` full 16-ball rack at marking orientation A, then placed the Pixel 11 face-up at table centre for about five seconds and moved it to the rail. The user reported **daylight** and **no direct glare**. The full-frame source image shows the cue, the 15-ball rack and blue chalk on the top rail; chalk is not a ball.

The TMD3743 Ambient Light service recorded three events during the inferred centre-hold window `11:26:33.248–11:26:38.067`: **77.41, 86.18 and 66.29 lux** (median **77.41 lux**, range 66.29–86.18). The phone location is not embedded in sensor events, so associating that five-second window with the user-confirmed centre placement is an inference from the requested sequence. This is a handset sensor measurement, not a calibrated lux-meter result. Exposure state is **unknown**: neither `/status` nor the saved receipt exposes it. The normal condition therefore has a provisional illuminance sample plus user-reported source/glare, but metadata is incomplete; **criteria-complete measured strata remain 0/2**. The warm/dim condition has not been measured.

Token-free ALS reading: `work/o09/device-state/normal-room-light-observation-20261102.json` (SHA-256 `6cfd9e2594b25bd6bc41a5b163dd08a39f603557167f3dc033e316349be27637`). Event excerpt: `work/o09/device-state/normal-room-als-samples-20261102.txt` (SHA-256 `a3db991af68e2640aa320db8b3a90f6b42a0cdf5d4e6d1b1b9fc7179212b6eff`).

## Source and receipt

| Field | Value |
| --- | --- |
| Operation | `274aea9e-a149-4645-aa42-851854c29a5b` |
| Frame / sample | `1530` / `274aea9ea1494645aa42851854c29a5b-1530` |
| Captured at / source PTS | `2026-11-02T11:31:24.0158896Z` / `4093819211` µs |
| Camera timeline | `bc9a6aadfd7345b49414a46b548b4690` |
| Calibration | `0ebf6a4965914c8bb3159f97686a6419` |
| Pixels | 1920×1080 lossless PNG; SHA-256 `b50d30a90e59d6626e897993485e1faffcffc2ccecf542c02dfabbbe416d5807` |
| Analyzer | width800; packed-colour boundary extent `.65`; exact identity retained in receipt |
| Independent labels | **Unavailable**; the recorded user response was conditioned on detector proposals |

The focused `O09-NORMAL-TRAIN-LAYOUT01-RAW-RECEIPT` audit passed **6/6**: operation/folder binding, sample/frame binding, named-file existence, pixel hash, dimensions, and source PTS/timeline/calibration/analyzer identity. Source and receipt are under `work/o09/development-v3/raw/normal-train-layout-01/attempt-274aea9ea1494645aa42851854c29a5b/274aea9ea1494645aa42851854c29a5b/`.

The single-frame analyzer proposed 16 objects (1 cue, 1 black, 7 red, 7 yellow). These are detector outputs, **not** ground truth or a score. The displayed [draft full overlay](../../work/o09/development-v3/raw/normal-train-layout-01/attempt-274aea9ea1494645aa42851854c29a5b/274aea9ea1494645aa42851854c29a5b/1530-draft-centre-overlay.png) and [enlarged crops](../../work/o09/development-v3/raw/normal-train-layout-01/attempt-274aea9ea1494645aa42851854c29a5b/274aea9ea1494645aa42851854c29a5b/1530-draft-centre-crops.png) were shown before the “Looks correct” response. The red/yellow captions retained raw detector suffixes rather than the sequential IDs named in the question. The response is therefore conditioned on the detector output and ID presentation; no independent centres are frozen, and this frame is excluded from training and accuracy scoring. The full context is preserved in [the proposal-conditioned review record](../../work/o09/development-v3/reviews/frame-1530-proposal-conditioned-visual-check.json).

## Capture and stop attempt history

The first Home start tap used a string-concatenated coordinate and landed off-screen. The camera remained `Ready` with zero frames/viewers; no capture action occurred. A fresh accessibility tree then supplied the enabled **Start live session** bounds; integer parsing produced the in-bounds centre tap `(820, 2192)`. The normal non-Cast action started the camera. The preview showed the table, the camera reported an active viewer and frames, and the verifier action was enabled.

After the verifier result appeared, Home **End recording session** was used. A fresh read at `2026-11-02T11:35:34.2292026Z` reported `/status` **Stopped**, revision3, 1,676 completed frames, zero viewers, 80% battery and `clipCount=139`. Home shows enabled **Start live session** and no **End recording session** action. Host ownership remains present through the existing flow. No table change, clip, match, Cast action, package update, or app-data reset occurred after the capture. Token-free [post-stop status](../../work/o09/device-state/normal-anchor-poststop-status-20261102.json) SHA-256 `ec5ca1d49e6a47ddb7fae6a30f06ebc3e9d0d74ea2a22bf0384c84e8f39d8ad0`; Home readback SHA-256 `6aaff8e3bd36a0c5c8e596d7757f3246e7d42e27be25e0046004f857d960ec13`.

## Metrics and next target

- `O09-LIGHTING-METADATA-PREFLIGHT`: readiness prerequisites **4/4 READY**; this is not a lighting pass.
- `O09-NORMAL-STRATUM-LUX-RECEIPT`: **PARTIAL**; handset lux/source/glare observations recorded, centre-time association inferred, exposure unknown. Complete measured strata **0/2**.
- `O09-NORMAL-TRAIN-LAYOUT01-RAW-RECEIPT`: **PASS 6/6**.
- `O09-NORMAL-TRAIN-LAYOUT01-VERIFIER`: **FAIL**, 1/5 saved, `SampleGap`; no readiness or stillness result accepted.
- `O09-NORMAL-TRAIN-LAYOUT01-PROPOSAL-CONDITIONED-REVIEW`: **RECORDED**, user said “Looks correct”; not independent ground truth and not scoreable.
- `O09-NORMAL-TRAIN-LAYOUT01-INDEPENDENT-LABEL`: **NOT AVAILABLE**; no blind raw-only annotation was done before detector output was revealed. Detector inventory remains **0/3**.
- Development spatial coverage: **0/12 usable independently reviewed layout anchors**. The frozen batch plans 24 state acquisitions (12 layouts plus 12 matched-control states); this first state was attempted once and yielded a valid source receipt, but contributes no independent label or verifier window.
- Software suites **3/3 PASS, carried forward**; focused motion checks **20/20 PASS, carried forward**. No code tests or detector replay ran in this physical cycle.
- Holdout remains unopened; physical D1/D2/E remain **0/3 proven**.

Before a first physical play test, the remaining work is three evidence blocks: complete Development collection/review/export and freeze one candidate; run independent Holdout scoring and pass D1; then pass installed-phone stationary/readiness gate D2. Only then can the physical shot sequence begin. Current collection has **0/12** usable layout anchors and **0/2** criteria-complete lighting strata. Calendar duration is unknown because successful capture rate, review throughput, candidate outcome and D2 duration have not been measured. Event/clip acceptance gate E follows the first play test.

Next target: **`O09-NORMAL-TRAIN-LAYOUT02-BLIND-ANCHOR`**. Stage the distinct full-table spread from the frozen batch plan. Freeze raw-only manual centres and the user's review before opening detector output; keep any post-unblinding response marked as conditioned. Continue the 12-group Development batch with the planned session/setup separation, bounded capture, and stop/readback at each table-change boundary.
