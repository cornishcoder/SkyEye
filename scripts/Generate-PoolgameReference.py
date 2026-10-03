"""Export unmodified upstream geometry outputs. Needs Python standard library only."""
import argparse
import hashlib
import json
import subprocess
import sys
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--source", type=Path, required=True)
parser.add_argument("--output", type=Path, default=Path(__file__).resolve().parents[1] / "tests/fixtures/poolgame/geometry-reference.json")
args = parser.parse_args()
sys.dont_write_bytecode = True
sys.path.insert(0, str(args.source))
from poollib.shots_calculations import get_shots, is_shot_blocked, is_edge_possible

class Ball:
    def __init__(self, point): self.point = tuple(point)
    def get_coordinates(self): return self.point

cases = []
for cue, target in [([700, 1400], [400, 800]), ([250, 1700], [750, 500]), ([500, 1500], [550, 1000])]:
    balls = [Ball(cue), Ball(target)]
    shots = []
    for shot in get_shots(*balls):
        target_path, cue_path = shot.get_lines()
        legs = list(target_path.values()) + list(cue_path.values())
        penalty = 8 if len(target_path) > 1 and len(cue_path) > 1 else 4 if len(target_path) > 1 or len(cue_path) > 1 else 1
        shots.append({"targetPath": list(target_path.values()), "cuePath": list(cue_path.values()),
                      "ghost": shot.get_ghost(), "angle": shot.get_angle(), "length": shot.get_length(),
                      "difficulty": ((180 - shot.get_angle()) * 300 + shot.get_length()) * penalty,
                      "valid": not any(is_shot_blocked(leg, balls, 23) for leg in legs) and shot.get_angle() > 120 and all(is_edge_possible(leg) for leg in legs)})
    cases.append({"cue": cue, "target": target, "shots": shots})
source_files = ["poollib/Shot.py", "poollib/shot_init.py", "poollib/shots_calculations.py"]
result = {"sourceRevision": subprocess.check_output(["git", "-C", str(args.source), "rev-parse", "HEAD"], text=True).strip(),
          "sourceHashes": {name: hashlib.sha256((args.source / name).read_bytes()).hexdigest() for name in source_files},
          "coordinateSystem": "portrait 1000x2000 pixels", "ballRadius": 23, "cases": cases}
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print(f"Exported {len(cases)} cases / {sum(len(c['shots']) for c in cases)} original candidates to {args.output}")
