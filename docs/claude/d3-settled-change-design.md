# D3: settled-frame change evidence in Live Detect — design

11 November 2026. Decision D3 of [the detection plan](skyeye-detection-fix-plan.md) (section 9),
approved by Richard on 11 November: build settled-frame change evidence into the live pipeline,
**not used by default** until reviewed labels (T9) score it, and gated off when lighting moves.

This is a design and a test-fixture plan only. Nothing under `src/` was changed: another agent is
changing the Live Detect reducer and `VisionAnalyzer` for D1 and D2 on the same branch
(`claude/live-detect-d1-d3`). Implementation follows once that lands. Every C# item below is
unbuilt until a Windows session runs `scripts/Build-SkyEye.ps1` and the check suites.

Inputs: [T4 report](t4-settled-change-experiment.md) and its prototype
`tools/evidence/prototypes/settled_change.py` (v3: per-tile gain compensation, ball-sized region
rule, rail-overlap rejection, cushion-band drift); [T3 report](t3-live-detect-replay.md); the
Required outcome in `docs/live-detect-plan.md`; and the live code as of commit 42a2c4f.

## 0. Summary

- **Where it runs.** A new image-side component, `SettledChangeProcessor`
  (`SkyEye.Vision.Android`), called from the existing Live Detect `frameObserver` hook. That hook
  runs inside `TableAnalysisSession.RunUpdateAsync` while the frame's bitmap is still alive, after
  the detector and before the image-free reducer sees the sample. No change to
  `TableAnalysisSession` or `VisionAnalyzer` is needed.
- **What it keeps.** Never a `TableFrame` or bitmap. It keeps small rectified copies it owns:
  one reference and up to three window rasters at 440x840 (about 1.1 MB each), plus one reusable
  full-frame RGBA scratch Mat (8.3 MB).
- **Resolution.** Half the T4 raster: a 400x800 table plus a 20 px margin (440x840). It was checked
  on the T4 data (section 1.4). It gives the same black hits (9 of 9), 0 false regions on controls,
  and keeps 101 of 102 T4 clip regions with 0 new ones.
- **Cost.** Measured on Richard's PC (one thread): 7.9 ms for the change map plus 2.9 ms for the
  warp, against 32 ms and 9.5 ms at full T4 size. Pixel estimate (not measured): about 10–18 ms on
  ordinary passes, and about 30–45 ms on the one pass per settled segment that runs the
  comparison. The median receipt analysis is 273 ms.
- **What it reports.** An image-free `SettledChangeEvidence` record: status and gate reasons; the
  reference and current window times; a drift measure; up to 12 change regions in table
  coordinates, each with zone, size, rail overlap, direction (arrival, departure or unclear) and
  appearance hints; and a coarse change-occupancy grid.
- **How the reducer uses it.**
  - **Hold** a baseline ball that the detector drops, when nothing changed at its position.
  - **Infer** a missing black or cue from one unexplained, ball-like arrival.
  - **Mark** a D1 pocket region as unchanged or changed.
  - **Upgrade** a D2 "not seen" black or cue to "inferred".

  Every such ball is labelled *held* or *inferred* in the readout and log, never *observed*. None
  of these uses can create a pot or return event.
- **Default.** Mode `Shadow`: the evidence is computed and logged on every live session, and the
  reducer computes what it *would* do (logged as proposals) but applies nothing. `Active` is
  switched on only after T9 scoring. `Off` is a kill switch that does no image work.

## 1. Where the image work can live

### 1.1 The live path today

| Step | Code | Pixels available? |
|---|---|---|
| Frame read | `PlayerTableFrameSource` returns a `TableFrame` whose `Image` is a `BitmapTableImage` (one raw decoded texture copy, 1920x1080 RGBA; "owns exactly one raw decoded texture copy") | yes |
| Detector | `TableAnalysisSession.RunUpdateAsync` → `VisionAnalyzer.Analyze(TableFrame, …)`: `Utils.BitmapToMat`, RGBA→BGR, then `Warp` to the analysis grid (`settings.Width` x 2·Width, **no margin**) | yes, inside the analyser only |
| Accept | Under the session lock: `TableTracker.Observe`, then the snapshot is published (`Vision`, `Observation`, receipt) | bitmap still alive |
| **`frameObserver(frame, detected)`** | Called after acceptance, still inside `using (frame)`. Exceptions are caught and counted (`frameObserverFailures`), so a failure cannot void the table reading | **yes: the last point where the bitmap exists** |
| Frame disposed | `BitmapTableImage.Dispose` recycles the bitmap | no |
| Reducer | `ShellLiveDetect.RunLiveDetectAsync` reads `table.State`, builds `LiveDetectTableEventObservation`, calls `LiveDetectTableEventTracker.Observe` (image-free) | no |
| Log | `LiveDetectLogStore.AppendFrame` (schema 14) | no (a PNG only if `LiveDetectFrameEvidenceCapture` took one in the observer) |

Live Detect already uses this hook: `frameObserver: (frame, vision) => liveDetectPendingFrameEvidence
= evidenceCapture.Observe(frame, vision)`. The result is held in a local and consumed later in the
same pass. D3 follows the same pattern.

Paths that were considered and not used:

- **`RoiMotionProcessor` / `TableMotionFeed`.** This is the diagnostic motion feed, with its own
  frame source and its own two-image ownership (one active frame and one pending). It crops ROIs
  from the bitmap before conversion. It is not run by Live Detect, and its ROIs are not the
  table. Adding a third image there would break its ownership rule. Its illumination-normalised
  change measure (`MeasureIlluminationNormalizedChange`) is a useful precedent only.
