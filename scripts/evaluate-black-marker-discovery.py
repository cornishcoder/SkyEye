import argparse,hashlib,json
from pathlib import Path
import cv2,numpy as np
FRAMES=(1049,190,206,229,375,461,477,678,697,844);S_MAX=100;V_MIN=170;AREA_MIN=20;AREA_MAX=1000;CIRC_MIN=.45;RING_IN=10;RING_OUT=20;RING_S_MAX=120;RING_V_MAX=80;MATCH=20

def point(corners,table):
 a=[];b=[]
 for q,t in zip(corners,((0,0),(2,0),(2,1),(0,1))):
  x,y=q['x'],q['y'];u,v=t;a.extend(((x,y,1,0,0,0,-u*x,-u*y),(0,0,0,x,y,1,-v*x,-v*y)));b.extend((u,v))
 h=np.r_[np.linalg.solve(a,b),1].reshape(3,3);p=np.linalg.inv(h)@np.array((table['x'],table['y'],1));p/=p[2];return float(p[0]*1920),float(p[1]*1080)

def main():
 ap=argparse.ArgumentParser();ap.add_argument('--root',default='work/o08/live-detect-black-pair-v1/physical-controls/replay-inputs/upper-right-development-v4');ap.add_argument('--labels',default='work/o08/live-detect-black-pair-v1/corpus-v3');ap.add_argument('--output',default='work/live-detect-scorecard/black-marker-discovery-annulus.json');args=ap.parse_args();root=Path(args.root);labelroot=Path(args.labels);results=[]
 for f in FRAMES:
  rp=root/f'{f}.json';ip=root/f'{f}.png';rec=json.loads(rp.read_text());ld=json.loads((labelroot/str(f)/'labels.json').read_text());assert ld['reviewed'];labels=ld['frames'][0]['balls'];black=next(x for x in labels if x['colour']=='black');expected=black['visibility'];assert expected in ('Visible','Absent')
  image=cv2.imread(str(ip));hsv=cv2.cvtColor(image,cv2.COLOR_BGR2HSV);mask=((hsv[:,:,1]<=S_MAX)&(hsv[:,:,2]>=V_MIN)).astype(np.uint8)*255;n,cc,stats,cent=cv2.connectedComponentsWithStats(mask,8);base=[];selected=[];yy,xx=np.ogrid[:1080,:1920]
  for i in range(1,n):
   x,y,w,h,area=map(int,stats[i]);
   if not AREA_MIN<=area<=AREA_MAX:continue
   contours,_=cv2.findContours((cc[y:y+h,x:x+w]==i).astype(np.uint8),cv2.RETR_EXTERNAL,cv2.CHAIN_APPROX_SIMPLE);per=cv2.arcLength(contours[0],True) if contours else 0;circ=4*np.pi*area/(per*per) if per else 0
   if circ<CIRC_MIN:continue
   cx,cy=map(float,cent[i]);rad=np.hypot(xx-cx,yy-cy);ring=(rad>=RING_IN)&(rad<=RING_OUT);sm=int(np.median(hsv[:,:,1][ring]));vm=int(np.median(hsv[:,:,2][ring]));c={'x':round(cx,2),'y':round(cy,2),'areaPx':area,'circularity':round(float(circ),4),'ringMedianS':sm,'ringMedianV':vm};base.append(c)
   if sm<=RING_S_MAX and vm<=RING_V_MAX:selected.append(c)
  bx,by=point(rec['calibrationCorners'],black);nearest=sorted([{'candidate':c,'distancePx':round(float(np.hypot(c['x']-bx,c['y']-by)),2)} for c in selected],key=lambda x:x['distancePx']);hit=bool(nearest and nearest[0]['distancePx']<=MATCH);ok=hit if expected=='Visible' else not hit
  # Surface collisions with reviewed visible non-black ball labels.
  otherHits=[]
  for label in labels:
   if label['id']==black['id'] or label['visibility']!='Visible':continue
   x,y=point(rec['calibrationCorners'],label);ds=[float(np.hypot(c['x']-x,c['y']-y)) for c in selected]
   if ds and min(ds)<=MATCH:otherHits.append({'id':label['id'],'distancePx':round(min(ds),2)})
  results.append({'frameId':f,'expectedBlack':expected,'baseBrightCircularComponents':len(base),'selectedDarkAnnulusCandidates':selected,'selectedCandidateCount':len(selected),'within20pxOfBlackLabel':hit,'nearestBlackDistancePx':nearest[0]['distancePx'] if nearest else None,'collisionsWithOtherBallLabels':otherHits,'case':'PASS' if ok else 'FAIL','timeline':rec['indexedFrame']['scope']['timeline'],'pixelSha256':rec['indexedFrame']['pixelSha256'],'pngFileSha256':hashlib.sha256(ip.read_bytes()).hexdigest()})
 report={'schemaVersion':1,'feature':'whole-frame bright circular components with dark neutral surrounding annulus','thresholds':{'hsvSMax':S_MAX,'hsvVMin':V_MIN,'areaPx':[AREA_MIN,AREA_MAX],'circularityMin':CIRC_MIN,'annulusRadiusPx':[RING_IN,RING_OUT],'annulusMedianSMax':RING_S_MAX,'annulusMedianVMax':RING_V_MAX,'labelMatchRadiusPx':MATCH},'scope':'post-hoc exploratory on four reviewed Development frames; not frozen acceptance, production detection, center accuracy or matched timeline evidence','passed':sum(x['case']=='PASS' for x in results),'total':len(results),'results':results};out=Path(args.output);out.parent.mkdir(parents=True,exist_ok=True);out.write_text(json.dumps(report,indent=2)+'\n');print(json.dumps({'passed':report['passed'],'total':report['total'],'frames':[{'frame':x['frameId'],'expected':x['expectedBlack'],'brightCircular':x['baseBrightCircularComponents'],'darkAnnulusCandidates':x['selectedCandidateCount'],'nearestPx':x['nearestBlackDistancePx'],'otherBallCollisions':x['collisionsWithOtherBallLabels'],'case':x['case']} for x in results]}))
if __name__=='__main__':main()
