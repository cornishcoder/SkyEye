package uk.co.skyeye.companion.nativebridge;

import android.graphics.*;
import android.media.MediaMetadataRetriever;
import org.json.*;

public final class AnnotationPixelProof {
    public static String verify(String marked,String plain,String pointsJson)throws Exception{
        MediaMetadataRetriever a=new MediaMetadataRetriever(),b=new MediaMetadataRetriever();JSONArray observations=new JSONArray(),points=new JSONArray(pointsJson);
        try{
            a.setDataSource(marked);b.setDataSource(plain);
            for(int i=0;i<points.length();i++){
                JSONObject point=points.getJSONObject(i);long pts=point.getLong("ptsUs");int ink=point.getInt("ink"),x=(int)Math.round(point.getDouble("x")),y=(int)Math.round(point.getDouble("y"));
                Bitmap actual=a.getFrameAtTime(pts,MediaMetadataRetriever.OPTION_CLOSEST),control=b.getFrameAtTime(pts,MediaMetadataRetriever.OPTION_CLOSEST);if(actual==null||control==null)throw new Exception("Missing decoded annotation frame");
                double delta=0;int samples=0,colors=0;
                for(int yy=Math.max(0,y-8);yy<Math.min(actual.getHeight(),y+9);yy++)for(int xx=Math.max(0,x-8);xx<Math.min(actual.getWidth(),x+9);xx++){
                    int v=actual.getPixel(xx,yy),c=control.getPixel(xx,yy),r=Color.red(v),g=Color.green(v),blue=Color.blue(v);
                    if(ink==1&&g>160&&blue>160&&g-r>65&&blue-r>65||ink==3&&r>160&&r-g>65&&r-blue>65||ink==2&&r>160&&g>160&&r-blue>65&&g-blue>65)colors++;
                    delta+=Math.abs(r-Color.red(c))+Math.abs(g-Color.green(c))+Math.abs(blue-Color.blue(c));samples+=3;
                }
                delta/=samples;actual.recycle();control.recycle();
                if(ink<0?delta>12:colors<5||delta<15)throw new Exception("Historical annotation frame differs: pts="+pts+", colors="+colors+", delta="+delta);
                observations.put(new JSONObject().put("ptsUs",pts).put("ink",ink).put("coloredPixels",colors).put("meanDifference",delta));
            }
            return new JSONObject().put("passed",true).put("samples",observations).toString();
        }finally{a.release();b.release();}
    }
}
