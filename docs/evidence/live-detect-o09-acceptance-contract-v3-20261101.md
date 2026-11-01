# O09 acceptance contract v3 binding — 1 November 2026

## Result

`O09-V3-ACCEPTANCE-CONTRACT-BINDING` **PASS 10/10**. The v3 Development contract preserves the substantive v2 gates and binds the predeclared corpus and split-plan bytes. This is contract-integrity evidence only; no detector evaluation, lighting collection, Holdout access, or physical acceptance occurred.

Run:

```powershell
python scripts/Freeze-O09AcceptanceContractV3.py
python tests/O09AcceptanceContractV3.Checks.py
```

The generator starts from the archived v2 bytes and changes only version/split metadata. The check compares all acceptance criteria and unchanged versioning protocol rules as structured JSON, verifies both SHA-256 links, checks the corpus/lighting/role plan, and confirms the Holdout and gate states.

## Immutable inputs and bindings

| Artifact | SHA-256 | Meaning |
| --- | --- | --- |
| [v2 source contract](live-detect-o09-acceptance-contract-v2-20261101.json) | `435af789cb20785286f147c52c4098bc9edb81a30f58e3cc305360ba3e4c1057` | Exact-byte archive of the previously frozen criteria |
| [v3 acceptance contract](live-detect-o09-acceptance-contract-v3-20261101.json) | `462bbed9c4f10d8ef5e139f4fb95380cd270ca19581deffc52cdc0ec8daef6d9` | Criteria-unchanged contract; capture pending |
| [Development split plan](live-detect-o09-development-split-plan-20261101.json) | `6c0d224aca32e5781c03c3a3452560aac5fd13a25311cd2ea241791ffe7f34d834` | 17 declared groups, including 12 new layouts |

The v3 contract ID is `live-detect-phone-acceptance-v3-predeclared-development-split`; it supersedes `live-detect-phone-acceptance-v2-dev-expansion`. `criteriaChanged` is false and `splitChanged` is true. The v3 corpus is `live-detect-20261101-black-cue-development-v4-o09-reviewed-layouts-v3`. The five existing groups remain train-only; the 12 planned layouts provide three declared groups in each of the train/validation × normal/warm cells. The exporter must still validate actual transitive session/setup isolation after capture.

## Check results and attempt history

| Check | Result |
| --- | --- |
| Archived v2 bytes match the frozen source hash | PASS |
| v3 source hash binds the archived v2 bytes | PASS |
| Substantive gates equal v2 | PASS |
| v3 parent and unchanged-criteria metadata | PASS |
| Corpus ID matches the split plan | PASS |
| Split-plan hash binds exact plan bytes | PASS |
| Contract and plan lighting strata agree | PASS |
| Twelve unique planned layouts cover all four cells at 3 groups each | PASS |
| Five legacy groups stay train-only | PASS |
| Holdout unopened and D1/D2/E not-run | PASS |

The initial invocation stopped before assertions because the new Python check file had a missing closing parenthesis (`SyntaxError`); after correction, the focused check passed 10/10. A review then found the first criteria projection omitted the entire `versioning` object, including its frozen change/Holdout rules. The projection was tightened to compare those protocol rules while excluding only fields intentionally changed for v3, and the final focused run passed 10/10. No detector replay was repeated.

## Current acceptance metrics

- Frozen detector probe `O09-TRANSFER-NEW-CONTROLS-PROBE`: **FAIL 0/7** complete layouts; 109 GT, 36 TP, 664 capped FP, 73 FN, 5.14% precision, 33.03% recall, and zero black/cue matches.
- Software suite count: **3/3 PASS, carried forward**; focused motion checks **20/20 PASS, carried forward**. They were not rerun for this metadata-only change.
- Detector inventory: **0/3**. Holdout: **unopened**. Measured lighting strata: **0**. Physical D1/D2/E: **0/3 proven**.
- The user reports the table is off and the light stops play tonight. Capture remains stopped; the 12 layouts and 24-frame minimum are planned, not collected.

The next physical target remains `O09-DEVELOPMENT-LAYOUT-LIGHTING-EXPANSION`, when both planned lighting strata are playable. Then freeze/review the captured Development corpus and run the exporter’s post-capture group-integrity check before another candidate evaluation. First play testing still needs Development candidate evidence, Holdout plus D1, and installed-phone D2; event/clip gate E remains separate. Elapsed time is not estimable until playable light and collection availability are known.
