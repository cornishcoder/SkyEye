# Poolgame C# port — implementation and verification

Implemented in SkyEye on 3 October 2026, in small committed checkpoints. The companion runs local C# algorithms with native OpenCV image primitives. It requires no Python installation, inference server or downloaded model. The user's mounted camera and current grey-blue cloth lighting are the baseline for the recorded regression frames.

## Source and scope

Reference: `C:\code\Poolgame-AI-shot-prediction`, upstream https://github.com/tomasz-pawlaczyk/Poolgame-AI-shot-prediction, revision `a7a803177c63ccb51f715c6920ba650fc341f76a`. This source checkout was retained unchanged. Its executable pipeline is OpenCV/Hough/HSV plus K-means and direct/bank/kick geometry. YOLO weights and the README's post-collision physics are absent; those are not implemented by this port. No LICENSE file was found in the source checkout. Preserve provenance and establish reuse terms before distributing incorporated upstream work.

The initial planning document remains in `python-port-plan.md`; this report records implementation and achieved evidence, including acceptance targets that remain open.

## What changed

- `SkyEye.OpenCv.Android` binds the official ARM64 OpenCV 4.14.0 Android library to C#. `SkyEye.Vision.Android` owns image decoding, perspective rectification, adaptive HSV cloth masking, morphology, Hough circles, cue-based normalization, bounded C# clustering and red/yellow/cue/black classification. All native matrices explicitly release their buffers before disposing their wrappers.
- `AnalysisCoordinates` maps the portrait analysis plane to SkyEye's horizontal 2 × 1 table plane, preserving each of the six pocket identities. The overlay uses the inverse calibration and the exact analyzed image's letterbox bounds.
- `ShotMath` and `ShotPlanner` generate direct pots, object-ball banks and cue-ball kicks, with at most one cushion rebound per ball. Every segment must clear obstacles and satisfy finite cushion bounds, contact direction and pocket-mouth restrictions. Up to three candidates are ranked by geometric difficulty with stable tie-breaking.
- `TableTracker` requires distinct fresh frames over at least two seconds to establish settlement. Confirmation and reviewed corrections belong to one calibration and capture timeline. A fixed anchor prevents slow cumulative movement from passing as stationary; small measured Hough jitter is tolerated. Physical obstruction, source changes, long gaps and actual layout changes invalidate confirmation.
- The companion analyzes one frame at a time, discards results from superseded calibration/lifecycle generations and never queues a backlog. It shows counts, frame age, analysis time and uncertainty. Advice expires even during a long replay operation. Candidate selection draws all cue/object segments, ghost contact and cushion contacts. Maeve's three pool tools use the same validated state and candidate IDs as the screen.
- Calibration freezes the image while corners are selected. Automatic corner proposals require review, with manual selection as fallback; they never silently replace a saved calibration.

## Intentional adaptations

Python's solid/stripe labels are retained as internal diagnostic patterns and adapted to International Eightball colours. A spotted cream cue and a black with a large cream number patch require considering the complete ball body rather than its most saturated patch. Quality scores describe feature strength, not detection probabilities.

The source mask's fixed saturation floor loses much of the user's normal grey-blue cloth. The C# pipeline estimates cloth hue and saturation/value floors from interior samples. Near the cushion, circles with overwhelmingly cloth-coloured interiors are rejected as shadows. Mixed, neutral and dark ball bodies remain available for classification or review. Pocket masks cannot prove a ball is absent: coloured pocket-opening pixels explicitly request review.

The default analysis plane remains 1000 × 2000. A 900-pixel experiment produced an extra cushion detection and was not deployed. Production geometry uses radius 27, finite cushion-centre bounds and inset pocket destinations; Python comparison fixtures separately use the original radius 23, rail/pocket settings and rounding. Production validation fixes upstream zero-length/nonfinite inputs, unbounded reflections, missing path legs and obstruction checks limited to an incomplete path. Difficulty ranks candidates; it does not predict pot probability, spin, pace, jaws, resulting cue position or full rules/refereeing.

## Verified evidence

