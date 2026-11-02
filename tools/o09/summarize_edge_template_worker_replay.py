#!/usr/bin/env python3
"""Validate the exact-profile Layout03 clipped-template worker replay batch."""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path


RUNS = ["edgeverc0414088", "edgever20ef43d2", "edgever3a80a2cf", "edgever2d605c22"]


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--report-root", type=Path, default=Path(
        "work/o09/development-v3/reviews/layout03-current-source-baseline-v1/worker-replay-exact-20261102"))
    parser.add_argument("--apk-sha256", default="EDE15D64E101F804B974E4BA7D31EB6C6B80035DAB99385CD9E9C72B4DEDCB6C")
    args = parser.parse_args()
    report_root = args.report_root.resolve()
    runs = []
    failures = []
    for run_id in RUNS:
        path = report_root / f"o08-replay-proof-{run_id}.json"
        if not path.is_file():
            failures.append(f"missing report {path}")
            continue
        report = json.loads(path.read_text(encoding="utf-8"))
        measurements = report["width800"]["measurements"]
        post_anchor = measurements[1:]
        exact = report["completed"] and report["configurationComparison"] == "exact-match"
        still = report["width800"]["allMotionSamplesStill"] and all(
            m["motion"] == 1 and m["reason"] == 0 and m["coverageValid"] for m in post_anchor)
        complete_support = len(post_anchor) == 5 and all(m["supportedBodies"] >= 14 for m in post_anchor)
        max_displacement = max((m["maximumBodyDisplacement"] or 0.0) for m in post_anchor)
        displacement_below_existing_gate = max_displacement <= 1.0
        passed = exact and still and complete_support and displacement_below_existing_gate
        if not passed:
            failures.append(f"{run_id}: exact={exact}, still={still}, support={complete_support}, maxDisplacement={max_displacement}")
        runs.append({"run": run_id, "reportSha256": sha(path), "completed": report["completed"],
                     "configurationComparison": report["configurationComparison"],
                     "width800AllPostAnchorSamplesStill": still,
                     "width1000AllPostAnchorSamplesStill": report["width1000"]["allMotionSamplesStill"],
                     "postAnchorMeasurementCount": len(post_anchor),
                     "supportedBodiesObserved": sorted({m["supportedBodies"] for m in post_anchor}),
                     "maxObservedBodyDisplacement": max_displacement,
                     "allBodiesSupportedAndBelowExistingOnePixelMotionGate": complete_support and displacement_below_existing_gate,
                     "pass": passed})

    post_count = sum(r["postAnchorMeasurementCount"] for r in runs)
    report = {"schemaVersion": 1,
              "testId": "O09-NORMAL-TRAIN-LAYOUT03-EDGE-TEMPLATE-WORKER-REPLAY",
              "scope": "Saved Development-only pixel replay through the actual Android verification worker; not live capture or physical acceptance.",
              "apkSha256": args.apk_sha256.lower(),
              "profile": {"width800And1000": True, "sparseBlackRankByRadius": True,
                          "configurationComparisonRequired": "exact-match",
                          "minimumTemplateMotionPixels": 1.0},
              "runs": runs,
              "aggregate": {"runCount": len(runs), "exactProfileRuns": sum(r["configurationComparison"] == "exact-match" for r in runs),
                            "postAnchorMeasurements": post_count,
                            "stillAndCoverageValidMeasurements": sum(r["postAnchorMeasurementCount"] for r in runs if r["pass"]),
                            "maxObservedBodyDisplacement": max((r["maxObservedBodyDisplacement"] for r in runs), default=0.0),
                            "physicalStillnessPasses": 0, "physicalRetestRequired": True},
              "excludedAttempts": [
                  {"directory": "worker-replay-20261102", "reason": "Retained initial worker run that exposed an un-clipped motion-mask bounds exception; excluded from results."},
                  {"directory": "worker-replay-edgefix-20261102", "reason": "Retained run used SparseBlackRankByRadius=false and reported configuration-mismatch; excluded from exact-profile results."}],
              "pass": len(runs) == 4 and not failures and post_count == 20}
    out = report_root / "edge-template-worker-replay-summary.json"
    out.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"testId": report["testId"], "pass": report["pass"],
                      "aggregate": report["aggregate"], "summary": str(out),
                      "summarySha256": sha(out), "failures": failures}, indent=2))
    return 0 if report["pass"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