- **Verifier (`TableVerificationWorker`).** This is a separate burst path that keeps only colour
  gains (`BurstAppearance`), not pixels. D3 is Live Detect only.
- **A hook inside `VisionAnalyzer`.** The pattern exists (`ObserveNextBoundaryPixels`) and would
  save one `BitmapToMat`, but it couples D3 to a file the D1/D2 agent is changing. The analyser's
  warp also has no margin, and the rail rule needs one. Revisit later as an optimisation only.

### 1.2 Placement

```
SkyEye.Core/Analysis/SettledChangeContracts.cs      image-free records, enums, ISettledChangeProcessor, settings
SkyEye.Core/Analysis/SettledChangeMath.cs           pure managed parts: tile median on byte arrays, drift,
                                                    region rule, zones, raster<->table mapping (desktop-testable)
SkyEye.Vision.Android/SettledChangeProcessor.cs     OpenCV parts: bitmap->Mat, warp, morphology, components,
                                                    distance transform, HSV hints; owns its Mats
SkyEye.Companion.Hybrid/LiveDetectSettledChange.cs  reducer-side policy (chain, held/inferred, proposals) - pure
ShellLiveDetect.cs (edit)                           create/reset/dispose the processor; call it in frameObserver;
                                                    pass evidence into the observation; log
LiveDetectTableEventTracker.cs (edit)               optional evidence field; ball states; uses gated by mode
LiveDetectLogStore.cs (edit)                        schema 15 fields; evidence raster sidecars
```

The reducer stays image-free: it only receives the `SettledChangeEvidence` record.

### 1.3 Ownership and memory rules

1. The processor never stores a `TableFrame`, `ITableFrameImage` or `Bitmap`. Inside `Observe` it
   copies what it needs into Mats it owns, and returns before the frame is disposed.
2. Retained native memory:
   - one reference raster (440x840 BGR, 1.1 MB);
   - a ring of at most three window rasters (3.3 MB);
   - the previous quarter-size grey for motion (220x420, 92 KB);
   - one reusable 1920x1080 RGBA scratch Mat (8.3 MB), allocated once per run to avoid native
     churn.

   That is about 13 MB, released in `Dispose`.
3. Lifetime: one processor per Live Detect run.
   - **Created** in `RunLiveDetectAsync` next to `LiveDetectFrameEvidenceCapture`.
   - **Reset** when `frameCursor` reports a timeline change, when `MediaGeneration` changes, and
     when the calibration ID changes (Live Detect already stops on a calibration change).
   - **Disposed** in the run's `finally` block.
4. Threading: `UpdateAsync` is awaited before the next pass, so `Observe` calls never overlap.
   OpenCV work runs on the thread-pool continuation that calls the observer. The detector's
   `analyzerGate` has been released by then, so the next pass's detector cannot start until this
   pass returns. Calls on separate Mats need no lock.
5. If `frame.Image` is not a `BitmapTableImage` (a JPEG-only source), the processor returns
   `Unavailable("no raw image")` and does not decode the JPEG.
6. Any exception inside the processor is caught by the processor itself. It returns
   `Unavailable(<exception type>)` and resets. Unlike the session's observer catch, this keeps the
   evidence field present in the log.

### 1.4 Resolution and cost

Measured with `work/evidence-audit/d3/scale_check.py` and `clips_scale.py` (git-ignored; they
import the T4 prototype read-only) on Richard's PC, OpenCV 5.0 Python, one thread:

| Raster (table + margin) | Change map, median / p95 | Warp from 1920x1080 | Stills: black hit | Stills: cue hit | Same-burst controls: false regions | Clip pairs vs T4 full size |
|---|---|---|---|---|---|---|
| 880x1680 (T4) | 32.4 / 50.1 ms | 9.5 ms | 9/9 (min p95 1.91) | 7/11 | 0 (max p95 0.36) | reference |
| **440x840** | **7.9 / 10.3 ms** | **2.9 ms** | **9/9 (min 1.86)** | **7/11** | **0 (max 0.33)** | **101/102 regions kept, 0 new; controls 0** |
| 352x672 | 5.1 / 6.8 ms | 2.2 ms | 9/9 (min 1.87) | 6/11 | 0 | not run |

Notes:

- The 4 cue misses at full size are the 3 merged black+cue pairs plus one cross-session pair, as
  in T4.
- At half scale, kernels are scaled: tile 40 px, blur 3x3, opening 3x3, elliptical closing 5x5,
  R = 10.8 px, rail tolerance 2 px.
- Drift on the half raster:
  - the three cross-session daylight pairs measure 0.112–0.342;
  - the worst other still pair measures 0.067;
  - the worst clip pair measures 0.046.
- **Choice: 440x840.** 352x672 starts to lose cue hits.

**Per-pass estimate on the Pixel (not measured).** Assumes the phone is 2–3x slower per thread
than this PC for these kernels, and that `BitmapToMat` of 8.3 MB takes 3–6 ms.

| Work | When | Estimate |
|---|---|---|
| `BitmapToMat` into scratch | every pass | 3–6 ms |
| Warp to 440x840 + grey | every pass | 6–9 ms |
| Quarter-size motion measure vs previous pass (tile-compensated, T4 `scan` rule) | every pass | 1–2 ms |
| Window ring update (copy) | every settled pass | < 1 ms |
| Median of 3 window rasters, change map, components, hints, grid | once per new settled segment | 20–30 ms |
| **Ordinary pass** | | **about 10–18 ms (4–7 % of 273 ms)** |
| **Comparison pass** | about once per shot | **about 30–45 ms (up to about 17 %)** |

