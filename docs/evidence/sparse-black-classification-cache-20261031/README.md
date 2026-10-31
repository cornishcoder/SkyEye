# Sparse-black existing-ball classification cache

This focused optimization caches whether an already-detected black ball is present while `AddSparseBlackBody` scans ranked dark seeds. Rejected seeds do not mutate `circles`, and a successful addition returns immediately, so the original check can be evaluated lazily once at its existing point in the loop. Candidate order, pocket exclusion, fit gates, classification, and audit ordering are unchanged.

## Output parity

Six black-present frames and six empty-pocket frames were run through the width-1000 `PocketArcProbeOnly` path on the Pixel 11, before and after the cache. Each source hash matched. A local comparison confirmed exact equality of all twelve `width1000.analysis` JSON objects, including counts, centres, uncertainty/obstruction state and candidate audits; ordered `(stage, kind, index, decision)` tuples also matched 12/12.

- Before, black-present: `blackcache-baseline/scbc-base-p-1365-210833` through `scbc-base-p-1736-210933`.
- Before, empty-pocket: `blackcache-baseline/scbc-base-n-978-210946` through `scbc-base-n-1198-211039`.
- After, black-present: `blackcache-after/scbc-after-p6-211256`.
- After, empty-pocket: `blackcache-after/scbc-after-n6-211335`.

The reports and manifests are under `work/production/evidence/local-raw-proposal-gates-20261031/`. Positive and negative sets are separate, unmatched lighting/session evidence with replay settings that do not match the capture's primary profile. This is an exact-output and hot-stage check, not a detector-accuracy acceptance run; it did not recover the missing pocket black.

## Timing and limitation

The per-frame pre-change runs reported `sparseBlackFitAndClassifyMs` means of 1,746.52 ms for the six positive frames and 1,715.81 ms for the six controls. The post-change six-frame batches reported means of 283.53 ms and 327.50 ms respectively (83.8% and 80.9% lower than those pre-change means). The old run schema exposed only a batch aggregate; the pre-change values required separate one-frame instrumentation invocations, while the after values came from a single six-frame batch per set. Startup/JIT and batch warmth are therefore not matched, so treat the size of the timing gain as preliminary. A repeated paired batch with per-frame stage timings is needed before claiming a stable reduction.

The Release Android replay build succeeded with zero errors and 23 existing warnings. The mounted Pixel 10 camera remained stopped; no Cast/TV action occurred.
