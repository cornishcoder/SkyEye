package uk.co.skyeye.companion.nativebridge;
import android.app.Activity;
import android.view.View;
import androidx.mediarouter.app.MediaRouteButton;
import com.google.android.gms.cast.*;
import com.google.android.gms.cast.framework.*;
import com.google.android.gms.cast.framework.media.RemoteMediaClient;
import org.json.JSONObject;

public final class CastBridge {
    public interface Listener { void onStatus(String status); }
    private final CastContext context;private final MediaRouteButton button;private final Listener listener;
    private String url;private boolean enabled;private final SessionManagerListener<CastSession> events;
    private volatile String metrics="{}";private String observed="";
    public CastBridge(Activity activity,Listener listener){this.listener=listener;context=CastContext.getSharedInstance(activity);button=new MediaRouteButton(activity);CastButtonFactory.setUpMediaRouteButton(activity,button);
        events=new SessionManagerListener<CastSession>(){
            public void onSessionStarted(CastSession s,String id){listener.onStatus("TV connected");load();}
            public void onSessionResumed(CastSession s,boolean suspended){listener.onStatus("TV connected");load();}
            public void onSessionEnded(CastSession s,int error){listener.onStatus("TV disconnected");}
            public void onSessionStartFailed(CastSession s,int error){listener.onStatus("Could not connect to TV: "+error);}
            public void onSessionResumeFailed(CastSession s,int error){listener.onStatus("Could not reconnect to TV: "+error);}
            public void onSessionStarting(CastSession s){}public void onSessionEnding(CastSession s){}public void onSessionResuming(CastSession s,String id){}public void onSessionSuspended(CastSession s,int reason){}
        };context.getSessionManager().addSessionManagerListener(events,CastSession.class);
    }
    public View getButton(){return button;}
    public void configure(String url,boolean enabled){boolean changed=this.url!=null&&!this.url.equals(url);this.url=url;this.enabled=enabled;button.setVisibility(enabled?View.VISIBLE:View.GONE);if(changed&&enabled)load();updateMetrics();}
    public String metrics(){updateMetrics();return metrics;}
    public void play(String source){if(source==null||source.isEmpty())return;boolean changed=url!=null&&!url.equals(source);configure(source,true);if(!changed)load();}
    private void updateMetrics(){try{CastSession session=context.getSessionManager().getCurrentCastSession();boolean connected=session!=null&&session.isConnected();JSONObject report=new JSONObject();report.put("connected",connected);
        if(connected){RemoteMediaClient client=session.getRemoteMediaClient();MediaStatus state=client==null?null:client.getMediaStatus();MediaInfo info=state==null?null:state.getMediaInfo();String kind=info==null?"none":info.getContentId().contains(":8766/")?"candidates":"camera";boolean playing=state!=null&&state.getPlayerState()==MediaStatus.PLAYER_STATE_PLAYING;
            report.put("playing",playing);report.put("stream",kind);report.put("positionMs",client==null?0:client.getApproximateStreamPosition());report.put("playerState",state==null?0:state.getPlayerState());
            String key=kind+":"+playing;if(playing&&!key.equals(observed)){observed=key;listener.onStatus(kind.equals("candidates")?"TV playing candidate overlay":"TV playing live camera");}
        }metrics=report.toString();}catch(Exception ignored){metrics="{}";}}
    public void load(){if(!enabled||url==null||url.isEmpty())return;CastSession session=context.getSessionManager().getCurrentCastSession();if(session==null||!session.isConnected())return;
        MediaMetadata metadata=new MediaMetadata(MediaMetadata.MEDIA_TYPE_MOVIE);metadata.putString(MediaMetadata.KEY_TITLE,"SkyEye · pool table");
        MediaInfo media=new MediaInfo.Builder(url).setContentType("application/x-mpegURL").setStreamType(MediaInfo.STREAM_TYPE_LIVE).setMetadata(metadata).build();
        session.getRemoteMediaClient().load(new MediaLoadRequestData.Builder().setMediaInfo(media).setAutoplay(true).build()).setResultCallback(result->listener.onStatus(result.getStatus().isSuccess()?"Casting silently in landscape":"TV could not load camera stream"));
    }
    public void stop(){if(enabled)context.getSessionManager().endCurrentSession(true);}
    public void release(){context.getSessionManager().removeSessionManagerListener(events,CastSession.class);}
}
