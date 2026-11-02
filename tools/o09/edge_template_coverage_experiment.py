#!/usr/bin/env python3
"""Development-only clipped-template experiment for saved Layout03 evidence.

This reproduces the worker's 400x800 perspective warp, then compares the existing
full-window bounds rule with a fixed-common-sample clipped patch candidate. It
does not change or call the production verifier. Synthetic edge controls check
stationary photometric change, known translation, in-bounds parity, and unknown
low-texture behavior.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path

import cv2
import numpy as np


def sha(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def common_samples(width: int, height: int, cx: int, cy: int, radius: int, reach: int):
    offsets = [(x, y) for y in range(-radius, radius + 1, 2)
               for x in range(-radius, radius + 1, 2) if x * x + y * y <= radius * radius]
    valid = [(x, y) for x, y in offsets
             if 0 <= cx + x - reach and cx + x + reach < width
             and 0 <= cy + y - reach and cy + y + reach < height]
    return offsets, valid


def compare(before: np.ndarray, after: np.ndarray, cx: int, cy: int, radius: int,
            *, clipped: bool, reach: int = 4):
    height, width, channels = before.shape
    if after.shape != before.shape or channels != 3 or radius < 5 or radius > 60:
        return None
    if not clipped and (cx - radius - reach < 0 or cy - radius - reach < 0
                        or cx + radius + reach >= width or cy + radius + reach >= height):
        return None
    offsets, usable = common_samples(width, height, cx, cy, radius, reach)
    samples = usable if clipped else offsets
    if clipped and (len(samples) < 30 or len(samples) / len(offsets) < .25):
        return None
    scores = np.full((2 * reach + 1, 2 * reach + 1), np.inf, dtype=np.float64)
    best = math.inf
    bx = by = 0
    for dy in range(-reach, reach + 1):
        for dx in range(-reach, reach + 1):
            xx = np.asarray([cx + x for x, _ in samples], dtype=np.int32)
            yy = np.asarray([cy + y for _, y in samples], dtype=np.int32)
            a = before[yy, xx, :].astype(np.float64)
            b = after[yy + dy, xx + dx, :].astype(np.float64)
            error = variance = 0.0
            for channel in range(3):
                aa = a[:, channel]
                bb = b[:, channel]
                sum_a, sum_b = aa.sum(), bb.sum()
                square_a, square_b = aa @ aa, bb @ bb
                cross = aa @ bb
                count = len(samples)
                var_a = max(0.0, square_a - sum_a * sum_a / count)
                var_b = max(0.0, square_b - sum_b * sum_b / count)
                covariance = cross - sum_a * sum_b / count
                gain = covariance / var_a if var_a > 1 else 1.0
                channel_variance = var_a + var_b
                variance += channel_variance
                if not math.isfinite(gain) or gain < .5 or gain > 2:
                    error += channel_variance
                else:
                    error += max(0.0, var_b - 2 * gain * covariance + gain * gain * var_a)
            if variance / (6 * len(samples)) < 4:
                return None
            score = max(0.0, error) / variance
            scores[dx + reach, dy + reach] = score
            if score < best:
                best, bx, by = score, dx, dy
    if best > .05 or abs(bx) == reach or abs(by) == reach:
        return None
    ix, iy = bx + reach, by + reach
    if (scores[iy, ix - 1] + scores[iy, ix + 1] - 2 * best < .0001
            or scores[iy - 1, ix] + scores[iy + 1, ix] - 2 * best < .0001):
        return None

    def offset(left: float, centre: float, right: float) -> float:
        denominator = left - 2 * centre + right
        return max(-.5, min(.5, .5 * (left - right) / denominator)) if denominator > .00001 else 0.0

    return {"dx": bx + offset(scores[iy, ix - 1], best, scores[iy, ix + 1]),
            "dy": by + offset(scores[iy - 1, ix], best, scores[iy + 1, ix]),
            "score": float(best), "samples": len(samples),
            "sampleFraction": len(samples) / len(offsets)}


def synthetic_controls():
    width, height, radius = 96, 72, 18
    cloth = np.empty((height, width, 3), dtype=np.uint8)
    cloth[:] = [82, 94, 101]

    def frame(cx: int, low_texture: bool = False):
        image = cloth.copy()
        yy, xx = np.mgrid[0:height, 0:width]
        inside = (xx - cx) ** 2 + (yy - 37) ** 2 <= radius ** 2
        if low_texture:
            image[inside] = [110, 110, 110]
        else:
            ball = np.empty((height, width, 3), dtype=np.int16)
            ball[:] = [126, 64, 47]
            local_x, local_y = xx - cx, yy - 37
            seed = local_x * 17 + local_y * 31 + (local_x * local_y * 7) % 23
            texture = np.stack([((seed + channel * 13) % 53) - 26 for channel in range(3)], axis=2)
            ball += texture // 2
            rings = (local_x ** 2 + local_y ** 2) % 9
            ball += (rings[..., None] < 3) * np.array([9, 13, 17], dtype=np.int16)
            image[inside] = np.clip(ball[inside], 0, 255).astype(np.uint8)
        return image

    before = frame(7)
    still_light = np.clip(before.astype(np.float32) * np.array([1.08, .94, 1.04])
                          + np.array([3, 8, 4]), 0, 255).astype(np.uint8)
    moved = frame(9)
    blank = frame(7, low_texture=True)
    still = compare(before, still_light, 7, 37, radius, clipped=True)
    motion = compare(before, moved, 7, 37, radius, clipped=True)
    unknown = compare(blank, blank, 7, 37, radius, clipped=True)
    # For a safely interior body both methods use the same circular samples.
    interior_before, interior_after = frame(48), frame(49)
    interior_full = compare(interior_before, interior_after, 48, 37, radius, clipped=False)
    interior_clipped = compare(interior_before, interior_after, 48, 37, radius, clipped=True)
    parity = bool(interior_full is not None and interior_clipped is not None
                  and abs(interior_full["dx"] - interior_clipped["dx"]) < .01
                  and abs(interior_full["dy"] - interior_clipped["dy"]) < .01)
    passed = bool(still is not None and math.hypot(still["dx"], still["dy"]) < 1.0
                  and motion is not None and motion["dx"] > 1.0
                  and unknown is None and parity)
    return {"stationaryPhotometricChange": still, "knownEdgeTranslation": motion,
            "lowTexture": unknown, "inBoundsParity": parity,
            "pass": passed}


def find_frame_png(operation_dir: Path, frame_id: int) -> Path:
    matches = [p for p in operation_dir.rglob(f"{frame_id}.png") if p.is_file()]
    if len(matches) != 1:
        raise ValueError(f"expected one raw PNG for frame {frame_id} under {operation_dir}, found {len(matches)}")
    return matches[0]


def find_receipt(operation_dir: Path, frame_id: int) -> Path:
    matches = [p for p in operation_dir.rglob(f"{frame_id}.json") if p.is_file()]
    if len(matches) != 1:
        raise ValueError(f"expected one raw receipt for frame {frame_id} under {operation_dir}, found {len(matches)}")
    return matches[0]


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", type=Path, default=Path.cwd())
    args = parser.parse_args()
    root = args.root.resolve()
    evidence = root / "work/o09/development-v3"
    review_dir = evidence / "reviews/layout03-current-source-baseline-v1"
    trace_path = review_dir / "verifier-failure-trace.json"
    trace = json.loads(trace_path.read_text(encoding="utf-8"))
    results = []
    for operation in trace["operations"]:
        operation_id = operation["operationId"]
        operation_dir = evidence / "raw/normal-train-layout-03" / f"operation-{operation_id.replace('-', '')}"
        rows = sorted(operation["frames"], key=lambda f: f["sourceTimeUs"])
        images = []
        receipt_hashes = []
        corners = None
        for row in rows:
            png = find_frame_png(operation_dir, row["frameId"])
            receipt_path = find_receipt(operation_dir, row["frameId"])
            receipt = json.loads(receipt_path.read_text(encoding="utf-8"))
            expected = receipt["indexedFrame"]["pixelSha256"]
            actual = sha(png)
            if actual != expected:
                raise ValueError(f"source pixel hash mismatch for frame {row['frameId']}")
            dimensions = receipt["indexedFrame"]["width"], receipt["indexedFrame"]["height"]
            if dimensions != (1920, 1080):
                raise ValueError(f"unexpected source size for frame {row['frameId']}: {dimensions}")
            frame_corners = receipt["calibrationCorners"]
            if corners is None:
                corners = frame_corners
            elif frame_corners != corners:
                raise ValueError(f"calibration corners changed in operation {operation_id}")
            image = cv2.imread(str(png), cv2.IMREAD_COLOR)
            if image is None:
                raise ValueError(f"cannot decode {png}")
            source = np.asarray([[c["x"] * 1920, c["y"] * 1080] for c in corners], dtype=np.float32)
            target = np.asarray([[400, 0], [400, 800], [0, 800], [0, 0]], dtype=np.float32)
            transform = cv2.getPerspectiveTransform(source, target)
            images.append(cv2.warpPerspective(image, transform, (400, 800)))
            receipt_hashes.append({"frameId": row["frameId"], "pixelSha256": actual,
                                   "receiptSha256": sha(receipt_path)})

        anchor = images[0]
        bodies = rows[0]["detectedCircles"]
        if len(bodies) != 14:
            raise ValueError(f"expected the reviewed 14-body 7R/7Y anchor, got {len(bodies)}")
        frame_results = []
        for index in range(1, len(rows)):
            current_row, current = rows[index], images[index]
            previous = images[index - 1]
            regions = list(bodies)
            for candidate in current_row["detectedCircles"]:
                if not any(math.hypot(body["motionCentreX"] - candidate["motionCentreX"],
                                      body["motionCentreY"] - candidate["motionCentreY"])
                           < min(body["templateRadius"], candidate["templateRadius"]) * .5
                           for body in regions):
                    regions.append(candidate)
            candidate_supported = 0
            supported_edge_ids = []
            candidate_max_displacement = 0.0
            for body in regions:
                x, y, radius = body["motionCentreX"], body["motionCentreY"], body["templateRadius"]
                anchor_match = compare(anchor, current, x, y, radius, clipped=True)
                previous_match = compare(previous, current, x, y, radius, clipped=True)
                full = body["fullSearchFitsFrame"]
                if anchor_match is not None and previous_match is not None:
                    candidate_supported += 1
                    if not full:
                        supported_edge_ids.append(body["ballId"])
                    candidate_max_displacement = max(candidate_max_displacement,
                        math.hypot(anchor_match["dx"], anchor_match["dy"]),
                        math.hypot(previous_match["dx"], previous_match["dy"]))
            frame_results.append({"frameId": current_row["frameId"],
                                  "existingSupportedBodies": current_row["supportedBodies"],
                                  "existingCoverageValid": current_row["coverageValid"],
                                  "candidateSupportedBodies": candidate_supported,
                                  "candidateCoverageValid": candidate_supported == len(regions),
                                  "regionCount": len(regions),
                                  "edgeTemplateIdsSupported": sorted(set(supported_edge_ids)),
                                  "candidateMaximumDisplacement": candidate_max_displacement,
                                  "candidateMotionOverOnePixel": candidate_max_displacement > 1.0})
        results.append({"operationId": operation_id, "receiptCount": len(rows),
                        "anchorDetectedBodies": len(bodies), "frames": frame_results,
                        "sourceBindings": receipt_hashes})

    controls = synthetic_controls()
    report = {"schemaVersion": 1,
              "testId": "O09-NORMAL-TRAIN-LAYOUT03-EDGE-TEMPLATE-COVERAGE-EXPERIMENT",
              "mode": "development-only offline candidate comparison; production verifier unchanged",
              "traceSha256": sha(trace_path),
              "workerWarp": {"width": 400, "height": 800, "targetCorners": [[400, 0], [400, 800], [0, 800], [0, 0]], "interpolation": "OpenCV default linear"},
              "candidate": {"method": "fixed common source-pixel sample mask over all +/-4 shifts, clipped to image; same photometric score, curvature, and low-texture rejection as current comparison", "minimumSamples": 30, "minimumFractionOfDiskSamples": .25},
              "syntheticControls": controls,
              "operations": results,
              "aggregate": {"sourceFrames": sum(o["receiptCount"] for o in results),
                            "laterMeasurements": sum(len(o["frames"]) for o in results),
                            "existingCoverageFalse": sum(not f["existingCoverageValid"] for o in results for f in o["frames"]),
                            "candidateCoverageTrue": sum(f["candidateCoverageValid"] for o in results for f in o["frames"]),
                            "laterMeasurements": sum(len(o["frames"]) for o in results),
                            "candidateEdgeBodySupportCount": sum(len(f["edgeTemplateIdsSupported"]) for o in results for f in o["frames"]),
                            "candidatePhysicalPassClaim": False}}
    out = review_dir / "edge-template-coverage-experiment.json"
    out.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"testId": report["testId"], "syntheticControls": controls,
                      "aggregate": report["aggregate"], "output": str(out), "sha256": sha(out)}, indent=2))
    if not controls["pass"]:
        raise SystemExit(1)


if __name__ == "__main__":
    main()
