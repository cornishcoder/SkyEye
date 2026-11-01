#!/usr/bin/env python3
"""Score the frozen black-marker feature on one reviewed black-absent frame.

This is a Development-only whole-frame false-candidate screen. It does not
score black-centre error because the reviewed inventory contains no black
point, and it is not a production detector acceptance test.
"""
import argparse
import hashlib
import json
from pathlib import Path

import cv2
import numpy as np


FEATURE = {
    "hsvSMax": 100,
    "hsvVMin": 170,
    "areaPx": [20, 1000],
    "circularityMin": 0.45,
    "annulusRadiusPx": [10, 20],
    "annulusMedianSMax": 120,
    "annulusMedianVMax": 80,
    "candidateMatchRadiusPx": 20,
}


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--predeclaration", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()

    predecl = json.loads(args.predeclaration.read_text(encoding="utf-8"))
    base = args.predeclaration.resolve().parents[5]
    script_path = base / predecl["inputs"]["script"]
    image_path = base / predecl["inputs"]["image"]
    labels_path = base / predecl["inputs"]["review"]
    receipt_path = base / predecl["inputs"]["receipt"]
    for path, key in (
        (script_path, "scriptSha256"),
        (image_path, "imageSha256"),
        (labels_path, "reviewSha256"),
        (receipt_path, "receiptSha256"),
    ):
        actual = sha256(path)
        expected = predecl["inputs"][key].lower()
        if actual != expected:
            raise SystemExit(f"input hash mismatch for {path}: expected {expected}, got {actual}")

    if predecl["feature"] != FEATURE:
        raise SystemExit("predeclared feature settings differ from the frozen implementation")

    review = json.loads(labels_path.read_text(encoding="utf-8"))
    receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
    if review["reviewedInventory"].get("black") != 0 or not review["reviewContext"].get("completeVisibleInventoryReviewed"):
        raise SystemExit("review does not establish a complete black-absent visible inventory")
    if review["source"]["pixelSha256"] != receipt["indexedFrame"]["pixelSha256"]:
        raise SystemExit("review and source receipt refer to different decoded pixels")

    image = cv2.imread(str(image_path), cv2.IMREAD_COLOR)
    if image is None:
        raise SystemExit(f"could not decode source image: {image_path}")
    height, width = image.shape[:2]
    if (width, height) != (review["source"]["width"], review["source"]["height"]):
        raise SystemExit(f"image dimensions {(width, height)} do not match reviewed dimensions")

    hsv = cv2.cvtColor(image, cv2.COLOR_BGR2HSV)
    mask = ((hsv[:, :, 1] <= FEATURE["hsvSMax"]) & (hsv[:, :, 2] >= FEATURE["hsvVMin"])).astype(np.uint8) * 255
    count, component_map, stats, centroids = cv2.connectedComponentsWithStats(mask, 8)
    candidates = []
    bright_circular_component_count = 0
    lo, hi = FEATURE["areaPx"]
    rin, rout = FEATURE["annulusRadiusPx"]
    yy, xx = np.ogrid[:height, :width]
    for component_id in range(1, count):
        x, y, w, h, area = map(int, stats[component_id])
        if not lo <= area <= hi:
            continue
        contours, _ = cv2.findContours(
            (component_map[y:y + h, x:x + w] == component_id).astype(np.uint8),
            cv2.RETR_EXTERNAL,
            cv2.CHAIN_APPROX_SIMPLE,
        )
        perimeter = cv2.arcLength(contours[0], True) if contours else 0
        circularity = 4 * np.pi * area / (perimeter * perimeter) if perimeter else 0
        if circularity < FEATURE["circularityMin"]:
            continue
        bright_circular_component_count += 1
        cx, cy = map(float, centroids[component_id])
        radius = np.hypot(xx - cx, yy - cy)
        ring = (radius >= rin) & (radius <= rout)
        ring_s = int(np.median(hsv[:, :, 1][ring]))
        ring_v = int(np.median(hsv[:, :, 2][ring]))
        if ring_s <= FEATURE["annulusMedianSMax"] and ring_v <= FEATURE["annulusMedianVMax"]:
            candidates.append({
                "xPx": round(cx, 2),
                "yPx": round(cy, 2),
                "areaPx": area,
                "circularity": round(float(circularity), 4),
                "annulusMedianS": ring_s,
                "annulusMedianV": ring_v,
            })

    collisions = []
    radius = FEATURE["candidateMatchRadiusPx"]
    for label in review["reviewedCentres"]:
        nearest = min(
            (float(np.hypot(c["xPx"] - label["xPx"], c["yPx"] - label["yPx"])) for c in candidates),
            default=None,
        )
        if nearest is not None and nearest <= radius:
            collisions.append({"id": label["id"], "distancePx": round(nearest, 2)})

    passed = len(candidates) == 0
    result = {
        "schemaVersion": 1,
        "target": predecl["target"],
        "scope": "Development-only whole-frame false-candidate screen; no reviewed black point, no centre-error score, no production acceptance.",
        "frameId": review["source"]["frameId"],
        "sampleId": predecl["sampleId"],
        "split": "Development",
        "expectedBlackCount": 0,
        "reviewedInventory": review["reviewedInventory"],
        "reviewedInventoryLimitations": review["limitations"],
        "feature": FEATURE,
        "imageSha256": sha256(image_path),
        "pixelSha256": receipt["indexedFrame"]["pixelSha256"],
        "reviewSha256": sha256(labels_path),
        "receiptSha256": sha256(receipt_path),
        "brightCircularComponentCount": bright_circular_component_count,
        "selectedCandidateCount": len(candidates),
        "selectedCandidates": candidates,
        "collisionsWithReviewedVisibleBallCentresWithin20Px": collisions,
        "case": "PASS" if passed else "FAIL",
        "passRule": "A complete user-reviewed inventory with no black ball must yield zero whole-frame selected black-marker candidates.",
        "source": {
            "timeline": receipt["indexedFrame"]["scope"]["timeline"],
            "sourceTimeUs": receipt["indexedFrame"]["sourceTimeUs"],
            "calibrationProvenance": receipt.get("calibrationProvenance"),
            "analyzerConfigurationIdentity": receipt.get("analyzerConfigurationIdentity"),
        },
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({
        "target": result["target"],
        "frameId": result["frameId"],
        "brightCircularComponentCount": result["brightCircularComponentCount"],
        "selectedCandidateCount": result["selectedCandidateCount"],
        "visibleBallLabelCollisions": collisions,
        "case": result["case"],
        "output": str(args.output),
    }))
    return 0 if passed else 1


if __name__ == "__main__":
    raise SystemExit(main())
