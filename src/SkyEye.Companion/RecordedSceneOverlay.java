package uk.co.skyeye.companion.nativebridge;

import android.graphics.*;
import androidx.media3.effect.BitmapOverlay;
import androidx.media3.common.VideoFrameProcessingException;
import org.json.*;

/** Historical scenes evaluated at decoder PTS. Never consult live TTL/state. */
public final class RecordedSceneOverlay extends BitmapOverlay {
    private final Bitmap bitmap;private final Canvas canvas;private final TableSceneOverlay drawing=new TableSceneOverlay();
    private final JSONArray spans;private final int width,height,sourceWidth,sourceHeight;private int last=-2;private long annotatedFrames;
    public RecordedSceneOverlay(String json,int width,int height,int sourceWidth,int sourceHeight)throws Exception{
        JSONObject plan=new JSONObject(json);if(plan.getInt("version")!=1||plan.getInt("width")!=width||plan.getInt("height")!=height)throw new IllegalArgumentException("Annotation viewport differs");
        this.width=width;this.height=height;this.sourceWidth=sourceWidth;this.sourceHeight=sourceHeight;spans=plan.getJSONArray("spans");
        if(json.length()>8000000||spans.length()>12512)throw new IllegalArgumentException("Annotation plan exceeds budget");
        long end=0;for(int i=0;i<spans.length();i++){JSONObject s=spans.getJSONObject(i);if(s.getLong("startUs")<end||s.getLong("endUs")<=s.getLong("startUs")||s.getJSONArray("pixelMatrix").length()!=9)throw new IllegalArgumentException("Invalid annotation interval");end=s.getLong("endUs");}
        bitmap=Bitmap.createBitmap(width,height,Bitmap.Config.ARGB_8888);canvas=new Canvas(bitmap);
    }
    @Override public Bitmap getBitmap(long pts)throws VideoFrameProcessingException{
        int index=-1;for(int i=0;i<spans.length();i++){JSONObject s=spans.optJSONObject(i);if(s.optLong("startUs")<=pts&&pts<s.optLong("endUs")){index=i;break;}}
        if(index>=0)annotatedFrames++;if(index==last)return bitmap;last=index;bitmap.eraseColor(Color.TRANSPARENT);
        if(index<0)return bitmap;
        try{
            JSONObject span=spans.getJSONObject(index),scene=span.getJSONObject("scene");JSONArray values=span.getJSONArray("pixelMatrix");
            float[] geometry=new float[9],text=new float[9];for(int i=0;i<9;i++){float v=(float)values.getDouble(i);if(!Float.isFinite(v))throw new IllegalArgumentException("Invalid annotation matrix");geometry[i]=text[i]=v;}
            for(int i:new int[]{0,3,6}){geometry[i]/=sourceWidth;text[i]/=width;}
            for(int i:new int[]{1,4,7}){geometry[i]/=sourceHeight;text[i]/=height;}
            Matrix image=new Matrix();image.setValues(geometry);canvas.save();canvas.concat(image);
            drawing.geometry(canvas,sourceWidth,sourceHeight,sourceWidth/(float)sourceHeight,scene);canvas.restore();
            Matrix labels=new Matrix();labels.setValues(text);drawing.textLayer(canvas,width,height,labels,scene);
            return bitmap;
        }catch(Exception error){throw new VideoFrameProcessingException(error);}
    }
    public long annotatedFrames(){return annotatedFrames;}
    @Override public void release()throws VideoFrameProcessingException{super.release();bitmap.recycle();}
}
