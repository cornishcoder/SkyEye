# SkyEye detection evidence index

Created 10 November 2026 by an independent review of ball detection, tracking
and table-motion recognition (repository HEAD `f3431ec7`). Companion
documents: [diagnosis](skyeye-detection-diagnosis.md) and
[handover notes](skyeye-detection-investigation-notes.md). A second,
concurrent review wrote its own material to [`docs/argon/`](../argon/README.md);
section 10 reconciles the two.

This is the register of what evidence exists, what it shows and what has
already been tried. **Check it before asking for any new physical capture.**
Everything here was produced from files already on disk. No camera session,
device replay or table staging was used.

## 1. How to regenerate

The tools are in `tools/evidence/`. They need only Python 3 with OpenCV,
numpy and Pillow, plus ffmpeg for video. They never write to evidence files,
and they put their output in the git-ignored folder `work/evidence-audit/`.

| Tool | Output | Purpose |
|---|---|---|
| `inventory_media.py` | `inventory.csv` | Every image, video and raw-pixel file: size, time, SHA-1, dimensions, difference hash |
| `index_receipts.py` | `receipts.jsonl` | Every frame receipt: capture time, timeline, calibration corners and the production detector's result |
| `index_labels.py` | `labels.jsonl` | Human-reviewed centre labels only (`reviewed: true`), de-duplicated by pixel hash |
| `frame_metrics.py`, `robust_light.py` | `frame-metrics.csv`, `robust-light.csv`, `rectified/` | Lighting measured from each frame's pixels |
| `contact_sheets.py` | `sheets/operations-*.jpg`, `sheet-index.csv` | One tile per capture operation, in capture order |
| `detector_overlay_sheets.py --failures` | `sheets/detector-fail-*.jpg`, `detector-fail-index.csv` | Rectified frames with the production result drawn on |
| `video_sheets.py` | `sheets/videos-*.jpg`, `videos-index.csv` | The middle frame of every unique video |
| `score_detections.py [--detections F]` | `score-<name>.json` | Precision, recall, position error and colour accuracy, by colour, zone and light |
| `burst_stability.py` | `burst-stability.csv` | Still-table stability, per frame and per ball |
| `video_change_signals.py` | `video-change-signals.csv` | Raw versus gain-compensated pixel change in each clip |
| `prototypes/disc_contrast_detector.py` | `detections-disc-contrast.jsonl` | Negative prototype, kept so it is not retried (section 7) |

## 2. Inventory

| Quantity | Count |
|---|---|
| Media files, excluding build folders, SDKs and a decompiled third-party app | 5,248 (5.7 GB) |
| Images / videos / raw-pixel files | 5,019 / 136 / 93 |
| Unique files by SHA-1 | 2,922; 3,041 files sit in 715 exact-duplicate groups |
| Camera frames, 1920x1080 | 1,868 files, 899 unique |
| Rectified table rasters (1000x2000, 800x1600) | 1,044 files, 401 unique |
| Phone screenshots / small crops / other overlays and sheets | 884 / 801 / 422 |
| Unique videos | 127, about 37 minutes in total |
| Frame receipts | 1,032, resolving to 492 unique camera frames with pixels present |
| ... from | 90 source timelines, 83 capture operations, 48 calibration IDs |
| ... captured on | 7 October, then every day from 25 October to 2 November 2026 |
| Unique frames with an embedded production detector result | 202 |
| Unique frames with human-reviewed centre labels | 26 (401 visible balls: 182 red, 179 yellow, 20 cue, 20 black; 10 expected-absent) |
| File timestamps | 3 October to 2 November 2026, plus one file from 16 September |

Git tracks only 840 files. Nearly all the evidence lives in the ignored
`work/` folder, mainly `work/production/evidence` (3,681 media files),
`work/o08` and `work/o09`, so **it has no backup in the repository**.

Largest evidence folders by media count: `shot-samples-20261027` (620),
`code70-daylight-pocket-capture-20261031` (468),
`work/o08/live-detect-black-pair-v1` (414),
`local-raw-proposal-gates-20261031` (298),
`code69-temporal-edge-check-20261031` (288),
`live-detect-pocket-lip-20261030` (255), `work/o09/development-v3` (252) and
`o08-packed-rack-investigation-20261028` (178).

### Inaccessible or uninspected material

