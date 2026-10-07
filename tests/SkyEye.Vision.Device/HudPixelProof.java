package uk.co.skyeye.companion.nativebridge;

import android.graphics.*;
import java.io.*;
import org.json.*;

/** Declared geometry proof using the same live/recorded scene renderer. */
public final class HudPixelProof {
    public static String render(String sceneJson,String output,int width,int height,String matrixJson)throws Exception{
        JSONObject scene=new JSONObject(sceneJson);long generation=scene.getLong("mediaGeneration");
        scene.put("expiresAt",System.currentTimeMillis()+10000);
        TableSceneOverlay renderer=new TableSceneOverlay();if(!renderer.set(scene.toString(),generation))throw new Exception("Native HUD validation refused scene");
        JSONArray values=new JSONArray(matrixJson);float[] transform=new float[9];for(int i=0;i<9;i++)transform[i]=(float)values.getDouble(i);
        // Input points are source-image normalized; draw at a fixed 1920x1080 source canvas.
        float[] geometry=transform.clone(),text=transform.clone();for(int i:new int[]{0,3,6}){geometry[i]/=1920;text[i]/=width;}
        for(int i:new int[]{1,4,7}){geometry[i]/=1080;text[i]/=height;}
        Bitmap image=Bitmap.createBitmap(width,height,Bitmap.Config.ARGB_8888);
        try{
            Canvas canvas=new Canvas(image);Matrix mapping=new Matrix();mapping.setValues(geometry);
            canvas.save();canvas.concat(mapping);renderer.geometry(canvas,1920,1080,1920f/1080,scene);canvas.restore();
            Matrix labels=new Matrix();labels.setValues(text);renderer.textLayer(canvas,width,height,labels,scene);
            long amber=0,cyan=0,white=0;int[] pixels=new int[width*height];image.getPixels(pixels,0,width,0,0,width,height);
            for(int pixel:pixels){int r=Color.red(pixel),g=Color.green(pixel),b=Color.blue(pixel);if(r>170&&g>110&&g<220&&b<160)amber++;if(g>150&&b>150&&r<120)cyan++;if(r>200&&g>200&&b>200)white++;}
            if(scene.getJSONArray("paths").length()>0&&white+cyan+amber<150)throw new Exception("Expected HUD ink is absent");
            // Composite only after counting overlay ink; the source image cannot
            // accidentally satisfy the overlay-presence check.
            Bitmap overlay=image.copy(Bitmap.Config.ARGB_8888,false);
            try{Bitmap source=BitmapFactory.decodeFile(new File(new File(output).getParentFile(),"hud-source.jpg").getAbsolutePath());
                image.eraseColor(0xff173735);if(source!=null)try{canvas.drawBitmap(source,mapping,new Paint(Paint.FILTER_BITMAP_FLAG));}finally{source.recycle();}
                canvas.drawBitmap(overlay,0,0,null);
            }finally{overlay.recycle();}
            try(FileOutputStream file=new FileOutputStream(output)){if(!image.compress(Bitmap.CompressFormat.PNG,100,file))throw new Exception("HUD PNG unavailable");}
            renderer.clear();if(renderer.snapshot(System.currentTimeMillis())!=null)throw new Exception("Clear retained advice");
            if(renderer.set(scene.toString(),generation))throw new Exception("Delayed same-revision scene resurrected clear");
            JSONObject expired=new JSONObject(scene.toString()).put("version",scene.getLong("version")+1).put("expiresAt",System.currentTimeMillis()-1);
            if(renderer.set(expired.toString(),generation))throw new Exception("Expired HUD accepted");
            JSONObject unknown=new JSONObject(scene.toString()).put("version",scene.getLong("version")+2).put("schema",3);
            if(renderer.set(unknown.toString(),generation))throw new Exception("Unknown HUD schema accepted");
            return new JSONObject().put("passed",true).put("width",width).put("height",height).put("amberPixels",amber).put("cyanPixels",cyan).put("whitePixels",white)
                .put("corridors",scene.optJSONArray("corridors").length()).put("paths",scene.getJSONArray("paths").length()).put("clearExpiryRevisionSchemaChecks",true).toString();
        }finally{image.recycle();}
    }
}
