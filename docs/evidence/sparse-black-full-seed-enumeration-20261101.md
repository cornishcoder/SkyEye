# Full sparse-black seed enumeration — Development result

Candidate commit `13fb7f9`; replay `allseed-positive-174021` on reviewed frames 1691/1726/1764. The opt-in option enumerates all sparse-black components before existing radius ranking; the fit budget remains 12. Debug and Release builds passed with 23 warnings and zero errors. Runner syntax passed.

All three width-800 results remain 15 balls (7 red, 7 yellow, cue; black absent), uncertain. Analysis wall times were 1038.41/855.37/872.02ms; these three correlated frames do not establish a runtime percentile. Frame1691 exposes108 available ball-sized seeds. A nearby seed now reaches fitting in every frame:

| Frame | Seed | Fitted centre | Support | Decision |
| --- | --- | --- | --- | --- |
|1691|(748,1158)|(748.72,1185.92)|18/32|Insufficient boundary support|
|1726|(747,1158)|(748.52,1185.12)|19/32|Insufficient boundary support|
|1764|(747,1158)|(750.12,1185.12)|18/32|Insufficient boundary support|

The configured support requirement remains21/32. This identifies an additional boundary-support failure after enumeration coverage improves. No threshold was relaxed. Candidate is not promoted; physical negatives and broader coverage remain required. Next inspect sector evidence and radius/arc geometry using saved controls before designing a conservative candidate.

APK SHA256: `8437e3ea1030ee547af9c2ae69a03373afca5c341024c4d8c13163956ad2e552` (runner artifact identity; separate installed pull not yet performed). Report SHA256: `fd8fc04006a5f69dffe4ff9fba427f640a1cb61c49fe0384afbb4348baf282d8`. Manifest SHA256: `44c9f9edc767337343b045fd8abfd256e2d559fa22e2bb9638501a2831a5f559`. Artifacts: `work/o08/live-detect-black-pair-v1/physical-controls/stage-replays/seed-enumeration-20261101/allseed-positive-174021`. Predeclaration hash: `c0f61a09d45beefa0f4bcf4b9289498382860dca8b018d401304fecee7859930`.

Fresh camera status after analysis is Stopped/revision63. D1/D2/E remain open. Darker lighting invalidated the pending physical pair; no new capture occurred.