| Check | Result and limits |
|---|---|
| Python mathematics | All 270 generated candidates across three reference layouts match ghost positions, cut angles, lengths, reflections and difficulty under upstream comparison settings. Source hashes are recorded with the fixtures. |
| Core behaviour | Direct/bank/kick generation, second-leg obstruction, bounded rail/pocket contacts, stable top-three ordering, target groups, nonfinite/malformed data and freshness gates pass. Settlement, duplicate timestamps, gaps, calibration/timeline changes, reviewed corrections, physical obstruction and slow cumulative movement checks pass. Existing ownership, capture-selection and HTTP checks also pass. |
| Real images on Pixel 11 | Three frames from the same mounted camera/layout retain 16 balls: seven reds, seven yellows, one cue and one black. The labelled first-frame maximum centre error is 0.00874 table units, or 8.74 analysis pixels. This passes the recorded 0.015 fixture tolerance; it does not establish the planning target below one quarter of a ball radius. |
| Negative images on Pixel 11 | Empty, very dark, interior obstruction, edge-connected obstruction and pocket-adjacent colour cases request review. Both obstruction cases flag physical obstruction. A dim variant retains all 16 balls. These synthetic probes are not precision/recall data. |
| Release performance | Twelve repeated 1000-pixel fixture analyses: median 573 ms, p95 793 ms, retained native allocation growth 3,808 bytes after collection. Normal allocation pressure drives GC during timing; forced GC belongs to the separate retained-memory measurement. This is a short fixture benchmark, not a live-session endurance measurement. |
| Native packaging | Packaged ARM64 ELF load segments and 16 KB APK alignment are checked. Both Pixels run Android 17 with 4 KB pages; physical execution on a 16 KB-page device remains untested. |
| Build | Core checks, the complete Debug solution and trimmed/AOT Release companion and device-check APKs build successfully. Generated-binding inherited-member warnings and duplicate native-entry warnings remain visible; existing WebRTC private callback warnings can occur on binding regeneration. |

Machine-readable device results: `docs/evidence/poolgame-device-vision.json`. Alignment evidence: `docs/evidence/poolgame-companion-native-alignment.json`. Actual package/dependency snapshot: `docs/poolgame-dependencies.json`. Fixture provenance and limitations: `tests/fixtures/poolgame/README.md`.

Live verification used the installed Release companion while the Pixel 10 continued capturing. Selecting the second candidate displayed a cue-ball kick, ghost contact and complete object-ball path on the camera image. After the cadence correction, eight UI samples over approximately 28 seconds retained confirmation, the same three candidates and all 16 correct colours. Sampled frame ages were 1.7–5.0 seconds, within the six-second gate. The eight displayed analysis durations had median 987 ms and p95 1,297 ms: **the one-second live target was not met**. These short UI samples are not a sustained benchmark. Evidence: `docs/evidence/poolgame-live-ui.json`.

The poll cadence counts network and analysis time within its 2.5-second interval instead of adding 2.5 seconds afterwards. It remains sequential, never accumulates queued frames and keeps the six-second advice freshness gate. The initial live run showed stale windows; outdated advice was suppressed. Long downloads/export also clear expired paths and require fresh confirmation after the resulting observation gap.

Saving 30 seconds while live viewing produced a 30.603-second camera clip, including its leading keyframe allowance. The 23,403,474-byte download matches camera SHA256; its H.264 1920 × 1080 video and AAC audio both decode. The Release companion completed portrait preparation, showed the new clip as available locally and reported playing its downloaded file. The camera remained Active with roughly 621 seconds of rolling history. This run verified export completion through the native UI; it did not retrieve the new exported bitstream or disconnect the camera for playback. The earlier prototype's inspected portrait export and camera-disconnected playback remain separate evidence. See `docs/evidence/poolgame-replay.json`.

## Reproduction

```powershell
python scripts/Generate-PoolgameReference.py --source C:/code/Poolgame-AI-shot-prediction
dotnet run --project tests/SkyEye.Checks -c Release
dotnet build SkyEye.sln -c Debug -m:1
dotnet build tests/SkyEye.Vision.Device -c Release
dotnet build src/SkyEye.Companion -c Release
python scripts/Test-NativeAlignment.py src/SkyEye.Companion/bin/Release/net10.0-android/android-arm64/uk.co.skyeye.companion-Signed.apk
```

Build Android projects sequentially. Install the signed device-check APK on the companion Pixel and run `uk.co.skyeye.visionchecks/.MainActivity`. Its bounded test report is exported to `/sdcard/Android/data/uk.co.skyeye.visionchecks/files/vision-checks.json`. The production Release companion does not export table diagnostics or credentials. The prior companion APK remains available for rollback; install with replacement to preserve app settings/calibration/clips.

## Acceptance still open

The implementation milestones are complete; broad field acceptance is not. The three real frames share one layout and were used for tuning. A held-out set of different layouts, shots, glare, touching balls, pocket-adjacent black/cue and real obstructions is needed before claiming 95% precision/recall or general detection reliability. Long-session memory, live latency, thermal/battery behaviour and physically executing the proposed banks/kicks need further measurement. Automatic corner proposals are implemented but not established as reliable on arbitrary tables. Paid Maeve voice/tool conversations, Chromecast playback and the wider original capture acceptance checklist remain unverified by this port.
