package uk.co.skyeye.companion.nativebridge;

import android.app.Activity;
import android.graphics.Matrix;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.view.*;
import android.widget.FrameLayout;
import androidx.media3.common.*;
import androidx.media3.exoplayer.ExoPlayer;
import org.json.JSONObject;

public final class PlayerBridge {
    public interface Listener { void onError(String error); void onPlaying(boolean playing); }
    public interface MotionFrameUpdateListener { void onFrameUpdated(); }
    private final AnnotationRecorder annotationRecorder=new AnnotationRecorder();
    private final DecodedFrameLedger decodedLedger=new DecodedFrameLedger();
    private final java.util.Map<String,Integer> copyFailures=new java.util.LinkedHashMap<>();private long copyBefore,copyAfter;
    private String lastCopyFailure="";private int lastPlaybackErrorCode,endedCount;
    public String lastCopyFailure(){return lastCopyFailure;}
    public String observationSourceStatus(){try{return new JSONObject().put("playing",player.isPlaying()).put("playWhenReady",player.getPlayWhenReady())
        .put("playbackState",player.getPlaybackState()).put("suppressionReason",player.getPlaybackSuppressionReason())
        .put("errorCode",player.getPlayerError()==null?JSONObject.NULL:player.getPlayerError().errorCode).put("textureAvailable",texture.isAvailable())
        .put("released",released).put("mediaGeneration",generation).put("copyFailure",lastCopyFailure).toString();}catch(Exception ignored){return "{}";}}
    private DecodedFrameSnapshot copyFailure(String reason){lastCopyFailure=reason;copyFailures.put(reason,copyFailures.getOrDefault(reason,0)+1);return null;}
    public String decodedCopyMetrics(){try{return new JSONObject().put("failures",new JSONObject(copyFailures)).put("beforeNs",copyBefore)
        .put("afterNs",copyAfter).put("newestReleaseNs",decodedLedger.newestRelease()).put("ledgerSize",decodedLedger.size()).toString();}catch(Exception ignored){return "{}";}}
    private final ExoPlayer player;private final TextureView texture;private final Listener listener;
    private final FrameLayout container;private final View overlay;private final CandidateOverlay drawing=new CandidateOverlay();private final TableSceneOverlay tableDrawing=new TableSceneOverlay(),tvDrawing=new TableSceneOverlay();
    private volatile String phoneScene="{}",phoneFraming="{}";
    private final android.os.Handler retry=new android.os.Handler(android.os.Looper.getMainLooper());private volatile long generation;private String activeUrl="";private boolean released;
    private int rotation;private int videoWidth=1920,videoHeight=1080;private org.json.JSONObject scene,tvScene;private boolean sceneApplied;private Bitmap captureScratch;private final android.graphics.Paint videoPaint=new android.graphics.Paint(android.graphics.Paint.ANTI_ALIAS_FLAG|android.graphics.Paint.FILTER_BITMAP_FLAG|android.graphics.Paint.DITHER_FLAG);
    private boolean diagnostics;private long decodedFrames,textureFrames,measurementStart,lastTextureNs,lastReleaseNs;private double releaseToTextureMs,lastBitmapCopyMs;
    private final java.util.concurrent.CopyOnWriteArrayList<MotionFrameUpdateListener> motionFrameUpdateListeners=new java.util.concurrent.CopyOnWriteArrayList<>();
    private static boolean liveUrl(String url){return url.contains(".m3u8")||url.contains("/live/stream.ts");}
    public void setDiagnostics(boolean enabled){diagnostics=enabled;if(measurementStart==0)measurementStart=System.nanoTime();}
    public void addMotionFrameUpdateListener(MotionFrameUpdateListener listener){if(listener!=null)motionFrameUpdateListeners.addIfAbsent(listener);}
    public void removeMotionFrameUpdateListener(MotionFrameUpdateListener listener){if(listener!=null)motionFrameUpdateListeners.remove(listener);}
    public PlayerBridge(Activity context,Listener listener){
        this.listener=listener;texture=new TextureView(context);texture.setOpaque(false);
        androidx.media3.exoplayer.DefaultLoadControl buffering=new androidx.media3.exoplayer.DefaultLoadControl.Builder()
            .setBufferDurationsMs(250,5000,100,250).setPrioritizeTimeOverSizeThresholds(true).build();
        player=new ExoPlayer.Builder(context).setLoadControl(buffering).build();player.setVideoTextureView(texture);player.setVolume(0);
        container=new FrameLayout(context);container.setBackgroundColor(android.graphics.Color.BLACK);container.addView(texture,new FrameLayout.LayoutParams(-1,-1));
        overlay=new View(context){protected void onDraw(Canvas canvas){super.onDraw(canvas);org.json.JSONObject snapshot=tableDrawing.snapshot(System.currentTimeMillis());Matrix matrix=texture.getTransform(new Matrix());canvas.save();if(sceneApplied)clipScene(canvas);canvas.concat(matrix);drawing.draw(canvas,getWidth(),getHeight());tableDrawing.geometry(canvas,getWidth(),getHeight(),videoWidth/(float)videoHeight,snapshot);if(sceneApplied)drawGuides(canvas,getWidth(),getHeight());canvas.restore();tableDrawing.textLayer(canvas,getWidth(),getHeight(),matrix,snapshot);phoneScene=tableDrawing.metrics(snapshot,generation,player.getCurrentPosition()).toString();phoneFraming=calculateFramingMetrics();annotationRecorder.drawn(snapshot,scene==null?"":scene.toString(),false,generation);if(liveUrl(activeUrl))WatermarkRenderer.draw(context,canvas,getWidth(),getHeight());}};
        overlay.setImportantForAccessibility(View.IMPORTANT_FOR_ACCESSIBILITY_NO);container.addView(overlay,new FrameLayout.LayoutParams(-1,-1));
        retry.post(new Runnable(){public void run(){if(released)return;overlay.invalidate();retry.postDelayed(this,200);}});
        texture.addOnLayoutChangeListener((v,l,t,r,b,ol,ot,or,ob)->transform());
        // Preserve Media3's surface lifecycle listener; observe its texture callback.
        TextureView.SurfaceTextureListener delegate=texture.getSurfaceTextureListener();
        texture.setSurfaceTextureListener(new TextureView.SurfaceTextureListener(){public void onSurfaceTextureAvailable(android.graphics.SurfaceTexture t,int w,int h){delegate.onSurfaceTextureAvailable(t,w,h);}public void onSurfaceTextureSizeChanged(android.graphics.SurfaceTexture t,int w,int h){delegate.onSurfaceTextureSizeChanged(t,w,h);}public boolean onSurfaceTextureDestroyed(android.graphics.SurfaceTexture t){return delegate.onSurfaceTextureDestroyed(t);}public void onSurfaceTextureUpdated(android.graphics.SurfaceTexture t){delegate.onSurfaceTextureUpdated(t);annotationRecorder.textureUpdated();if(diagnostics){textureFrames++;lastTextureNs=System.nanoTime();releaseToTextureMs=(lastTextureNs-lastReleaseNs)/1000000d;}for(MotionFrameUpdateListener pending:motionFrameUpdateListeners)if(motionFrameUpdateListeners.remove(pending))pending.onFrameUpdated();}});
        player.addListener(new Player.Listener(){
            public void onPlayerError(PlaybackException e){lastPlaybackErrorCode=e.errorCode;android.util.Log.w("SkyEyePlayer","Playback error code="+e.errorCode+" live="+liveUrl(activeUrl)+"; waiting for retry");listener.onError(liveUrl(activeUrl)?"Viewing interrupted. Waiting for the live feed to recover.":"Downloaded clip could not be played.");long expected=generation;
                if(liveUrl(activeUrl))retry.postDelayed(()->{if(!released&&expected==generation){play(activeUrl);}},1000);
            }
            public void onIsPlayingChanged(boolean playing){listener.onPlaying(playing);}
            public void onPlaybackStateChanged(int state){if(state==Player.STATE_ENDED&&liveUrl(activeUrl)){endedCount++;android.util.Log.w("SkyEyePlayer","Live source ended; restarting transport="+(activeUrl.contains("/live/stream.ts")?"phone-ts":"hls")+" generation="+generation);long expected=generation;retry.postDelayed(()->{if(!released&&expected==generation)play(activeUrl);},100);}}
            public void onVideoSizeChanged(VideoSize size){if(size.width<=0||size.height<=0)return;videoWidth=size.width;videoHeight=size.height;transform();listener.onPlaying(player.isPlaying());}
        });
    }
    public View getView(){return container;}
    public void setOverlay(String json){tableDrawing.clear();tvDrawing.clear();drawing.set(json);overlay.invalidate();}
    public boolean setTableScene(String json){return setTableSceneForTarget(json,false)&&setTableSceneForTarget(json,true);}
    public boolean setTableSceneForTarget(String json,boolean tv){boolean accepted=(tv?tvDrawing:tableDrawing).set(json,generation);if(accepted){drawing.set("{}");overlay.invalidate();}return accepted;}
    public void clearTableScene(){tableDrawing.clear();tvDrawing.clear();overlay.invalidate();}
    public void clearTableSceneForTarget(boolean tv){(tv?tvDrawing:tableDrawing).clear();overlay.invalidate();}
    public void setRecordingClock(String timeline,long sourceUs,long mediaGeneration){annotationRecorder.clock(timeline,sourceUs,mediaGeneration);}
    public void setRecordingClockSample(String timeline,long sourceUs,long mediaGeneration,double sampleAgeMs,double uncertaintyMs){annotationRecorder.clockSample(timeline,sourceUs,mediaGeneration,sampleAgeMs,uncertaintyMs);}
    public String drainAnnotations(){return annotationRecorder.drain();}
    public String annotationMetrics(){return annotationRecorder.metrics();}
    public String sceneMetrics(){return phoneScene;}
    public String presentation(){return drawing.metrics();}
    public String playbackMetrics(){try{return new org.json.JSONObject().put("playbackState",player.getPlaybackState()).put("loading",player.isLoading()).put("playWhenReady",player.getPlayWhenReady()).put("suppressionReason",player.getPlaybackSuppressionReason()).put("lastErrorCode",lastPlaybackErrorCode).put("endedCount",endedCount).put("playing",player.isPlaying()).put("mediaGeneration",generation).put("volume",player.getVolume()).put("rotation",rotation).put("width",videoWidth).put("height",videoHeight).put("positionMs",player.getCurrentPosition()).put("liveOffsetMs",player.getCurrentLiveOffset()==C.TIME_UNSET?JSONObject.NULL:player.getCurrentLiveOffset()).put("bufferedMs",player.getTotalBufferedDuration()).put("decodedFrames",decodedFrames).put("textureFrames",textureFrames).put("textureFps",measurementStart==0?0:textureFrames*1000000000d/(System.nanoTime()-measurementStart)).put("releaseToTextureMs",releaseToTextureMs).put("textureAgeMs",lastTextureNs==0?JSONObject.NULL:(System.nanoTime()-lastTextureNs)/1000000d).toString();}catch(Exception ignored){return "{}";}}
    public void setSceneTransform(String json){try{scene=json==null||json.isEmpty()?null:new org.json.JSONObject(json);}catch(Exception ignored){scene=null;}transform();}
    public void setTvSceneTransform(String json){try{tvScene=json==null||json.isEmpty()?null:new org.json.JSONObject(json);}catch(Exception ignored){tvScene=null;}}
    public byte[] snapshotForFraming(){if(!player.isPlaying()||!texture.isAvailable())return null;Bitmap bitmap=texture.getBitmap(960,540);if(bitmap==null)return null;try{java.io.ByteArrayOutputStream bytes=new java.io.ByteArrayOutputStream();bitmap.compress(Bitmap.CompressFormat.JPEG,90,bytes);return bytes.toByteArray();}finally{bitmap.recycle();}}
    public DecodedFrameSnapshot snapshotDecodedFrame(){return snapshotDecodedFrame(1);}
    public DecodedFrameSnapshot snapshotDecodedFrame(int readbackScale){
        if(readbackScale<1||readbackScale>4)throw new IllegalArgumentException("Decoded readback scale is outside the supported bounds");
        lastBitmapCopyMs=0;
        lastCopyFailure="";
        if(android.os.Looper.myLooper()!=android.os.Looper.getMainLooper())throw new IllegalStateException("Decoded copy requires the player looper");
        if(released||!player.isPlaying()||!texture.isAvailable())return copyFailure("no-playing-texture");
        long media=generation;android.graphics.SurfaceTexture surface=texture.getSurfaceTexture();if(surface==null)return null;
        // If a new texture frame arrives during getBitmap(), immediately retry
        // against the newly latched timestamp. Waiting for another callback
        // would discard a frame that is already available and can phase-lock
        // repeated readbacks to the same copy race.
        for(int attempt=0;attempt<2;attempt++){
            DecodedFrameSnapshot snapshot=snapshotDecodedFrameOnce(media,surface,readbackScale);
            if(snapshot!=null||!"changed-during-copy".equals(lastCopyFailure)
                ||media!=generation||surface!=texture.getSurfaceTexture())return snapshot;
        }
        return null;
    }
    private DecodedFrameSnapshot snapshotDecodedFrameOnce(long media,android.graphics.SurfaceTexture surface,int readbackScale){
        long stamp=surface.getTimestamp();copyBefore=stamp;DecodedFrameLedger.Stamp paired=decodedLedger.match(media,stamp);
        if(paired==null)return copyFailure("unmapped-surface");
        // Media3 stamps the requested render time. TextureView may latch its
        // actual pixels before that deadline; pairing uses identity, not now.
        long releaseAge=System.nanoTime()-stamp;
        if(releaseAge>250000000L||releaseAge< -100000000L)return copyFailure("stale-or-far-future-surface");
        long clockAt=android.os.SystemClock.elapsedRealtime();
        JSONObject clock=liveUrl(activeUrl)?annotationRecorder.exactFrameMetrics(stamp,media):new JSONObject();
        if(clock==null)return copyFailure("unmapped-source-clock");
        // Allocate explicitly so a failed/throwing TextureView copy cannot leak
        // an internally allocated bitmap. An untouched transparent buffer is
        // not a successful opaque AVC decoder copy.
        int copyWidth=Math.max(1,paired.width/readbackScale),copyHeight=Math.max(1,paired.height/readbackScale);
        Bitmap bitmap=Bitmap.createBitmap(copyWidth,copyHeight,Bitmap.Config.ARGB_8888);
        try{
            long bitmapCopyStartedNs=System.nanoTime();
            Bitmap copied=texture.getBitmap(bitmap);
            lastBitmapCopyMs+=(System.nanoTime()-bitmapCopyStartedNs)/1000000d;
            if(copied==null||android.graphics.Color.alpha(bitmap.getPixel(copyWidth/2,copyHeight/2))==0)
                return copyFailure("empty-copy");
            copyAfter=surface.getTimestamp();
            if(media!=generation||surface!=texture.getSurfaceTexture()||stamp!=copyAfter
                ||decodedLedger.match(media,stamp)!=paired)return copyFailure("changed-during-copy");
            // The clock receipt precedes native readback. Copy time is part of
            // source age at return, not evidence that the pixels became newer.
            if(liveUrl(activeUrl)){
                long copyElapsed=android.os.SystemClock.elapsedRealtime()-clockAt+1;
                clock.put("ageEstimateMs",clock.getDouble("ageEstimateMs")+copyElapsed);
                clock.put("clockSampleAgeMs",clock.getLong("clockSampleAgeMs")+copyElapsed);
            }
            String metadata=clock.put("mediaGeneration",media).put("frameId",paired.frameId).put("presentationUs",paired.presentationUs)
                .put("surfaceTimestampNs",stamp).put("releaseTimestampNs",paired.releaseNs).put("width",paired.width).put("height",paired.height)
                .put("imageWidth",bitmap.getWidth()).put("imageHeight",bitmap.getHeight())
                .put("textureUpdateCount",textureFrames).put("textureAgeMs",lastTextureNs==0?JSONObject.NULL:(System.nanoTime()-lastTextureNs)/1000000d)
                .put("bitmapCopyMs",lastBitmapCopyMs)
                .put("pairing","exact-surface-timestamp").toString();
            DecodedFrameSnapshot result=new DecodedFrameSnapshot(bitmap,metadata);bitmap=null;lastCopyFailure="";return result;
        }catch(org.json.JSONException invalid){return copyFailure("invalid-source-metadata");}finally{if(bitmap!=null)bitmap.recycle();}
    }
    public String decodedFrameCandidate(){
        String probe=decodedFrameProbe();if(probe==null)return null;
        try{return "available".equals(new JSONObject(probe).optString("candidateStatus"))?probe:null;}catch(Exception ignored){return null;}
    }
    public String decodedFrameProbe(){
        if(android.os.Looper.myLooper()!=android.os.Looper.getMainLooper())throw new IllegalStateException("Candidate query requires player looper");
        if(released||!player.isPlaying()||!texture.isAvailable())return unavailableCandidateProbe("not-playing",0);
        android.graphics.SurfaceTexture surface=texture.getSurfaceTexture();if(surface==null)return unavailableCandidateProbe("no-surface",0);
        long stamp=surface.getTimestamp();DecodedFrameLedger.Stamp frame=decodedLedger.match(generation,stamp);
        if(frame==null)return unavailableCandidateProbe("unmapped-surface",stamp);
        JSONObject clock=annotationRecorder.exactFrameMetrics(stamp,generation);
        if(clock==null)return unavailableCandidateProbe("unmapped-source-clock",stamp);
        try{return clock.put("candidateStatus","available").put("mediaGeneration",generation).put("frameId",frame.frameId)
            .put("textureUpdateCount",textureFrames).put("textureAgeMs",lastTextureNs==0?JSONObject.NULL:(System.nanoTime()-lastTextureNs)/1000000d)
            .put("presentationUs",frame.presentationUs).put("surfaceTimestampNs",stamp).put("releaseTimestampNs",frame.releaseNs).toString();}
        catch(org.json.JSONException invalid){return null;}
    }
    public double lastBitmapCopyMs(){return lastBitmapCopyMs;}
    private String unavailableCandidateProbe(String reason,long surfaceTimestampNs){
        try{return new JSONObject().put("candidateStatus",reason).put("surfaceTimestampNs",surfaceTimestampNs)
            .put("textureUpdateCount",textureFrames).put("textureAgeMs",lastTextureNs==0?JSONObject.NULL:(System.nanoTime()-lastTextureNs)/1000000d).toString();}
        catch(org.json.JSONException invalid){return null;}
    }
    public String framingMetrics(){return phoneFraming;}
    private String calculateFramingMetrics(){try{org.json.JSONObject result=new org.json.JSONObject().put("applied",sceneApplied).put("framed",sceneApplied&&scene.optBoolean("framed")).put("hardware",texture.isHardwareAccelerated()).put("viewportWidth",texture.getWidth()).put("viewportHeight",texture.getHeight()).put("mediaGeneration",generation).put("playing",player.isPlaying()).put("positionMs",player.getCurrentPosition());
        org.json.JSONArray mapped=new org.json.JSONArray();org.json.JSONArray points=sceneApplied?scene.optJSONArray("guides"):null;Matrix matrix=texture.getTransform(new Matrix());
        if(points!=null)for(int i=0;i<points.length();i++){org.json.JSONArray point=points.getJSONArray(i);float[] xy={(float)point.getDouble(0)*texture.getWidth(),(float)point.getDouble(1)*texture.getHeight()};matrix.mapPoints(xy);mapped.put(new org.json.JSONArray().put(xy[0]).put(xy[1]));}return result.put("guidePixels",mapped).put("pocketPixels",mappedPockets(sceneApplied?scene:null,matrix,texture.getWidth(),texture.getHeight())).toString();}catch(Exception ignored){return "{}";}}
    private org.json.JSONArray mappedPockets(org.json.JSONObject framing,Matrix matrix,int width,int height)throws org.json.JSONException{org.json.JSONArray mapped=new org.json.JSONArray(),points=framing==null?null:framing.optJSONArray("pockets");if(points!=null)for(int i=0;i<points.length();i++){org.json.JSONArray p=points.getJSONArray(i);float[] xy={(float)p.getDouble(0)*width,(float)p.getDouble(1)*height};matrix.mapPoints(xy);mapped.put(new org.json.JSONArray().put(xy[0]).put(xy[1]));}return mapped;}
    private void clipScene(Canvas canvas){org.json.JSONArray clip=scene.optJSONArray("clip");if(clip!=null&&clip.length()==4)canvas.clipRect((float)clip.optDouble(0),(float)clip.optDouble(1),(float)clip.optDouble(2),(float)clip.optDouble(3));}
    private void drawGuides(Canvas canvas,int width,int height){org.json.JSONArray points=scene.optJSONArray("guides");if(points==null||points.length()!=6)return;
        android.graphics.Paint paint=new android.graphics.Paint(android.graphics.Paint.ANTI_ALIAS_FLAG);paint.setStyle(android.graphics.Paint.Style.STROKE);paint.setColor(android.graphics.Color.MAGENTA);paint.setStrokeWidth(3);
        for(int i=0;i<6;i++){org.json.JSONArray p=points.optJSONArray(i);float x=(float)p.optDouble(0)*width,y=(float)p.optDouble(1)*height;canvas.drawCircle(x,y,10,paint);}
        for(int[] pair:new int[][]{{0,5},{2,3}}){org.json.JSONArray a=points.optJSONArray(pair[0]),b=points.optJSONArray(pair[1]);canvas.drawLine((float)a.optDouble(0)*width,(float)a.optDouble(1)*height,(float)b.optDouble(0)*width,(float)b.optDouble(1)*height,paint);}
    }
    String captureFrame(Bitmap output){
        if(!player.isPlaying()||!liveUrl(activeUrl)||!texture.isAvailable())return null;
        output.eraseColor(android.graphics.Color.BLACK);
        if(captureScratch==null||captureScratch.getWidth()!=output.getWidth()||captureScratch.getHeight()!=output.getHeight()){if(captureScratch!=null)captureScratch.recycle();captureScratch=Bitmap.createBitmap(output.getWidth(),output.getHeight(),Bitmap.Config.ARGB_8888);}
        if(texture.getBitmap(captureScratch)==null)return null;
        // Copy only the video surface, then this content renderer. Never capture the window, sheet or key editor.
        org.json.JSONObject snapshot=tvDrawing.snapshot(System.currentTimeMillis());long position=player.getCurrentPosition();Canvas canvas=new Canvas(output);int wholeFrame=canvas.save();
        Matrix matrix=new Matrix();boolean framed=false;
        if(tvScene!=null)try{org.json.JSONArray values=tvScene.getJSONArray("values"),clip=tvScene.getJSONArray("clip");float sx=output.getWidth()/(float)tvScene.optInt("width",960),sy=output.getHeight()/(float)tvScene.optInt("height",540);float[] a=new float[9];for(int i=0;i<9;i++)a[i]=(float)values.getDouble(i);for(int i=0;i<3;i++)a[i]*=sx;for(int i=3;i<6;i++)a[i]*=sy;for(int i:new int[]{0,3,6})a[i]/=output.getWidth();for(int i:new int[]{1,4,7})a[i]/=output.getHeight();matrix.setValues(a);canvas.clipRect((float)clip.getDouble(0)*sx,(float)clip.getDouble(1)*sy,(float)clip.getDouble(2)*sx,(float)clip.getDouble(3)*sy);framed=true;}catch(Exception ignored){matrix.reset();}
        canvas.save();canvas.concat(matrix);canvas.drawBitmap(captureScratch,0,0,videoPaint);drawing.draw(canvas,output.getWidth(),output.getHeight());tvDrawing.geometry(canvas,output.getWidth(),output.getHeight(),videoWidth/(float)videoHeight,snapshot);canvas.restore();tvDrawing.textLayer(canvas,output.getWidth(),output.getHeight(),matrix,snapshot);
        canvas.restoreToCount(wholeFrame);WatermarkRenderer.draw(container.getContext(),canvas,output.getWidth(),output.getHeight());
        annotationRecorder.drawn(snapshot,tvScene==null?"":tvScene.toString(),true,generation);
        try{return tvDrawing.metrics(snapshot,generation,position).put("watermarked",true).put("rectangular",framed).put("pocketPixels",mappedPockets(framed?tvScene:null,matrix,output.getWidth(),output.getHeight())).toString();}catch(Exception ignored){return "{}";}
    }
    public void play(String url){generation++;annotationRecorder.reset(generation);decodedLedger.reset(generation);
        long frameGeneration=generation;
        player.setVideoFrameMetadataListener((pts,release,format,mediaFormat)->{
            if(frameGeneration!=generation)return;
            decodedLedger.frame(frameGeneration,pts,release,format.width,format.height);annotationRecorder.frame(pts,release);
            if(diagnostics){decodedFrames++;lastReleaseNs=release;}
        });
        tableDrawing.reset();tvDrawing.reset();drawing.set("{}");phoneScene="{}";activeUrl=url;if(url.contains(".m3u8")){
            long expectedGeneration=generation;
            androidx.media3.exoplayer.hls.DefaultHlsExtractorFactory delegate=new androidx.media3.exoplayer.hls.DefaultHlsExtractorFactory();
            androidx.media3.exoplayer.hls.HlsExtractorFactory factory=(uri,format,captions,adjuster,headers,input,id)->{
                annotationRecorder.extractor(adjuster,expectedGeneration);return delegate.createExtractor(uri,format,captions,adjuster,headers,input,id);
            };
            MediaItem item=new MediaItem.Builder().setUri(url).setLiveConfiguration(new MediaItem.LiveConfiguration.Builder()
                .setTargetOffsetMs(2500).setMinOffsetMs(2000).setMaxOffsetMs(5000).setMinPlaybackSpeed(.97f).setMaxPlaybackSpeed(1.03f).build()).build();
            player.setMediaSource(new androidx.media3.exoplayer.hls.HlsMediaSource.Factory(new androidx.media3.datasource.DefaultDataSource.Factory(container.getContext())).setExtractorFactory(factory).createMediaSource(item));
        }else if(url.contains("/live/stream.ts")){
            long expectedGeneration=generation;
            androidx.media3.common.util.TimestampAdjuster adjuster=new androidx.media3.common.util.TimestampAdjuster(0);
            androidx.media3.extractor.ExtractorsFactory extractors=()->{
                annotationRecorder.extractor(adjuster,expectedGeneration);
                return new androidx.media3.extractor.Extractor[]{new androidx.media3.extractor.ts.TsExtractor(androidx.media3.extractor.ts.TsExtractor.MODE_SINGLE_PMT,adjuster,new androidx.media3.extractor.ts.DefaultTsPayloadReaderFactory())};
            };
            player.setMediaSource(new androidx.media3.exoplayer.source.ProgressiveMediaSource.Factory(new androidx.media3.datasource.DefaultDataSource.Factory(container.getContext()),extractors)
                .setContinueLoadingCheckIntervalBytes(16384).createMediaSource(new MediaItem.Builder().setUri(url).setMimeType(MimeTypes.VIDEO_MP2T).build()));
        }else player.setMediaItem(MediaItem.fromUri(url));player.prepare();player.play();}
    public void stop(){generation++;annotationRecorder.reset(generation);decodedLedger.reset(generation);tableDrawing.reset();tvDrawing.reset();drawing.set("{}");phoneScene="{}";activeUrl="";player.stop();player.clearMediaItems();}
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
    public void release(){released=true;generation++;motionFrameUpdateListeners.clear();decodedLedger.reset(generation);retry.removeCallbacksAndMessages(null);player.release();if(captureScratch!=null){captureScratch.recycle();captureScratch=null;}}
}
