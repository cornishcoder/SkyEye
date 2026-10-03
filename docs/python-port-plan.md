# Plan: port Poolgame analysis into SkyEye

Prepared 3 October 2026 from the local Python checkout at `C:\code\Poolgame-AI-shot-prediction`, revision `a7a803177c63ccb51f715c6920ba650fc341f76a`, and SkyEye at `C:\code\SkyEye`, deployed baseline `b076287`.

This is a plan only. No port, package installation or phone deployment was performed for this request. Three local SkyEye files contain unfinished edits from the earlier detection debugging; these remain undeployed and must be accounted for when implementation starts.

## Intended result

Replace SkyEye's current connected-component detector and direct-pot-only suggestions with a C# port of the Python project's implemented image-analysis and shot-generation pipeline. Run analysis locally on the Pixel 11 companion against snapshots from the mounted Pixel 10 camera. Return correctly positioned red/yellow/cue/black detections and ranked direct, bank and kick candidates to the existing UI and Maeve tools.

The application's algorithms and orchestration will be C#. OpenCV remains a native image-processing dependency, as it is in Python. Python is used during development to produce comparison fixtures; the Android apps will not require Python or a remote analysis server.

## What the source actually contains

The executable pipeline uses OpenCV HSV masking, morphology and Hough circles for detection, plus scikit-learn K-means for colour classification. The README describes YOLOv5 and post-collision physics, but the checked-out code contains neither YOLO inference/model weights nor the described velocity/friction simulation. Port the implemented pipeline; those other capabilities require separate source/assets and work.

The source uses a portrait 1000×2000 analysis image, a nominal 23-pixel ball radius, fixed cloth sample positions and fixed pocket/cushion geometry. It generates 90 candidates per target before filtering: six pockets, direct/object-bank paths, and cue-ball rebound combinations. Both balls can each rebound once. Its ranking is `((180 - angle) * 300 + length) * reboundPenalty`, with penalties 1, 4 or 8, and returns up to three shots. This differs from the README's percentage weighting description.

The sample image used by `main.py`, and the image directory/CSV required by `test.py`, are absent from this checkout. The `img` directory contains documentation figures rather than a labelled evaluation dataset. No repository LICENSE file was found. Record provenance and establish reuse terms for incorporated source; this is separate from choosing an OpenCV runtime.

| Python source | Planned C# responsibility |
|---|---|
| `transform.py` | Perspective rectification, orientation and coordinate conversions; optional automatic pocket-based calibration |
| `normalize.py` | Cue-reference colour normalization with bounded gains and explicit failure/fallback |
| `detect.py` | Cloth/pocket masks, morphology, circle candidates and geometric quality checks |
| `categorize.py` | Ball-region sampling, repeatable colour clustering, cue/black identification and red/yellow adaptation |
| `Ball.py`, `Table.py` | Immutable detections, analysis results and frame pipeline orchestration |
| `shot_init.py`, `Shot.py` | Ghost-ball positions, cut angles and multi-segment paths |
| `shots_calculations.py` | Candidate generation, obstruction and rebound validation, difficulty ranking |
| `visualize.py` | Android rendering of ball outlines, ghost contacts and every path segment |
| `main.py`, `test.py` | Development comparison harness and regression fixtures; no desktop GUI in the phone app |

## Architecture and dependency choice

