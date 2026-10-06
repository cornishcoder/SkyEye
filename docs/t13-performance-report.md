# T13 performance investigation

Status: implementation in progress. Physical performance acceptance is not yet complete.

## Original pipeline, 6 October 2026

Pixel 10 Pro capture remained Active in session `a061183702574b4b837c57fc2f61fe7e`, timeline `afbdc86e83d44dd8aaa7702bcc1dcc38`, with eight saved clips. Companion-only instrumentation updates preserved app UID/private storage. Claiming the available host lease advanced authority revision to four without changing capture. The camera was not updated or stopped.

The user observed approximately ten seconds between a physical table event and the phone picture. This is a human estimate, not a calibrated glass-to-glass percentile. Sixty software samples over approximately seventy seconds yielded Media3 live-offset p50 12,808 ms and p95 12,814 ms. Live offset uses playlist wall time and is a software estimate. Annotation source-clock mapping independently reports approximately twelve seconds; telemetry RTT was 36–77 ms at the sample endpoints. Neither decoder release nor texture callbacks prove photons on the display. Network delay, telemetry binding delay and unsynchronized wall clocks remain uncertainty.

The opt-in diagnostics accumulated 29 polling-triggered full analyses. Frame acquisition (camera extraction/JPEG/network) p50/p95: 771/976 ms; vision: 194/255 ms; post-processing: 0.049/0.494 ms; whole observation: 940/1,150 ms. Acquired camera frames were p50 3,302 ms / p95 3,916 ms old. No candidate presentation was requested. The default TV relay nevertheless encoded a 1920×1080, nominal 10 fps corrected/watermarked stream. The texture callback counter is separate from decoder releases and is not an external display FPS measurement.

Camera completed-frame count advanced by 2,100 over approximately seventy seconds (30 fps). Battery stayed 100%, reported temperature 37.2→37.1°C, saved clips stayed eight and camera error stayed empty. This is a brief development sample, not endurance acceptance.

Evidence: `evidence/t13-baseline-metrics.json`, with bounded counters/stage distributions. The repeatable harness is `scripts/Measure-LivePipeline.ps1`. Compile with `SkyEyePerformance=true` and launch with `--ez skyeye-performance true`; metrics/control listen only on device loopback and are reached through owned ADB forwarding. Normal builds omit the server. Data excludes image bytes and credentials. Quantiles require at least twenty samples.

## Build and measurement limits

The instrumented production Release build and baseline core/HTTP checks passed. Capture/encode/transport/receive/decode are not individually isolated by this first harness. Decoder-to-texture pairing is approximate because Media3 may schedule future releases; negative offsets must not be interpreted as negative latency. Resource/transport changes and integrated acceptance follow this baseline.

## Requested observations (T13.02)

Both companion routes now separate telemetry from full table observations and remove ten-second candidate-analysis refresh and automatic settlement retries. Touch and Sky use `ObserveTable` on `AppCoordinator`. Receipts identify camera/session/capture scope, analysis/media generation, timeline, capture timestamp, frame age, acquisition/vision/total duration and honest Ready/Unsettled/Unclear outcome. Source, media, calibration/layout edits and cancellation fence publication; requests have a ten-second limit. Existing table-session single-flight work coalesces bursts; command admission is bounded to sixteen active effects. Duplicate operation IDs reuse the original receipt. No observation starts a loop.

Physical Pixel 11 evidence: sixty idle samples contain zero full analyses and zero predictions. One explicit request performs one analysis and returns Unclear, rather than inventing a settled table. An eight-request burst performs one further analysis, with seven coalesced requests and eight scoped receipts. Recording stays Active in the original session/timeline and the camera catalogue stays eight clips. Evidence: `evidence/t13-requested-observation.json`. Core/HTTP checks cover 100 coalesced requests, cancellation, source/media fencing and duplicate commands; production and retained-native Release builds pass. Device testing exposed Media3 thread affinity in the completion check; native generation is now cached on the UI thread and failure checks always produce a bounded receipt.

Automatic display-only pocket detection and the corrected/watermarked TV relay are still measured costs for T13.06. Source acquisition still uses the camera snapshot in this chunk. HLS tuning and decoded-frame observations follow; this chunk does not claim the latency target.

## Live transport (T13.03)

The smaller HLS experiment uses Media3's explicit live configuration and a bounded 250–5,000 ms buffer, 100 ms playback admission and 250 ms rebuffer admission. Forty-five physical-device software samples (42 playing) still yield live-offset p50/p95 6,977/7,629 ms and source-age estimates 5,949/6,669 ms. This measured limit justified a separate responsive phone path. The existing two-second archive/TS segment path remains for recording/replay/Chromecast. Configuration follows [Media3 live-streaming controls](https://developer.android.com/media/media3/exoplayer/live-streaming).

The dedicated phone path sends silent MPEG-TS containing the existing hardware AVC samples immediately, without another camera encoder or waiting for segment finalization. Three viewers maximum; four encoded frames per viewer maximum; samples capped at 1 MiB. Network IO is outside the capture callback. Slow viewers disconnect, discard buffered dependent frames and reconnect at a new keyframe. Publication is skipped when no phone viewer exists. Capture transitions close the old timeline's viewers. Media3 hardware decode and existing TS timestamp mapping remain in use, including the annotation journal's camera-monotonic clock.

After the explicitly approved stop/update/re-arm/restart, camera UID/storage and eight saved clips remain. Recording runs in new session `6b89aadb6bce4e5a9f9b1a6b478884e9`, timeline `0f748d2fa84d4dde9ed8d8cf3a15d89a`, with the camera display off. Sixty samples show 30.00 decoder releases/sec and 25.97 texture updates/sec, zero perception/prediction calls, and source-age estimate p50/p95 599/682 ms. The user estimates physical event-to-phone delay at about one second. **The <500 ms glass-to-glass gate is not established.** Idle relay/pocket-detection costs are still present at this point; subsequent rendering work will be measured separately. Camera temperature stays 36.5°C in this brief run, capture advances and error stays empty.

Evidence: `evidence/t13-hls-tuning.json`, `evidence/t13-live-stream.json`. Core checks cover keyframe admission, viewer/frame bounds, discarded overflow and timeline reset. Independent native mux tests compare 60 decoded frames in archive Annex-B, AVC length-prefix and per-sample stream modes; all match the original. Both signed Release apps build. An owned development forwarding is used for safe status checks; no read token is persisted in evidence. This is LAN/TCP phone transport, not a claim of WebRTC or internet streaming support.
