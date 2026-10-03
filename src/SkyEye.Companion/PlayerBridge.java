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
    private int rotation;private int videoWidth=1920,videoHeight=1080;
    public PlayerBridge(Activity context,Listener listener){
        this.listener=listener;texture=new TextureView(context);player=new ExoPlayer.Builder(context).build();player.setVideoTextureView(texture);player.setVolume(0);
        container=new FrameLayout(context);container.setBackgroundColor(android.graphics.Color.BLACK);container.addView(texture,new FrameLayout.LayoutParams(-1,-1));
        overlay=new View(context){protected void onDraw(Canvas canvas){super.onDraw(canvas);canvas.save();canvas.concat(texture.getTransform(new Matrix()));drawing.draw(canvas,getWidth(),getHeight());canvas.restore();}};
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
    boolean capture(Bitmap output){
        if(!player.isPlaying()||!activeUrl.contains(".m3u8")||!texture.isAvailable())return false;
        if(texture.getBitmap(output)==null)return false;drawing.draw(new Canvas(output),output.getWidth(),output.getHeight());return true;
    }
    public void play(String url){generation++;activeUrl=url;player.setMediaItem(MediaItem.fromUri(url));player.prepare();player.play();}
    public void stop(){generation++;activeUrl="";player.stop();player.clearMediaItems();}
    public void setMuted(boolean muted){player.setVolume(muted?0:1);}
    public void setPortrait(boolean portrait){rotation=portrait?90:0;transform();}
    private void transform(){float w=texture.getWidth(),h=texture.getHeight();if(w<=0||h<=0)return;float vw=videoWidth,vh=videoHeight;
        float scale=Math.min(w/(rotation==90?vh:vw),h/(rotation==90?vw:vh));
        Matrix matrix=new Matrix();matrix.setScale(vw*scale/w,vh*scale/h,w/2,h/2);matrix.postRotate(rotation,w/2,h/2);texture.setTransform(matrix);
    }
    public void release(){released=true;retry.removeCallbacksAndMessages(null);player.release();}
}
