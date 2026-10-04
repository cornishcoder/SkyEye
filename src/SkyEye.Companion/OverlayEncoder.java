package uk.co.skyeye.companion.nativebridge;

import android.app.Activity;
import android.graphics.*;
import android.media.*;
import android.opengl.*;
import android.os.*;
import android.view.Surface;
import java.io.*;
import java.nio.*;
import java.util.*;
import java.util.concurrent.*;

/** Bounded 960x540, 10fps, silent AVC feed of the main live view with annotations.
 * Camera archive/capture is independent. Only two reusable bitmaps may be in flight. */
public final class OverlayEncoder {
    public interface Listener { void onError(String message); }
    private final PlayerBridge source;private final Listener listener;private final File directory;
    private final Handler main=new Handler(Looper.getMainLooper());
    private final ArrayBlockingQueue<Bitmap> free=new ArrayBlockingQueue<>(2);
    private final ArrayBlockingQueue<CapturedFrame> pending=new ArrayBlockingQueue<>(1);
    private static final class CapturedFrame {final Bitmap image;final String metadata;CapturedFrame(Bitmap image,String metadata){this.image=image;this.metadata=metadata;}}
    private final ArrayDeque<Segment> segments=new ArrayDeque<>();private final int[] counters=new int[8192];
    private final String generation=UUID.randomUUID().toString().replace("-","");
    private volatile boolean closed;private Thread worker;private volatile long frames,skipped,encodedPtsUs;private volatile String error="",encodedFrame="{}";
    private static final class Segment { final String name;final long sequence;final double duration;Segment(String name,long sequence,double duration){this.name=name;this.sequence=sequence;this.duration=duration;}String name(){return name;}long sequence(){return sequence;}double duration(){return duration;} }
    public OverlayEncoder(Activity activity,PlayerBridge source,String directory,Listener listener){this.source=source;this.listener=listener;this.directory=new File(directory);this.directory.mkdirs();}
    public void start(){for(int i=0;i<2;i++)free.add(Bitmap.createBitmap(960,540,Bitmap.Config.ARGB_8888));worker=new Thread(this::encode,"SkyEyeOverlayVideo");worker.start();main.post(capture);}
    private final Runnable capture=new Runnable(){public void run(){if(closed)return;
        if(pending.isEmpty()){Bitmap image=free.poll();if(image!=null){try{String metadata=source.captureFrame(image);if(metadata==null){metadata="{}";image.eraseColor(Color.BLACK);Paint p=new Paint(Paint.ANTI_ALIAS_FLAG);p.setColor(Color.WHITE);p.setTextSize(26);new Canvas(image).drawText("Waiting for live video…",24,48,p);}if(!pending.offer(new CapturedFrame(image,metadata)))free.offer(image);}catch(Exception e){free.offer(image);}}else skipped++;}else skipped++;
        main.postDelayed(this,100);
    }};
    public synchronized boolean ready(){return segments.size()>=2;}
    public synchronized String playlist(String access){if(!ready())return "";StringBuilder text=new StringBuilder("#EXTM3U\n#EXT-X-VERSION:3\n#EXT-X-INDEPENDENT-SEGMENTS\n");
        List<Segment> list=new ArrayList<>(segments);list=list.subList(Math.max(0,list.size()-6),list.size());
        text.append("#EXT-X-TARGETDURATION:").append((int)Math.ceil(list.stream().mapToDouble(Segment::duration).max().orElse(3))).append("\n#EXT-X-MEDIA-SEQUENCE:").append(list.get(0).sequence()).append('\n');
        for(Segment s:list)text.append("#EXTINF:").append(String.format(Locale.ROOT,"%.6f",s.duration())).append(",\n").append(s.name()).append("?access=").append(access).append('\n');return text.toString();
    }
    public synchronized String metrics(){return "{\"width\":960,\"height\":540,\"fps\":10,\"frames\":"+frames+",\"skipped\":"+skipped+",\"segments\":"+segments.size()+",\"failed\":"+(!error.isEmpty())+",\"encodedPtsUs\":"+encodedPtsUs+",\"encodedFrame\":"+encodedFrame+",\"phoneFrame\":"+source.sceneMetrics()+",\"presentation\":"+source.presentation()+"}";}
    private synchronized void completed(File temporary,File finalFile,long sequence,double duration)throws IOException{
        if(!temporary.renameTo(finalFile))throw new IOException("Overlay segment could not be finalized");segments.add(new Segment(finalFile.getName(),sequence,duration));
        while(segments.size()>16){Segment old=segments.remove();new File(directory,old.name()).delete();}
    }
    private void encode(){MediaCodec codec=null;Surface input=null;Egl egl=null;OverlayTsWriter writer=null;File temporary=null,finalFile=null;
        try{MediaFormat format=MediaFormat.createVideoFormat("video/avc",960,540);format.setInteger(MediaFormat.KEY_COLOR_FORMAT,MediaCodecInfo.CodecCapabilities.COLOR_FormatSurface);format.setInteger(MediaFormat.KEY_FRAME_RATE,10);format.setInteger(MediaFormat.KEY_BIT_RATE,2000000);format.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL,2);
            codec=MediaCodec.createEncoderByType("video/avc");codec.configure(format,null,null,MediaCodec.CONFIGURE_FLAG_ENCODE);input=codec.createInputSurface();codec.start();egl=new Egl(input);
            MediaCodec.BufferInfo info=new MediaCodec.BufferInfo();byte[] config=null;long origin=System.nanoTime(),nextSync=2000000,start=0,last=0,sequence=0;TreeMap<Long,String> submitted=new TreeMap<>();
            while(!closed){CapturedFrame frame=pending.poll(200,TimeUnit.MILLISECONDS);if(frame==null)continue;Bitmap bitmap=frame.image;long pts=(System.nanoTime()-origin)/1000;
                if(pts>=nextSync){Bundle sync=new Bundle();sync.putInt(MediaCodec.PARAMETER_KEY_REQUEST_SYNC_FRAME,0);codec.setParameters(sync);nextSync=pts+2000000;}
                try{egl.draw(bitmap,pts);submitted.put(pts,frame.metadata);while(submitted.size()>64)submitted.pollFirstEntry();}finally{free.offer(bitmap);}frames++;
                for(int n=0;n<64;n++){int index=codec.dequeueOutputBuffer(info,0);if(index==MediaCodec.INFO_OUTPUT_FORMAT_CHANGED){ByteArrayOutputStream bytes=new ByteArrayOutputStream();MediaFormat output=codec.getOutputFormat();for(String key:new String[]{"csd-0","csd-1"}){ByteBuffer c=output.getByteBuffer(key);if(c!=null){byte[] raw=new byte[c.remaining()];c.duplicate().get(raw);bytes.write(OverlayTsWriter.annex(raw));}}config=bytes.toByteArray();continue;}if(index<0)break;
                    try{if(info.size==0||(info.flags&MediaCodec.BUFFER_FLAG_CODEC_CONFIG)!=0)continue;boolean key=(info.flags&MediaCodec.BUFFER_FLAG_KEY_FRAME)!=0;
                        if(key&&(writer==null||info.presentationTimeUs-start>=1900000)){if(writer!=null){writer.close();completed(temporary,finalFile,sequence,(info.presentationTimeUs-start)/1000000.0);}start=info.presentationTimeUs;sequence++;finalFile=new File(directory,generation+"-"+sequence+".ts");temporary=new File(finalFile.getPath()+".part");writer=new OverlayTsWriter(temporary,counters);}
                        if(writer!=null){ByteBuffer output=codec.getOutputBuffer(index);output.position(info.offset);output.limit(info.offset+info.size);byte[] bytes=new byte[info.size];output.get(bytes);writer.video(bytes,info.presentationTimeUs,key,config);last=info.presentationTimeUs;String actual=submitted.remove(info.presentationTimeUs);encodedFrame=actual==null?"{}":actual;encodedPtsUs=info.presentationTimeUs;}
                    }finally{codec.releaseOutputBuffer(index,false);}
                }
            }
        }catch(Exception e){if(!closed){error=e.getClass().getSimpleName();main.post(()->listener.onError("Annotated TV view could not start. Toggle it off and try again."));}}
        finally{main.removeCallbacks(capture);try{if(writer!=null)writer.close();}catch(Exception ignored){}if(temporary!=null)temporary.delete();if(egl!=null)egl.close();if(codec!=null){try{codec.stop();}catch(Exception ignored){}codec.release();}if(input!=null)input.release();for(Bitmap b:free)b.recycle();for(CapturedFrame f:pending)f.image.recycle();}
    }
    public void close(){closed=true;main.removeCallbacks(capture);if(worker!=null)worker.interrupt();}
    private static final class Egl implements AutoCloseable {
        final EGLDisplay display;final EGLContext context;final EGLSurface surface;final int program,texture;final FloatBuffer vertices,uv;
        Egl(Surface target){display=EGL14.eglGetDisplay(EGL14.EGL_DEFAULT_DISPLAY);int[] version=new int[2];if(!EGL14.eglInitialize(display,version,0,version,1))throw new IllegalStateException("EGL unavailable");
            EGLConfig[] configs=new EGLConfig[1];int[] count=new int[1];int[] attrs={EGL14.EGL_RED_SIZE,8,EGL14.EGL_GREEN_SIZE,8,EGL14.EGL_BLUE_SIZE,8,EGL14.EGL_ALPHA_SIZE,8,EGL14.EGL_RENDERABLE_TYPE,EGL14.EGL_OPENGL_ES2_BIT,0x3142,1,EGL14.EGL_NONE};
            EGL14.eglChooseConfig(display,attrs,0,configs,0,1,count,0);if(count[0]==0)throw new IllegalStateException("No encoder EGL configuration");context=EGL14.eglCreateContext(display,configs[0],EGL14.EGL_NO_CONTEXT,new int[]{EGL14.EGL_CONTEXT_CLIENT_VERSION,2,EGL14.EGL_NONE},0);surface=EGL14.eglCreateWindowSurface(display,configs[0],target,new int[]{EGL14.EGL_NONE},0);if(!EGL14.eglMakeCurrent(display,surface,surface,context))throw new IllegalStateException("Encoder surface unavailable");
            program=GLES20.glCreateProgram();GLES20.glAttachShader(program,shader(GLES20.GL_VERTEX_SHADER,"attribute vec4 p;attribute vec2 t;varying vec2 v;void main(){gl_Position=p;v=t;}"));GLES20.glAttachShader(program,shader(GLES20.GL_FRAGMENT_SHADER,"precision mediump float;uniform sampler2D s;varying vec2 v;void main(){gl_FragColor=texture2D(s,v);}"));GLES20.glLinkProgram(program);int[] linked=new int[1];GLES20.glGetProgramiv(program,GLES20.GL_LINK_STATUS,linked,0);if(linked[0]==0)throw new IllegalStateException("Encoder shader link failed");
            int[] ids=new int[1];GLES20.glGenTextures(1,ids,0);texture=ids[0];GLES20.glBindTexture(GLES20.GL_TEXTURE_2D,texture);GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D,GLES20.GL_TEXTURE_MIN_FILTER,GLES20.GL_LINEAR);GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D,GLES20.GL_TEXTURE_MAG_FILTER,GLES20.GL_LINEAR);GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D,GLES20.GL_TEXTURE_WRAP_S,GLES20.GL_CLAMP_TO_EDGE);GLES20.glTexParameteri(GLES20.GL_TEXTURE_2D,GLES20.GL_TEXTURE_WRAP_T,GLES20.GL_CLAMP_TO_EDGE);
            vertices=buffer(new float[]{-1,-1,1,-1,-1,1,1,1});uv=buffer(new float[]{0,1,1,1,0,0,1,0});
        }
        static FloatBuffer buffer(float[] data){FloatBuffer b=ByteBuffer.allocateDirect(data.length*4).order(ByteOrder.nativeOrder()).asFloatBuffer();b.put(data).position(0);return b;}
        static int shader(int type,String source){int id=GLES20.glCreateShader(type);GLES20.glShaderSource(id,source);GLES20.glCompileShader(id);int[] ok=new int[1];GLES20.glGetShaderiv(id,GLES20.GL_COMPILE_STATUS,ok,0);if(ok[0]==0)throw new IllegalStateException("Encoder shader compile failed");return id;}
        void draw(Bitmap bitmap,long pts){GLES20.glViewport(0,0,960,540);GLES20.glUseProgram(program);GLES20.glBindTexture(GLES20.GL_TEXTURE_2D,texture);GLUtils.texImage2D(GLES20.GL_TEXTURE_2D,0,bitmap,0);int position=GLES20.glGetAttribLocation(program,"p"),coord=GLES20.glGetAttribLocation(program,"t");GLES20.glEnableVertexAttribArray(position);GLES20.glEnableVertexAttribArray(coord);GLES20.glVertexAttribPointer(position,2,GLES20.GL_FLOAT,false,0,vertices);GLES20.glVertexAttribPointer(coord,2,GLES20.GL_FLOAT,false,0,uv);GLES20.glDrawArrays(GLES20.GL_TRIANGLE_STRIP,0,4);EGLExt.eglPresentationTimeANDROID(display,surface,pts*1000);if(!EGL14.eglSwapBuffers(display,surface))throw new IllegalStateException("Encoder frame unavailable");}
        public void close(){GLES20.glDeleteTextures(1,new int[]{texture},0);GLES20.glDeleteProgram(program);EGL14.eglMakeCurrent(display,EGL14.EGL_NO_SURFACE,EGL14.EGL_NO_SURFACE,EGL14.EGL_NO_CONTEXT);EGL14.eglDestroySurface(display,surface);EGL14.eglDestroyContext(display,context);EGL14.eglTerminate(display);}
    }
}
