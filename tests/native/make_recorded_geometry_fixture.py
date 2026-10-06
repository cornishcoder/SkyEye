"""Generate a controlled historical-geometry video from the labelled table still.

Requires the existing OpenCV Python environment and ffmpeg. Outputs only to work/.
Movement and light/obstruction changes are synthetic, not new camera recordings.
"""
import cv2
import numpy as np
import pathlib
import subprocess
import hashlib
import json

root = pathlib.Path(__file__).resolve().parents[2]
image_path = root / 'tests/fixtures/poolgame/pixel-racked-dim.jpg'
base = cv2.imread(str(image_path))
h, w = base.shape[:2]
moved = cv2.warpAffine(base, np.float32([[1, 0, 40], [0, 1, 20]]), (w, h), borderValue=(60, 60, 60))
dim = np.clip(moved.astype(np.float32) * .65, 0, 255).astype(np.uint8)
covered = dim.copy()
cv2.rectangle(covered, (0, 0), (300, 300), (70, 70, 70), -1)
cases = [('original', base), ('moved', moved), ('dim', dim), ('covered', covered), ('recovered', dim)]
command = ['ffmpeg', '-hide_banner', '-loglevel', 'error', '-y']
for name, frame in cases:
    path = root / 'work' / f't08-{name}.png'
    cv2.imwrite(str(path), frame)
    command += ['-loop', '1', '-framerate', '10', '-t', '4', '-i', str(path)]
output = root / 'work/t08-geometry-fixture.mp4'
command += ['-filter_complex', '[0:v][1:v][2:v][3:v][4:v]concat=n=5:v=1:a=0[v]', '-map', '[v]',
            '-c:v', 'libx264', '-preset', 'fast', '-crf', '18', '-pix_fmt', 'yuv420p', '-g', '20', '-bf', '0', '-movflags', '+faststart', str(output)]
subprocess.run(command, check=True)
manifest = {'source': 'tests/fixtures/poolgame/pixel-racked-dim.jpg', 'sourceSha256': hashlib.sha256(image_path.read_bytes()).hexdigest().upper(),
            'outputSha256': hashlib.sha256(output.read_bytes()).hexdigest().upper(), 'width': w, 'height': h, 'fps': 10,
            'keyframeIntervalSeconds': 2, 'segments': [{'name': name, 'startSeconds': i * 4, 'endSeconds': (i + 1) * 4} for i, (name, _) in enumerate(cases)],
            'changes': {'movementPixels': [40, 20], 'dimMultiplier': .65, 'coveredRectanglePixels': [0, 0, 300, 300]},
            'meaning': 'Controlled fixture derived from one labelled still, not evidence of general moving-camera or dim-light accuracy.'}
(root / 'work/t08-fixture-manifest.json').write_text(json.dumps(manifest, indent=2))
