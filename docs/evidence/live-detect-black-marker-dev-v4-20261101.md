# Frozen black-marker feature on corpus-v4 Development — 1 November 2026

Target `BLACK-MARKER-CUE-FROZEN-DEV`: the whole-frame bright-circle/dark-neutral-annulus feature was frozen before applying it to all ten reviewed corpus-v4 frames. Predeclaration SHA256 `20277fff900cb4b2e3ed7d400cb2965a36b7a3b80c7cafe0ff3691dda7a9bc1e`. Script SHA256 `b214fe4313b701b0b587951e7a513de8e1801ede71977e27024d8dfb673c9802`. Result SHA256 `88663d04cd5125f0a8f1cdf3dbdf38ccff1396bda8fe22df353acb2ff088f4cb`.

| Measure | Result |
| --- | ---: |
| Reviewed black-present frames | 5/7 found within20 source px |
| Reviewed black-absent frames | 3/3 correctly produced none near expected point |
| All per-frame black-point cases | 8/10 |
| Collisions with other reviewed visible ball labels | 0 |
| False candidates in whole image | The whole-frame component stage emits 7–26 bright circular components before annulus filtering; selected dark-annulus outputs were 0 or1 per frame |

Both false negatives, frames477 and697, contain no qualifying bright circular component within40 source px of the reviewed black location using the fixed feature. There are ten distinct frame entries, but split-lock shows **10 Development, 0 Holdout**. Thresholds were previously explored on four of these frames, so this is frozen Development evidence only, not independent held-out acceptance. The four original pairs also have distinct source timeline IDs; no matched-light claim follows. The black detector inventory/readiness gate remains failed. Marker-feature performance cannot replace centre-accuracy or false-ready denominators.

Artifacts: run the unchanged script with `--labels work/o08/live-detect-black-pair-v1/corpus-v4 --output work/o08/live-detect-black-pair-v1/physical-controls/stage-replays/black-marker-dev-v4-20261101/results.json`. Full per-frame proposals, hashes and label-collision records are in that JSON. No production code or thresholds changed. Current scorecard remains software3/3 suites, focused motion20/20, production inventory0/3, physical gates0/3 proven.

Next valid decision requires a newly reviewed setup/recording group not used for feature design, with both present and empty controls under declared lighting strata. Freeze candidate and criteria before seeing those outputs; do not tune on corpus-v4 misses.
