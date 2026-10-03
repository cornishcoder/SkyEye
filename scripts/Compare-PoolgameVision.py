"""Run the upstream vision pipeline in a time-bounded child, retaining intermediate stages."""
import argparse
import json
import subprocess
import sys
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument('--source', type=Path, required=True)
parser.add_argument('--image', type=Path, required=True)
parser.add_argument('--labels', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
parser.add_argument('--worker', action='store_true')
args = parser.parse_args()
args.output.mkdir(parents=True, exist_ok=True)
if not args.worker:
    try:
        run = subprocess.run([sys.executable, __file__, '--worker', '--source', str(args.source), '--image', str(args.image),
                              '--labels', str(args.labels), '--output', str(args.output)], capture_output=True, text=True, timeout=40)
        (args.output / 'upstream.log').write_text(run.stdout + run.stderr, encoding='utf-8')
        if run.returncode: raise RuntimeError(f'Upstream exited {run.returncode}')
    except subprocess.TimeoutExpired:
        (args.output / 'result.json').write_text(json.dumps({'passed': False, 'error': 'Upstream pipeline exceeded 40 seconds; child stopped'}))
    print((args.output / 'result.json').read_text())
else:
    sys.dont_write_bytecode = True
    sys.path.insert(0, str(args.source))
    import cv2 as cv
    import numpy as np
    from poollib.normalize import normalize
    from poollib.detect import detect_balls
    from poollib.categorize import categorize, calculate_color
    result = {'opencv': cv.__version__, 'passed': False, 'stages': []}
    try:
        image = cv.imread(str(args.image))
        labels = json.loads(args.labels.read_text(encoding='utf-8-sig'))
        source = np.float32([[c['x']*image.shape[1], c['y']*image.shape[0]] for c in labels['calibration']])
        target = np.float32([[1000,0],[1000,2000],[0,2000],[0,0]])
        warped = cv.warpPerspective(image, cv.getPerspectiveTransform(source, target), (1000,2000))
        cv.imwrite(str(args.output / 'rectified.jpg'), warped); result['stages'].append('rectification with supplied calibration')
        normalized = normalize(warped); cv.imwrite(str(args.output / 'normalized.jpg'), normalized); result['stages'].append('upstream normalization')
        circles, masked = detect_balls(normalized); cv.imwrite(str(args.output / 'masked.jpg'), masked); result['stages'].append('upstream detection')
        result['circles'] = [] if circles is None else circles[0].tolist()
        result['classifications'] = []
        for x,y,r in [] if circles is None else np.round(circles[0]).astype(int):
            pattern, bgr = categorize(int(x),int(y),masked)
            result['classifications'].append({'x':int(x),'y':int(y),'radius':int(r),'pattern':pattern,'bgr':bgr})
        result['stages'].append('upstream classification'); result['passed'] = True
    except Exception as error:
        result['error'] = str(error)
    (args.output / 'result.json').write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
