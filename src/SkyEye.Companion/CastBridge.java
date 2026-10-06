package uk.co.skyeye.companion.nativebridge;
import android.app.Activity;
import android.view.View;
import android.util.Log;
import androidx.mediarouter.app.MediaRouteButton;
import com.google.android.gms.cast.*;
import com.google.android.gms.cast.framework.*;
import com.google.android.gms.cast.framework.media.RemoteMediaClient;
import org.json.JSONObject;
import java.util.HashSet;
import com.google.android.gms.common.api.PendingResult;

public final class CastBridge {
    public interface Listener { void onStatus(String status); }
    private final CastContext context;private final MediaRouteButton button;private final Listener listener;
    private String url;private boolean enabled;private final SessionManagerListener<CastSession> events;
    private volatile String metrics="{}";private String observed="";
    private boolean buffered;private String title="SkyEye · pool table",clipId="",request="";private long durationMs,loadGeneration;
    private RemoteMediaClient watched;private long progressCallbacks;private String lastLoggedKey="",liveRequest="";
    private String commandId="",commandRequest="",commandOutcome="",commandAction="";private long commandTarget;
    private final HashSet<String> commandIds=new HashSet<>();
    private boolean liveEdgePending;private String liveEdgeOutcome="";
    private final RemoteMediaClient.Callback mediaEvents=new RemoteMediaClient.Callback(){
        @Override public void onStatusUpdated(){updateMetrics();}
        @Override public void onMetadataUpdated(){updateMetrics();}
    };
    private final RemoteMediaClient.ProgressListener progress=(position,duration)->{progressCallbacks++;updateMetrics();};
    private void watch(CastSession session){RemoteMediaClient next=session==null?null:session.getRemoteMediaClient();if(next==watched)return;unwatch();watched=next;if(watched!=null){watched.registerCallback(mediaEvents);watched.addProgressListener(progress,1000);}}
    private void unwatch(){if(watched!=null){watched.unregisterCallback(mediaEvents);watched.removeProgressListener(progress);watched=null;}}
    public CastBridge(Activity activity,Listener listener){this.listener=listener;context=CastContext.getSharedInstance(activity);button=new MediaRouteButton(activity);CastButtonFactory.setUpMediaRouteButton(activity,button);
        events=new SessionManagerListener<CastSession>(){
            public void onSessionStarted(CastSession s,String id){watch(s);listener.onStatus("TV connected");load();}
            public void onSessionResumed(CastSession s,boolean suspended){watch(s);listener.onStatus("TV connected");if(!isPlayingSource(url))load();}
            public void onSessionEnded(CastSession s,int error){unwatch();++loadGeneration;listener.onStatus("TV disconnected");}
            public void onSessionStartFailed(CastSession s,int error){listener.onStatus("Could not connect to TV: "+error);}
            public void onSessionResumeFailed(CastSession s,int error){listener.onStatus("Could not reconnect to TV: "+error);}
            public void onSessionStarting(CastSession s){}public void onSessionEnding(CastSession s){}public void onSessionResuming(CastSession s,String id){}public void onSessionSuspended(CastSession s,int reason){}
        };context.getSessionManager().addSessionManagerListener(events,CastSession.class);watch(context.getSessionManager().getCurrentCastSession());
    }
    public View getButton(){return button;}
    public void configure(String url,boolean enabled){boolean changed=this.url!=null&&(!this.url.equals(url)||buffered);if(changed)++loadGeneration;buffered=false;clipId=request="";if(url==null||url.isEmpty())liveRequest="";title="SkyEye · pool table";durationMs=0;this.url=url;this.enabled=enabled;button.setVisibility(enabled?View.VISIBLE:View.GONE);if(changed&&enabled)load();updateMetrics();}
    public String metrics(){updateMetrics();return metrics;}
    public void play(String source){if(source==null||source.isEmpty())return;boolean changed=url!=null&&!url.equals(source);configure(source,true);if(!changed)load();}
    public void playLive(String source,String requestId){if(requestId==null||!requestId.matches("[0-9a-fA-F]{32}"))return;liveRequest=requestId;play(source);}
    public void playClip(String source,String id,String name,long duration,String requestId){
        if(source==null||source.isEmpty()||id==null||!id.matches("[0-9a-fA-F]{32}")||requestId==null||!requestId.matches("[0-9a-fA-F]{32}")||duration<=0)return;
        ++loadGeneration;url=source;clipId=id;request=requestId;liveRequest="";title=name==null?"SkyEye · saved shot":name;durationMs=duration;buffered=true;enabled=true;lastLoggedKey="";progressCallbacks=0;commandIds.clear();button.setVisibility(View.VISIBLE);load();
    }
    public boolean controlClip(String expectedRequest,String id,String action,long position){
        if(!buffered||!request.equals(expectedRequest)||id==null||!id.matches("[0-9a-fA-F]{32}"))return false;
        CastSession session=context.getSessionManager().getCurrentCastSession();RemoteMediaClient client=session==null?null:session.getRemoteMediaClient();
        MediaStatus state=client==null?null:client.getMediaStatus();MediaInfo info=state==null?null:state.getMediaInfo();JSONObject data=info==null?null:info.getCustomData();
        if(session==null||!session.isConnected()||info==null||!isPlayingSource(url)||data==null||!expectedRequest.equals(data.optString("request"))||info.getStreamType()!=MediaInfo.STREAM_TYPE_BUFFERED)return false;
        if(commandIds.contains(id))return true;
        int playerState=state.getPlayerState();if(playerState!=MediaStatus.PLAYER_STATE_PLAYING&&playerState!=MediaStatus.PLAYER_STATE_PAUSED)return false;
        if("pause".equals(action)&&(!state.isMediaCommandSupported(MediaStatus.COMMAND_PAUSE)||playerState!=MediaStatus.PLAYER_STATE_PLAYING))return false;
        if("resume".equals(action)&&playerState!=MediaStatus.PLAYER_STATE_PAUSED)return false;
        if("seek".equals(action)&&(!state.isMediaCommandSupported(MediaStatus.COMMAND_SEEK)||position<0||position>Math.max(0,info.getStreamDuration()-250)))return false;
        if(!"pause".equals(action)&&!"resume".equals(action)&&!"seek".equals(action))return false;
        final long generation=loadGeneration;commandIds.add(id);commandId=id;commandRequest=expectedRequest;commandAction=action;commandTarget=position;commandOutcome="pending";
        try{
            PendingResult<RemoteMediaClient.MediaChannelResult> result="pause".equals(action)?client.pause():"resume".equals(action)?client.play():client.seek(new MediaSeekOptions.Builder().setPosition(position).setResumeState(MediaSeekOptions.RESUME_STATE_UNCHANGED).build());
            result.setResultCallback(response->{if(generation!=loadGeneration||!request.equals(expectedRequest)||!commandId.equals(id)||context.getSessionManager().getCurrentCastSession()!=session)return;commandOutcome=response.getStatus().isSuccess()?"accepted":"rejected";updateMetrics();});
            updateMetrics();return true;
        }catch(Exception ignored){commandOutcome="rejected";updateMetrics();return false;}
    }
    public boolean isPlayingSource(String source){try{CastSession session=context.getSessionManager().getCurrentCastSession();MediaInfo info=session==null||session.getRemoteMediaClient()==null?null:session.getRemoteMediaClient().getMediaInfo();return info!=null&&info.getContentId().equals(source);}catch(Exception ignored){return false;}}
    public void returnToSourceIfOwned(String owned,String fallback){boolean ours=isPlayingSource(owned);if(url!=null&&url.equals(owned))url=fallback;if(ours&&fallback!=null&&!fallback.isEmpty())play(fallback);}
    private void updateMetrics(){try{CastSession session=context.getSessionManager().getCurrentCastSession();boolean connected=session!=null&&session.isConnected();JSONObject report=new JSONObject();report.put("connected",connected);
        if(connected){watch(session);RemoteMediaClient client=session.getRemoteMediaClient();MediaStatus state=client==null?null:client.getMediaStatus();MediaInfo info=state==null?null:state.getMediaInfo();JSONObject data=info==null?null:info.getCustomData();boolean clip=info!=null&&info.getStreamType()==MediaInfo.STREAM_TYPE_BUFFERED&&data!=null&&"skyeye-clip".equals(data.optString("kind"));String kind=info==null?"none":clip?"clip":info.getContentId().contains(":8766/")?(info.getContentId().contains("scene=1")?"scene":"candidates"):"camera";boolean playing=state!=null&&state.getPlayerState()==MediaStatus.PLAYER_STATE_PLAYING;
            report.put("playing",playing);report.put("stream",kind);report.put("positionMs",client==null?0:client.getApproximateStreamPosition());report.put("playerState",state==null?0:state.getPlayerState());
            report.put("idleReason",state==null?0:state.getIdleReason());
            if(!clip&&info!=null&&info.getStreamType()==MediaInfo.STREAM_TYPE_LIVE&&state.getLiveSeekableRange()!=null){
                report.put("liveEdgeLagMs",Math.max(0,client.getApproximateLiveSeekableRangeEnd()-client.getApproximateStreamPosition()));
                report.put("liveEdgeOutcome",liveEdgeOutcome);
                // One live-edge request per owned composed-stream load. Never
                // seek clips, another sender's media, or chase the edge on a timer.
                if(liveEdgePending&&playing&&info.getContentId().equals(url)&&state.isMediaCommandSupported(MediaStatus.COMMAND_SEEK)){
                    liveEdgePending=false;liveEdgeOutcome="pending";final long expected=loadGeneration;final CastSession owner=session;
                    long safePosition=Math.max(state.getLiveSeekableRange().getStartTime(),client.getApproximateLiveSeekableRangeEnd()-1000);
                    client.seek(new MediaSeekOptions.Builder().setPosition(safePosition).setResumeState(MediaSeekOptions.RESUME_STATE_PLAY).build()).setResultCallback(result->{if(expected!=loadGeneration||context.getSessionManager().getCurrentCastSession()!=owner)return;liveEdgeOutcome=result.getStatus().isSuccess()?"accepted":"rejected";});
                }
            }
            if(clip){String actualId=data.optString("clipId"),actualRequest=data.optString("request");if(actualId.matches("[0-9a-fA-F]{32}")&&actualRequest.matches("[0-9a-fA-F]{32}")){report.put("clipId",actualId);report.put("request",actualRequest);report.put("durationMs",info.getStreamDuration());report.put("progressCallbacks",progressCallbacks);report.put("canPause",state.isMediaCommandSupported(MediaStatus.COMMAND_PAUSE));report.put("canSeek",state.isMediaCommandSupported(MediaStatus.COMMAND_SEEK));if(!commandId.isEmpty()&&commandRequest.equals(actualRequest)){report.put("commandId",commandId);report.put("commandRequest",commandRequest);report.put("commandAction",commandAction);report.put("commandTargetMs",commandTarget);report.put("commandOutcome",commandOutcome);}String logKey=report.optLong("positionMs")/1000+":"+state.getPlayerState()+":"+commandId+":"+commandOutcome;if(buffered&&request.equals(actualRequest)&&!logKey.equals(lastLoggedKey)){lastLoggedKey=logKey;Log.i("SkyEyeCast",report.toString());}}}
            else if(info!=null&&data!=null&&"skyeye-live".equals(data.optString("kind"))&&data.optString("request").matches("[0-9a-fA-F]{32}")){report.put("liveRequest",data.optString("request"));}
            String key=kind+":"+playing;if(playing&&!key.equals(observed)){observed=key;listener.onStatus(clip?"TV playing saved clip":kind.equals("candidates")?"TV playing candidate overlay":kind.equals("scene")?"TV playing shared table scene":"TV playing live camera");}
        }metrics=report.toString();}catch(Exception ignored){metrics="{}";}}
    public void load(){if(!enabled||url==null||url.isEmpty())return;CastSession session=context.getSessionManager().getCurrentCastSession();if(session==null||!session.isConnected())return;
        observed="";
        watch(session);final long generation=++loadGeneration;final boolean clip=buffered;
        liveEdgePending=!clip&&url.contains(":8766/");liveEdgeOutcome="";
        commandId=commandRequest=commandOutcome=commandAction="";commandIds.clear();
        MediaMetadata metadata=new MediaMetadata(MediaMetadata.MEDIA_TYPE_MOVIE);metadata.putString(MediaMetadata.KEY_TITLE,title);
        MediaInfo.Builder builder=new MediaInfo.Builder(url).setContentType(clip?"video/mp4":"application/x-mpegURL").setStreamType(clip?MediaInfo.STREAM_TYPE_BUFFERED:MediaInfo.STREAM_TYPE_LIVE).setMetadata(metadata);
        if(clip){try{JSONObject data=new JSONObject();data.put("kind","skyeye-clip");data.put("clipId",clipId);data.put("request",request);builder.setStreamDuration(durationMs).setCustomData(data);}catch(Exception ignored){return;}}
        else if(!liveRequest.isEmpty()){try{JSONObject data=new JSONObject();data.put("kind","skyeye-live");data.put("request",liveRequest);builder.setCustomData(data);}catch(Exception ignored){return;}}
        session.getRemoteMediaClient().load(new MediaLoadRequestData.Builder().setMediaInfo(builder.build()).setAutoplay(true).build()).setResultCallback(result->{if(generation!=loadGeneration||context.getSessionManager().getCurrentCastSession()!=session)return;listener.onStatus(result.getStatus().isSuccess()?(clip?"TV accepted the clip playback request":"Casting silently in landscape"):(clip?"TV could not load saved clip":"TV could not load camera stream"));});
    }
    public void stop(){if(enabled)context.getSessionManager().endCurrentSession(true);}
    public void release(){++loadGeneration;unwatch();context.getSessionManager().removeSessionManagerListener(events,CastSession.class);}
}
