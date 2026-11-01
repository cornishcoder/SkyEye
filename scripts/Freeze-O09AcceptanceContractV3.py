#!/usr/bin/env python3
"""Freeze the v3 O09 Development contract from the immutable v2 contract."""

from __future__ import annotations

import copy
import hashlib
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
EVIDENCE = ROOT / "docs" / "evidence"
SOURCE = EVIDENCE / "live-detect-o09-acceptance-contract-v2-20261101.json"
PLAN = EVIDENCE / "live-detect-o09-development-split-plan-20261101.json"
OUTPUT = EVIDENCE / "live-detect-o09-acceptance-contract-v3-20261101.json"
CORPUS_ID = "live-detect-20261101-black-cue-development-v4-o09-reviewed-layouts-v3"


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def main() -> None:
    source_bytes = SOURCE.read_bytes()
    plan_bytes = PLAN.read_bytes()
    source = json.loads(source_bytes)
    plan = json.loads(plan_bytes)
    contract = copy.deepcopy(source)

    contract["contractId"] = "live-detect-phone-acceptance-v3-predeclared-development-split"
    contract["status"] = "criteria-frozen-v3-capture-pending"
    contract["basis"] = (
        "The v2 acceptance gates and 1 November continuation prompt, with a "
        "predeclared Development-only setup-group split. No acceptance criteria "
        "have been lowered or changed."
    )
    versioning = contract["versioning"]
    versioning["supersedes"] = source["contractId"]
    versioning["sourceContractSha256"] = sha256(source_bytes)
    versioning["criteriaChanged"] = False
    versioning["developmentCorpusId"] = CORPUS_ID
    versioning["developmentSplitPlanSha256"] = sha256(plan_bytes)
    versioning["splitChanged"] = True
    versioning["splitChangeReason"] = (
        "v2 had one correlated validation setup group. v3 predeclares 12 new "
        "Development layouts with three planned groups in every role-by-light "
        "cell; the post-capture exporter must still verify transitive session "
        "and setup isolation. Planned coverage is not collected evidence."
    )
    versioning["holdoutExposure"] = (
        "None; no Holdout labels or scores have been opened. v3 planned groups "
        "are Development train/validation only."
    )

    if plan["corpusId"] != CORPUS_ID:
        raise SystemExit(f"Split plan corpus mismatch: {plan['corpusId']}")

    OUTPUT.write_text(
        json.dumps(contract, indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
        newline="\n",
    )
    print(f"Wrote {OUTPUT.relative_to(ROOT)}")
    print(f"sourceContractSha256={sha256(source_bytes)}")
    print(f"developmentSplitPlanSha256={sha256(plan_bytes)}")


if __name__ == "__main__":
    main()