At full T4 size the comparison alone would be roughly 65–100 ms, which is why half scale is
chosen. The processor logs its own `computeMs`, so the first shadow session measures the real
figure. If the p95 exceeds 40 ms, the first lever is the `VisionAnalyzer` pixel hook (saves the
`BitmapToMat`); the second is running the comparison on a background task with the result arriving
one pass later.

## 2. The evidence contract

### 2.1 Image side: what the processor does each pass

Inputs: the `TableFrame` (bitmap, `SourceTimeUs`, `DecodedReceipt`, `Timeline`), the run's
`TableCalibration` and `calibrationId`, and the detector's `VisionResult` (for obstruction
components only).

1. **Rectify.** The homography maps calibration corners (`VisionAnalyzer.Warp` order) to a
   400x800 portrait table with a 20 px margin. It is recomputed only when the calibration or
   frame size changes.
2. **Motion.** Use `log(grey+8)` via a 256-entry table at quarter size (220x420), against the
   previous pass:
   - subtract a 16 px per-tile median, blur, threshold 0.08 and open 3x3;
   - take the fraction of the inner table.

   This is T4's `scan` rule. If the source gap to the previous pass exceeds 1.5 s, motion is
   *unknown*, and the settle count restarts.
3. **Settle state.** States: `Moving`, then `Settling`, then `Settled`. A pass is still when
   motion is below 0.0025. A segment is settled after at least 3 consecutive still passes
   spanning at least 1.2 s. This is T4's rule, re-expressed for Live Detect's cadence of about
   0.3–0.5 s per pass.
4. **Reference.** While settled, keep a ring of the last 3 rasters. On the first moving pass
   after a settled segment, freeze the per-pixel median of the ring as the **reference**, with
   its window times and the segment's start and end times. The reference is always the *last*
   settled view before motion, so slow daylight drift during a long still period is absorbed.
5. **Comparison.** When a new segment becomes settled and a reference exists:
   - take the median of the window as **current**;
   - run the T4 v3 grey change map at half scale;
   - evaluate the gates (section 3);
   - extract regions;
   - cache the result as this segment's evidence, with a new `Sequence` number.

   Later passes in the same segment re-report the cached evidence and do not recompute it.
6. **Change map details:**
   - `lr = LUT[gb] - LUT[ga]`, quantised to 1/64 into a byte (128 = 0);
   - per-tile median by 256-bin histogram on 40 px tiles, with the field resized bilinearly;
   - absolute residual, blur 3x3, threshold 0.18 (`> 11.5/64`);
   - opening 3x3, elliptical closing 5x5, connected components.

   The quantisation error (≤ 0.008) is well under the 0.18 threshold. This is the one deliberate
   change from the prototype. The Python reference (section 5) uses the same quantisation, so
   parity can be exact.
7. **Regions.** For each component of at least 0.3 ball area (cap 12, largest first):
   - inscribed radius (distance transform on the bounding-box crop);
   - area;
   - fraction outside the table raster (the rail rule);
   - peak residual;
   - zone (T4 `zone`: jaw within 5R of a pocket point, cushion within 4.4R of an edge);
   - `BallSized` = 0.6R ≤ inscribed radius ≤ 1.45R, and area ≤ 5 ball areas, and outside fraction
     ≤ 0.05;
   - `RejectReason` ∈ {`rail`, `oversize`, `thin`, `under_obstruction`}.

   Appearance hints, from HSV inside the component in reference (before) and current (after):
   - cloth-likeness (T4 `clothlike`, with the cloth model taken from the bed median);
   - dark fraction (V < 0.35 x cloth V);
   - white fraction (S < 60 and V > 1.25 x cloth V, tentative).

   Direction: `Arrival` if cloth before − cloth after > 0.25; `Departure` if the reverse; else
   `Unclear`. T4 found this is mostly `Unclear` in dim light, so the reducer never relies on it
   alone.
8. **Change grid.** A 20 x 40 bit grid over the table, in 20 px cells (0.05 table width). A bit
   is set when at least 5 % of the cell's pixels are above threshold *after the opening* and
   before any size filter. It is 100 bytes. The reducer uses it to refuse a hold wherever
   anything changed, including changes too small or too odd-shaped to be a region. The T4 miss
   599b s0 (a red departure "too thin") is the case this protects.

### 2.2 Records (`SkyEye.Core.Analysis`, image-free)

```csharp
public enum SettledChangeMode { Off, Shadow, Active }
public enum SettledChangeState { Unavailable, Moving, Settling, Settled }
public enum SettledChangeStatus { NoReference, Compared, Gated }
public enum ChangeDirection { Arrival, Departure, Unclear }

public sealed record SettledChangeRegion(
    double TableX, double TableY,          // Ball.Position convention: X 0..2, Y 0..1
    string Zone,                           // "table" | "cushion" | "jaw"
    double AreaBalls, double InscribedRadiusBalls, double OutsideFraction, double Peak,
    bool BallSized, string? RejectReason,  // rail | oversize | thin | under_obstruction
    ChangeDirection Direction,
    double ClothBefore, double ClothAfter, double DarkBefore, double DarkAfter,
    double WhiteBefore, double WhiteAfter);

public sealed record SettledChangeEvidence(
    Guid Epoch, long Sequence,             // Epoch changes on processor reset; Sequence per comparison
    string Timeline, long MediaGeneration, string CalibrationId,
    SettledChangeStatus Status, ImmutableArray<string> GateReasons,
    long ReferenceSegmentStartUs, long ReferenceSegmentEndUs,
    long ReferenceWindowStartUs, long ReferenceWindowEndUs,
    long CurrentWindowStartUs, long CurrentWindowEndUs,
    double CushionBandDrift, double BedDrift, double BedP99,
    int RawComponentCount, ImmutableArray<SettledChangeRegion> Regions,
    byte[] ChangeGrid);                    // 20x40 bits, row-major over table Y then X

public sealed record SettledChangePass(    // one per Live Detect pass
    SettledChangeMode Mode, SettledChangeState State, double? MotionFraction, int StillPasses,
    double ComputeMs, string? UnavailableReason, SettledChangeEvidence? Evidence, bool EvidenceIsNew);

public interface ISettledChangeProcessor : IDisposable
{
    SettledChangePass Observe(TableFrame frame, TableCalibration calibration, string calibrationId,
        VisionResult vision);
    void Reset(string reason);
}
```

