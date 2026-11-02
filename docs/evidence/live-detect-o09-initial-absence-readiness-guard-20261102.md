# Untrusted initial absence readiness guard — 2 November 2026

**Test:** `O09-D2-UNTRUSTED-INITIAL-ABSENCE-READINESS-GUARD`  
**Result:** PASS in `SkyEye.Checks`. The source-level regression is closed; the physical companion has not yet been rebuilt or retested with this guard.

## Failure traced

The current-source physical Layout03 verifier run used a full 16-ball setup with visible cue and black, but its three detections contained only seven reds and seven yellows. `VisionResult.Uncertain` was true while `HasUncertainLayout` was false. `VerificationConsensus` accepted the stable 14-ball list, `TableAnalysisVerification.PublishVerification` treated it as ready, `TableTracker.Verified` confirmed it, and `AppCoordinator.HasVerificationProof` did not inspect final detection uncertainty. `GameObservationAdapter.EstablishBaseline` also considered a clear layout with known colours sufficient to seed `GameState`, where cue and black counts of zero are legal count values.

## Change

- `VerifyAsync` captures a prior table only when it is confirmed, settled, unobstructed, in the same calibration and source timeline, and within the existing review-freshness window. The verifier still resets transient tracking before a new burst.
- A verifier result can be `Ready` when its final detection is certain and unobstructed. A reduced detection with broad `Uncertain=true` can be `Ready` only when it matches that fresh, confirmed prior inventory one-to-one in the same source scope. The evidence records `ReviewedInventoryMatch`; otherwise the result is `Unclear` with `DetectionAmbiguous`, even if motion is `Still` and the reduced consensus is stable.
- The command proof boundary rejects an uncertain final inventory without `ReviewedInventoryMatch`. The canonical baseline adapter sets `Clear=false` for that case, so an uncertain observation cannot seed `GameState` as a starting inventory.
- `VerificationConsensus` still returns stable reduced inventories for offline D1 scoring. The change does not manufacture missing balls, relax detector thresholds, or turn uncertainty into a detector label.

## Targeted checks

The full command `dotnet run --project tests/SkyEye.Checks/SkyEye.Checks.csproj -c Release` exited **0**, with **69/69 named PASS groups**. The new `O09-D2-UNTRUSTED-INITIAL-ABSENCE-READINESS-GUARD` check covers:

1. An initially missing cue and black can have valid motion evidence (`Still`), but remains `Unclear / DetectionAmbiguous / Confirmed=false`; its stable reduced consensus remains available for offline scoring.
2. A fresh explicit correction can establish a reduced black-absent layout; matching verifier evidence remains `Ready` with `ReviewedInventoryMatch=true`, while shot advice stays ineligible.
3. An unexplained black disappearance after a confirmed full inventory remains unclear.
4. `AppCoordinator.HasVerificationProof` rejects a broad-uncertain final sample without review proof and accepts the same sample with the generated review-match proof.

The existing persistent-obstruction, source freshness, coverage and motion checks passed in the same run. The baseline fixture now starts from a full rack; a later changed count remains held for review, and typed A/B inventory acceptance still covers reviewed reduced counts.

Two earlier test attempts exited 1 on assertions made stale by the test edits: one still expected the old fixture's single yellow after switching to a full rack; the next compared the stop result to the prior historical-report object after the new proof cases had replaced it. Both assertions were corrected before the passing run.

## Acceptance status

This is source/check evidence, not physical acceptance. The installed Pixel 11 companion remains APK SHA-256 `03767BB703B500AA51A5004BF2261F68E146489C529B7212FD489EBEFF803549`, built before this readiness guard. The camera remains stopped; do not count the guard as a D2 pass until a source-matched Production build is installed and the staged full-layout verifier is retested. The previous physical run remains valid as **motion stillness 1/1 on current-source edge-template code**, with **1/6** Layout03 attempts passed overall; its `Ready` output is the reproduced safety failure this change addresses.

Broader counts remain: reviewed Development anchors **2/12**, captured staged states **6/24**, additional label-frozen matched states **3/12**, criteria-complete lighting **0/2**, detector inventory acceptance **0/3**, Holdout unopened, D1/D2/E **0/3** proven, D2 supported windows **0/200**, unsafe/source/control windows **0/100**, and **19** planned Development acquisition operations remaining.

**Next named test:** `O09-NORMAL-TRAIN-LAYOUT03-SAFETY-GUARD-PHYSICAL-RETEST`.