- Put image-independent models, table geometry, clustering and shot mathematics under `SkyEye.Core/Analysis`. Keep that code testable with ordinary .NET tests.
- Add `SkyEye.Vision.Android` for the C# image pipeline and frame conversion. Expose an analyzer interface so the companion does not depend on OpenCV types throughout its UI.
- First prove a narrow .NET Android binding to the official OpenCV Android AAR. Keep algorithm decisions and orchestration in C#; Java/JNI should provide library access and marshaling only. Microsoft supports Maven library references in Android binding projects, and OpenCV publishes an official Android AAR. [Microsoft binding documentation](https://learn.microsoft.com/en-us/dotnet/android/binding-libs/msbuild-reference/build-items#androidmavenlibrary), [OpenCV Android distribution](https://opencv.org/opencv4android-usage-models/).
- Do not select a Windows OpenCvSharp package and assume it runs on Android. Its own documentation excludes Xamarin platforms; a usable Android runtime must be demonstrated. Emgu CV is an alternative if the official binding proves impractical, but its Android distribution requires a commercial licence. [OpenCvSharp requirements](https://github.com/shimat/opencvsharp#requirements), [Emgu Android installation](https://www.emgu.com/wiki/index.php/Download_And_Installation#Android).
- Pin the native release only after a device packaging experiment. Record any difference from the Python reference's OpenCV 4.11.0. Check ARM64 loading, required image operations, release/trimming behaviour, JNI disposal and 16 KB native-library/ZIP alignment. The currently tested Pixels use 4 KB pages, so alignment checks alone cannot establish execution on a 16 KB device.

## Implementation sequence

### 1. Establish the reference and preserve current work

Inventory the source and document the intended behaviour of each module. Preserve the original Python checkout unchanged. Separate the previous local overlay/calibration edits from the detector threshold experiment; the new port supersedes that detector experiment, while independently verified coordinate fixes can be retained.

Create a small Python comparison harness in SkyEye's development tooling. Use the source's pinned dependencies and capture intermediate results: rectified images, normalization gains, masks, detected circles, classifications, candidate paths, rejected paths and final rankings. Preserve failures as failures rather than silently repairing the Python reference.

Use the captured Pixel table image and calibration already obtained during debugging as the first labelled fixture. Expand the dataset across layouts and lighting, including shadows, cream cue balls, orange-toned yellows, pocket-adjacent balls, touching balls, occlusion and empty cloth. Label centres and colours, not just total ball counts.

**Exit:** reproducible reference inputs/outputs and an explicit list of source defects/adaptations. Missing YOLO assets do not block porting the implemented classical detector.

### 2. Prove OpenCV access from C# on Android

Build the smallest binding/vision experiment before porting the entire pipeline. From C#, decode one actual snapshot, convert channels, warp perspective, create a mask, run morphology and Hough circles, and release every native matrix. Install and run this experiment on the Pixel 11 when implementation is authorized.

Measure processing time, memory and APK growth. Confirm the experiment works in the project's actual .NET 10 Android configuration. If the official binding cannot be made practical, document the failure and the concrete alternative before migrating the rest of the code.

**Exit:** working device image primitives and a pinned, reproducible native dependency.

### 3. Port calibration, normalization and detection

Keep SkyEye's explicit four-corner calibration as the default source of the table boundary. Port the Python automatic pocket calibration as a separate optional method, with a confirmation step and manual fallback. Do not silently overwrite an existing calibrated mount.

Retain the Python portrait analysis convention initially to simplify comparison. Make orientation explicit. For a clockwise conversion from SkyEye's horizontal table coordinates `(x in 0..2, y in 0..1)`, use `analysisX = 1000 * (1 - y)` and `analysisY = 1000 * x`; provide the inverse and test pocket-ID mapping. Define the correspondence of calibration corner order explicitly—the two source projects currently order their points differently.

Warp the exact snapshot being analyzed. Keep both forward and inverse transforms, the raw image bounds, capture timestamp and calibration version. Render detections back onto that same raw image through the inverse transform and its letterbox bounds. Keep a separate schematic if desired; never stretch table coordinates across the preview.

Port the cloth mask, morphology, circle detector and normalization. Start with the Python parameters on canonical-size fixtures, then make dimensions, radius limits, circle separation, kernels and cloth sampling configuration-driven. Replace fixed edge sample pixels with validated cloth samples for production use. Fit pocket exclusions conservatively so real balls next to a pocket are not simply erased.

Bound mask search iterations and report non-convergence. The Python pocket-mask loop can repeat at the same crop size indefinitely, and receives HSV data that its grayscale step treats as BGR. Correct these behaviours explicitly in the production port and record the differences in comparison results.

Normalization must use a credible cue-ball region, not any bright cushion or highlight. Missing/ambiguous cue references should produce a quality warning and a documented unnormalized fallback, rather than inventing a reference.

**Exit:** circle centres/radii and masks can be inspected independently of classification and overlays; every result belongs to one identifiable frame.

### 4. Port and adapt ball classification

Keep the source's dominant-colour/white-area clustering approach, using a bounded, repeatable C# K-means implementation. Record initialization and stopping criteria. Compare classified results and cluster centres within tolerances; identical seeds across NumPy/scikit-learn and .NET do not guarantee identical cluster ordering or initialization.

Restrict sampling to the detected ball region. Handle empty regions, near-white/near-black pixels and fewer than four usable colours. The Python fallback currently returns an incompatible shape, and its saturation filtering can discard useful white pixels; these require explicit repairs.

Represent colour separately from solid/stripe pattern. In SkyEye's international eight-ball flow, map colours to `red`, `yellow`, `cue`, `black` or `unknown`; do not relabel every solid as red or every stripe as yellow. Validate orange/yellow hue and cream cue-ball behaviour against the actual Pixel image. Carry classification uncertainty and shape quality without presenting them as calibrated statistical probabilities.

Keep manual correction and confirmation. Reject impossible group counts; suppress advice for missing/ambiguous cue balls or uncertain layouts. Associate corrections with the current calibration, timeline and layout rather than restoring them as fresh observations later.

**Exit:** the clear labelled 16-ball reference frame produces seven reds, seven yellows, one cue and one black, with no pocket counted as a ball. Broader accuracy must be established separately on held-out images.

### 5. Port shot generation, validation and ranking

Port ghost-ball geometry, cut angles, reflected-path construction, obstruction checks and the source ranking formula into image-independent C#. Represent cue and target paths as explicit ordered segments, with pocket ID, shot type, ghost contact, total distance, cut angle and difficulty.

Use a table-geometry configuration for radius, pocket mouths and cushion rebound boundaries. Preserve source constants in comparison fixtures; adapt production geometry deliberately instead of mixing Python pixel dimensions with SkyEye normalized units. Preserve documented rounding for comparison, while retaining floating-point geometry internally in production.

Repair zero-length directions, parallel reflection cases, reflection points outside a cushion segment, pocket-mouth rebounds, duplicate candidates and non-finite results. A failed reflection must not silently turn into a direct shot. The source currently permits that fallback when a reflection is `None`.

Check every trajectory segment and handle the cue/target identities explicitly, rather than skipping arbitrary obstacles solely because their coordinates equal an endpoint. Cover near-tangent contacts and balls touching cushions. Make ranking non-mutating and ties deterministic; the Python implementation deletes entries from its supplied shot list.

Apply SkyEye's group eligibility before generation. The Python `all` option includes black targets; it is not a rules implementation. Keep the existing freshness, settled-state, obstruction and confirmation gates. Return the three best valid candidates, including direct pots, object banks, cue kicks and combinations where supported by the source.

The implemented source estimates geometric shot difficulty, not post-shot cue-ball position, spin, pace or pot probability. Those remain separate future work.

**Exit:** valid non-degenerate geometry matches reference fixtures within agreed numerical tolerances; intentional corrections have their own tests and documented expected differences.

### 6. Integrate with SkyEye and Maeve

Replace `MainActivity.UpdateTableAsync`'s current detector with the new analyzer. Continue using the existing camera snapshot endpoint. Run analysis on the companion away from the UI thread, with one analysis in flight and no accumulating frame queue. Cancel/discard results when the camera, calibration, capture timeline or settings change. Freeze the calibration image while corners are being selected.

Show colour counts, uncertainty and detections over the analyzed frame. Extend the shot model and renderer to show all cue/target path segments, ghost contacts and rebound points. Let the user choose among the three candidates without confusing live-video delay with analysis-frame time.

Update `get_table_state`, `suggest_shots` and `explain_shot` so Maeve receives the same versioned candidates shown in the app, including path type, age and limitations. Keep stale shot IDs invalid. This is a local tool integration; testing the port does not require a paid voice session.

**Exit:** UI and tools agree on the current layout and candidate IDs, and uncertain/stale states cannot produce actionable suggestions.

### 7. Validate and deploy

- Compare C# output against Python fixtures at each stage, rather than relying only on final ball counts or a screenshot.
- Test coordinate round trips for all corners, ball centres and path vertices, including perspective, orientation, letterboxing and different screen sizes. Target overlay-centre error below one quarter of the visible ball radius on labelled clear fixtures.
- Evaluate held-out real frames by per-colour precision/recall and matched-centre error. Use an initial target of at least 95% precision and recall on clear, unobstructed held-out frames; report achieved results and failures rather than treating this target as guaranteed. Include empty-pocket negatives, cream cue balls and orange-toned yellows as explicit regression cases.
- Test fail-closed behaviour for occlusion, ambiguous cue/black identity, stale timestamps, changed calibration, paused capture and detector failures. Near-pocket/touching-ball uncertainty must remain visible.
- Test direct pots, banks, kicks, blocked second legs, invalid rebounds, tangencies and zero-length inputs. Confirm every recommended path passes the production validator and respects the selected group.
- Benchmark on the Pixel 11 while live viewing is active. Start with a target of p95 analysis below one second at the current roughly 2.5-second polling interval, then tune resolution and scaled parameters from measurements. Bound native allocations and verify they do not grow across repeated analysis. If the budget is missed, surface analysis age and revise cadence without presenting old data as current.
- Verify the Pixel 10 continues recording and saving clips while the companion analyzes frames. Smoke-test live viewing, portrait download/export and offline playback after integration.

Build Android projects sequentially. Ship a reviewed source change, comparison report, pinned dependency manifest and updated signed APKs only after the device checks. Keep the prior installed version available for rollback.

## Proposed milestones

1. **Foundation:** reference fixtures, source audit and C# Android OpenCV experiment.
2. **Detection:** rectification, masks, normalization and correct red/yellow/cue/black results on real frames.
3. **Prediction:** direct/bank/kick geometry, validation and top-three ranking.
4. **Integration:** frame-correct overlays, freshness/confirmation flow and Maeve tool output.
5. **Acceptance:** held-out accuracy, Pixel performance, capture regression checks and deployment.

The highest uncertainty is mobile OpenCV packaging and detection quality on real images. Prove those early. A reliable elapsed-time estimate should follow the dependency experiment and initial fixture run; translating the mathematics alone would underestimate the work.