Coordinates: region centres use the `Ball.Position` table convention. The reducer converts to the
`SettledBallPosition` convention (x/2, y) as `ShellLiveDetect.CaptureLiveDetectLayout` does. All
distance tests in this document are in table-width units (one ball radius R = 0.027): "near"
means within 1.6R = 0.043. This avoids the anisotropy of the (x/2, y) convention.

### 2.3 Reducer side: how the evidence is used

`LiveDetectTableEventObservation` gains two optional members with defaults:
`SettledChangePass? SettledChange = null` and `SettledChangeMode Mode = Off`. The D1/D2 code paths
are unchanged when they are null.

**Chain since the baseline.** The reducer keeps the list of evidence records (by `Sequence`)
whose reference segment lies at or after its current baseline. A link is accepted only when all
of these hold:

- `Status == Compared`;
- the timeline, media generation and calibration match;
- the `Epoch` is the same as the previous link;
- `Sequence` is the previous link's sequence + 1;
- the first link's reference segment contains the baseline's source time
  (`ReferenceSegmentStartUs ≤ baselineSourceTimeUs ≤ ReferenceSegmentEndUs`), or ends after it
  with no image motion in between.

Any other evidence **breaks** the chain until the next baseline advance: a gated link, a
`NoReference` after motion, an epoch change, or a sequence gap. When the baseline advances, the
links whose current window ends at or before the new baseline time are absorbed and dropped. The
chain is capped at 8 links; overflow breaks it.

Per-ball readout states (new, logged every pass):

| State | Meaning | Readout wording |
|---|---|---|
| `observed` | detected in this frame | as today |
| `held` | in the baseline, not detected now, chain intact, nothing changed at its position since it was last observed | "held · not re-observed for 14 s · no change at its position" |
| `inferred` | never or not currently detected; position from settled change only | "black inferred at top cushion from a settled change · not observed" |
| `not_seen` | D2's state for a black or cue with no observation and no usable inference | "black not seen" |

The rules (U1–U5) each produce a **proposal**. In `Shadow` mode proposals are logged and nothing
else happens. In `Active` mode they are applied as stated.

**U1 Hold (suppresses a false "ball gone").** Applies to a baseline ball that has no current
detection within the existing position tolerance, while the chain is intact. It is held only if:

- no change-grid bit within 1.6R of it is set in any chain link (any size, including rail-rejected
  and oversize regions); and
- no obstruction component overlapped it during any chain link.

*Active effect:* for stability matching and count comparison, the layout is detections plus held
balls. A count decrease made up only of held balls is not a pot candidate, and the check outcome
stays `clear_unchanged`. The readout shows the ball as `held` with its age. When the chain breaks,
held balls become `not_seen` with their last observed position and time. That is today's
behaviour, so the next clear frames decide.

**U2 Infer a missing black or cue.** Applies when the current detections contain no black (or no
cue), and the baseline has no *held* black (or cue) elsewhere. A region is a candidate if it:

- is in the newest chain link and ball-sized, not rejected, not under obstruction;
- has `Direction` of `Arrival` or `Unclear`;
- has no current detection of any kind within 1.6R;
- has an appearance hint for the kind:
  - black: `DarkAfter ≥ 0.30` and `DarkAfter − DarkBefore ≥ 0.20`;
  - cue: `WhiteAfter ≥ 0.30` and `WhiteAfter − WhiteBefore ≥ 0.20`.

  Both are tentative, to be set from the 9 staged black pairs and the clip regions, which already
  log `dark_a` and `dark_b`.

Exactly one candidate for the kind is required; two or more give nothing (ambiguous). It must
also persist: the same position must still differ from the reference in a recheck at least 3 s
later in the same settled segment. That recheck is a second comparison at about 25 ms, run only
when a U2 candidate exists, and it guards against a player's shadow on a cushion (T4 b699 s0).

*Active effect:* the ball is `inferred` at that position, with the evidence sequence and source
time.

- It joins the layout for *stability matching only*, so the table can settle with it.
- It never appears in `LiveDetectCounts` as observed. The inventory line reads, for example,
  "15 observed + black inferred".
- If the detector later sees that kind within 1.6R, the state becomes `observed`, and this is
  logged as "previously inferred" with no `balls_returned` event.

**U3 Departure evidence (log and safety only).** A ball-sized region at a baseline ball's position
whose ball is not detected now means the ball genuinely left. The rule never holds that ball (U1
already refuses), and it attaches the region to any resulting pot or change event as supporting
evidence. It does not itself create or confirm a pot.

**U4 D1 pocket regions.** D1 marks only the balls inside a flagged pocket region as uncertain.

- If no change-grid bit inside the pocket region is set in any chain link, the region is
  `unchanged since baseline`: its baseline balls are `held` (U1 rules), and an activity that ends
  with only that region uncertain can still close as `no_pot_observed` if everything else
  supports it.
- If any bit is set there, the region is `changed while uncertain`: the activity cannot close as
  `no_pot_observed` and becomes `settled_layout_change_unclassified` until the flag clears.

This makes D1's risk ("a pot into that pocket could be missed until the flag clears") visible
instead of silent. It never upgrades a pocket ball to observed.

