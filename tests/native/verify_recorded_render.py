"""Compare actual Android MP4 pixels to an independent OpenCV homography.
Uses recorded, labelled fixture geometry; never samples the current live table.
"""
import pathlib, json, cv2, numpy as np
root=pathlib.Path(__file__).resolve().parents[2]
geometry=json.loads((root/'work/t08-recorded-device.json').read_text(encoding='utf-8-sig'))['journal']['spans']
def frame(path,seconds):
    video=cv2.VideoCapture(str(path)); video.set(cv2.CAP_PROP_POS_MSEC,seconds*1000)
    ok,image=video.read(); video.release()
    if not ok: raise RuntimeError(f'No frame at {seconds}')
    return image
results=[]
for portrait,name in [(False,'t09-rendered-fixture.mp4'),(True,'t09-rendered-portrait.mp4')]:
    w,h=(1080,1920) if portrait else (1920,1080)
    for seconds in [1,3,5,9,13,17]:
        original=frame(root/'work/t09-render-fixture.mp4',seconds)
        actual=frame(root/'work'/name,seconds)
        span=next(s for s in geometry if s['startUs']<=seconds*1e6<s['endUs'])
        if span['layout'] is None:
            if portrait:
                rotated=cv2.rotate(original,cv2.ROTATE_90_CLOCKWISE)
                scale=min(w/rotated.shape[1],h/rotated.shape[0]); rw,rh=round(rotated.shape[1]*scale),round(rotated.shape[0]*scale)
                expected=np.zeros((h,w,3),np.uint8);expected[(h-rh)//2:(h-rh)//2+rh,(w-rw)//2:(w-rw)//2+rw]=cv2.resize(rotated,(rw,rh))
            else: expected=original
        else:
            across,along=(1.15,2.15) if portrait else (2.15,1.15)
            scale=min(w/across,h/along);left=(w-across*scale)/2;top=(h-along*scale)/2;m=.075*scale
            target=np.float32([[left+across*scale-m,top+m],[left+across*scale-m,top+along*scale-m],[left+m,top+along*scale-m],[left+m,top+m]]) if portrait else np.float32([[left+m,top+m],[left+across*scale-m,top+m],[left+across*scale-m,top+along*scale-m],[left+m,top+along*scale-m]])
            source=np.float32([[p['x']*original.shape[1],p['y']*original.shape[0]] for p in span['layout']['corners']])
            expected=cv2.warpPerspective(original,cv2.getPerspectiveTransform(source,target),(w,h),flags=cv2.INTER_LINEAR)
        error=np.abs(actual.astype(np.float32)-expected.astype(np.float32))
        mean=float(error.mean());p95=float(np.percentile(error,95))
        results.append({'portrait':portrait,'seconds':seconds,'corrected':span['layout'] is not None,'meanChannelError':mean,'p95ChannelError':p95})
        if mean>12: raise RuntimeError(f'Recorded GPU image differs from reference: {results[-1]}')
        if seconds==1:cv2.imwrite(str(root/'work'/('t09-portrait-preview.png' if portrait else 't09-landscape-preview.png')),actual)
(root/'work/t09-pixel-checks.json').write_text(json.dumps({'passed':True,'reference':'Independent OpenCV point-to-point homography, full-frame fallback, same labelled source intervals','meanChannelErrorLimit':12,'results':results},indent=2))
print(json.dumps(results))