- `work/companion-candidates.png` is not an image. The Argon review found it
  is adb text output saved with a `.png` name.
- One video could not be decoded for a thumbnail (status `undecodable` in
  `sheets/videos-index.csv`).
- Videos: one middle frame of each was viewed. They were not watched end to
  end; section 6 gives only the measured change signals.
- The 93 raw-pixel files (`.bgr`, `.argb`, `.gray`) were counted but not
  rendered.
- The 884 phone screenshots and 801 crops were classified by size only; they
  were not viewed one by one.
- No receipt or status file records exposure time, ISO or white-balance
  values, so camera auto-exposure can only be inferred from pixels.
- Documents not read in full: `docs/live-detect-plan.md` (334 KB),
  `docs/o08-observation-reliability-report.md` (191 KB),
  `docs/device-acceptance.md`, `docs/glow-up-*.md`, the two test ledgers in
  `docs/evidence/`, and most individual `docs/evidence/*.md` reports.

## 3. What was viewed

- All 101 tiles of `sheets/operations-01..06.jpg`: one representative raw
  frame per capture operation.
- All 46 tiles of `sheets/detector-fail-01..04.jpg`: the worst frame of every
  operation whose production result was not a clean 16, rectified, with the
  detector's circles drawn on.
- All 126 tiles of `sheets/videos-01..02.jpg`.

These were thumbnail-scale views (480x270 raw, 320x640 rectified). No
individual frame was opened at full 1920x1080 resolution in this pass. The
Argon review did view full-resolution failure crops (its sheets 5 and 6).

## 4. Scene families (near-duplicate grouping)

A 64-bit difference hash collapses the 899 unique camera frames to 242
distinct hashes and 35 loose clusters, because the fixed overhead view
dominates the image. Grouping by capture operation and viewing each group
gives the families below. Tile numbers refer to `sheets/sheet-index.csv`.