**U5 D2 first baseline with black or cue not seen.**

- After a D2 baseline, a black or cue that is `not_seen` can become `inferred` by U2.
- If the detector later sees it:
  - *within 1.6R of the inferred position:* `observed (previously inferred)`, no event;
  - *elsewhere, with no arrival region there in the chain:* D2's correction path ("first seen,
    was present"), not a ball returned to the table;
  - *elsewhere, with an arrival region there:* ordinary movement.
- A departure region at an inferred ball's position returns it to `not_seen`.

**What no rule may do:**

- create a `possible_pot`, `balls_returned_candidate` or rack event;
- change `LiveDetectCounts` (observed counts);
- present `held` or `inferred` balls as observed in the inventory text, the Identify Balls overlay
  or the log's `Balls` array;
- reach `GameState`.

This keeps the Required outcome: "Keep clear what is observed, uncertain, stale, or inferred; a
last good inventory must not masquerade as a fresh observation." The overlay gets a distinct style
for held and inferred balls (hollow marker with age). The overlay change itself is out of scope
here.

## 3. Gates

Every gate is recorded by name in `GateReasons`. A gated comparison breaks the reducer chain. The
regions are still logged, because gated pairs are wanted for threshold setting.

| Gate | Rule (tentative values) | Source |
|---|---|---|
| Lighting drift | `CushionBandDrift > 0.08` → `lighting_drift`. Also logged: `BedDrift`, `BedP99` | T4: bad pairs 0.105–0.335 (0.112–0.342 at half scale) vs at most 0.065/0.067 stills and 0.047 clips. Three bad pairs only; treat 0.08 as provisional |
| Too much change | more than 6 components of at least 0.3 ball area outside the moved-ball explanation, or more than 2 % of the bed changed → `too_much_change` | not in T4; guards camera bumps, exposure steps and rack breaks (a break gives about 10 landings plus an oversize rack; the reducer has its own rack path) |
| Obstruction | per region: overlaps a triggering component of `ObstructionAudit` (mapped from analysis grid to raster) → `under_obstruction`. Whole comparison: if either window had a pass with `Obstructed` and no component audit → `obstruction_unlocalised` | T3: 941 s of obstruction, much of it false, so the flag alone does not void the comparison |
| Hands | per region: rail rule, outside fraction > 0.05 → `rail`; inscribed radius > 1.45R → `oversize` | T4 v3: removed all 5 hand regions, no real cushion or jaw arrival lost |
| Time gap | reference window end → current window start > 60 s → `gap_too_long` | T4: clean at 38 s; failed at 22 min (daylight). 60 s is a guess to be tested on shadow logs |
| Stillness quality | a window with a pass whose source gap > 1.5 s, or with fewer than 3 frames → `window_incomplete` | live cadence |
| Camera timeline / decoder | timeline, media generation or calibration change → processor `Reset`, new `Epoch`, `NoReference` | `frameCursor`, `decoder_recovered` |
| Exposure | no metadata exists (T7). Global steps are removed by the tile gain; residual steps show up as drift | T7 would add metadata |

## 4. Settings, default and shadow-mode logging

### 4.1 Setting

`SettledChangeMode` is stored in the existing `skyeye` SharedPreferences as
`live-detect-settled-change` = `off` | `shadow` | `active`.

- **Default: `shadow`.** Computed and logged; the reducer applies nothing. This satisfies "off by
  default": no readout, event or baseline differs from today.
- `active` is set by Richard after the T9 score (criteria in 5.4).
- `off` is a kill switch (no image work), for a performance problem.

The thresholds live in one `SettledChangeSettings` record with the defaults above. The run's
`started` run event logs the mode and the full settings, so every log names the rules it was
written under. The Live Detect status line shows `Settled-change evidence: shadow (not used)` or
`active`.

### 4.2 Log fields (schema 14 → 15, all optional, `WhenWritingNull`)

On `LiveDetectLogFrame`, a new `SettledChange` object:

- **Every pass:**
  - `mode`, `state`;
  - `motionFraction`, `stillPasses`, `computeMs`, `unavailableReason`;
  - `epoch`, `evidenceSequence` (the evidence in force).
- **When `evidenceIsNew`** (once per settled segment), the full `evidence`:
  - status and gate reasons;
  - the six window and segment times, and gap seconds;
  - `cushionBandDrift`, `bedDrift`, `bedP99`, `rawComponentCount`;
  - `changeGrid` (base64);
  - `regions[]` with every field of `SettledChangeRegion`, plus `nearestDetectionNow` (kind and
    distance) and `nearestBaselineBall` (kind and distance).
- **Reducer output, every pass:**
  - `chainIntact`, `chainLength`;
  - `ballStates[]` (kind, x, y, state, sinceSourceTimeUs, evidenceSequence) for non-observed
    balls only;
  - `proposals[]` (`hold`, `infer_black`, `infer_cue`, `pocket_unchanged`, `pocket_changed`,
    `departure_support`, `unexplained_change`), each with `applied` (false in shadow mode) and the
    evidence sequence.

**Evidence rasters.** When an evidence record is new and has a ball-sized region, a proposal or a
`lighting_drift` gate, the processor encodes the reference and current 440x840 rasters as JPEG
(quality 92, about 80–120 kB each). It also samples 1 in 10 of the empty comparisons as controls.

- Writes use the existing evidence-sidecar mechanism with a separate cap: 60 pairs per run, inside
  the store's byte limit.
- Paths and SHA-256 values go in `evidence.rasters`.
- Encoding happens in the observer while the processor owns the Mats; the file write happens later
  in the pass, as `SaveFrameEvidence` does today.

This is how D3 gets scored without staging:

