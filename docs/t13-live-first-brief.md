# T13 — Make SkyEye Sing: Live-First Performance & Sky Orchestration

SkyEye works, but the current experience feels significantly slower than it should. Before adding more perception, physics, game intelligence or UI features, reshape the runtime around a fast live-video experience and an event-driven Sky-controlled architecture.

Treat this as a vertical performance mission, not a general refactor.

## Hardware topology

This is canonical:

- **Pixel 10 Pro** — dedicated overhead camera and video-streaming device.
- **Pixel 11 Pro XL** — companion/host; receives the video, displays it, performs perception/analysis, runs Sky and controls presentation.
- **Chromecast Ultra / TV** — presentation surface controlled by the Pixel 11/Sky.

Do not move expensive analysis back onto the Pixel 10 unless there is a compelling measured reason.

## Product behaviour

SkyEye is primarily a **live video appliance with intelligence available on demand**.

The default state should be:

```text
Pixel 10 camera
    ↓
live stream
    ↓
Pixel 11
    ├── live phone view
    └── live TV view
```

Expensive table analysis must **not** run periodically just because time has passed.

Remove the existing behaviour where ordinary polling indirectly causes table analysis every few seconds, and remove any similar fixed-cadence candidate analysis from the normal runtime.

A fresh table observation should happen only when:

1. the user explicitly asks for something requiring it;
2. Sky invokes it as part of answering/acting for the user;
3. Sky deliberately decides that a fresh observation is useful because of some future lightweight event/policy.

Do not implement speculative autonomous behaviour in this mission beyond what is necessary to establish that architecture.

## Sky is the orchestrator

Sky should ultimately be able to direct the whole application.

Introduce or reshape typed application commands/tools so that Sky and touch UI go through the **same underlying application command surface**.

At minimum establish commands equivalent to:

```text
observe_table

set_phone_presentation
set_tv_presentation

show_shot_choices
show_shot
show_annotation
clear_annotations
```

Exact naming is up to the implementation if existing domain terminology suggests something better.

Presentation state should be explicit.

Examples:

```text
Live
ShotChoices
SelectedShot
Replay
```

Annotations should be independently controllable where practical rather than being inseparable from one monolithic "candidate mode".

Examples could include:

```text
TargetBall
GhostBall
CueBallPath
ObjectBallPath
Pocket
ShotLabel
Confidence
GameState
```

Do not build every possible annotation now. Establish an extensible typed model and prove it with the annotations SkyEye already supports.

The default phone and TV presentation is **plain live footage**.

Example desired interactions:

> "Sky, what shots have I got?"

Sky obtains a fresh observation, calculates the relevant choices and decides what presentation is useful.

> "Show me number two on the TV."

The TV shows that prediction.

> "Put the cue-ball path on my phone too."

The phone presentation changes independently.

> "Clear that."

The relevant surface returns to live footage.

## Performance investigation first

Before making major changes, instrument the current pipeline and establish a real-device baseline.

Measure separately where possible:

```text
camera capture
encode
stream/transport
receive
decode
display
frame acquisition for analysis
frame conversion/copying
vision analysis
post-processing
annotation/compositing
TV delivery
```

Particularly investigate the existing HLS implementation.

The current camera stream uses approximately two-second media segments and the Pixel 11 uses a comparatively standard ExoPlayer path. Determine how much of the perceived latency comes from this before assuming perception is responsible.

Measure actual:

```text
Pixel 10 camera event → Pixel 11 displayed image
```

Use timestamps/instrumentation where possible rather than subjective estimation.

Investigate the smallest coherent improvement first:

- lower-latency Media3/HLS configuration;
- shorter segments if appropriate;
- buffering/live-offset configuration;
- eliminating unnecessary queues;
- latest-frame-wins behaviour.

If conventional HLS cannot provide an acceptably responsive Pixel 10 → Pixel 11 live view, document the evidence and introduce a dedicated lower-latency live transport rather than endlessly tuning it.

It is acceptable to keep the existing HLS/segment path for recording, replay or Chromecast while using a different path for the realtime Pixel 11 view.

Do not replace working infrastructure merely because another technology is theoretically faster. Let measurements drive that decision.

## Stop round-tripping analysis frames unnecessarily

Investigate the current table-analysis path closely.

At present a requested observation can involve asking the Pixel 10 for a snapshot derived from its recorded media, encoding/transferring JPEG and then decoding/analyzing it on the Pixel 11.

The preferred target architecture is:

