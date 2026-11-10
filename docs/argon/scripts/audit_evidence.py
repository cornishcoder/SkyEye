import os
import sys
import json
import hashlib
import time
from pathlib import Path
from collections import defaultdict, Counter
from datetime import datetime, timezone

try:
    from PIL import Image
    HAS_PIL = True
except ImportError:
    HAS_PIL = False

try:
    import cv2
    HAS_CV2 = True
except ImportError:
    HAS_CV2 = False

try:
    import numpy as np
    HAS_NP = True
except ImportError:
    HAS_NP = False

ROOT = Path(r"c:\code\SkyEye")
OUT_DIR = Path(r"C:\Users\richardkeast\.gemini\antigravity\brain\84dc0d15-24fb-4a73-9ecf-d2adee6f9b98\scratch")
OUT_DIR.mkdir(parents=True, exist_ok=True)

SCAN_TARGETS = [
    ROOT / "tests" / "fixtures",
    ROOT / "docs" / "evidence",
    ROOT / "work",
]

PRUNE_DIRS = {
    "android-sdk-11",
    "railbird-pool-1.2.10",
    ".venv",
    "venv",
    "node_modules",
    "bin",
    "obj",
    ".git",
    "__pycache__",
}

IMAGE_EXTS = {".png", ".jpg", ".jpeg", ".bmp", ".webp", ".gray"}
VIDEO_EXTS = {".mp4", ".ts", ".h264", ".mkv", ".mov"}
DATA_EXTS = {".json", ".jsonl", ".csv", ".xml", ".log", ".md", ".txt", ".yaml", ".yml"}

def sha256_file(path: Path) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as f:
        while chunk := f.read(1024 * 1024):
            h.update(chunk)
    return h.hexdigest()

def compute_hashes_and_stats(img: Image.Image):
    gray = img.convert("L")
    # Mean luma
    stat_small = gray.resize((64, 64), Image.Resampling.BILINEAR)
    px = list(stat_small.getdata())
    mean_luma = sum(px) / len(px)

    # dHash (9x8)
    d_img = gray.resize((9, 8), Image.Resampling.BILINEAR)
    d_px = list(d_img.getdata())
    dhash = 0
    for row in range(8):
        row_offset = row * 9
        for col in range(8):
            if d_px[row_offset + col] > d_px[row_offset + col + 1]:
                dhash |= (1 << (row * 8 + col))

    # aHash (8x8)
    a_img = gray.resize((8, 8), Image.Resampling.BILINEAR)
    a_px = list(a_img.getdata())
    avg = sum(a_px) / 64.0
    ahash = 0
    for i, val in enumerate(a_px):
        if val >= avg:
            ahash |= (1 << i)

    return f"{dhash:016x}", f"{ahash:016x}", round(mean_luma, 2)

def classify_image(rel_path: str, width: int, height: int) -> str:
    name_lower = rel_path.lower()
    if any(k in name_lower for k in ["contact", "sheet", "overlay", "guide", "crop", "review", "visual", "comparison", "grid", "zoom", "patch-map", "reference"]):
        if "crop" in name_lower and width < 800 and height < 800:
            return "crop_or_patch"
        return "diagnostic_overlay_or_contact_sheet"
    if any(k in name_lower for k in ["mask", "warped", "rectified", "distance-window", "ClothMask".lower()]):
        return "rectified_or_mask"
    if (width, height) in {(1080, 2400), (2400, 1080), (1344, 2992), (2992, 1344)} or "screen" in name_lower or "home" in name_lower or "hud" in name_lower or "live-start" in name_lower or "candidate-check" in name_lower:
        if (width, height) in {(1920, 1080), (1068, 585)} and "home" not in name_lower and "screen" not in name_lower and "hud" not in name_lower and "live-start" not in name_lower and "candidate-check" not in name_lower:
            pass
        else:
            return "ui_screenshot"
    if (width, height) in {(800, 1600), (1000, 2000), (400, 800), (600, 1200)}:
        return "rectified_or_mask"
    if (width, height) in {(1920, 1080), (1068, 585), (1280, 720)}:
        return "raw_table_frame"
    if width < 600 and height < 600:
        return "crop_or_patch"
    return "other_image"

def inspect_adjacent_json(img_path: Path):
    json_path = img_path.with_suffix(".json")
    if not json_path.exists():
        return None
    try:
        with open(json_path, "r", encoding="utf-8") as f:
            data = json.load(f)
        if not isinstance(data, dict):
            return None
        info = {}
        idx = data.get("indexedFrame") or data
        if isinstance(idx, dict):
            for k in ("frameId", "sourceTimeUs", "sampleId", "pixelSha256"):
                if k in idx:
                    info[k] = idx[k]
            scope = idx.get("scope")
            if isinstance(scope, dict):
                info["timeline"] = scope.get("timeline")
                info["calibration"] = scope.get("calibration")
        if "detectionRequested" in data:
            info["detectionRequested"] = data["detectionRequested"]
        vision = data.get("vision")
        if isinstance(vision, dict):
            info["ballCount"] = vision.get("ballCount") or (len(vision.get("balls", [])) if isinstance(vision.get("balls"), list) else None)
            info["uncertain"] = vision.get("uncertain")
            info["layoutUncertain"] = vision.get("layoutUncertain")
            info["obstructed"] = vision.get("obstructed")
        return info if info else None
    except Exception:
        return None