- Ordinary play sessions in shadow mode record every comparison, the reducer's would-be decisions,
  and the two rasters behind each one.
- T9-style review sheets can be built from those rasters alone.
- The rasters also let a later Python run re-score with changed thresholds.

## 5. Port plan and tests

### 5.1 From Python to C#

| Prototype step (`settled_change.py`) | C# | OpenCV call (Android binding, `org.opencv` 4.14) | Already used in repo? |
|---|---|---|---|
| decode | `Utils.BitmapToMat` into scratch | `Utils.BitmapToMat` | yes (3 sites) |
| `homography` | per calibration | `Imgproc.GetPerspectiveTransform` | yes |
| warp to raster | per pass | `Imgproc.WarpPerspective` (linear) | yes |
| BGR/grey/HSV | per pass / per region | `Imgproc.CvtColor` (`ColorRgba2bgr`, `ColorBgr2gray`, `ColorBgr2hsv`) | yes |
| quarter-size grey | per pass | `Imgproc.Resize` (area) | yes |
| `log(grey+8)`, tile median, residual | managed on `byte[]` via `Mat.Get`/`Put` (`SettledChangeMath`) | none (`Core.LUT`/`Absdiff` optional) | `Get`/`Put` pattern yes; `LUT`/`Absdiff` are in `org.opencv.core`, which `Metadata.xml` keeps, but are unused so far; confirm at build |
| field resize | per comparison | `Imgproc.Resize` (linear) | yes |
| blur | per comparison | `Imgproc.GaussianBlur` | yes |
| threshold | managed or `Core.Compare` | `Cv.Compare` | yes |
| open / close | per comparison | `Imgproc.MorphologyEx`, `Imgproc.GetStructuringElement(MorphEllipse)` | yes |
| components | per comparison | `Imgproc.ConnectedComponentsWithStats` | yes (8 sites) |
| inscribed radius | per candidate | `Imgproc.DistanceTransform(DistL2, 5)` | yes |
| median of 3 rasters | per comparison | managed, or `Core.Min`/`Core.Max` | managed preferred |
| circularity / `findContours` | **dropped** (v2/v3 do not use it) | not needed | n/a |

`Metadata.xml` keeps `org.opencv.core`, `imgproc`, `imgcodecs`, `android` (Utils, OpenCVLoader)
and only `calcOpticalFlowPyrLK` from `video`. Every required call is in a kept package. No new
native dependency is needed.

**Python reference.** Write `tools/evidence/prototypes/settled_change_live.py`, a line-for-line
mirror of the C# algorithm:

- half scale;
- byte-quantised residual and histogram tile median;
- the settle state machine at Live Detect cadence;
- the same gates;
- it emits the same JSON as the schema-15 `evidence` object.

It is the oracle for parity and the tool for offline scoring. It must not import or edit
`live_detect_reducer_replay.py`, which belongs to another agent. A reducer-policy replay wraps
that module from a separate script, once its owner agrees.

### 5.2 Unit checks with synthetic images

**Pure managed checks** (`tests/SkyEye.Checks`, desktop, no OpenCV), on `SettledChangeMath`:

1. Tile median: a uniform gain x1.3 leaves a residual of exactly 0 after quantisation; a linear
   gain ramp of 0.85→1.2 leaves |residual| ≤ 0.02 away from tile seams; one bright disc leaves
   the median unchanged.
2. Quantisation: the maximum error is at most 1/128 against float over the full byte range.
3. Raster↔table round trip through the four calibration corners: within 0.5 px.
4. Zones: each pocket point gives `jaw`; (1.48, 0.067) gives `cushion`; the table centre gives
   `table`.
5. Region rule from stats:
   - disc r = R → ball-sized;
   - r = 0.5R → `thin`;
   - r = 2R → `oversize`;
   - disc 30 % outside the raster → `rail`.
6. Drift: the band and bed fractions on hand-built masks.

**Reducer-policy checks** (`tests/SkyEye.AgentUi.Checks`, beside
`LiveDetectTableEventTrackerChecks`), using synthetic `SettledChangeEvidence` records:

1. **Hold:** a baseline ball missing with no grid bit nearby → `held`; in Active, no pot
   candidate; in Shadow, the proposal is logged and events are identical to Off.
2. **No hold when anything changed:** any grid bit within 1.6R (including from a rail-rejected
   region) → not held, and the decrease proceeds as today.
3. **Chain:** a gated link, epoch change or sequence gap → held balls become `not_seen`.
4. **Infer black:**
   - one dark arrival with no detection nearby and no held black → `inferred`;
   - two candidates → none;
   - a held black elsewhere → none;
   - the recheck missing → none.
5. **Inferred then observed:** the detector sees the black within 1.6R → `observed (previously
   inferred)`, no `balls_returned`.
6. **D1:** a pocket region unchanged → `no_pot_observed` allowed; changed → `unclassified`.
7. **D2:** `not_seen` → `inferred` → `observed`; seen elsewhere with no arrival → D2 correction.
8. **Timeline reset** clears all D3 state.
9. **Golden shadow test.** Replay every saved schema-14 log fixture through the reducer:
   - with `SettledChange = null`;
   - with synthetic shadow evidence.

   Check outcomes and table events must be byte-identical. This proves shadow mode is inert.

**Image checks** (OpenCV), run in `tests/SkyEye.Vision.Device` as an instrumentation, or on desktop
once T2's OpenCV look-alike exists.

- Generate fixtures with a new `tools/evidence/make_settled_change_fixtures.py` into
  `tests/fixtures/settled-change/synthetic/`.
- Each case is a pair or sequence of 1920x1080 PNGs, plus the corners and an `expected.json`
  produced by the Python reference.