| Family | Operations | Dates | Lighting covered | Notes |
|---|---|---|---|---|
| Full rack with the cue on its spot | about 45 (#2, #7-#47, #58, #85, #94) | 25-29 Oct, 2 Nov | Near dark (table V about 30) to daylight (V about 180); warm lamp light that turns the cloth grey; hard window shadows | The same arrangement staged again and again under different lights |
| "Pocket-lip": a ball in every pocket jaw plus one centre yellow | 16 (#49-#64) | 30-31 Oct | Mostly dim warm light (V 50-76); two in daylight | Staged repeatedly across code versions 59 to 70 |
| Grid along the cushions, black and cue moved between pockets | 20 (#65-#84) | 1 Nov | Daylight fading to dusk (V 160 to 94) | Present/absent controls for black and cue |
| Cushion layout with black and cue in a corner jaw | 8 (#86-#93) | 2 Nov | Daylight (V 117-141) | |
| Open spreads | 11 (#3-#6, #48, #95-#100) | 7 Oct, 29 Oct, 2 Nov | Daylight and dusk | The only still frames that resemble ordinary play |
| Break aftermath, dim | 1 (#1) | undated | V about 35 | |
| Hand over the rack / cue stick in view | 2 (#15, #58) | 26 Oct, 30 Oct | | Obstruction controls |

Videos (`sheets/videos-index.csv`): V1-V24 are checkerboard and bench tests,
and V30-V31 and V52-V59 are synthetic patterns. The table videos are the rack
under changing light (V65-V72 and V79-V87, about 30 s each), the shot samples
of 27 October (V74-V78), and **about 35 event clips of real play on 29
October** (V88-V122: breaks, pots, misses, arms and cue over the table and
natural layouts, 6 to 61 s each). Those real-play clips are the only evidence
of genuine ball motion, and they have never been turned into a labelled
corpus: `docs/observation-corpus-guide.md` still records zero real timed
shots.

## 5. Lighting, measured from the frames

From `robust-light.csv` (medians over the inner playing area of 492 frames):

- Median brightness V runs from 28 to 143; the 10th to 90th percentile is 64
  to 134.
- Median saturation runs from 0 to 89 and is below 40 in roughly half the
  frames. Under warm or dim light the blue-grey cloth is effectively neutral
  grey, so any cloth model based on hue has nothing to work with.
- Brightness varies across a single frame by a median factor of 1.7 and up to
  2.1 (the window side against the far side).
- Daylight frames show long, hard ball shadows. A black ball beside a cushion
  sits inside the cushion's own shadow band.

The earlier plan to stage two named lighting strata with 12 layouts each
recorded 0 measured strata. The frames already on disk cover the whole
brightness range continuously.

## 6. Observations tied to files

The production results below are those embedded in each receipt when the
frame was captured. They come from 10 algorithm versions (v1 to v25) and are
not a re-run of HEAD.

1. **Reds and yellows are reliable from about V 65 upwards.** On the 19
   labelled frames that carry a result: red 133/133 and yellow 133/133, with
   correct colours, no false positives, and centre error of median 0.03 and
   p95 0.15 ball diameters (`score-production-at-capture.json`).
2. **The black ball at a cushion or pocket jaw is the persistent miss**, at
   every light level. Among labelled frames black is found 12/15; all three
   misses are the black touching the top cushion
   (`work/o08/live-detect-black-pair-v1`, captured 1 Nov at 12:48, 15:49 and
   17:13). The same miss is visible, unlabelled, in tiles D2-D9, D11-D18, D21,
   D26, D31-D34, D37, D38 and D41-D43.
3. **The cue ball is missed when it sits in a corner jaw next to the black**:
   D18, D31-D34, D38 and D41-D43 (2 Nov, daylight).
4. **Correct readings are reported as uncertain.** 16 of the 19 labelled
   frames are entirely correct, but only 7 were reported clear. The other 9
   are scenes where the black or the cue was deliberately removed.
5. **Dim warm light plus pocket jaws loses one ball of each jaw pair**: D3,
   D5-D9 and D13 (30 Oct, V 68-76, saturation 6-17). Later code versions read
   15 of 16 on the same layout (D12, D14, D15).
6. **Near darkness collapses classification**: D1 (29 Oct 00:13, V 28) has
   eight detections, all `unknown`.
7. **One wrong colour label erases a whole colour.** In D20 (2 Nov 16:47) the
   cue is called yellow, giving eight yellows, and all eight then become
   `unknown`. D23 shows the same effect with three unknowns.
8. **Still tables flicker under constant light.** Across 42 bursts of 3 or
   more frames (2 to 3.5 s each), 31 of 126 consecutive frame pairs disagree
   under the tracker's own rule and 16 of 42 bursts are unstable, while table
   brightness within those bursts changes by 0 or 1 level. Per ball, only 28
   of 617 balls (4.5%) are intermittent, no colour label ever flips, and
   centres move by a median of 0.02 and a p95 of 0.08 ball diameters
   (`burst-stability.csv`).
9. **Two saved "false event" clips show a motionless, constantly lit table**:
   `code62-pocket-lip-20261031/code62-false-pot-clip-003.mp4` and
   `code62-false-shot-clip-004.mp4` have a brightness ratio of 1.00-1.02 and
   essentially no pixel change throughout (`video-change-signals.csv`).
   Whatever raised those events was not in the image.
10. **Exposure or lighting steps do happen inside clips.** Five clips contain a
    14-20% brightness step within 0.3 s. Raw differencing then flags 33-60% of
    pixels as changed; per-tile gain compensation flags 3-5%
    (`o08-code30-live-detect-20261027/clips/de273197...`, `91eb56e6...` and
    `56a92219...`; `code57-readout-recovery-20261029/event-clips/265b12a1...`
    and `603ae482...`). Real-shot clips keep 6-19% after compensation.
11. **The camera never locks exposure or white balance.**
    `src/SkyEye.Camera/CaptureEngine.java` builds a `TEMPLATE_RECORD` request
    and sets only continuous autofocus and a 30 fps range. Argon measured the
    resulting cloth colour shift between captures in unchanged room light
    (`docs/argon/contact-sheets/sheet_03_*`).
12. The receipt field "User declared still table and N balls" always says 16,
    even on frames whose reviewed labels show 15. It is not ground truth.
13. Production analysis time recorded in the receipts: median 273 ms, p95
    386 ms, maximum 694 ms per frame (203 frames), on the Pixel 11.

## 7. Historical experiments (do not repeat)

Taken from `docs/evidence/live-detect-scorecard-20261101.md`, the workflow
note and the code comments in `VisionSettings.cs`.

| Experiment | Recorded outcome |
|---|---|
| Rack under daylight, all lights, mid light and lights off, repeated on at least five days | Rack reads complete in most conditions; used to tune thresholds |
| Local illumination normalisation on/off | Disabled after it produced a false neutral body |
| Normalised geometry for detection | Disabled after clear rack reads fell from 23/24 to 20/24 |
| Pocket-lip layout across code versions 59 to 70 | Edge margin 65 to 23 px and sparse-colour support 20/32 recovered edge reds and yellows; black still missed |
| Sparse-black seed enumeration, fit budget 12 to 64 | 0/3; black seeds reach 18-19 of 32 sectors against a threshold of 21 |
| Partial-arc discrimination for black | Positive and empty controls both pass, so it cannot discriminate |
| Black "marker" (number patch) feature | 8/10 on development points, with a post-hoc threshold |
| Black and cue present/absent controls, restaged | Confounded because daylight faded between states |
| Raw-perspective Hough, colour-body, packed-peak, local-scale and boundary-support probes (31 Oct) | Diagnostic only; none adopted |
| Learned detector, YOLO11n, trained on 6 and then 15 frames | 0/4 and 0/7 complete layouts, with hundreds of false positives |
| Cloth-relative disc matched filter (this review, `prototypes/`) | Recall 0.915, precision 0.275 on 25 labelled frames; it fires on cushion and pocket edges. Negative |

## 8. Missing labels and unresolved questions

- No dim or warm-light frame has reviewed labels. All 26 labelled frames come
  from 1-2 November in mid or bright light.
- No frame of ordinary mid-game play is labelled, and no video has motion or
  stillness intervals labelled.
- Six labelled frames have no embedded detector result
  (`work/o09/development-v3/partial-normal-reviewed-radius-corpus-v2/{1085,1274,3360,382,395}`
  and `work/o08/frame978-development-v1/corpus-v1/978`). They are scored only
  when a candidate's detections are supplied.
- This review has not re-run HEAD on these frames, because the detector only
  builds for Android. The results above come from the versions that ran at
  capture time.
- Calibration extent: in rectified frames the full cushion rubber is inside
  the raster and the pocket holes are cut in half at its border, so a ball
  deep in a jaw is partly outside the analysed image. Argon additionally
  reports a 14-27 px outward parallax error for balls at the cushions. This
  review has not measured that independently.

## 9. Register of proposed experiments

Record every proposal and its outcome here so it is not requested twice.

| Date | Proposal | Needs the table? | Status |
|---|---|---|---|
| 10 Nov 2026 | Confirm draft centre labels for about 40 frames already on disk (dim, warm-light, pocket-jaw and open-play frames chosen from sections 4 and 6), shown on prepared sheets | No: review of saved images | Proposed |
| 10 Nov 2026 | Mark moving, still and obstructed intervals on the 29 October real-play clips | No | Proposed |
| 10 Nov 2026 | One bounded live check that an exposure and white-balance lock holds, **after** the software change is built | Yes, but no ball arrangement needed | Deferred until the code exists |
| 10 Nov 2026 | Any new staged layout or lighting session | Yes | **Not justified.** Every failure in section 6 is reproducible from existing frames |

## 10. Relationship to the `docs/argon/` review

A concurrent review wrote `docs/argon/` between 22:18 and 22:45 on
10 November. Its counts differ slightly because it hashed with SHA-256 over a
different file set (4,970 images, 740 unique raw frames, 36 scene clusters,
"32 reviewed ground-truth frames"; this index finds 26 unique labelled pixel
hashes). Its code-level claims were spot-checked against HEAD:

| Argon claim | Check |
|---|---|
| Camera AE/AWB never locked (`CaptureEngine.java`) | Confirmed |
| `PackedBallClusterRecovery.cs:506` stops after 64 components | Confirmed (`enumerateAllSeeds` lifts the cap only in evaluation) |
| `CueBallRecovery.cs:290` aborts with more than 4 candidates | Confirmed |
| `BallBoundaryRefiner` rejects black/cue sectors with exterior difference > 40 | Confirmed, line 617 |
| All eight yellows demoted to unknown when one extra is classified yellow | Mechanism confirmed, but the cited function `EnforceKnownTableBallInventory` at `VisionAnalyzer.cs:1174-1228` does not exist. The code is `BallClassifier.ConstrainInventory` (`BallClassifier.cs:18-27`), called at `VisionAnalyzer.cs:479` |
| `SkyEye.Checks` 72/72 passing | Not re-run here (no .NET SDK in this shell) |
