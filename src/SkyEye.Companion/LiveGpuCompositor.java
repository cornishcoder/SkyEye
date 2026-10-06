package uk.co.skyeye.companion.nativebridge;

import android.content.Context;
import android.graphics.*;
import android.graphics.Matrix;
import android.media.*;
import android.opengl.*;
import android.os.*;
import android.view.Surface;
import androidx.media3.common.*;
import androidx.media3.exoplayer.*;
import org.json.*;
import java.io.*;
import java.nio.*;
import java.util.*;
import java.util.concurrent.atomic.AtomicBoolean;

/** Independent, service-owned decode -> external GLES texture -> AVC encoder.
 * No TextureView, Activity, window capture, CPU video bitmap or perception loop. */
public final class LiveGpuCompositor {
    public interface Listener { void onError(String message); }
    private static final int W=1920,H=1080;
    private final Context context;private final File directory;private final Listener listener;
    private final Handler main=new Handler(Looper.getMainLooper());
    private final HandlerThread thread=new HandlerThread("SkyEyeTvGpu");private Handler gl;
    private final AnnotationRecorder recorder=new AnnotationRecorder();private final TableSceneOverlay scenes=new TableSceneOverlay();
    private final AtomicBoolean framePending=new AtomicBoolean();
    private ExoPlayer player;private Gpu gpu;private MediaCodec encoder;private Surface encoderInput,decoderOutput;private SurfaceTexture decoderTexture;
    private volatile boolean closed,playing;private volatile long generation=1,frames,replaced,encodedUs;private volatile String error="",frameMetrics="{}";
    private volatile String geometry="",scene="";private String overlayKey="";
    private final ArrayDeque<Segment> segments=new ArrayDeque<>();private final int[] counters=new int[8192];
    private final String id=UUID.randomUUID().toString().replace("-","");private OverlayTsWriter writer;private File part,finalFile;private byte[] config;private long startUs,sequence,lastPts=-1;
    private static final class Segment {final String name;final long sequence;final double duration;Segment(String n,long s,double d){name=n;sequence=s;duration=d;}}
    public LiveGpuCompositor(Context context,String directory,Listener listener){this.context=context.getApplicationContext();this.directory=new File(directory);this.listener=listener;this.directory.mkdirs();}
    public void start(String url){recorder.reset(generation);thread.start();gl=new Handler(thread.getLooper());gl.post(()->initialize(url));}
    private void initialize(String url){try{
        MediaFormat format=MediaFormat.createVideoFormat("video/avc",W,H);format.setInteger(MediaFormat.KEY_COLOR_FORMAT,MediaCodecInfo.CodecCapabilities.COLOR_FormatSurface);
        format.setInteger(MediaFormat.KEY_FRAME_RATE,30);format.setInteger(MediaFormat.KEY_BIT_RATE,6000000);format.setFloat(MediaFormat.KEY_I_FRAME_INTERVAL,0.5f);format.setInteger(MediaFormat.KEY_MAX_B_FRAMES,0);
        encoder=MediaCodec.createEncoderByType("video/avc");encoder.configure(format,null,null,MediaCodec.CONFIGURE_FLAG_ENCODE);encoderInput=encoder.createInputSurface();encoder.start();
        gpu=new Gpu(encoderInput);decoderTexture=new SurfaceTexture(gpu.external);decoderTexture.setDefaultBufferSize(W,H);decoderOutput=new Surface(decoderTexture);
        decoderTexture.setOnFrameAvailableListener(t->{if(closed)return;if(!framePending.compareAndSet(false,true)){replaced++;return;}gl.post(this::frame);},gl);
        main.post(()->{if(closed)return;player=new ExoPlayer.Builder(context).setLoadControl(new DefaultLoadControl.Builder().setBufferDurationsMs(250,2000,100,250).build()).build();
            player.setVideoSurface(decoderOutput);player.setVolume(0);player.setVideoFrameMetadataListener((pts,release,f,m)->recorder.frame(pts,release));
            player.addListener(new Player.Listener(){public void onIsPlayingChanged(boolean value){playing=value;}
                public void onPlayerError(PlaybackException e){playing=false;main.postDelayed(()->{if(!closed)play(url);},1000);}
                public void onPlaybackStateChanged(int state){if(state==Player.STATE_ENDED&&!closed)main.postDelayed(()->{if(!closed)play(url);},100);}
                public void onVideoSizeChanged(VideoSize size){if(size.width>0&&size.height>0&&(size.width!=W||size.height!=H)){geometry="";scene="";error="Source shape changed; recalibration required";}}
            });play(url);
        });gl.post(drainLoop);
    }catch(Exception e){fail(e);}}
    private void play(String url){
        generation++;recorder.reset(generation);scene="";gl.post(()->{scenes.reset();overlayKey="";});
        long expected=generation;androidx.media3.common.util.TimestampAdjuster adjuster=new androidx.media3.common.util.TimestampAdjuster(0);
        androidx.media3.extractor.ExtractorsFactory extractors=()->{recorder.extractor(adjuster,expected);return new androidx.media3.extractor.Extractor[]{new androidx.media3.extractor.ts.TsExtractor(androidx.media3.extractor.ts.TsExtractor.MODE_SINGLE_PMT,adjuster,new androidx.media3.extractor.ts.DefaultTsPayloadReaderFactory())};};
        if(url.contains("/live/stream.ts"))player.setMediaSource(new androidx.media3.exoplayer.source.ProgressiveMediaSource.Factory(new androidx.media3.datasource.DefaultDataSource.Factory(context),extractors).setContinueLoadingCheckIntervalBytes(16384).createMediaSource(new MediaItem.Builder().setUri(url).setMimeType(MimeTypes.VIDEO_MP2T).build()));
        else{androidx.media3.exoplayer.hls.DefaultHlsExtractorFactory delegate=new androidx.media3.exoplayer.hls.DefaultHlsExtractorFactory();
            androidx.media3.exoplayer.hls.HlsExtractorFactory factory=(uri,f,c,a,h,i,tid)->{recorder.extractor(a,expected);return delegate.createExtractor(uri,f,c,a,h,i,tid);};
            player.setMediaSource(new androidx.media3.exoplayer.hls.HlsMediaSource.Factory(new androidx.media3.datasource.DefaultDataSource.Factory(context)).setExtractorFactory(factory).createMediaSource(new MediaItem.Builder().setUri(url).setLiveConfiguration(new MediaItem.LiveConfiguration.Builder().setTargetOffsetMs(2500).build()).build()));}
        player.prepare();player.play();
    }
    public long getGeneration(){return generation;}
    public void setClock(String timeline,long sourceUs){recorder.clock(timeline,sourceUs,generation);}
    public void setGeometry(String json){if(json!=null&&json.length()<=32768)geometry=json;}
    public void setScene(String json){scene=json==null?"":json;if(scene.isEmpty()&&gl!=null)gl.post(()->{scenes.clear();overlayKey="";});}
    public static boolean receiverConnected(Context context){try{com.google.android.gms.cast.framework.CastSession session=com.google.android.gms.cast.framework.CastContext.getSharedInstance(context).getSessionManager().getCurrentCastSession();return session!=null&&session.isConnected();}catch(Exception e){return false;}}
    public String drainAnnotations(){return recorder.drain();}
    private void frame(){framePending.set(false);if(closed||gpu==null)return;try{
        decoderTexture.updateTexImage();recorder.textureUpdated();
        String stamp=recorder.frameMetrics(generation);JSONObject source=new JSONObject(stamp);long sourceUs=source.optLong("sourceUs",-1);
        if(sourceUs<0||sourceUs<=lastPts)return;lastPts=sourceUs;
        float[] textureMatrix=new float[16];decoderTexture.getTransformMatrix(textureMatrix);
        JSONObject transform=geometry.isEmpty()?null:new JSONObject(geometry);float[] matrix={W,0,0,0,H,0,0,0,1};
        Matrix image=new Matrix();boolean corrected=false;
        if(transform!=null){JSONArray values=transform.getJSONArray("values");float sx=W/(float)transform.getInt("width"),sy=H/(float)transform.getInt("height");
            for(int j=0;j<9;j++)matrix[j]=(float)values.getDouble(j);for(int j=0;j<3;j++)matrix[j]*=sx;for(int j=3;j<6;j++)matrix[j]*=sy;corrected=true;}
        float[] pixel=matrix.clone();for(int j:new int[]{0,3,6})pixel[j]/=W;for(int j:new int[]{1,4,7})pixel[j]/=H;image.setValues(pixel);
        if(!scene.isEmpty()){scenes.set(scene,generation);scene="";}
        JSONObject snapshot=scenes.snapshot(System.currentTimeMillis());String key=geometry+":"+(snapshot==null?"clear":snapshot.toString());
        if(!key.equals(overlayKey)){overlayKey=key;Bitmap overlay=Bitmap.createBitmap(W,H,Bitmap.Config.ARGB_8888);try{
            Canvas canvas=new Canvas(overlay);canvas.save();canvas.concat(image);scenes.geometry(canvas,W,H,W/(float)H,snapshot);canvas.restore();scenes.textLayer(canvas,W,H,image,snapshot);WatermarkRenderer.draw(context,canvas,W,H);gpu.overlay(overlay);
        }finally{overlay.recycle();}}
        gpu.draw(textureMatrix,matrix,sourceUs);frames++;recorder.drawn(snapshot,geometry,true,generation);
        frameMetrics=scenes.metrics(snapshot,generation,0).put("sourceUs",sourceUs).put("rectangular",corrected).put("watermarked",true).toString();drain();
    }catch(Exception e){fail(e);}}
    private final Runnable drainLoop=new Runnable(){public void run(){if(closed)return;try{drain();}catch(Exception e){fail(e);}if(!closed)gl.postDelayed(this,10);}};
    private void drain()throws Exception{MediaCodec.BufferInfo info=new MediaCodec.BufferInfo();for(int n=0;n<64;n++){
        int index=encoder.dequeueOutputBuffer(info,0);if(index==MediaCodec.INFO_OUTPUT_FORMAT_CHANGED){ByteArrayOutputStream out=new ByteArrayOutputStream();MediaFormat f=encoder.getOutputFormat();for(String key:new String[]{"csd-0","csd-1"}){ByteBuffer b=f.getByteBuffer(key);if(b!=null){byte[] bytes=new byte[b.remaining()];b.duplicate().get(bytes);out.write(OverlayTsWriter.annex(bytes));}}config=out.toByteArray();continue;}if(index<0)break;
        try{if(info.size<=0||(info.flags&MediaCodec.BUFFER_FLAG_CODEC_CONFIG)!=0)continue;boolean key=(info.flags&MediaCodec.BUFFER_FLAG_KEY_FRAME)!=0;
            if(key&&(writer==null||info.presentationTimeUs-startUs>=450000)){if(writer!=null){writer.close();complete((info.presentationTimeUs-startUs)/1000000d);}startUs=info.presentationTimeUs;sequence++;finalFile=new File(directory,id+"-"+sequence+".ts");part=new File(finalFile+".part");writer=new OverlayTsWriter(part,counters);}
            if(writer!=null){ByteBuffer b=encoder.getOutputBuffer(index);b.position(info.offset);b.limit(info.offset+info.size);byte[] bytes=new byte[info.size];b.get(bytes);writer.video(bytes,info.presentationTimeUs,key,config);encodedUs=info.presentationTimeUs;}
        }finally{encoder.releaseOutputBuffer(index,false);}
    }}
    private synchronized void complete(double duration)throws IOException{if(!part.renameTo(finalFile))throw new IOException("Segment publication failed");segments.add(new Segment(finalFile.getName(),sequence,duration));while(segments.size()>16)new File(directory,segments.removeFirst().name).delete();}
    // Keep at least three target durations available for ordinary HLS receivers,
    // while using short independent segments instead of a 12-second playlist.
    private List<Segment> window(){List<Segment> list=new ArrayList<>(segments);return list.subList(Math.max(0,list.size()-8),list.size());}
    public synchronized boolean ready(){if(segments.size()<6)return false;List<Segment> list=window();double maximum=list.stream().mapToDouble(s->s.duration).max().orElse(1);return list.stream().mapToDouble(s->s.duration).sum()+0.001>=3*Math.ceil(maximum);}
    public synchronized String playlist(String access){if(!ready())return "";List<Segment> list=window();StringBuilder text=new StringBuilder("#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-INDEPENDENT-SEGMENTS\n");text.append("#EXT-X-TARGETDURATION:").append((int)Math.ceil(list.stream().mapToDouble(s->s.duration).max().orElse(1))).append("\n#EXT-X-MEDIA-SEQUENCE:").append(list.get(0).sequence).append('\n');for(Segment s:list)text.append("#EXTINF:").append(String.format(Locale.ROOT,"%.6f",s.duration)).append(",\n").append(s.name).append("?access=").append(access).append('\n');return text.toString();}
    public synchronized String metrics(){try{return new JSONObject().put("serviceOwned",true).put("gpuVideo",true).put("cpuVideoCopies",0).put("playing",playing).put("generation",generation).put("frames",frames).put("replacedFrames",replaced).put("segments",segments.size()).put("encodedSourceUs",encodedUs).put("frame",new JSONObject(frameMetrics)).put("error",error).toString();}catch(Exception e){return "{}";}}
    private void fail(Exception e){error=e.getClass().getSimpleName()+": "+e.getMessage();main.post(()->listener.onError("Background TV pipeline interrupted: "+e.getClass().getSimpleName()));}
    public void close(){closed=true;main.post(()->{if(player!=null){player.release();player=null;}if(gl!=null)gl.post(()->{try{if(writer!=null)writer.close();if(part!=null)part.delete();if(decoderOutput!=null)decoderOutput.release();if(decoderTexture!=null)decoderTexture.release();if(gpu!=null)gpu.close();if(encoder!=null){encoder.stop();encoder.release();}if(encoderInput!=null)encoderInput.release();}catch(Exception ignored){}finally{File[] files=directory.listFiles();if(files!=null)for(File f:files)if(f.isFile()&&(f.getName().endsWith(".ts")||f.getName().endsWith(".part")))f.delete();directory.delete();thread.quitSafely();}});});}
    private static final class Gpu implements AutoCloseable {
        final EGLDisplay display;final EGLContext context;final EGLSurface surface;final int videoProgram,overlayProgram,external,overlayTexture;final FloatBuffer positions;
        Gpu(Surface target){display=EGL14.eglGetDisplay(EGL14.EGL_DEFAULT_DISPLAY);int[] version=new int[2];if(!EGL14.eglInitialize(display,version,0,version,1))throw new IllegalStateException("EGL unavailable");EGLConfig[] configs=new EGLConfig[1];int[] count=new int[1];EGL14.eglChooseConfig(display,new int[]{EGL14.EGL_RED_SIZE,8,EGL14.EGL_GREEN_SIZE,8,EGL14.EGL_BLUE_SIZE,8,EGL14.EGL_ALPHA_SIZE,8,EGL14.EGL_RENDERABLE_TYPE,EGL14.EGL_OPENGL_ES2_BIT,0x3142,1,EGL14.EGL_NONE},0,configs,0,1,count,0);if(count[0]==0)throw new IllegalStateException("Encoder EGL unavailable");context=EGL14.eglCreateContext(display,configs[0],EGL14.EGL_NO_CONTEXT,new int[]{EGL14.EGL_CONTEXT_CLIENT_VERSION,2,EGL14.EGL_NONE},0);surface=EGL14.eglCreateWindowSurface(display,configs[0],target,new int[]{EGL14.EGL_NONE},0);if(!EGL14.eglMakeCurrent(display,surface,surface,context))throw new IllegalStateException("Encoder EGL context unavailable");
            videoProgram=program("attribute vec2 p;uniform mat3 warp;uniform mat4 st;varying vec2 uv;void main(){vec3 o=warp*vec3(p,1.);gl_Position=vec4(2.*o.x/1920.-o.z,o.z-2.*o.y/1080.,0.,o.z);uv=(st*vec4(p.x,1.-p.y,0.,1.)).xy;}","#extension GL_OES_EGL_image_external : require\nprecision mediump float;uniform samplerExternalOES tex;varying vec2 uv;void main(){gl_FragColor=texture2D(tex,uv);}");
            overlayProgram=program("attribute vec2 p;varying vec2 uv;void main(){gl_Position=vec4(2.*p.x-1.,1.-2.*p.y,0.,1.);uv=p;}","precision mediump float;uniform sampler2D tex;varying vec2 uv;void main(){gl_FragColor=texture2D(tex,uv);}");
            int[] ids=new int[2];GLES20.glGenTextures(2,ids,0);external=ids[0];overlayTexture=ids[1];setup(GLES11Ext.GL_TEXTURE_EXTERNAL_OES,external);setup(GLES20.GL_TEXTURE_2D,overlayTexture);positions=ByteBuffer.allocateDirect(8*4).order(ByteOrder.nativeOrder()).asFloatBuffer();positions.put(new float[]{0,1,1,1,0,0,1,0}).position(0);
        }
        static void setup(int target,int texture){GLES20.glBindTexture(target,texture);GLES20.glTexParameteri(target,GLES20.GL_TEXTURE_MIN_FILTER,GLES20.GL_LINEAR);GLES20.glTexParameteri(target,GLES20.GL_TEXTURE_MAG_FILTER,GLES20.GL_LINEAR);GLES20.glTexParameteri(target,GLES20.GL_TEXTURE_WRAP_S,GLES20.GL_CLAMP_TO_EDGE);GLES20.glTexParameteri(target,GLES20.GL_TEXTURE_WRAP_T,GLES20.GL_CLAMP_TO_EDGE);}
        static int program(String vertex,String fragment){int p=GLES20.glCreateProgram();GLES20.glAttachShader(p,shader(GLES20.GL_VERTEX_SHADER,vertex));GLES20.glAttachShader(p,shader(GLES20.GL_FRAGMENT_SHADER,fragment));GLES20.glLinkProgram(p);int[] ok=new int[1];GLES20.glGetProgramiv(p,GLES20.GL_LINK_STATUS,ok,0);if(ok[0]==0)throw new IllegalStateException(GLES20.glGetProgramInfoLog(p));return p;}
        static int shader(int type,String code){int s=GLES20.glCreateShader(type);GLES20.glShaderSource(s,code);GLES20.glCompileShader(s);int[] ok=new int[1];GLES20.glGetShaderiv(s,GLES20.GL_COMPILE_STATUS,ok,0);if(ok[0]==0)throw new IllegalStateException(GLES20.glGetShaderInfoLog(s));return s;}
        void overlay(Bitmap bitmap){GLES20.glBindTexture(GLES20.GL_TEXTURE_2D,overlayTexture);GLUtils.texImage2D(GLES20.GL_TEXTURE_2D,0,bitmap,0);}
        void quad(int program,int target,int texture){GLES20.glUseProgram(program);GLES20.glActiveTexture(GLES20.GL_TEXTURE0);GLES20.glBindTexture(target,texture);GLES20.glUniform1i(GLES20.glGetUniformLocation(program,"tex"),0);int p=GLES20.glGetAttribLocation(program,"p");GLES20.glEnableVertexAttribArray(p);GLES20.glVertexAttribPointer(p,2,GLES20.GL_FLOAT,false,0,positions);GLES20.glDrawArrays(GLES20.GL_TRIANGLE_STRIP,0,4);}
        void draw(float[] texture,float[] row,long pts){GLES20.glViewport(0,0,W,H);GLES20.glClearColor(0,0,0,1);GLES20.glClear(GLES20.GL_COLOR_BUFFER_BIT);GLES20.glDisable(GLES20.GL_BLEND);GLES20.glUseProgram(videoProgram);float[] column={row[0],row[3],row[6],row[1],row[4],row[7],row[2],row[5],row[8]};GLES20.glUniformMatrix3fv(GLES20.glGetUniformLocation(videoProgram,"warp"),1,false,column,0);GLES20.glUniformMatrix4fv(GLES20.glGetUniformLocation(videoProgram,"st"),1,false,texture,0);quad(videoProgram,GLES11Ext.GL_TEXTURE_EXTERNAL_OES,external);GLES20.glEnable(GLES20.GL_BLEND);GLES20.glBlendFunc(GLES20.GL_ONE,GLES20.GL_ONE_MINUS_SRC_ALPHA);quad(overlayProgram,GLES20.GL_TEXTURE_2D,overlayTexture);GLES20.glDisable(GLES20.GL_BLEND);EGLExt.eglPresentationTimeANDROID(display,surface,pts*1000);if(!EGL14.eglSwapBuffers(display,surface))throw new IllegalStateException("Encoder swap failed");}
        public void close(){GLES20.glDeleteTextures(2,new int[]{external,overlayTexture},0);GLES20.glDeleteProgram(videoProgram);GLES20.glDeleteProgram(overlayProgram);EGL14.eglMakeCurrent(display,EGL14.EGL_NO_SURFACE,EGL14.EGL_NO_SURFACE,EGL14.EGL_NO_CONTEXT);EGL14.eglDestroySurface(display,surface);EGL14.eglDestroyContext(display,context);EGL14.eglTerminate(display);}
    }
}
