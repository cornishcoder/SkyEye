package uk.co.skyeye.companion.nativebridge;

import android.app.Activity;
import android.graphics.Matrix;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.view.*;
import android.widget.FrameLayout;
import androidx.media3.common.*;
import androidx.media3.exoplayer.ExoPlayer;

public final class PlayerBridge {
    public interface Listener { void onError(String error); void onPlaying(boolean playing); }
    private final ExoPlayer player;private final TextureView texture;private final Listener listener;
    private final FrameLayout container;private final View overlay;private final CandidateOverlay drawing=new CandidateOverlay();
    private final android.os.Handler retry=new android.os.Handler(android.os.Looper.getMainLooper());private long generation;private String activeUrl="";private boolean released;
    private int rotation;private int videoWidth=1920,videoHeight=1080;private org.json.JSONObject scene;private boolean sceneApplied;
    public PlayerBridge(Activity context,Listener listener){
        this.listener=listener;texture=new TextureView(context);texture.setOpaque(false);player=new ExoPlayer.Builder(context).build();player.setVideoTextureView(texture);player.setVolume(0);
        container=new FrameLayout(context);container.setBackgroundColor(android.graphics.Color.BLACK);container.addView(texture,new FrameLayout.LayoutParams(-1,-1));
        overlay=new View(context){protected void onDraw(Canvas canvas){super.onDraw(canvas);canvas.save();if(sceneApplied)clipScene(canvas);canvas.concat(texture.getTransform(new Matrix()));drawing.draw(canvas,getWidth(),getHeight());if(sceneApplied)drawGuides(canvas,getWidth(),getHeight());canvas.restore();}};
        overlay.setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO);container.addView(overlay,new FrameLayout.LayoutParams(-1,-1));
        retry.post(new Runnable(){public void run(){if(released)return;overlay.invalidate();retry.postDelayed(this,200);}});
        texture.addOnLayoutChangeListener((v,l,t,r,b,ol,ot,or,ob)->transform());
        player.addListener(new Player.Listener(){
            public void onPlayerError(PlaybackException e){listener.onError(activeUrl.contains(".m3u8")?"Viewing interrupted. Waiting for the live feed to recover.":"Downloaded clip could not be played.");long expected=generation;
                if(activeUrl.contains(".m3u8"))retry.postDelayed(()->{if(!released&&expected==generation){player.prepare();player.play();}},3000);
            }
            public void onIsPlayingChanged(boolean playing){listener.onPlaying(playing);}
            public void onVideoSizeChanged(VideoSize size){videoWidth=size.width;videoHeight=size.height;transform();}
        });
    }
    public View getView(){return container;}
    public void setOverlay(String json){drawing.set(json);overlay.invalidate();}
    public String presentation(){return drawing.metrics();}
    public String playbackMetrics(){try{return new org.json.JSONObject().put("playing",player.isPlaying()).put("volume",player.getVolume()).put("rotation",rotation).put("width",videoWidth).put("height",videoHeight).put("positionMs",player.getCurrentPosition()).toString();}catch(Exception ignored){return "{}";}}
    public void setSceneTransform(String json){try{scene=json==null||json.isEmpty()?null:new org.json.JSONObject(json);}catch(Exception ignored){scene=null;}transform();}
    public String framingMetrics(){try{org.json.JSONObject result=new org.json.JSONObject().put("applied",sceneApplied).put("framed",sceneApplied&&scene.optBoolean("framed")).put("hardware",texture.isHardwareAccelerated()).put("viewportWidth",texture.getWidth()).put("viewportHeight",texture.getHeight()).put("mediaGeneration",generation).put("playing",player.isPlaying()).put("positionMs",player.getCurrentPosition());
        org.json.JSONArray mapped=new org.json.JSONArray();org.json.JSONArray points=sceneApplied?scene.optJSONArray("guides"):null;Matrix matrix=texture.getTransform(new Matrix());
        if(points!=null)for(int i=0;i<points.length();i++){org.json.JSONArray point=points.getJSONArray(i);float[] xy={(float)point.getDouble(0)*texture.getWidth(),(float)point.getDouble(1)*texture.getHeight()};matrix.mapPoints(xy);mapped.put(new org.json.JSONArray().put(xy[0]).put(xy[1]));}return result.put("guidePixels",mapped).toString();}catch(Exception ignored){return "{}";}}
    private void clipScene(Canvas canvas){org.json.JSONArray clip=scene.optJSONArray("clip");if(clip!=null&&clip.length()==4)canvas.clipRect((float)clip.optDouble(0),(float)clip.optDouble(1),(float)clip.optDouble(2),(float)clip.optDouble(3));}
    private void drawGuides(Canvas canvas,int width,int height){org.json.JSONArray points=scene.optJSONArray("guides");if(points==null||points.length()!=6)return;
        android.graphics.Paint paint=new android.graphics.Paint(android.graphics.Paint.ANTI_ALIAS_FLAG);paint.setStyle(android.graphics.Paint.Style.STROKE);paint.setColor(android.graphics.Color.MAGENTA);paint.setStrokeWidth(3);
        for(int i=0;i<6;i++){org.json.JSONArray p=points.optJSONArray(i);float x=(float)p.optDouble(0)*width,y=(float)p.optDouble(1)*height;canvas.drawCircle(x,y,10,paint);}
        for(int[] pair:new int[][]{{0,5},{2,3}}){org.json.JSONArray a=points.optJSONArray(pair[0]),b=points.optJSONArray(pair[1]);canvas.drawLine((float)a.optDouble(0)*width,(float)a.optDouble(1)*height,(float)b.optDouble(0)*width,(float)b.optDouble(1)*height,paint);}
    }
    boolean capture(Bitmap output){
        if(!player.isPlaying()||!activeUrl.contains(".m3u8")||!texture.isAvailable())return false;
        if(texture.getBitmap(output)==null)return false;drawing.draw(new Canvas(output),output.getWidth(),output.getHeight());return true;
    }
    public void play(String url){generation++;activeUrl=url;player.setMediaItem(MediaItem.fromUri(url));player.prepare();player.play();}
    public void stop(){generation++;activeUrl="";player.stop();player.clearMediaItems();}
    public void setMuted(boolean muted){player.setVolume(muted?0:1);}
    public void setPortrait(boolean portrait){rotation=portrait?90:0;transform();}
    private void transform(){float w=texture.getWidth(),h=texture.getHeight();if(w<=0||h<=0)return;sceneApplied=false;texture.setClipBounds(null);
        if(scene!=null&&scene.optInt("width")==w&&scene.optInt("height")==h)try{
            org.json.JSONArray values=scene.getJSONArray("values"),clip=scene.getJSONArray("clip");if(values.length()!=9||clip.length()!=4)throw new IllegalArgumentException();float[] a=new float[9];
            for(int i=0;i<9;i++){a[i]=(float)values.getDouble(i);if(!Float.isFinite(a[i]))throw new IllegalArgumentException();}
            for(int i:new int[]{0,3,6})a[i]/=w;for(int i:new int[]{1,4,7})a[i]/=h;
            Matrix matrix=new Matrix();matrix.setValues(a);Matrix inverse=new Matrix();if(!matrix.invert(inverse))throw new IllegalArgumentException();texture.setTransform(matrix);
            texture.setClipBounds(new android.graphics.Rect((int)Math.floor(clip.getDouble(0)),(int)Math.floor(clip.getDouble(1)),(int)Math.ceil(clip.getDouble(2)),(int)Math.ceil(clip.getDouble(3))));sceneApplied=true;overlay.invalidate();return;
        }catch(Exception ignored){}float vw=videoWidth,vh=videoHeight;
        float scale=Math.min(w/(rotation==90?vh:vw),h/(rotation==90?vw:vh));
        Matrix matrix=new Matrix();matrix.setScale(vw*scale/w,vh*scale/h,w/2,h/2);matrix.postRotate(rotation,w/2,h/2);texture.setTransform(matrix);
    }
    public void release(){released=true;retry.removeCallbacksAndMessages(null);player.release();}
}
