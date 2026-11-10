import json
from pathlib import Path
import numpy as np

REPO = Path(r"c:\code\SkyEye")
SCRATCH = Path(r"C:\Users\richardkeast\.gemini\antigravity\brain\84dc0d15-24fb-4a73-9ecf-d2adee6f9b98\scratch")

# Standard ball diameter in table-2x1 units:
# Rectified width = 800 px -> 1.0 unit; expectedRadius = 21.5 px -> diameter = 43.0 px
# In table-2x1 coordinates (width=1.0, length=2.0), 1 ball radius = 21.5 / 800 = 0.026875; 1 ball diameter = 43.0 / 800 = 0.05375.
BALL_RADIUS_TABLE_UNITS = 21.5 / 800.0
BALL_DIAMETER_TABLE_UNITS = 43.0 / 800.0

corpora_files = [
    ("O01 Seed Regression (16-ball rack)", "docs/evidence/o01-baseline.json", "Reviewed 16-ball rack"),
    ("O08 Code65 Spread (Frame 978)", "work/o08/frame978-development-v1/device-replays/f978current65v2-o08-export/baseline-development-tolerance-0.010.json", "Reviewed 15-ball spread (black near pocket unlabelled)"),
    ("O08 Pocket/Cushion Controls v4 (10 frames)", "work/o08/live-detect-black-pair-v1/physical-controls/exports/merged-v4-current-065/baseline-development-tolerance-0.010.json", "Reviewed 10-frame lower-left, lower-right, upper-right 4-state controls"),
    ("O09 Transfer New Controls (7 frames)", "work/o09/transfer-new-controls-pixel-baseline-replay-20261102/baseline-development-tolerance-0.010-table2x1.json", "Reviewed 7-frame opposite-cue, mid-cushion & lower-left controls"),
    ("O09 Normal-Train Layout02 (Frame 1085)", "work/o09/layout02-baseline-v1/baseline-current-source-tolerance-0.010.json", "Reviewed 16-ball normal-light spread"),
    ("O09 Normal-Train Layout03 (4 frames)", "work/o09/development-v3/reviews/layout03-current-source-baseline-v1/baseline-current-source-tolerance-0.010.json", "Reviewed 4-state cushion/pocket control (1274, 3360, 395, 382)"),
    ("O09 Normal-Validation Layout03 (4 frames, Post-DupFix)", "work/o09/development-v3/reviews/layout03-post-fix-current-source-20261102/baseline-post-duplicate-v2-tolerance-0.010.json", "Reviewed 4-state validation control (2083, 962, 957, 2263)"),
    ("O09 Normal-Validation Layout03 (4 frames, Pre-DupFix)", "work/o09/development-v3/reviews/layout03-validation-spatial-baseline-current-source-20261102/baseline-current-source-score-ready-tolerance-0.010.json", "Reviewed 4-state validation control before exact-duplicate inventory fix"),
]

summary_rows = []
frame_rows = []

