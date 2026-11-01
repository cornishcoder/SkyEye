#!/usr/bin/env python3
"""Focused integrity checks for the frozen O09 v3 acceptance contract."""

from __future__ import annotations

import hashlib
import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
EVIDENCE = ROOT / "docs" / "evidence"
V2_PATH = EVIDENCE / "live-detect-o09-acceptance-contract-v2-20261101.json"
V3_PATH = EVIDENCE / "live-detect-o09-acceptance-contract-v3-20261101.json"
PLAN_PATH = EVIDENCE / "live-detect-o09-development-split-plan-20261101.json"
EXPECTED_V2_SHA256 = "435af789cb20785286f147c52c4098bc9edb81a30f58e3cc305360ba3e4c1057"


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def criteria(contract: dict) -> dict:
    top_level_metadata = {"contractId", "status", "frozenDate", "basis"}
    version_metadata = {
        "supersedes",
        "sourceContractSha256",
        "criteriaChanged",
        "developmentCorpusId",
        "developmentSplitPlanSha256",
        "splitChanged",
        "splitChangeReason",
        "holdoutExposure",
    }
    projected = {key: value for key, value in contract.items() if key not in top_level_metadata}
    projected["versioning"] = {
        key: value
        for key, value in contract["versioning"].items()
        if key not in version_metadata
    }
    return projected


def main() -> int:
    v2_bytes = V2_PATH.read_bytes()
    v3_bytes = V3_PATH.read_bytes()
    plan_bytes = PLAN_PATH.read_bytes()
    v2 = json.loads(v2_bytes)
    v3 = json.loads(v3_bytes)
    plan = json.loads(plan_bytes)
    checks: list[tuple[str, bool]] = []

    checks.append(("archived v2 bytes match the frozen source hash", sha256(v2_bytes) == EXPECTED_V2_SHA256))
    checks.append(("v3 source hash binds the archived v2 bytes", v3["versioning"]["sourceContractSha256"] == sha256(v2_bytes)))
    checks.append(("all substantive acceptance gates equal v2", criteria(v3) == criteria(v2)))
    checks.append(("v3 identifies v2 as parent and preserves criteria", v3["versioning"]["supersedes"] == v2["contractId"] and v3["versioning"]["criteriaChanged"] is False))
    checks.append(("v3 corpus ID matches the split plan", v3["versioning"]["developmentCorpusId"] == plan["corpusId"]))
    checks.append(("v3 split-plan hash binds exact plan bytes", v3["versioning"]["developmentSplitPlanSha256"] == sha256(plan_bytes)))

    declared_lights = set(v3["supportedConditions"]["lightingStrata"][i]["id"] for i in range(len(v3["supportedConditions"]["lightingStrata"])))
    checks.append(("split-plan strata match contract strata", set(plan["lightingStrata"]) == declared_lights))

    new_groups = [group for group in plan["setupGroups"] if group["layoutId"] is not None]
    cells = {(light, role): 0 for light in declared_lights for role in ("train", "validation")}
    for group in new_groups:
        key = (group["lightingStratum"], group["role"])
        if key in cells:
            cells[key] += 1
    layout_ids = [group["layoutId"] for group in new_groups]
    checks.append(("12 distinct layouts provide three groups per role-light cell", len(new_groups) == 12 and len(set(layout_ids)) == 12 and all(count == 3 for count in cells.values())))

    legacy_groups = [group for group in plan["setupGroups"] if group["layoutId"] is None]
    checks.append(("five legacy control groups remain train-only", len(legacy_groups) == 5 and all(group["role"] == "train" and group["lightingStratum"] is None for group in legacy_groups)))
    checks.append(("Holdout remains unopened and D1/D2/E remain not-run", "no Holdout labels or scores" in v3["versioning"]["holdoutExposure"] and all(v3["gateStatus"][name] == "not-run" for name in ("D1OfflineInventoryAndReadiness", "D2InstalledPhoneStationaryReadiness", "EPhysicalEventsClipsAndCanonicalReview"))))

    failed = 0
    for index, (description, passed) in enumerate(checks, 1):
        print(f"{'PASS' if passed else 'FAIL'} [{index:02d}/{len(checks):02d}] {description}")
        failed += not passed
    print(f"O09-V3-ACCEPTANCE-CONTRACT-BINDING: {len(checks) - failed}/{len(checks)}")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
