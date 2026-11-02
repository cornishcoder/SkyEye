# O09 Layout03 edge-template coverage and worker replay — 2 November 2026

**Status: saved-image worker replay passes; physical stillness remains unverified.** The former full-window template rejected several bodies near the warped image edges, leaving the four Layout03 physical attempts at **0/4 stillness**. A fixed-sample clipped-template candidate now preserves the existing one-pixel motion gate and complete-body requirement.

## Named tests and results

### `O09-NORMAL-TRAIN-LAYOUT03-EDGE-TEMPLATE-COVERAGE-EXPERIMENT`

The offline candidate check hash-verified all **30/30** saved PNG/receipt pairs and traced the **26** later measurements across the four operation timelines. The fixed common sample mask restores complete 14/14 body-template coverage in **26/26** measurements; maximum candidate displacement was **0.353 px**, below the existing 1.0 px motion gate.

Synthetic controls passed **4/4**: an edge body moved by 2 px remains measurable, a stationary body under a channel-wise lighting change stays at zero displacement, a textureless patch stays unknown, and an in-bounds patch matches the existing comparison. This is a Development diagnostic, not physical evidence. It ran with OpenCV 4.12.0 and NumPy 2.2.6. The script and result are [edge_template_coverage_experiment.py](../../tools/o09/edge_template_coverage_experiment.py) and `work/o09/development-v3/reviews/layout03-current-source-baseline-v1/edge-template-coverage-experiment.json` (SHA-256 `f1b033cf2aedafe0f3f3f3e6fbd2b9ff8c4bae36ad2b4f556ccdd728019ef812`).

### `O09-NORMAL-TRAIN-LAYOUT03-EDGE-TEMPLATE-WORKER-REPLAY`

The Android verifier now uses the clipped comparison and clips the corresponding pixel mask to the motion image. It retains the existing requirement that every tracked body has both anchor and previous-frame support. The matcher uses the same photometric score, curvature, and texture rejection; its clipped sample set must contain at least 30 points and at least 25% of the full disk.

`SkyEye.Checks` passed with **exit code 0**. Five focused assertions cover in-bounds parity, rejection by the old full-window method at an edge, detection of a 2 px edge movement, stationary photometric change, and unknown low texture. A Vision Device Release build passed with **0 errors and 23 warnings**. The APK SHA-256 is `EDE15D64E101F804B974E4BA7D31EB6C6B80035DAB99385CD9E9C72B4DEDCB6C`.

The device replay used the first six chronological frames from each of the four independent saved timelines, within the runner's six-frame full-audit limit. All four runs report `configurationComparison=exact-match` with `SparseBlackRankByRadius=true`. At width 800 and width 1000, all four runs report every post-anchor sample as motion `Still` with valid coverage. At width 800 this is **20/20** post-anchor measurements, supporting 14 or 15 regions; maximum measured displacement was **0.3942 px**, below the existing 1.0 px gate. The result manifest is `work/o09/development-v3/reviews/layout03-current-source-baseline-v1/worker-replay-exact-20261102/edge-template-worker-replay-summary.json` (SHA-256 `a96a3fea6bdd331639b95b7fc311ce4ef76b50950024f7fb71c379985d86d7fe`). Replay reports are in the same folder.

Width-800 `Measure` wall time across all 24 replay frames was **120.1–1,055.3 ms** (median 501.8 ms). The 12 detector-requested frames took **841.5–1,055.3 ms** (median 967.5 ms); the 12 frames without detection took **120.1–162.1 ms** (median 146.9 ms). These are replay worker timings, not live source-gap or latency acceptance. They flag a potential cadence/timeout risk for the physical retest, despite the saved inputs' maximum 533.378 ms source gap.

These are still per-frame saved-image results, not a `TableAnalysisVerification` publication and not a live-camera pass. `VerificationConsensus` remains null with `final-quality-or-count`, because the independently measured detector still misses black and cue. The four physical Layout03 results therefore remain **0/4**; no stillness result is claimed from replay.

## Failed attempts retained, not counted

- The first integrated worker replay exposed an out-of-bounds pixel-mask access after the clipped matcher supported an edge body. The mask loop is now clipped to the 400×800 motion frame; the original failed report remains under `worker-replay-20261102/`.
- The next replay had `configuration-mismatch` because the runner defaulted `SparseBlackRankByRadius` to false. Those four reports remain under `worker-replay-edgefix-20261102/` and are excluded. The exact-profile batch was rerun once with the recorded setting and passed 4/4; do not repeat it unless source, settings, or inputs change.
- An initial synthetic C# edge fixture used a 5 px body inside a 12 px template and correctly failed the variance floor. It was replaced with a full-sized textured edge body; all five focused assertions then passed.

## Stillness estimate and next gate

For a **physical motion-still result**, the remaining work is one bounded live verifier attempt using the current Layout03 setup: normally up to seven samples, with a two-second/five-sample stillness span and a four-second timeout. The saved worker replay supplies the expected motion/coverage behavior, but the physical retest may still fail on timing, lighting, source freshness, or other live conditions.

The mounted-camera `/status` endpoint at `192.168.1.124:80` refused the connection at **14:03:56 UTC**. The last successful status read, at **13:36:25 UTC**, reported `Stopped`; capture was not started during this work. Restore `/status` reachability before the physical retest so capture state can be verified. Next named test: **`O09-NORMAL-TRAIN-LAYOUT03-BOUNDED-PHYSICAL-VERIFIER-RETEST`**.

A still motion result can remain **Unclear** because final detector consensus is a separate gate. Current-source Layout03 spatial score remains **56/62 TP, 0 FP, 6 FN**; black and cue are each detected in **0/3** present frames, all four spatial outputs remain uncertain, and complete-inventory acceptance is **0/3**. Thus this one retest is the remaining near-term work to check physical stillness; it does not make Live Detect ready for play testing.

Broader play-test metrics are unchanged: reviewed Development anchors **2/12**, staged states captured **6/24**, additional matched-control states label-frozen **3/12**, complete lighting strata **0/2**, Holdout unopened, D1/D2/E **0/3 proven**, and **19 acquisition operations** remain for the broader Development matrix. No calendar estimate is defensible until the endpoint and physical retest are available.