def main():
    t0 = time.time()
    print(f"Starting SkyEye evidence audit... PIL={HAS_PIL}, CV2={HAS_CV2}")

    ext_counts = Counter()
    ext_bytes = Counter()
    group_counts = defaultdict(lambda: Counter())
    group_bytes = defaultdict(int)

    images = []
    videos = []
    inaccessible = []
    json_summaries = []

    for base in SCAN_TARGETS:
        if not base.exists():
            continue
        for dirpath, dirnames, filenames in os.walk(base):
            dirnames[:] = [d for d in dirnames if d not in PRUNE_DIRS]
            rel_dir = os.path.relpath(dirpath, ROOT).replace("\\", "/")
            top_group = rel_dir.split("/")[0] if "/" in rel_dir else rel_dir
            if top_group == "work":
                parts = rel_dir.split("/")
                top_group = "/".join(parts[:2]) if len(parts) >= 2 else "work"
                if top_group == "work/production" and len(parts) >= 3:
                    top_group = "/".join(parts[:3])
                elif top_group in ("work/o08", "work/o09") and len(parts) >= 3:
                    top_group = "/".join(parts[:3])

            for fn in filenames:
                fp = Path(dirpath) / fn
                rel_path = os.path.relpath(fp, ROOT).replace("\\", "/")
                ext = fp.suffix.lower()
                try:
                    st = fp.stat()
                    sz = st.st_size
                    mtime = datetime.fromtimestamp(st.st_mtime, tz=timezone.utc).isoformat()
                except Exception as e:
                    inaccessible.append({"path": rel_path, "error": str(e)})
                    continue

                ext_counts[ext] += 1
                ext_bytes[ext] += sz
                group_counts[top_group][ext] += 1
                group_bytes[top_group] += sz

                if ext in IMAGE_EXTS:
                    sha = sha256_file(fp)
                    if ext == ".gray":
                        # Check if raw grayscale buffer (e.g. 1920x1080 = 2,073,600 bytes)
                        w, h = (1920, 1080) if sz == 1920 * 1080 else (0, 0)
                        if w > 0 and HAS_PIL and HAS_NP:
                            try:
                                arr = np.fromfile(fp, dtype=np.uint8).reshape((h, w))
                                im = Image.fromarray(arr, mode="L")
                                dhash, ahash, mean_luma = compute_hashes_and_stats(im)
                                images.append({
                                    "path": rel_path,
                                    "group": top_group,
                                    "size": sz,
                                    "mtime": mtime,
                                    "sha256": sha,
                                    "width": w,
                                    "height": h,
                                    "mode": "GRAY8",
                                    "kind": "raw_table_frame",
                                    "dhash": dhash,
                                    "ahash": ahash,
                                    "mean_luma": mean_luma,
                                })
                            except Exception as e:
                                inaccessible.append({"path": rel_path, "error": f"gray decode: {e}"})
                        else:
                            images.append({
                                "path": rel_path,
                                "group": top_group,
                                "size": sz,
                                "mtime": mtime,
                                "sha256": sha,
                                "width": w,
                                "height": h,
                                "mode": "GRAY_RAW",
                                "kind": "raw_table_frame",
                            })
                    else:
                        try:
                            with Image.open(fp) as im:
                                w, h = im.size
                                mode = im.mode
                                dhash, ahash, mean_luma = compute_hashes_and_stats(im)
                            kind = classify_image(rel_path, w, h)
                            adj = inspect_adjacent_json(fp)
                            rec = {
                                "path": rel_path,
                                "group": top_group,
                                "size": sz,
                                "mtime": mtime,
                                "sha256": sha,
                                "width": w,
                                "height": h,
                                "mode": mode,
                                "kind": kind,
                                "dhash": dhash,
                                "ahash": ahash,
                                "mean_luma": mean_luma,
                            }
                            if adj:
                                rec["receipt"] = adj
                            images.append(rec)
                        except Exception as e:
                            inaccessible.append({"path": rel_path, "error": f"image open: {e}"})

                elif ext in VIDEO_EXTS:
                    sha = sha256_file(fp)
                    vrec = {
                        "path": rel_path,
                        "group": top_group,
                        "ext": ext,
                        "size": sz,
                        "mtime": mtime,
                        "sha256": sha,
                    }
                    if HAS_CV2:
                        try:
                            cap = cv2.VideoCapture(str(fp))
                            if cap.isOpened():
                                vw = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
                                vh = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
                                fps = cap.get(cv2.CAP_PROP_FPS)
                                fcnt = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
                                vrec.update({"width": vw, "height": vh, "fps": round(fps, 3), "frame_count": fcnt})
                            else:
                                inaccessible.append({"path": rel_path, "error": "cv2.VideoCapture failed to open"})
                            cap.release()
                        except Exception as e:
                            inaccessible.append({"path": rel_path, "error": f"video probe: {e}"})
                    videos.append(vrec)

    # Deduplication analysis
    by_sha = defaultdict(list)
    for im in images:
        by_sha[im["sha256"]].append(im)

    unique_sha_count = len(by_sha)
    duplicate_sha_groups = {k: [x["path"] for x in v] for k, v in by_sha.items() if len(v) > 1}

    # Unique raw table frames by sha256
    raw_unique = []
    for sha, items in by_sha.items():
        # Prefer item with receipt or shortest path
        items_sorted = sorted(items, key=lambda x: (0 if "receipt" in x else 1, len(x["path"])))
        rep = items_sorted[0]
        if rep.get("kind") == "raw_table_frame":
            raw_unique.append(rep)

    # Perceptual near-duplicate clustering among unique raw table frames (Hamming distance <= 6 on dHash)
    def hamming(h1: str, h2: str) -> int:
        return (int(h1, 16) ^ int(h2, 16)).bit_count()

    visited = set()
    perceptual_clusters = []
    for i, r1 in enumerate(raw_unique):
        if i in visited or "dhash" not in r1:
            continue
        cluster = [r1]
        visited.add(i)
        for j in range(i + 1, len(raw_unique)):
            if j in visited or "dhash" not in raw_unique[j]:
                continue
            if hamming(r1["dhash"], raw_unique[j]["dhash"]) <= 6:
                cluster.append(raw_unique[j])
                visited.add(j)
        perceptual_clusters.append({
            "cluster_id": len(perceptual_clusters) + 1,
            "representative": r1["path"],
            "representative_sha256": r1["sha256"],
            "dhash": r1["dhash"],
            "mean_luma_range": [min(x.get("mean_luma", 0) for x in cluster), max(x.get("mean_luma", 0) for x in cluster)],
            "width": r1["width"],
            "height": r1["height"],
            "unique_sha_members": len(cluster),
            "total_file_copies": sum(len(by_sha[x["sha256"]]) for x in cluster),
            "sample_paths": [x["path"] for x in cluster[:10]],
        })

    # Video deduplication
    vid_by_sha = defaultdict(list)
    for v in videos:
        vid_by_sha[v["sha256"]].append(v)

    summary = {
        "generated_at_utc": datetime.now(timezone.utc).isoformat(),
        "elapsed_seconds": round(time.time() - t0, 2),
        "totals_by_extension": {k: {"count": ext_counts[k], "bytes": ext_bytes[k]} for k in sorted(ext_counts)},
        "totals_by_group": {
            g: {"total_files": sum(group_counts[g].values()), "bytes": group_bytes[g], "extensions": dict(group_counts[g])}
            for g in sorted(group_counts)
        },
        "image_summary": {
            "total_image_files": len(images),
            "unique_image_sha256": unique_sha_count,
            "exact_duplicate_sha256_groups": len(duplicate_sha_groups),
            "counts_by_kind_all_files": dict(Counter(x.get("kind", "unknown") for x in images)),
            "counts_by_kind_unique_sha256": dict(Counter(by_sha[sha][0].get("kind", "unknown") for sha in by_sha)),
            "counts_by_dimensions_unique_sha256": dict(Counter(f"{by_sha[sha][0].get('width')}x{by_sha[sha][0].get('height')}" for sha in by_sha)),
            "unique_raw_table_frames": len(raw_unique),
            "perceptual_scene_clusters_raw_frames": len(perceptual_clusters),
        },
        "video_summary": {
            "total_video_files": len(videos),
            "unique_video_sha256": len(vid_by_sha),
            "videos": videos,
        },
        "inaccessible_files": inaccessible,
        "perceptual_clusters": perceptual_clusters,
        "duplicate_sha_groups_top50": dict(list(sorted(duplicate_sha_groups.items(), key=lambda kv: -len(kv[1])))[:50]),
    }

    out_file = OUT_DIR / "evidence_inventory_summary.json"
    with open(out_file, "w", encoding="utf-8") as f:
        json.dump(summary, f, indent=2)

    full_file = OUT_DIR / "evidence_inventory_all_images.json"
    with open(full_file, "w", encoding="utf-8") as f:
        json.dump(images, f, indent=2)

    print(f"Done in {summary['elapsed_seconds']}s.")
    print(f"Total images: {len(images)} (Unique SHA-256: {unique_sha_count})")
    print(f"Image kinds (all): {summary['image_summary']['counts_by_kind_all_files']}")
    print(f"Image kinds (unique SHA): {summary['image_summary']['counts_by_kind_unique_sha256']}")
    print(f"Image dimensions (unique SHA): {summary['image_summary']['counts_by_dimensions_unique_sha256']}")
    print(f"Unique raw table frames: {len(raw_unique)} -> {len(perceptual_clusters)} perceptual clusters (dHash <= 6)")
    print(f"Total videos: {len(videos)} (Unique SHA-256: {len(vid_by_sha)})")
    print(f"Inaccessible/corrupt files: {len(inaccessible)}")

if __name__ == "__main__":
    main()
