package uk.co.skyeye.companion.nativebridge;

import android.app.Activity;
import android.graphics.Matrix;
import android.view.*;
import androidx.media3.common.*;
import androidx.media3.exoplayer.ExoPlayer;

public final class PlayerBridge {
    public interface Listener { void onError(String error); void onPlaying(boolean playing); }
    private final ExoPlayer player;private final TextureView texture;private final Listener listener;
    private final android.os.Handler retry=new android.os.Handler(android.os.Looper.getMainLooper());private long generation;private String activeUrl="";private boolean released;
    private int rotation;private int videoWidth=1920,videoHeight=1080;
    public PlayerBridge(Activity context,Listener listener){
        this.listener=listener;texture=new TextureView(context);player=new ExoPlayer.Builder(context).build();player.setVideoTextureView(texture);player.setVolume(0);
        texture.addOnLayoutChangeListener((v,l,t,r,b,ol,ot,or,ob)->transform());
        player.addListener(new Player.Listener(){
            public void onPlayerError(PlaybackException e){listener.onError(activeUrl.contains(".m3u8")?"Viewing interrupted. Waiting for the live feed to recover.":"Downloaded clip could not be played.");long expected=generation;
                if(activeUrl.contains(".m3u8"))retry.postDelayed(()->{if(!released&&expected==generation){player.prepare();player.play();}},3000);
            }
            public void onIsPlayingChanged(boolean playing){listener.onPlaying(playing);}
            public void onVideoSizeChanged(VideoSize size){videoWidth=size.width;videoHeight=size.height;transform();}
        });
    }
    public View getView(){return texture;}
    public void play(String url){generation++;retry.removeCallbacksAndMessages(null);activeUrl=url;player.setMediaItem(MediaItem.fromUri(url));player.prepare();player.play();}
    public void stop(){generation++;retry.removeCallbacksAndMessages(null);activeUrl="";player.stop();player.clearMediaItems();}
    public void setMuted(boolean muted){player.setVolume(muted?0:1);}
    public void setPortrait(boolean portrait){rotation=portrait?90:0;transform();}
    private void transform(){float w=texture.getWidth(),h=texture.getHeight();if(w<=0||h<=0)return;float vw=videoWidth,vh=videoHeight;
        float scale=Math.min(w/(rotation==90?vh:vw),h/(rotation==90?vw:vh));
        Matrix matrix=new Matrix();matrix.setScale(vw*scale/w,vh*scale/h,w/2,h/2);matrix.postRotate(rotation,w/2,h/2);texture.setTransform(matrix);
    }
    public void release(){released=true;retry.removeCallbacksAndMessages(null);player.release();}
}
