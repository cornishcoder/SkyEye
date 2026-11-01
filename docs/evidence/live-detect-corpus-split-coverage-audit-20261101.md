# Live Detect corpus and split coverage audit

**Target:** `CORPUS-SPLIT-COVERAGE-AUDIT`  
**Result:** corpus identity/integrity **PASS 6/6 checks**; acceptance coverage **incomplete**; physical D1/D2/E remain **0/3 proven**.  
**Audit date:** 2026-11-01  
**Scoring checkout:** `6b4c8c87a6d744fc40e4c6751d028cb5d8fb526b`  
**Scope:** read-only inspection of the frozen `corpus-v4` manifest, split lock, labels, source files, and acceptance contract. No detector replay, build, device action, or table capture was performed.

## Integrity checks

The manifest hash matches the split lock, the unique sequence IDs match, all source PNG/index/label hashes match their locked values, and every labelled frame's pixel hash agrees with both its source image and lock entry.

| Check | Result |
| --- | ---: |
| Manifest SHA-256 equals split-lock binding | PASS |
| Manifest sequence IDs equal split-lock IDs | PASS |
| Source image hashes | PASS 10/10 |
| Frame-index hashes | PASS 10/10 |
| Label-file hashes | PASS 10/10 |
| Labelled-frame pixel hash binds source and split lock | PASS 10/10 |

Inputs were bound by these hashes:

- `manifest.json`: `7AD75F41C3F452D7FD71685C363EA334EB129D2FAFC2D557EA27998A07EDB277`
- `split-lock.json`: `49B3C3BCC01FDEA4D0CFC6989E3F70814077E8DB3A762921739A5D941A3BFD2E`
- `acceptance-contract.json`: `1574245E516431EA3087C9202875D4B26C17701F4D424457F722F987EAD47563`

## Coverage readout

The corpus contains **10 sequences and 10 still frames**, each tagged `StillRegression`, with 16 ball/expected-absence labels per frame: **155 visible centres and 5 absence points**. All 10 label files set `reviewed: true`. They are split **Development 10/10, Holdout 0/10**, across **3 tagged setup groups and 10 recording-session IDs**. The frames are still samples; they do not represent Live Detect request windows or repeated verification intervals.

| Acceptance evidence | Required by frozen contract | Present in corpus-v4 | Result |
| --- | --- | --- | --- |
| Independently reviewed spatial layouts | At least 12; 1–2 anchor frames/layout | 10 frames maximum, no explicit `layoutId` field | **Not met / distinct-layout denominator unproven** |
| Development control groups | 2–3 matched groups, positive and negative on the same split | 3 tagged groups; all Development | **Group count met; matched-condition coverage not proven by this audit** |
| Lighting strata | Normal pool-room and warm-dim-playable; measure illuminance and record source/exposure/glare | 0 sequences assigned a lighting stratum; no measured illuminance field | **0/2 strata evidenced** |
| Held-out layouts and sessions | At least 5 layouts in at least 2 separate sessions, with matched positive/negative controls in each stratum | 0 Holdout layouts; 0 Holdout sessions | **0/5 layouts, 0/2 sessions** |
| Request windows | 100 non-overlapping requests per stratum | 0 request-window records in this still-regression corpus | **0/200** |
| Unsafe/source windows | At least 100 held-out windows across the listed unsafe categories | 0 held-out windows in this corpus | **0/100** |
| Unchanged-table soak | At least 30 seconds per representative layout, retaining every request/result/source interval | No soak interval or request series in this corpus | **0 recorded** |
| Centre-label precision | P95 ≤0.25 labelled radius = 0.00675 table units; reviewed label uncertainty ≤0.00675 | No numeric radius or numeric uncertainty fields in ball labels | **Not scoreable against contract** |
| Physical event/clip scenarios | E01–E07 with canonical event, clip, and review evidence | No event/clip windows in this corpus | **E remains unproven** |

The maximum of ten one-frame samples is below the contract's 12-layout minimum even if every frame were a different layout; the manifest does not assign layout identities, so the exact distinct-layout count cannot be claimed. All samples are on the Development side. The three setup-group tags establish grouping metadata, not proof of a lighting-stratum match. Existing review notes describe approximate image-centre estimates, and some notes explicitly give rough source-pixel uncertainty; the labels do not establish the contract's numeric uncertainty bound or physical cloth-plane accuracy.

## Decision and next evidence

This result audits package integrity and denominators. It does not score a detector, certify any acceptance gate, or change prior detector metrics. Keep software at **3/3 suites PASS carried forward**, focused motion at **20/20 carried forward**, production inventory at **0/3**, and physical **D1/D2/E at 0/3 proven**.

Do not repeat this unchanged corpus audit or replay these stills just to increase a count. The next useful physical target is a new, reviewed, stable-light matched control batch with explicit layout, session, lighting, and uncertainty metadata. The current table-light condition was reported as darker, so it does not satisfy that prerequisite. The corpus then needs additional layouts and session-separated Holdout data, followed by the contract's request-window, unsafe-window, soak, installed-phone, and physical event/clip evidence. Keep capture stopped until a bounded collection step is actually ready.

## Audit execution notes

The first summary attempt used unsupported PowerShell `Sort-Object -Join` syntax. Two subsequent read-only summaries assumed incorrect manifest/lock property names (`sampleId` and `samples`); they stopped before hashing sample files. The field names were checked and the corrected audit above completed all six integrity checks. These are orchestration/script errors, not detector failures, and they changed no files or device state. The audit was run once successfully after correcting the schema assumptions; no detector or device test was repeated.
