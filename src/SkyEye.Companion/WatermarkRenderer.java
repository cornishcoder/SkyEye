package uk.co.skyeye.companion.nativebridge;

import android.content.Context;
import android.graphics.*;
import androidx.media3.effect.*;
import com.google.common.collect.ImmutableList;
import java.io.*;
import java.security.MessageDigest;

/** One upright output-space brand for phone, TV and standalone derivatives. */
public final class WatermarkRenderer {
    private static Bitmap logo;private static String profile;
    public static synchronized String profile(Context context){load(context);return profile;}
    private static synchronized Bitmap load(Context context){
        if(logo!=null)return logo;
        try(InputStream input=context.getAssets().open("watermark.png")){
            byte[] bytes=input.readAllBytes();Bitmap full=BitmapFactory.decodeByteArray(bytes,0,bytes.length);
            if(full==null||full.getWidth()>4096||full.getHeight()>4096)throw new IOException("Invalid watermark asset");
            int w=full.getWidth(),h=full.getHeight(),left=w,top=h,right=-1,bottom=-1;int[] pixels=new int[w*h];full.getPixels(pixels,0,w,0,0,w,h);
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)if((pixels[y*w+x]>>>24)!=0){left=Math.min(left,x);top=Math.min(top,y);right=Math.max(right,x);bottom=Math.max(bottom,y);}
            if(right<left)throw new IOException("Empty watermark asset");
            logo=Bitmap.createBitmap(full,left,top,right-left+1,bottom-top+1);if(logo!=full)full.recycle();
            StringBuilder sha=new StringBuilder();for(byte b:MessageDigest.getInstance("SHA-256").digest(bytes))sha.append(String.format(java.util.Locale.ROOT,"%02X",b));
            profile="watermark-v1:edge8:margin2:alpha85:"+sha;return logo;
        }catch(Exception error){throw new IllegalStateException("Watermark unavailable",error);}
    }
    public static Rect bounds(int width,int height){int shortEdge=Math.min(width,height),size=Math.max(1,Math.round(shortEdge*.08f)),margin=Math.max(1,Math.round(shortEdge*.02f));return new Rect(margin,margin,margin+size,margin+size);}
    public static void draw(Context context,Canvas canvas,int width,int height){Bitmap bitmap=load(context);Paint paint=new Paint(Paint.ANTI_ALIAS_FLAG|Paint.FILTER_BITMAP_FLAG);paint.setAlpha(217);canvas.drawBitmap(bitmap,null,bounds(width,height),paint);}
    public static OverlayEffect effect(Context context,int width,int height){
        Rect rect=bounds(width,height);Bitmap bitmap=Bitmap.createScaledBitmap(load(context),rect.width(),rect.height(),true);
        StaticOverlaySettings settings=new StaticOverlaySettings.Builder().setAlphaScale(.85f)
            .setBackgroundFrameAnchor(-1f+2f*rect.left/width,1f-2f*rect.top/height).setOverlayFrameAnchor(-1f,1f).build();
        return new OverlayEffect(ImmutableList.of(BitmapOverlay.createStaticBitmapOverlay(bitmap,settings)));
    }
}
