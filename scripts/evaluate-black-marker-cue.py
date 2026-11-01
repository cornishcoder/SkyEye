import argparse, hashlib, json
from pathlib import Path
import cv2
import numpy as np

FRAMES=(678,844,375,461)
VISIBLE={678,375}
SAT_MAX=100
VALUE_MIN=170
ROI_RADIUS_PX=20
AREA_MIN=20
AREA_MAX=1000
CIRCULARITY_MIN=0.45

def source_point(corners, table):
    a=[]; b=[]
    for q,t in zip(corners,((0,0),(2,0),(2,1),(0,1))):
        x,y=q['x'],q['y']; u,v=t
        a.extend(((x,y,1,0,0,0,-u*x,-u*y),(0,0,0,x,y,1,-v*x,-v*y)))
        b.extend((u,v))
    h=np.r_[np.linalg.solve(a,b),1].reshape(3,3)
    p=np.linalg.inv(h)@np.array((table['x'],table['y'],1)); p/=p[2]
    return float(p[0]*1920),float(p[1]*1080)

def main():
    ap=argparse.ArgumentParser()
    ap.add_argument('--root',default='work/o08/live-detect-black-pair-v1/physical-controls/replay-inputs/upper-right-development-v4')
    ap.add_argument('--labels',default='work/o08/live-detect-black-pair-v1/corpus-v3')
    ap.add_argument('--output',default='work/live-detect-scorecard/black-marker-cue.json')
    args=ap.parse_args(); root=Path(args.root); labels=Path(args.labels); results=[]
    for frame in FRAMES:
        receipt=json.loads((root/f'{frame}.json').read_text())
        if receipt['indexedFrame']['frameId']!=frame or receipt['indexedFrame']['width']!=1920 or receipt['indexedFrame']['height']!=1080:
            raise ValueError(f'Unexpected receipt dimensions/frame {frame}')
        labeldoc=json.loads((labels/str(frame)/'labels.json').read_text())
        if not labeldoc.get('reviewed'): raise ValueError(f'Frame {frame} labels are not reviewed')
        label=next(x for x in labeldoc['frames'][0]['balls'] if x['colour']=='black')
        expected='Visible' if frame in VISIBLE else 'Absent'
        if label['visibility']!=expected: raise ValueError(f'Frame {frame} expected {expected}, got {label["visibility"]}')
        px,py=source_point(receipt['calibrationCorners'],label)
        image=cv2.imread(str(root/f'{frame}.png'))
        hsv=cv2.cvtColor(image,cv2.COLOR_BGR2HSV)
        mask=((hsv[:,:,1]<=SAT_MAX)&(hsv[:,:,2]>=VALUE_MIN)).astype(np.uint8)*255
        yy,xx=np.ogrid[:1080,:1920]
        mask[(xx-px)**2+(yy-py)**2>ROI_RADIUS_PX**2]=0
        count,component,stats,centres=cv2.connectedComponentsWithStats(mask,8)
        candidates=[]
        for i in range(1,count):
            x,y,w,h,area=map(int,stats[i])
            if not AREA_MIN<=area<=AREA_MAX: continue
            contours,_=cv2.findContours((component[y:y+h,x:x+w]==i).astype(np.uint8),cv2.RETR_EXTERNAL,cv2.CHAIN_APPROX_SIMPLE)
            perimeter=cv2.arcLength(contours[0],True) if contours else 0
            circularity=4*np.pi*area/(perimeter*perimeter) if perimeter else 0
            if circularity<CIRCULARITY_MIN: continue
            candidates.append({'areaPx':area,'circularity':round(float(circularity),4),'offsetPx':[round(float(centres[i,0]-px),2),round(float(centres[i,1]-py),2)]})
        detected=bool(candidates)
        passed=detected if expected=='Visible' else not detected
        results.append({'frameId':frame,'expected':expected,'featureDetected':detected,'case':'PASS' if passed else 'FAIL','expectedPointSourcePx':[round(px,2),round(py,2)],'candidates':candidates,'timeline':receipt['indexedFrame']['scope']['timeline'],'receiptPixelSha256':receipt['indexedFrame']['pixelSha256'],'pngFileSha256':hashlib.sha256((root/f'{frame}.png').read_bytes()).hexdigest()})
    report={'schemaVersion':1,'feature':'low-saturation bright circular marker within 20 source px of reviewed black point','thresholds':{'hsvSMax':SAT_MAX,'hsvVMin':VALUE_MIN,'componentAreaPx':[AREA_MIN,AREA_MAX],'circularityMin':CIRCULARITY_MIN},'scope':'location-conditioned marker check only; not whole-table detection, centre accuracy, matched-light evidence, readiness, or production output','passed':sum(x['case']=='PASS' for x in results),'total':len(results),'results':results}
    out=Path(args.output);out.parent.mkdir(parents=True,exist_ok=True);out.write_text(json.dumps(report,indent=2)+'\n');print(json.dumps({'passed':report['passed'],'total':report['total'],'output':str(out),'cases':[(x['frameId'],x['expected'],x['case'],len(x['candidates'])) for x in results]}))
if __name__=='__main__': main()