- Base scene: a projected table using the 31 October corners, a green bed with mild noise, a
  darker cushion band, a brown rail outside, and JPEG round-trip noise.

| Case | Change | Expect |
|---|---|---|
| S1 | none (two noise draws) | 0 regions, drift < 0.01 |
| S2 | global gain x1.25 | 0 regions |
| S3 | smooth gain ramp 0.85→1.2 across the table | 0 regions |
| S4 | black disc (V≈25, r = R) appears at table (1.48, 0.067), the real miss spot | 1 ball-sized region, `cushion`, centre within 0.3R, `DarkAfter` high |
| S5 | same at the lower-right jaw | 1 region, `jaw` |
| S6 | white disc removed on the open bed | 1 region, cloth after > cloth before (`Departure`) |
| S7 | skin-tone blob 3Rx6R straddling the rail line | region rejected `rail`; grid bits set |
| S8 | thin dark streak 0.3R wide (shadow or cue shaft) | no ball-sized region |
| S9 | two touching discs arrive | one region (oversize or credited to one); documented, not a pass/fail on identity |
| S10 | cushion band brightened 40 %, bed unchanged | `lighting_drift` gate |
| S11 | 12-frame sequence: disc rolls across, stops at the cushion, 3 still frames | states Moving → Settling → Settled at the right pass; exactly one comparison |
| S12 | disc arrives while a triggering obstruction component covers it | `under_obstruction` |

C# must match `expected.json`: the same region count and reject reasons, centres within 1 raster
px, and drift within 0.003.

### 5.3 Offline scoring on real material

All paths are relative to the repo root.

**A. 1 November staged stills: parity and regression.**

- 33 pairs, named in `work/evidence-audit/t4/stills-grey.csv`.
- Corners come from `work/evidence-audit/receipts.jsonl`.
- Reviewed centres:
  - `docs/evidence/mid-cushion-user-reviewed-centres-20261101.json`;
  - `work/o08/live-detect-black-pair-v1/physical-controls/lower-right-user-reviewed-centres-20261101.json`;
  - `…/upper-right-user-reviewed-centres-20261101.json`;
  - `…/opposite-cue-user-reviewed-centres-20261101.json`;
  - `…/cue-present-opposite-cue-restaged-20261101/review-v1/user-reviewed-centres-20261101.json`.
- Source frames, under `work/o08/live-detect-black-pair-v1/physical-controls/`:
  - `mid-cushion-absent/8622ddce3f3d467eadeec0a67a668822/{542,564,582,644}.png`
  - `mid-cushion-cue-absent/2a366ec7339e4f219dbfc8e2a361fdcf/{295,316,334,394}.png`
  - `mid-cushion-restored/f44428135f72479eaae0572c64aea970/{288,311,331,392}.png`
  - `mid-cushion-present/9d2bc09785cf4f499f48faf30da9fe10/9d2bc09785cf4f499f48faf30da9fe10/2831.png`
  - `mid-cushion-opposite-cue/87b15e584e33483eb2c29e8f299b481f/87b15e584e33483eb2c29e8f299b481f/{959,982,999,1057}.png`
  - `cue-absent-opposite-cue/3e6bdb2531a9486e95cb2322e5ee0eae/3e6bdb2531a9486e95cb2322e5ee0eae/{562,583,598,660}.png`
  - `cue-present-opposite-cue-restaged-20261101/884a3abb604048379cc148185728df73/{1691,1711,1726,1783}.png`
  - `lower-right-present/d40c437eca594004a1c756d55c048bb3/1049.png`
  - `lower-right-absent/f34c75699b0644938e977ad53e1774a5/461.png`
  - `lower-right-cue-absent/54bace4697cf403d8813d4099ab1a5e4/477.png`
  - `lower-right-restored/96722d9109f84c059bf2eab9812e4d7e/375.png`
  - `upper-right-present/697.png`, `upper-right-absent/229.png`, `upper-right-cue-absent/206.png`,
    `upper-right-restored/190.png`
- Pass criteria:
  - the 9 black-change pairs hit 9/9:
    - `mid_absent-542_cueabs-295`, `mid_absent-542_restored-288`,
      `mid_absent-542_present2831-2831`;
    - `lowright_present-1049_absent-461`, `lowright_absent-461_cueabs-477`,
      `lowright_absent-461_restored-375`;
    - `upright_present-697_absent-229`, `upright_absent-229_cueabs-206`,
      `upright_absent-229_restored-190`;
  - the 12 `control-same-state` pairs give 0 ball-sized regions;
  - the 3 cross-session `opp_*` change pairs are gated `lighting_drift`:
    `opp_present-959_cueabs-562`, `opp_present-959_restaged-1691`,
    `opp_cueabs-562_restaged-1691`;
  - cue hits are reported, not gated: 7/11 at half scale today.
- C# against the Python reference: identical region sets within 1.5 px.
- Half-scale baseline: `work/evidence-audit/d3/stills-scale.csv`.

**B. 29 and 31 October clip pairs: parity now, score after T9.**

- 46 pairs listed in `work/evidence-audit/t4/pairs.csv`: 28 `shot`, 16 `control-same-segment`,
  2 `control-code62`.
- Frames: `work/evidence-audit/t4/frames/<pair>_a.png` and `_b.png` (92 files, 880x1680 medians;
  downscale to 440x840 with area interpolation).
- The by-eye baseline is `work/evidence-audit/t4/regions-grey.csv`. The half-scale baseline is
  `work/evidence-audit/d3/clips-scale.csv` (101/102 kept, 0 new, controls 0).
