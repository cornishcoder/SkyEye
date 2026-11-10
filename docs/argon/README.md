# Argon Forensic Computer-Vision Investigation (`docs/argon/`)

This directory contains all working notes, evidence inventories, perceptual scene clusters, contact sheets, reproducible evaluation scripts, JSON benchmarks, and the final engineering diagnosis produced during the Argon computer-vision review on 10 November 2026.

All Argon-generated repository artefacts are isolated inside [`docs/argon/`](file:///c:/code/SkyEye/docs/argon) so another model or engineer can pick up seamlessly without path collisions.

---

## Directory Contents

### 1. Core Documents
- **[`investigation-handoff-notes.md`](file:///c:/code/SkyEye/docs/argon/investigation-handoff-notes.md)** — Complete living handoff notes, end-to-end C#/Java pipeline trace with exact line numbers, verified software test suite status (`72/72` test groups passing in `SkyEye.Checks`), and step-by-step continuation instructions.
- **[`skyeye-detection-evidence.md`](file:///c:/code/SkyEye/docs/argon/skyeye-detection-evidence.md)** — Complete forensic evidence inventory (`4,970` project images / `2,707` unique `SHA-256` hashes, `78` raw byte dumps, `134` videos / `125` unique `SHA-256`), `36` perceptual `dHash` scene clusters of raw table frames, colorimetric lighting & camera AE/AWB drift audit, visual inspection findings, historical experiment ledger, and unified offline regression benchmark across `32` human-reviewed ground-truth frames.
- **[`engineering-diagnosis.md`](file:///c:/code/SkyEye/docs/argon/engineering-diagnosis.md)** — Full Sections A–F actionable engineering diagnosis:
  - **A.** Evidence coverage
  - **B.** Current pipeline analysis (strengths & architectural weaknesses)
  - **C.** Failure analysis table (linking failures `F1–F8` to actual images, code paths, probable causes, and supporting/contradictory evidence)
  - **D.** Ranked solutions (top 3 improvements with benefits, risks, dependencies, and offline validation)
  - **E.** Minimal, testable implementation plan
  - **F.** Remaining unknowns

### 2. Visual Contact Sheets (`docs/argon/contact-sheets/`)
- **[`sheet_01_scene_clusters_part1.png`](file:///c:/code/SkyEye/docs/argon/contact-sheets/sheet_01_scene_clusters_part1.png)** — Perceptual Scene Clusters `01–18` (of `36` unique raw table scenes)
- **[`sheet_02_scene_clusters_part2.png`](file:///c:/code/SkyEye/docs/argon/contact-sheets/sheet_02_scene_clusters_part2.png)** — Perceptual Scene Clusters `19–36` (of `36` unique raw table scenes)
- **[`sheet_03_lighting_strata_and_ae_awb_drift.png`](file:///c:/code/SkyEye/docs/argon/contact-sheets/sheet_03_lighting_strata_and_ae_awb_drift.png)** — Lighting Strata (`Daylight`, `All-Lights`, `Lights-Off`, `Mid-Light`, `Ultra-Dark`, `Warm-Dim`, `Normal`) & Within-Sequence Camera AE/AWB Drift Pairs (`2831 vs 542`, `678 vs 455`)
- **[`sheet_04_ball_arrangements_and_controls.png`](file:///c:/code/SkyEye/docs/argon/contact-sheets/sheet_04_ball_arrangements_and_controls.png)** — Physical Ball Arrangements, 4-State Controls & Obstruction Cases
- **[`sheet_05_failure_mechanisms_black_and_cue.png`](file:///c:/code/SkyEye/docs/argon/contact-sheets/sheet_05_failure_mechanisms_black_and_cue.png)** — Stage Masks, Distance Windows & Boundary Sector Rejections
- **[`sheet_06_failure_mechanisms_homography_inventory_verifier.png`](file:///c:/code/SkyEye/docs/argon/contact-sheets/sheet_06_failure_mechanisms_homography_inventory_verifier.png)** — Original-Resolution Failure Crops & Historical Diagnostic Overlays

### 3. Reproducible Audit & Evaluation Scripts (`docs/argon/scripts/`)
- **[`audit_evidence.py`](file:///c:/code/SkyEye/docs/argon/scripts/audit_evidence.py)** — Recursive `SHA-256`, `dHash`/`aHash`, luminance, and OpenCV video probe across `tests/`, `docs/`, and `work/`.
- **[`inspect_and_generate_sheets.py`](file:///c:/code/SkyEye/docs/argon/scripts/inspect_and_generate_sheets.py)** & **[`update_sheet6.py`](file:///c:/code/SkyEye/docs/argon/scripts/update_sheet6.py)** — Builds Contact Sheets `01–06` and computes global + central-cloth RGB/luminance statistics.
- **[`aggregate_regression_corpus.py`](file:///c:/code/SkyEye/docs/argon/scripts/aggregate_regression_corpus.py)** — Aggregates all human-reviewed ground-truth corpora into unified precision, recall, colour accuracy, and ball-diameter centre-error metrics.

### 4. Structured Data Outputs (`docs/argon/data/`)
- **[`evidence_inventory_summary.json`](file:///c:/code/SkyEye/docs/argon/data/evidence_inventory_summary.json)** — Complete summary of file extensions, `SHA-256` deduplication groups, video probe metadata, and the `36` perceptual `dHash` scene clusters.
- **[`inaccessible_and_raw_report.json`](file:///c:/code/SkyEye/docs/argon/data/inaccessible_and_raw_report.json)** — Byte-level inspection of `work/companion-candidates.png` (`adb` text output) and headerless `.gray`, `.bgr`, `.rgb`, `.argb`, and `.ppm` buffers.
- **[`aggregated_regression_benchmark.json`](file:///c:/code/SkyEye/docs/argon/data/aggregated_regression_benchmark.json)** — Corpus-level and per-frame metrics across all `32` human-reviewed ground-truth evaluations.
