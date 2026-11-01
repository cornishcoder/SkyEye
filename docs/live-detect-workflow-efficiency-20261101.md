# Live Detect workflow efficiency — 1 November 2026

This operating plan implements the user's request to batch staged table changes and process the evidence afterwards. It changes work order, not the D1/D2/E acceptance requirements. The continuation goal and device guide remain authoritative.

## Highest value changes

| Priority | Change | Concrete practice | Evidence preserved |
| --- | --- | --- | --- |
| 1 | Finish a physical control batch before deep analysis | Predeclare a short sequence, capture each step, check saved images/receipts immediately, then release the table and work offline. | Source PTS, configuration, calibration, setup identity and lighting checks for every step. |
| 2 | Make each offline cycle answer one question | Write the hypothesis, intended configuration delta, selected positive/negative frames and decision rule before running. Stop widening a sweep when it cannot answer that question. | Baseline and candidate on identical coverage; failures and skipped samples retained. |
| 3 | Build once per candidate, replay many frames | Use the runner's required Release configuration directly. Hash and install one isolated APK, then run all planned batches against it. Rebuild only for source changes. | Tested checkout, APK identity, complete settings, warm-up and batch shape. |
| 4 | Reuse validated evidence | Maintain one compact run index linking immutable artifacts and hashes. Reopen the relevant record instead of rereading the entire plan or rerunning valid baselines. | Original reports, receipts, review status and declared execution scope. |
| 5 | Put cheap checks before device work | Validate runner syntax, explicit label coordinates, frame/receipt hashes, source/configuration identity and command arguments before install/replay. | Reject mismatches early; never repair retained reports by relabelling. |
| 6 | Review readable image batches | Display actual full overlays and enlarged crops together, then ask one clearly scoped review question. Keep a per-frame label record. | Each frame's independent review; no copied truth, implied tolerance or review by silence. |
| 7 | Parallelize bounded offline work | Delegate independent provenance, scoring or documentation audits while the primary agent owns code and the device queue. | One writer per artifact; no concurrent device jobs or overlapping edits. |

The current session demonstrates why these changes matter: the restaged cue-positive frame group was internally stable, but later cue removal occurred after daylight darkened; earlier groups also differed in cloth-patch luminance. Interleaving lengthy replay and label work between physical steps lost the matched comparison. Separately, a wrong implicit black trace point required a runner guard, and inspection of the component cap revealed useful source evidence without another capture. See [restaged evidence](evidence/opposite-cue-positive-restaged-stage-replay-20261101.md) and commits `02c5405`, `52bd55a`, `8ebf829`, `13fb7f9`.

## Measured lesson from the O09 v2 export — 1 November

The seven newly reviewed controls were efficiently handled as one guide-sheet review and one immutable corpus expansion, then the exact frozen transfer checkpoint was probed once across all seven frames. That avoided seven separate model runs and prevented another training cycle before a split audit. The export audit showed why that ordering matters: all seven additions landed in train, while validation remained only two frames/31 boxes from one correlated group. A second training run on that split would not answer whether the detector generalizes. Before asking for another staged batch, precompute group-aware train/validation roles and verify that validation has multiple independent, varied full-table layouts in each supported lighting stratum. Review the contact sheets together and keep the same-table present/absent sequence within one setup group.

The one-pass probe then rejected the checkpoint decisively: 0/7 layouts, every frame at the 100-prediction cap, no cue/black matches, and capped 664 false positives. The next useful work is coverage collection and split design, not confidence sweeps or repeated inference on the same seven images. This measured reuse is recorded in the [probe report](evidence/live-detect-o09-transfer-new-controls-probe-20261101.md) and [targeted ledger](evidence/live-detect-targeted-test-ledger-20261101.md).