for name, rel_path, desc in corpora_files:
    p = REPO / rel_path
    if not p.exists():
        print("MISSING:", rel_path)
        continue
    data = json.loads(p.read_text(encoding="utf-8"))
    frames = data.get("frames", [])
    tot_vis = sum(f["visibleLabels"] for f in frames)
    tot_det = sum(f["detections"] for f in frames)
    tot_tp  = sum(f["spatialTruePositives"] for f in frames)
    tot_fp  = sum(f["falsePositives"] for f in frames)
    tot_fn  = sum(f["missedVisible"] for f in frames)
    tot_col = sum(f["colourCorrect"] for f in frames)
    unc_cnt = sum(1 for f in frames if f.get("uncertain"))
    complete_cnt = sum(1 for f in frames if f["missedVisible"] == 0 and f["falsePositives"] == 0 and f["colourCorrect"] == f["visibleLabels"])

    all_errors = []
    for f in frames:
        errs = [m["centreError"] for m in f.get("matches", []) if m.get("centreError") is not None]
        all_errors.extend(errs)
        p95_t = f.get("centreErrorP95") or (float(np.percentile(errs, 95)) if errs else 0.0)
        max_t = max(errs) if errs else 0.0
        frame_rows.append({
            "corpus": name,
            "sampleId": f["sampleId"],
            "model": f.get("model", ""),
            "visible": f["visibleLabels"],
            "detected": f["detections"],
            "tp": f["spatialTruePositives"],
            "fp": f["falsePositives"],
            "fn": f["missedVisible"],
            "colourCorrect": f["colourCorrect"],
            "precision": round(f["precision"], 4),
            "recall": round(f["recall"], 4),
            "colourAccOfTP": round(f["colourCorrect"] / max(1, f["spatialTruePositives"]), 4),
            "colourAccOfVisible": round(f["colourCorrect"] / max(1, f["visibleLabels"]), 4),
            "p95_table_units": round(p95_t, 5),
            "p95_rect800_px": round(p95_t * 800.0, 2),
            "p95_ball_diameters": round(p95_t / BALL_DIAMETER_TABLE_UNITS, 3),
            "max_ball_diameters": round(max_t / BALL_DIAMETER_TABLE_UNITS, 3),
            "missedIds": f.get("missedIds", []),
            "extraIds": f.get("extraIds", []),
            "uncertain": f.get("uncertain", False),
        })

    p50_all = float(np.percentile(all_errors, 50)) if all_errors else 0.0
    p95_all = float(np.percentile(all_errors, 95)) if all_errors else 0.0
    max_all = max(all_errors) if all_errors else 0.0

    summary_rows.append({
        "corpus": name,
        "path": rel_path,
        "description": desc,
        "frames": len(frames),
        "complete_frames": complete_cnt,
        "uncertain_frames": unc_cnt,
        "visible_balls": tot_vis,
        "detections": tot_det,
        "tp": tot_tp,
        "fp": tot_fp,
        "fn": tot_fn,
        "colour_correct": tot_col,
        "precision": round(tot_tp / max(1, tot_det), 4),
        "recall": round(tot_tp / max(1, tot_vis), 4),
        "colour_acc_given_tp": round(tot_col / max(1, tot_tp), 4),
        "end_to_end_ball_acc": round(tot_col / max(1, tot_vis), 4),
        "p50_err_diam": round(p50_all / BALL_DIAMETER_TABLE_UNITS, 3),
        "p95_err_diam": round(p95_all / BALL_DIAMETER_TABLE_UNITS, 3),
        "max_err_diam": round(max_all / BALL_DIAMETER_TABLE_UNITS, 3),
        "p95_err_px800": round(p95_all * 800.0, 2),
    })

# Also add O09 Normal-Validation Layout01/02 (frames 1587, 31) and Warm266
val0102 = json.loads((REPO / "work/o09/normal-validation-layout01-02-current-replay-20261102/reviewed-score.json").read_text(encoding="utf-8"))
warm266 = json.loads((REPO / "work/o09/development-v3/warm-batch-20261102/warm266-reviewed-spatial-score.json").read_text(encoding="utf-8"))

out = {
    "ball_diameter_table_units": BALL_DIAMETER_TABLE_UNITS,
    "ball_radius_table_units": BALL_RADIUS_TABLE_UNITS,
    "corpus_summaries": summary_rows,
    "per_frame_details": frame_rows,
    "additional_reviewed_sets": {
        "normal_validation_layout01_02": val0102,
        "warm_train_266_summary": {
            "visible_balls": len(warm266.get("labels", [])),
            "comparisons": warm266.get("comparisons", warm266.get("results", {}))
        }
    }
}

with open(SCRATCH / "aggregated_regression_benchmark.json", "w", encoding="utf-8") as f:
    json.dump(out, f, indent=2)

print(f"Aggregated {len(summary_rows)} standard corpora ({len(frame_rows)} frame evaluations).")
for r in summary_rows:
    print(f"  {r['corpus']}: frames={r['frames']} (complete={r['complete_frames']}), TP={r['tp']}/{r['visible_balls']} (P={r['precision']:.3f}, R={r['recall']:.3f}), Col={r['colour_correct']}/{r['visible_balls']} ({r['end_to_end_ball_acc']:.3f}), p95_err={r['p95_err_diam']:.3f} diam ({r['p95_err_px800']} px)")
