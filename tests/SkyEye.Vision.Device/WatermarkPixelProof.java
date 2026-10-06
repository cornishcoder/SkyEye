package uk.co.skyeye.companion.nativebridge;

import android.content.Context;
import android.graphics.*;
import android.media.MediaMetadataRetriever;
import org.json.*;

public final class WatermarkPixelProof {
    public static String verify(Context context,String marked,String plain)throws Exception{
        MediaMetadataRetriever a=new MediaMetadataRetriever(),b=new MediaMetadataRetriever();Bitmap target=null,control=null,expected=null;
        try{
            a.setDataSource(marked);b.setDataSource(plain);target=a.getFrameAtTime(500000,MediaMetadataRetriever.OPTION_CLOSEST);control=b.getFrameAtTime(500000,MediaMetadataRetriever.OPTION_CLOSEST);
            if(target==null||control==null||target.getWidth()!=control.getWidth()||target.getHeight()!=control.getHeight())throw new Exception("Missing comparable decoded frames");
            expected=control.copy(Bitmap.Config.ARGB_8888,true);WatermarkRenderer.draw(context,new Canvas(expected),expected.getWidth(),expected.getHeight());
            Rect region=WatermarkRenderer.bounds(target.getWidth(),target.getHeight());double error=0,delta=0;int n=0;
            for(int y=region.top;y<region.bottom;y++)for(int x=region.left;x<region.right;x++){
                int actual=target.getPixel(x,y),wanted=expected.getPixel(x,y),before=control.getPixel(x,y);
                for(int shift:new int[]{0,8,16}){error+=Math.abs(((actual>>shift)&255)-((wanted>>shift)&255));delta+=Math.abs(((actual>>shift)&255)-((before>>shift)&255));n++;}
            }
            error/=n;delta/=n;
            if(error>20||delta<10)throw new Exception("Encoded watermark differs from upright top-left Canvas brand: error="+error+", delta="+delta);
            return new JSONObject().put("passed",true).put("width",target.getWidth()).put("height",target.getHeight())
                .put("bounds",new JSONArray().put(region.left).put(region.top).put(region.right).put(region.bottom))
                .put("meanChannelError",error).put("meanChangeFromPlain",delta).put("profile",WatermarkRenderer.profile(context)).toString();
        }finally{a.release();b.release();if(target!=null)target.recycle();if(control!=null)control.recycle();if(expected!=null)expected.recycle();}
    }
}
