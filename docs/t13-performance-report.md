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
