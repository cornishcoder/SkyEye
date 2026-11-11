# T2 Desktop detector runner: design

Status: draft, 11 November 2026. Sections 1 to 3 and 5 are complete enough to
implement from. Section 2's per-call table is written out in prose (the CSV is
TODO). Section 4 needs the T1 manifest schema, which is still being built.
Section 6 has effort estimates but no checked build. Nothing here has been
compiled, because the investigation shell has no .NET SDK.

Source state: the detector files under `src/SkyEye.Vision.Android` and
`src/SkyEye.Core` are the same at HEAD (6aeb125) as at checkout `da161891`,
the source of the Layout03 reference replay. `git diff --stat da161891 HEAD`
on both folders is empty, and the working tree differs only in CRLF.

## 1. What exists already, and why none of it runs the detector off-device

- **SkyEye.Observation.Workbench** (net10.0, 355 lines). It has the
  `validate`, `freeze`, `replay`, `baseline`, `compare`, `export-o08`,
  `merge-o08` and `association` commands. It validates corpus manifests,
  freezes split locks, and scores stored detections against labels
  (`baseline` describes itself as "Scoring stored results, not re-running
  native CV"). It references Core and Observation only, with no OpenCV. Reuse
  its corpus and lock types and the `StoredDetection` scoring if wanted. It
  cannot produce detections.
- **tests/SkyEye.Vision.Device** (net10.0-android APK, instrumentation).
  `VerificationReplayInstrumentation` (1,581 lines) reads receipts
  (`<frameId>.json` with `indexedFrame.pixelFile`, `pixelSha256`,
  `calibrationCorners`, `analyzerConfigurationIdentity`, `detectionRequested`)
  and the pixel files from the device's external files dir (`o08-replay[-run]`).
  It builds `VisionAnalyzer(new VisionSettings(Width: 800 | 1000, ...))` with
  the production values, calls `Analyze(byte[], TableCalibration)` and writes
  `o08-replay-proof-<run>.json`. Its `Summarize(VisionResult, width)` emits
  circles as table-2x1 (`x = Centre.Y / width`, `y = 1 - Centre.X / width`).
  It needs the phone, adb and an APK install, and allows at most 6 to 24
  frames per run.
- **Receipts and run manifests** under `work/o09/.../reviews/*/full-audit/*`:
  the `manifest.json` per run (APK hash, settings, source frames and hashes)
  and `source-receipts/`. `scripts/Summarize-VisionReplayBatch.ps1` flattens
  proof reports into CSV or JSON.
- **tools/evidence/score_detections.py** scores any JSONL of detections
  against `work/evidence-audit/labels.jsonl`.
- **SkyEye.Checks** (net10.0 exe) is the desktop check suite. It references
  Core and Observation, links some Companion files, and dispatches on argument
  flags (`--motion-feed`, `--boundary-only`, ...). It has no OpenCV.

Why nothing runs the detector on desktop: every detector file imports
`Org.Opencv.*`, which comes from `SkyEye.OpenCv.Android`. That is a Java
binding project (`IsBindingProject`, `AndroidMavenLibrary org.opencv:opencv
4.14.0`, net10.0-android, android-arm64), so it exists only for Android.
`BallBoundaryRefiner`, `BallClassifier` and `BoundaryColourMask` are already
pure C# in Core, working on `byte[]` BGR or HSV. Core has no pixel-image
abstraction: `ITableFrameImage` only encodes, `EncodeForReview` and
`EncodeForEvidence`.

## 2. OpenCV surface used by `src/SkyEye.Vision.Android`

Counts are textual call sites, from grep across the 9 files. "Default path"
means it is reached by `Analyze(byte[] jpeg, calibration)` with production
settings (Width 800, falling back to 1000 for an incomplete packed cluster,
`LocalIlluminationNormalization = false`).

| Java binding (Org.Opencv) | Sites | Default path | OpenCvSharp4 equivalent and notes |
|---|---|---|---|
| `Imgcodecs.Imdecode(MatOfByte, ImreadColor)` | 1 | yes | `Cv2.ImDecode(byte[], ImreadModes.Color)` |
| `Imgcodecs.Imencode(".png", Mat, MatOfByte)` | 1 | diagnostics only | `Cv2.ImEncode(".png", mat, out byte[])` |
| `new MatOfByte(byte[])`, `.ToArray()` | 4 | yes | compat: hold `byte[]` |
| `new MatOfPoint2f(Point[])` | 7 (2 in shared files, rest in RoiMotion) | yes | **Java converts to float32.** Use `Point2f[]` so `GetPerspectiveTransform` sees the same rounded corners |
| `Imgproc.GetPerspectiveTransform` | 2 | yes | `Cv2.GetPerspectiveTransform(IEnumerable<Point2f>, IEnumerable<Point2f>)` |
| `Imgproc.WarpPerspective(src,dst,M,size)` | 2 | yes | `Cv2.WarpPerspective(src,dst,M,size)`, defaults INTER_LINEAR, BORDER_CONSTANT |
| `Imgproc.CvtColor` (BGR2HSV x6, BGR2GRAY x4, RGBA2BGR x2, RGBA2GRAY x1) | 13 | BGR2HSV yes | `Cv2.CvtColor(src,dst,ColorConversionCodes.BGR2HSV, ...)` |
| `Core.InRange(Mat, Scalar, Scalar, Mat)` | 11 | yes | `Cv2.InRange(src, Scalar, Scalar, dst)` |
| `Core.Bitwise_or` / `Bitwise_and` / `Bitwise_not` | 4 / 3 / 2 | yes | `Cv2.BitwiseOr/And/Not(a, b, dst)` |
| `Core.Compare(Mat, Mat or Scalar, Mat, CmpEq or CmpGt)` | 5 | yes | `Cv2.Compare(a, b, dst, CmpType.EQ or GT)`. For the Scalar overload, compare against `new Mat(size, type, scalar)` or `InputArray.Create` (verify the overload) |
| `Core.CountNonZero` | 1 | yes | `Cv2.CountNonZero` |
| `Imgproc.GetStructuringElement(MorphRect or MorphEllipse, Size)` | 7 | yes | `Cv2.GetStructuringElement(MorphShapes.Rect or Ellipse, Size)` |
| `Imgproc.MorphologyEx(src,dst,MorphOpen or MorphClose,k)` | 6 | yes | `Cv2.MorphologyEx(src,dst,MorphTypes.Open or Close,k)`, same defaults |
| `Imgproc.Dilate(src,dst,k)` on CV_32F | 2 | yes | `Cv2.Dilate` |
| `Imgproc.DistanceTransform(src,dst,DistL2,3)` | 2 | yes | `Cv2.DistanceTransform(src,dst,DistanceTypes.L2,DistanceTransformMasks.Mask3)`, CV_32F |
| `Imgproc.ConnectedComponentsWithStats(img,labels,stats,centroids)` | 8 | yes | `Cv2.ConnectedComponentsWithStats(img,labels,stats,centroids)` returns int, 8-connectivity, CV_32S labels. Stats are int, centroids double |
| `Imgproc.CcStatLeft/Top/Width/Height/Area` | 2/2/7/7/7 | yes | `ConnectedComponentsTypes.Left..Area` (0 to 4) |
| `Imgproc.HoughCircles(img, Mat out, HoughGradient, dp, minDist, p1, p2, minR, maxR)` | 4 | yes (2 on path, 1 neutral-cue probe, 1 FindCorners) | `Cv2.HoughCircles(...)` **returns `CircleSegment[]`**, with no Mat output. Compat writes a 1xN CV_32FC3 Mat, or leaves it empty when N = 0, to match Java |
| `Imgproc.Circle(Mat, Point, int, Scalar, -1)` | 2 | yes | `Cv2.Circle(...)`. **Java truncates the double centre to int**, so use `new Point((int)x, (int)y)` |
| `Imgproc.Blur(src,dst,Size)` | 1 | neutral-cue recovery (when there is no first-pass cue) | `Cv2.Blur` |
| `Imgproc.GaussianBlur(src,dst,Size,0)` | 3 | no (illumination off; pockets and corners) | `Cv2.GaussianBlur` |
| `Imgproc.Canny(src,dst,170,200)` | 1 | no (FindCorners) | `Cv2.Canny` |
| `Imgproc.Resize(src,dst,size)` | 1 | no (FindPockets) | `Cv2.Resize`, INTER_LINEAR |
| `Imgproc.GoodFeaturesToTrack`, `OpticalFlow.CalcOpticalFlowPyrLK` | 2, 2 | no (RoiMotionProcessor) | exclude from desktop |
| `Utils.BitmapToMat` | 3 | no (live bitmap frames) | desktop stub that throws |
| `OpenCVLoader.InitLocal` | 1 | yes (Load) | compat returns true |
| `Core.NumThreads` (setter), `Core.Version` | 1, 1 | yes | `Cv2.SetNumThreads`, `Cv2.GetVersionString()`. `Version` becomes `VisionResult.Runtime`, so expect "4.13.0" against "4.14.0" |
| `Mat`: `new Mat()`, `new Mat(r,c,CvType.Cv8uc1)`, `Rows`, `Cols`, `Channels`, `Empty`, `CopyTo`, `SetTo(Scalar)`, `Submat(Rect)`, `Release`, `Dispose` | about 76 constructions | yes | Direct. Compat `Mat` wraps `OpenCvSharp.Mat` |
| `Mat.Get(r,c)` returning `double[]` | about 55 | yes | Compat reads by depth: 8U, 32S, 32F, 64F, with channels widened to double |
| `Mat.Get(0,0,byte[])` and `Mat.Put(0,0,byte[])` | about 10 | yes | `Marshal.Copy` to and from `mat.Data`. Assert `IsContinuous()` and depth 8U, as Java does |
| `Scalar`, `Size`, `Point`, `Rect` (IDisposable in the binding) | 28, 15, 4, 2 | yes | Compat structs or classes with a no-op `Dispose` |

In all, about 25 distinct OpenCV functions (about 19 on the default path) and
about 10 binding value or container types.

### Numerical parity risks: Android OpenCV 4.14.0 (Maven AAR, arm64) against OpenCvSharp 4.13.0 (x64)

1. **Version gap.** The latest OpenCvSharp4.Windows is 4.13.0.20261107, which
   bundles OpenCV 4.13.0. Use 4.13.0.20260627 to respect the two-week age
   rule. Any 4.13 to 4.14 change to Hough, CCL or warp would show up as a
   parity failure.
2. **ARM HAL acceleration.** The official Android build has used KleidiCV by
   default since 4.11, and has Carotene. Both replace kernels on arm64 (blur
   and filter, resize, colour conversion, possibly morphology and warp; TODO:
   confirm the 4.14 HAL list). x64 uses the reference and SIMD kernels.
   Off-by-one differences are plausible in **BGR2HSV hue** (thresholded by
   `InRange` at hard bounds: cloth band, red 0 to 10 and 170 to 179, yellow 10
   to 40, dark V <= 40) and in **WarpPerspective bilinear rounding**. Either
   can flip mask pixels, then Hough votes and CCL areas.
3. **HoughCircles (HOUGH_GRADIENT)** runs internal Canny, Sobel and an
   accumulator, then a vote sort. Ties can reorder, and order matters: the
   detector processes candidates in list order, with a separation dedupe and
   a `result.Count <= 32` cap. Set threads to 4 on desktop to match, and run
   twice to confirm determinism.
4. **Decode.** The Layout03 inputs are PNG (`decoded-bitmap-lossless`), so
   decoding is exact. JPEG inputs (older corpus, `work/o08/native-pixels/*.jpg`)
   depend on the libjpeg-turbo version and its IDCT.
5. The rest are integer or deterministic and low risk: `GetPerspectiveTransform`
   from float32 points, DistanceTransform with mask 3, CCL labels, morphology
   with constant borders, `InRange`, and `Compare` on equal floats.
6. Resize, GaussianBlur and Canny are off the default path. They matter only
   for FindPockets and FindCorners, which the runner does not call because
   the manifest supplies corners.

## 3. Approach

Alternatives:

- **(a) Source shim plus multi-targeting `SkyEye.Vision.Android.csproj`** to
  net10.0 and net10.0-android, with `#if ANDROID` `using` aliases. This
  touches the production csproj (RuntimeIdentifier, the binding reference,
  and InternalsVisibleTo all need TFM conditions) and every file header. It
  risks Android build drift.
- **(b) Interface `IVisionOps`**: rewrite about 150 call sites to go through
  an interface. This changes production code paths, adds virtual dispatch on
  hot paths, and needs a review of every site. Too much churn for the "no
  behaviour change" go-ahead.
- **(c) OpenCvSharp everywhere, including Android.** OpenCvSharp has no
  supported Android runtime (see `docs/python-port-plan.md`), and it would
  change production numerics. Rejected.

**Chosen: (a') a binding-compatible shim with linked source, and zero edits
to the detector.**

- New `src/SkyEye.OpenCv.Desktop/SkyEye.OpenCv.Desktop.csproj` (net10.0)
  references `OpenCvSharp4` and `OpenCvSharp4.runtime.win` (pinned
  4.13.0.20260627; add `runtime.linux-x64` later if wanted). It declares the
  namespaces `Org.Opencv.Core`, `Org.Opencv.Imgproc`, `Org.Opencv.Imgcodecs`
  and `Org.Opencv.Android`. They contain types with the binding's exact
  managed names and signatures as used above (`Mat`, `MatOfByte`,
  `MatOfPoint2f`, `Scalar`, `Size`, `Point`, `Rect`, `CvType`, `Core`
  (aliased as `Cv` in the sources), `Imgproc` with its constants, `Imgcodecs`,
  `Utils`, `OpenCVLoader`), each implemented over OpenCvSharp. About 400 to
  600 lines.
- New `src/SkyEye.Vision.Desktop/SkyEye.Vision.Desktop.csproj` (net10.0,
  library, RootNamespace `SkyEye.Vision.Android`). It references Core and
  OpenCv.Desktop and links the detector files:
  ```xml
  <ItemGroup>
    <Compile Include="../SkyEye.Vision.Android/*.cs" Exclude="../SkyEye.Vision.Android/BitmapTableImage.cs;../SkyEye.Vision.Android/RoiMotionProcessor.cs" LinkBase="Linked" />
    <InternalsVisibleTo Include="SkyEye.Vision.Desktop.Runner" />
  </ItemGroup>
  ```
  The repo already links files this way (`SkyEye.Checks` and
  `SkyEye.Vision.Device` link Companion sources).
- Desktop `BitmapTableImage.cs` stub in the same namespace:
  `public sealed class BitmapTableImage : ITableFrameImage { public DesktopBitmap Bitmap => throw new PlatformNotSupportedException(); ... }`.
  It is never constructed, so `frame.Image is BitmapTableImage` is always
  false. `Org.Opencv.Android.Utils.BitmapToMat(DesktopBitmap, Mat)` throws.
  `TableVerificationWorker.cs` compiles unchanged and can be used on desktop
  for JPEG or PNG frames.
- RoiMotionProcessor is excluded (live motion only, and nothing in the
  detector references it).
- No conditional compilation symbols are needed. `SkyEye.Vision.Android.csproj`,
  `SkyEye.OpenCv.Android` and every `.cs` file under `src/SkyEye.Vision.Android`
  are untouched, **so Android production cannot change**. Add both new
  projects to `SkyEye.sln`.
- Guard: a check that fails if a new `Org.Opencv` member appears in the
  linked files without a compat implementation. The desktop build fails to
  compile anyway, which is the intended signal.

## 4. Runner command

New exe `tests/SkyEye.Vision.Desktop.Runner` (net10.0). Keep it out of
`SkyEye.Checks` so the main check suite does not need native OpenCV.

```
dotnet run --project tests/SkyEye.Vision.Desktop.Runner -- detect \
  --manifest tests/fixtures/detection-corpus/manifest.json \
  --root . --settings production|receipt|FILE.json \
  [--width 800] [--diagnostics] [--threads 4] --output work/t2/<name>.jsonl
```

For each manifest row:

1. Read `<root>/<imagePath>` and check the SHA-256 against the row hash.
2. Build `TableCalibration` from the corners. Use the normalised 0 to 1 image
   corners, ordered as in receipts `calibrationCorners`.
3. Create one `VisionAnalyzer` per settings identity and call
   `Analyze(bytes, calibration)`.
4. Write one JSONL line:
   `{"sha256", "balls":[[kind,x,y],...], "uncertain", "runtime", "configurationIdentity", "receiptConfigurationIdentity", "algorithmMatchesReceipt", "circles":[...Summarize-shaped...], "elapsedMs"}`.
   `balls` come from `VisionResult.Balls[].Position`, which is already
   table-2x1 (`AnalysisCoordinates.ToTable`), so the values are identical to
   receipts' `balls`. `score_detections.py --detections` reads only `sha256`
   and `balls`, so it ignores the extra fields.

Settings:

- `production` (the default) mirrors `ShellTable.cs`:
  `new VisionSettings(Width: 800) { PackedColourBoundarySearchExtentFraction = .65 }`.
- `receipt` parses the receipt's `analyzerConfigurationIdentity.Settings`
  into `VisionSettings` init properties. It reports `algorithmMatchesReceipt`
  but never claims to reproduce older algorithm code.
- `FILE.json` holds explicit overrides for ablations.

Always print `ConfigurationIdentity` in the run header.

TODO: bind to the final T1 manifest field names (image path, corners and
their space, role, split). Refuse rows marked Holdout unless an explicit
flag is given. The rules say Holdout is never opened.

## 5. Parity test

- Reference: `work/o09/development-v3/reviews/layout03-post-fix-current-source-20261102/`.
  `replay-audit-post-duplicate.json` (checkout da161891, fix 432c0a13, APK
  e405c1cb...). Per-frame proofs are in
  `full-audit/l03cur{2083,957,962,2263}-*/o08-replay-proof-*.json`, under
  `samples[0].width800.analysis.circles` (table-2x1 x, y, radius, kind). The
  detections are in `detections-current-source-post-duplicate-v2.json` and
  the score is `baseline-post-duplicate-v2-tolerance-0.010.json`: 15/16,
  15/16, 15/15 and 14/15, so 59/62 found, 0 FP, black missed in 2083, 2263
  and 962, colour-correct 43.
- Inputs: `work/o09/development-v3/reviews/layout03-validation-spatial-baseline-current-source-20261102/source-inputs/{both-present/2083,black-absent/957,cue-absent/962,both-restored/2263}.{png,json}`.
  The SHA-256 of 2083.png has been checked: 731ce91d....
- Run with the same flags as the device: Width 800, extent .65, and
  `includeBoundaryDiagnostics` and `includeCandidateDiagnostics` both true,
  because `auditEnabled` was true. Emit the same `Summarize` shape.
- **Pass**: per frame, the same circle count, the same kinds, and one-to-one
  centres within 1 px at width 800 (0.00125 table units). Also the same
  `uncertain`, `layoutUncertain` and `packedClusterFallback.Recovered`.
  Report the radius delta separately.
- **Stage-level diagnosis** when a frame fails: compare the warped BGR first.
  `work/o08/native-pixels/{1241,...,1316}-1000.bgr` and `-400.bgr` are device
  warps of the matching `.jpg` and `.json` (6 frames, OpenCV 4.14 era), and
  they need no phone. Then compare HSV through the `ObserveNextClassifierPixels`
  and `ObserveNextBoundaryPixels` hooks, then the Hough list.
- **Fallback** (as in the plan): if parity fails and cannot be fixed by
  matching thread count or the float corners, keep the device as the
  reference. Use desktop only for before-and-after deltas on the same
  desktop build, labelled "desktop OpenCV 4.13, not device". Re-check any
  promoted change on the device.

## 6. Steps, effort and risks

1. Compat shim plus desktop library, compiling with zero detector edits: 0.5
   to 1 day.
2. Spike: frame 2083 end to end, comparing against the proof circles: 0.5
   day. This is the plan's step 1.
3. Runner `detect` command over the T1 manifest, writing JSONL, and checking
   it through `score_detections.py`: 0.5 day (after T1 lands).
4. Parity command on the four Layout03 frames plus a warp check against
   native-pixels: 0.5 day.
5. If there are differences: stage diffs and, at worst, build OpenCvSharpExtern
   against OpenCV 4.14: 1 to 2 days, optional.

Total about 2 to 3 days, plus up to 2 more for parity repair.

Risks:

- HAL (KleidiCV or Carotene) differences in HSV and warp.
- The 4.13 against 4.14 version gap.
- Hough tie order.
- A compat signature mistake. The `Get` depth handling and the Circle point
  truncation are the likeliest.
- Namespace squatting of `Org.Opencv` in a desktop assembly. It is harmless
  but must never be referenced by an Android project.
- `global.json` pins SDK 11.0.100-rc.1 with `rollForward: disable`, so the
  Windows machine needs that SDK.

TODO: `docs/claude/data/opencv-surface.csv` (machine-readable form of the
table in section 2).