```text
Pixel 10
camera
  ↓
hardware video encode
  ↓
live transport
  ↓
Pixel 11
hardware decode
  ↓
latest decoded frame
  ├── display
  └── on request → VisionAnalyzer
```

Where technically reasonable, analyse the **latest usable Pixel 11 frame that is already part of the live stream**.

Avoid:

- extracting another frame on Pixel 10;
- JPEG encoding solely for analysis;
- transferring a duplicate image;
- JPEG decoding again on Pixel 11;
- analysing stale queued frames.

The observation should have an explicit timestamp/age so consumers know how fresh it is.

If using the decoded player frame directly is not viable with the present Media3 surface architecture, find the least-copy alternative and document the trade-off.

## Latest wins

Realtime work must not build a backlog.

If processing cannot keep up, prefer:

```text
discard stale work
process the newest relevant frame
```

over:

```text
queue frame 1
queue frame 2
queue frame 3
...
eventually analyse history
```

There should be bounded queues and no ever-growing latency.

## Use the hardware sensibly

The Pixel 11 should not perform unnecessary continuous computation while merely displaying the table.

During normal live viewing:

- hardware video decode should do the video work;
- full table detection should be idle;
- shot prediction should be idle;
- annotation encoding should be idle unless a surface actually requires annotations;
- Sky remains available for interaction.

Do not move the existing OpenCV detector to Tensor/TPU merely for the sake of using the TPU. The existing detector is not currently a neural model.

First make the data path fast.

After that, record the isolated cost of `VisionAnalyzer` so a later mission can decide whether OpenCV optimisation, tracking, segmentation or LiteRT/Tensor acceleration is justified.

## Preserve existing capabilities

Do not regress:

- camera pairing/session control;
- calibration;
- recording;
- replay/clip buffer;
- clip saving/downloading;
- existing table detection;
- existing shot prediction;
- casting;
- current working recovery behaviour.

Restructure these only where necessary for the performance architecture.

## Observability

Add useful diagnostics so future optimisation is empirical.

Expose/log at least:

```text
live video latency if measurable
display FPS
analysis request count
analysis duration
age of frame analysed
dropped/replaced analysis requests
active presentation mode
whether annotated encoding is running
```

Prefer p50/p95 measurements where enough samples exist.

Do not leave verbose diagnostics permanently polluting normal UX.

## Acceptance criteria

T13 is complete when all of the following are demonstrated on the actual Pixel 10 Pro + Pixel 11 Pro XL setup:

### Live-first

- Starting SkyEye produces a continuously updating live view without automatically running full table analysis.
- Idle full-table analyses per minute = **0**.
- Candidate/shot analysis does not run on a timer.

### On-demand perception

A command such as:

> "analyse the table"

or its programmatic equivalent:

- uses a fresh/latest frame;
- performs one bounded analysis;
- returns the result with frame timestamp/age and measured analysis duration;
- does not start a permanent analysis loop.

### Sky/presentation architecture

Sky-compatible typed commands can:

- request a fresh table observation;
- change the phone presentation;
- change the TV presentation;
- show at least one existing shot/trajectory annotation;
- clear the presentation back to live.

Touch UI and Sky commands should converge on the same application behaviour/state rather than duplicate implementations.

### Latency

Record before/after measurements for Pixel 10 camera → Pixel 11 display.

Target initially:

```text
< 500 ms glass-to-glass
```

with:

```text
< 250 ms
```

as a stretch goal.

If <500 ms cannot reasonably be reached with the current transport, T13 may instead introduce a measured lower-latency live transport and retain HLS where it remains useful.

Do not claim a target has been achieved without physical-device evidence.

### Analysis

Record real-device:

```text
p50
p95
frame age
```

for requested table observations.

Do not optimise perception accuracy or replace the detector in this mission unless profiling identifies a straightforward performance defect.

### Resource behaviour

- no unbounded video/frame/analysis queues;
- latest-frame-wins where appropriate;
- no continuous annotation re-encoding when both surfaces only require live video;
- no obvious thermal runaway during a representative development test;
- background/recovery endurance work is explicitly deferred to T14.

## Deliverable

At completion provide a concise engineering report containing:

1. the original measured bottlenecks;
2. the architecture changes made;
3. before/after latency measurements;
4. requested-analysis timings;
5. what work remains expensive;
6. whether the current live transport should remain or be replaced later;
7. any recommended follow-up mission specifically justified by measurements.

Do not add unrelated features.

The purpose of T13 is simple:

> **Make the existing SkyEye experience feel immediate, make expensive intelligence intentional, and establish Sky as the single orchestration layer over a fast live system.**