The automatic split audit found a second repeat-loop risk before staging: five v2 groups yielded only one validation group, and every new control frame was training-only. The O09 exporter now accepts a hash-bound role plan, verifies its expected group set against the actual transitive session/setup components, checks each frame's lighting tag, and carries layout/light tags into the export report. A 12-layout plan was audited before the user is asked to stage: 3 train/3 validation layouts in each of the two contract lighting strata, with four matched-control sequences assigned inside their own groups. The focused planner/export checks pass 7/7 and 6/6; synthetic checks do not count as detector evidence. This moves the split decision and its failure checks ahead of physical staging, preventing a repeat of “collect, export, discover the validation split is unusable.” See the [batch plan](evidence/live-detect-o09-development-layout-lighting-batch-plan-20261101.md).

## Batch capture protocol

1. Prepare offline: choose the hypothesis and a small batch, assign setup/step IDs, list exactly what moves, expected visible inventory, required controls and maximum capture per step. Prepare output paths and receipt checks. Confirm suitable light before requesting staging.
2. Send the whole sequence in advance so the user can prepare. For example: both black/cue present → remove only black → restore black/remove only cue → restore cue. Request a hands-clear confirmation for each actual table change; an advance sequence is not permission to assume a step happened.
3. At each step use the normal Home start and one bounded verifier operation. Save the source/receipt pairs, then Home **End recording session**. Verify camera `/status: Stopped` and Home **Start live session** before any table change or waiting.
4. Before releasing that setup, perform only rapid acquisition checks: required frames exist, hashes/PTS/dimensions/configuration/corners agree, target centres are visible, no hand/overlap invalidates the step, and fixed cloth patches show whether comparison remains plausible. Save a contact sheet. Do not interleave builds, threshold sweeps or detailed label review.
5. If acquisition is invalid, record the failed attempt and resolve it immediately while the setup exists. Lighting or camera drift starts a new setup group; do not salvage a causal claim across it. Equal corners alone do not prove pose stability.
6. At batch end verify stopped/Home, release the user from staging, and process saved data offline: display reviews, freeze labels, run baseline/candidate comparisons, then update one result record, ledger and plan link.

A batch can collect Development controls only. Holdout sessions, split locks, acceptance-contract limits and independent review remain required. Do not open holdout results during tuning or replace physical negatives with synthetic ones.

## Faster offline loop

Use this sequence: **question → cheapest decisive inspection → focused candidate → build → planned replay set → measured decision**. For the current seed-enumeration experiment, test the same saved positives and reviewed physical negatives with the option off/on, preserving the 12-fit budget and every other setting. Inspect recovered centres and false additions, uncertainty, seed coverage and runtime; counts alone are insufficient. A failure should identify the next narrow cause, rather than trigger another broad physical collection.

Keep diagnostic breadth proportional to the question. Use one primary width/configuration for the comparison; add width/trace arms only when needed to explain a result. Reuse completed exact baseline results only when checkout/configuration/execution semantics are genuinely compatible. A diagnostic option must be visible in manifest/report identity. Never infer installed-package identity from a successful build.

Track a live build or replay by its process/session handle until terminal. A quiet interval is not a stalled job. Poll at useful intervals, report meaningful changes, and avoid launching duplicate work after an observation timeout. Check the process tree when necessary. Serialize builds that share outputs, installs, instrumentation, companion actions and camera capture. Offline artifact reads and non-overlapping audits may run concurrently.

## Keep progress easy to see

Each work cycle should produce a runnable score, measured candidate, focused check, reviewed evidence or precise blocker. Keep one current status paragraph with tested commit/APK, last concrete result, next executable action and actual device readback. Put detailed hashes in the evidence record, not repeated status prose. Historical device snapshots must remain dated; take a fresh read when device state matters.

For the next batch, record wall-clock capture-window length, user staging/review interruptions, usable versus rejected steps, build/install count, replay reuse count and time to the next measured decision. Use these to identify waste; do not set speed targets that encourage weaker evidence. No arbitrary timing improvement is claimed by this plan.