- Shot pairs:

  ```
  code44_f748a31bbd26_s0, code44_8c42f73b5067_s0, code44_5abc075c390a_s0, code45_cue-pot-even_s0,
  code45_cue-pot-even_s1, code45_cue-pot-even_s2, code47_7099ec7ca65a_s0, code47_7099ec7ca65a_s1,
  code47_f2778959b153_s0, preroll_break-red_s0, code52_test-nopot_s0, code52_1785df71a2a7_s0,
  code52_1785df71a2a7_s1, code52_1a0d54af3ea3_s0, code52_593e60bb1098_s0, code52_593e60bb1098_s1,
  code52_599b462bf76c_s0, code52_6c74866c18bd_s0, code52_b699cd650b91_s0, code52_b699cd650b91_s1,
  code52_d8dc052ace5d_s0, code57_926cfd749837_s0, code57_265b12a13313_s0, code57_265b12a13313_s1,
  code57_7730ca232eef_s0, code57_931d99d87c7f_s0, code57_931d99d87c7f_s1, code57_f49ddb559c12_s0
  ```

- Controls:

  ```
  code44_f748a31bbd26_c0, code44_8c42f73b5067_c0, code44_5abc075c390a_c0, code44_5abc075c390a_c1,
  code45_cue-pot-even_c0..c2, code47_7099ec7ca65a_c0, code47_7099ec7ca65a_c1, code47_f2778959b153_c0,
  preroll_intervening-_c0, code52_test-nopot_c0, code57_cue-obstruct_c0, code57_eb73dd4f76e7_c0,
  code62_false-pot-cl_c0, code62_false-pot-cl_m, code62_false-shot-c_c0, code62_false-shot-c_m
  ```

- With T9 sheet 2 labels (rest position of each moved ball; settled intervals), score:
  - moved-ball recall;
  - cushion and jaw arrival recall;
  - false ball-sized regions per shot after gates;
  - the U1 safety count (held balls that had in fact moved or been potted).

**C. Live-cadence simulation.** The pairs above were hand-picked by T4's 5 fps segmentation. The
live state machine sees one pass every 0.3–0.5 s, with stale gaps.

- Decode the same 37 clips at the pass intervals recorded in the matching code44–code57 logs, or a
  jittered 0.35 s where no log matches.
- Run `settled_change_live.py` end to end.
- Compare its settled segments and comparisons with the T9 settled intervals.
- This is the only test of the settle detector itself.

**D. Shadow sessions.** These are ordinary play, with no staging.

- Score `proposals` against review sheets built from the saved rasters.
- Measure `computeMs` p50 and p95 on the Pixel.
- Measure how often each gate fires.

### 5.4 Proposed criteria for switching to `Active` (for Richard)

1. A, B and C pass. On T9-labelled clips: no U1 hold of a ball that moved or was potted (hard
   zero), and false ball-sized regions after gates of at most 1 per 20 shots.
2. U2: no inferred black or cue where none arrived, across all labelled clips plus at least 3
   shadow sessions. The staged stills remain the only positive black cases until real play
   provides one.
3. The golden shadow test is byte-identical, and Pixel `computeMs` p95 ≤ 40 ms.
4. Then turn on U1 and U4 first (hold and pocket unchanged/changed: safety-leaning), and U2 and U5
   (inference) second, as separate commits, each scored.

## 6. Risks and open questions

**Risks**

1. **Permanence hiding a real pot** (the T6 risk). U1 refuses a hold if any grid bit near the ball
   is set, and the grid is taken before size filtering. A pot in which the departure leaves no
   above-threshold pixels at all would still be hidden. That is unlikely for a ball-sized object,
   but it is the critical number for criterion 1.
2. **A player's shadow on a cushion read as an inferred black.** T4 has one such false region
   (b699 s0). It is mitigated by needing a dark hint, a single candidate, and the 3 s recheck.
   Not proven.
3. **The drift threshold rests on 3 bad pairs** from separate stagings. Lamp switching, cloud and
   exposure steps between shots are untested. With no exposure metadata (T7), drift is the only
   guard.
4. **Settle detection at Live Detect cadence is untested.** T4 used 5 fps with hand-picked
   windows. Stale-frame waits and decoder restarts will break chains often; the cost is lost
   evidence, not wrong evidence.
5. **Cost is estimated, not measured.** The extra `BitmapToMat` duplicates the analyser's work.
   About 13 MB of native memory is held for the run.
6. **Merged neighbours** (black touching cue, two arrivals together) give one region: one ball
   inferred, or none.
7. **No real-play black-at-cushion event exists.** The black result rests on 9 staged stills.
8. **Coupling to D1/D2.** The contract is additive (nullable observation field, new log object),
   but the U4 and U5 wording depends on how D1 represents pocket regions and how D2 represents
   "not seen". Re-check after that lands.

**Open questions for Richard**

1. **Shadow by default.** It costs an estimated 4–7 % per pass (up to about 17 % on the
   comparison pass) and stores up to 60 small raster pairs (about 6–12 MB) per run on the Pixel.
   Acceptable, or opt-in only?
2. **Inferred balls in the stable layout.** Should an inferred black let the table settle and stop
   a later "returned" event (proposed)? Or should it be readout-only?
3. **Time limit on holds.** Should a held ball have a limit while the chain stays intact (proposed:
   none; the chain breaks on any gate)? Or a fixed cap such as 10 minutes?
4. **Obstruction.** Proposed: the obstruction flag alone does not void a comparison (T3 shows it
   is often false); only the triggering components' areas are excluded. Agree?
5. **Turn-on criteria** in 5.4: agree, and who reviews the shadow sheets?
6. **Gap limit** of 60 s and drift limit of 0.08: accept as provisional, to be revised from shadow
   logs?
7. **Setting control.** Is a SharedPreferences key enough, or should the mode be shown and
   switchable in the Live Detect panel?
