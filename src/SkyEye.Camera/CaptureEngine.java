package uk.co.skyeye.camera.nativebridge;

import android.content.Context;
import android.hardware.camera2.*;
import android.media.*;
import android.os.*;
import android.view.Surface;
import java.io.*;
import java.nio.ByteBuffer;
import java.util.*;

/** One continuous hardware encode. MP4 archive has audio; live TS deliberately does not. */
public final class CaptureEngine {
    public interface Listener { void onReady(); void onSegment(String name,long sequence,String timeline,long startUs,double duration,long wallMs); void onError(String message); }
    private final Context context; private final File directory; private final Listener listener;
    private final String instance=UUID.randomUUID().toString().replace("-","");
    private HandlerThread cameraThread; private Handler handler;
    private CameraDevice camera; private CameraCaptureSession session; private CaptureRequest request;
    private MediaCodec video,audio; private AudioRecord microphone; private Surface surface;
    private MediaFormat videoFormat,audioFormat; private Thread pump, sound;
    private volatile boolean closed,active,ready,boundary;
    private long sequence,startUs,lastVideoUs,wallMs,lastAudioUs; private String name,timeline;
    private MediaMuxer mux; private int videoTrack,audioTrack; private TsWriter ts; private byte[] config;
    private boolean reportedError;
    private final java.util.concurrent.atomic.AtomicBoolean disposing=new java.util.concurrent.atomic.AtomicBoolean();
    private final int[] transportCounters=new int[8192];
    private static final class AudioSample { final byte[] bytes; final long pts; AudioSample(byte[] bytes,long pts){this.bytes=bytes;this.pts=pts;} }
    private final ArrayDeque<AudioSample> pendingAudio=new ArrayDeque<>();
    public CaptureEngine(Context context,String directory,Listener listener) { this.context=context.getApplicationContext();this.directory=new File(directory);this.listener=listener;this.directory.mkdirs(); }
    public void arm() {
        try {
            cameraThread=new HandlerThread("SkyEyeCamera");cameraThread.start();handler=new Handler(cameraThread.getLooper());
            MediaFormat vf=MediaFormat.createVideoFormat("video/avc",1920,1080);
            vf.setInteger(MediaFormat.KEY_COLOR_FORMAT,MediaCodecInfo.CodecCapabilities.COLOR_FormatSurface);
            vf.setInteger(MediaFormat.KEY_BIT_RATE,6000000);vf.setInteger(MediaFormat.KEY_FRAME_RATE,30);vf.setInteger(MediaFormat.KEY_I_FRAME_INTERVAL,1);
            video=MediaCodec.createEncoderByType("video/avc");video.configure(vf,null,null,MediaCodec.CONFIGURE_FLAG_ENCODE);surface=video.createInputSurface();video.start();
            MediaFormat af=MediaFormat.createAudioFormat("audio/mp4a-latm",48000,1);af.setInteger(MediaFormat.KEY_AAC_PROFILE,MediaCodecInfo.CodecProfileLevel.AACObjectLC);af.setInteger(MediaFormat.KEY_BIT_RATE,96000);
            audio=MediaCodec.createEncoderByType("audio/mp4a-latm");audio.configure(af,null,null,MediaCodec.CONFIGURE_FLAG_ENCODE);audio.start();
            int min=AudioRecord.getMinBufferSize(48000,AudioFormat.CHANNEL_IN_MONO,AudioFormat.ENCODING_PCM_16BIT);
            microphone=new AudioRecord(MediaRecorder.AudioSource.CAMCORDER,48000,AudioFormat.CHANNEL_IN_MONO,AudioFormat.ENCODING_PCM_16BIT,Math.max(16384,min*2));
            if(microphone.getState()!=AudioRecord.STATE_INITIALIZED) throw new IOException("Microphone unavailable");
            pump=new Thread(this::drain,"SkyEyeEncoding");pump.start();sound=new Thread(this::record,"SkyEyeRoomAudio");sound.start();
            CameraManager manager=(CameraManager)context.getSystemService(Context.CAMERA_SERVICE);String id=null;
            for(String candidate:manager.getCameraIdList()) { if(Integer.valueOf(CameraCharacteristics.LENS_FACING_BACK).equals(manager.getCameraCharacteristics(candidate).get(CameraCharacteristics.LENS_FACING))) {id=candidate;break;} }
            if(id==null) throw new IOException("Rear camera unavailable");
            manager.openCamera(id,new CameraDevice.StateCallback(){
                public void onOpened(CameraDevice value){ if(closed){value.close();return;}camera=value;
                    try { camera.createCaptureSession(Collections.singletonList(surface),new CameraCaptureSession.StateCallback(){
                        public void onConfigured(CameraCaptureSession value){ if(closed){value.close();return;}session=value;try {
                            CaptureRequest.Builder b=camera.createCaptureRequest(CameraDevice.TEMPLATE_RECORD);b.addTarget(surface);
                            b.set(CaptureRequest.CONTROL_AF_MODE,CaptureRequest.CONTROL_AF_MODE_CONTINUOUS_VIDEO);
                            b.set(CaptureRequest.CONTROL_AE_TARGET_FPS_RANGE,new android.util.Range<Integer>(30,30));
                            request=b.build();ready=true;listener.onReady();
                        }catch(Exception e){fail(e);}}
                        public void onConfigureFailed(CameraCaptureSession value){ fail(new IOException("Camera session configuration failed")); }
                    },handler); }catch(Exception e){fail(e);}
                }
                public void onDisconnected(CameraDevice value){value.close();fail(new IOException("Camera disconnected"));}
                public void onError(CameraDevice value,int error){value.close();fail(new IOException("Camera error "+error));}
            },handler);
        }catch(Exception e){fail(e);}
    }
    public synchronized boolean isReady(){return ready&&!closed&&!reportedError;}
    public synchronized void begin(String timeline) throws Exception {
        if(!isReady()) throw new IOException("Camera is not ready. Reopen and arm the camera app.");
        this.timeline=timeline;boundary=false;pendingAudio.clear();active=true;microphone.startRecording();
        Bundle sync=new Bundle();sync.putInt(MediaCodec.PARAMETER_KEY_REQUEST_SYNC_FRAME,0);video.setParameters(sync);
        session.setRepeatingRequest(request,null,handler);
    }
    public synchronized void pause() throws Exception {
        active=false;if(session!=null)session.stopRepeating();if(microphone!=null&&microphone.getRecordingState()==AudioRecord.RECORDSTATE_RECORDING)microphone.stop();flushAudio(lastVideoUs+33333);finish();pendingAudio.clear();
    }
    public synchronized void requestBoundary() { boundary=true;Bundle sync=new Bundle();sync.putInt(MediaCodec.PARAMETER_KEY_REQUEST_SYNC_FRAME,0);video.setParameters(sync); }
    private void record() {
        byte[] pcm=new byte[4096];long base=0,samples=0;boolean wasActive=false;
        try { while(!closed) {
            if(!active){wasActive=false;Thread.sleep(20);continue;}
            if(!wasActive){base=timeUs();samples=0;wasActive=true;}
            int n=microphone.read(pcm,0,pcm.length);if(n<0&&active&&!closed)throw new IOException("Room microphone read failed: "+n);if(n<=0){Thread.sleep(5);continue;}
            int position=0;
            while(position<n&&active&&!closed){int index=audio.dequeueInputBuffer(10000);if(index<0)continue;
                ByteBuffer input=audio.getInputBuffer(index);input.clear();int length=Math.min(input.remaining(),n-position)&~1;
                if(length==0)throw new IOException("Audio encoder input buffer is too small");input.put(pcm,position,length);
                audio.queueInputBuffer(index,0,length,base+(samples+position/2)*1000000/48000,0);position+=length;
            }
            samples+=n/2;
        }}catch(Exception e){if(!closed)fail(e);}
    }
    private void drain() {
        MediaCodec.BufferInfo vi=new MediaCodec.BufferInfo(),ai=new MediaCodec.BufferInfo();
        try { while(!closed){drainOne(video,vi,true);drainOne(audio,ai,false);Thread.sleep(2);} }
        catch(Exception e){if(!closed)fail(e);}
    }
    private void drainOne(MediaCodec codec,MediaCodec.BufferInfo info,boolean isVideo) throws Exception {
        for(int count=0;count<32;count++) {
            int index=codec.dequeueOutputBuffer(info,0);
            if(index==MediaCodec.INFO_OUTPUT_FORMAT_CHANGED){ synchronized(this){ if(isVideo){videoFormat=codec.getOutputFormat();ByteArrayOutputStream c=new ByteArrayOutputStream();for(String key:new String[]{"csd-0","csd-1"}){ByteBuffer b=videoFormat.getByteBuffer(key);if(b!=null){byte[] data=new byte[b.remaining()];b.duplicate().get(data);c.write(TsWriter.annex(data));}}config=c.toByteArray();}else audioFormat=codec.getOutputFormat();}continue; }
            if(index<0)break;
            try { if(info.size>0&&(info.flags&MediaCodec.BUFFER_FLAG_CODEC_CONFIG)==0) { ByteBuffer buffer=codec.getOutputBuffer(index);buffer.position(info.offset);buffer.limit(info.offset+info.size);byte[] bytes=new byte[info.size];buffer.get(bytes);sample(bytes,info,isVideo); } }
            finally {codec.releaseOutputBuffer(index,false);}
        }
    }
    private synchronized void sample(byte[] bytes,MediaCodec.BufferInfo info,boolean isVideo) throws Exception {
        if(!active||closed)return;
        if(!isVideo){pendingAudio.add(new AudioSample(bytes,info.presentationTimeUs));if(pendingAudio.size()>3000)throw new IOException("Audio/video clocks diverged");if(mux!=null)flushAudio(lastVideoUs+33333);return;}
        if(videoFormat==null||audioFormat==null)return;
        boolean key=(info.flags&MediaCodec.BUFFER_FLAG_KEY_FRAME)!=0;
        if(key&&(mux==null||info.presentationTimeUs-startUs>=2000000||boundary)) {if(mux!=null)flushAudio(info.presentationTimeUs-1);finish();boundary=false;startUs=info.presentationTimeUs;wallMs=System.currentTimeMillis();lastVideoUs=startUs;lastAudioUs=-1;
            name=instance+"-"+(++sequence);mux=new MediaMuxer(new File(directory,name+".mp4.part").getPath(),MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);
            videoTrack=mux.addTrack(videoFormat);audioTrack=mux.addTrack(audioFormat);mux.start();ts=new TsWriter(new File(directory,name+".ts.part"),transportCounters);
        }
        if(mux==null||info.presentationTimeUs<startUs)return;
        long relative=info.presentationTimeUs-startUs;
        MediaCodec.BufferInfo out=new MediaCodec.BufferInfo();out.set(0,bytes.length,relative,info.flags&MediaCodec.BUFFER_FLAG_KEY_FRAME);
        mux.writeSampleData(videoTrack,ByteBuffer.wrap(bytes),out);
        lastVideoUs=info.presentationTimeUs;ts.video(bytes,info.presentationTimeUs,key,config);flushAudio(lastVideoUs+33333);
    }
    private void flushAudio(long until) throws Exception {
        if(mux==null)return;
        while(!pendingAudio.isEmpty()&&pendingAudio.peek().pts<=until){AudioSample sample=pendingAudio.remove();long relative=sample.pts-startUs;if(relative<0||relative<=lastAudioUs)continue;
            MediaCodec.BufferInfo out=new MediaCodec.BufferInfo();out.set(0,sample.bytes.length,relative,0);mux.writeSampleData(audioTrack,ByteBuffer.wrap(sample.bytes),out);lastAudioUs=relative;
        }
    }
    private synchronized void finish() throws Exception {
        if(mux==null)return;MediaMuxer done=mux;mux=null;done.stop();done.release();ts.close();ts=null;
        File mp4=new File(directory,name+".mp4"),transport=new File(directory,name+".ts");
        if(!new File(directory,name+".mp4.part").renameTo(mp4)||!new File(directory,name+".ts.part").renameTo(transport))throw new IOException("Cannot finalise video segment");
        listener.onSegment(name,sequence,timeline,startUs,Math.max(0.033333,(lastVideoUs-startUs)/1000000.0+1.0/30),wallMs);
    }
    private void fail(Exception e) { android.util.Log.e("SkyEye", "Capture failed",e);synchronized(this){if(reportedError||closed)return;reportedError=true;active=false;}listener.onError(e.getMessage()==null?e.getClass().getSimpleName():e.getMessage()); }
    public void close() {
        if(!disposing.compareAndSet(false,true))return;
        closed=true;active=false;ready=false;
        try{if(microphone!=null)microphone.stop();}catch(Exception ignored){}
        try{if(session!=null)session.close();}catch(Exception ignored){}try{if(camera!=null)camera.close();}catch(Exception ignored){}
        try{if(pump!=null)pump.join(3000);if(sound!=null)sound.join(3000);}catch(Exception ignored){}
        synchronized(this){try{finish();}catch(Exception ignored){}}
        try{if(video!=null){video.stop();video.release();}if(audio!=null){audio.stop();audio.release();}}catch(Exception ignored){}
        if(microphone!=null)microphone.release();if(surface!=null)surface.release();if(cameraThread!=null)cameraThread.quitSafely();
    }
    // Camera2's default MediaCodec output uses MONOTONIC, which excludes device sleep.
    // Sensor REALTIME timestamps do not determine the encoder output clock.
    public static long timeUs(){return System.nanoTime()/1000;}
    public static void joinClips(String[] paths,long[] starts,long endUs,String output) throws Exception {
        MediaMuxer result=null;boolean started=false;
        try {
            result=new MediaMuxer(output,MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);
            ByteBuffer data=ByteBuffer.allocateDirect(4*1024*1024);MediaCodec.BufferInfo info=new MediaCodec.BufferInfo();
            MediaFormat videoTrackFormat=null,audioTrackFormat=null;
            for(String path:paths){MediaExtractor inspect=new MediaExtractor();try{inspect.setDataSource(path);for(int i=0;i<inspect.getTrackCount();i++){MediaFormat f=inspect.getTrackFormat(i);String mime=f.getString(MediaFormat.KEY_MIME);if(mime.startsWith("video/"))videoTrackFormat=f;else if(mime.startsWith("audio/"))audioTrackFormat=f;}}finally{inspect.release();}if(videoTrackFormat!=null&&audioTrackFormat!=null)break;}
            if(videoTrackFormat==null||audioTrackFormat==null)throw new IOException("Recording does not yet contain both video and room audio");
            int targetVideo=result.addTrack(videoTrackFormat),targetAudio=result.addTrack(audioTrackFormat);result.start();started=true;
            long[] written={-1,-1};
            for(int part=0;part<paths.length;part++) {
                MediaExtractor source=new MediaExtractor();
                try { source.setDataSource(paths[part]);int[] targets=new int[source.getTrackCount()];
                    for(int i=0;i<targets.length;i++){String mime=source.getTrackFormat(i).getString(MediaFormat.KEY_MIME);targets[i]=mime.startsWith("video/")?targetVideo:targetAudio;source.selectTrack(i);}
                    while(true){int track=source.getSampleTrackIndex();if(track<0)break;data.clear();int size=source.readSampleData(data,0);if(size<0)break;long pts=source.getSampleTime();
                        long absolute=starts[part]+pts,relative=absolute-starts[0];int target=targets[track];
                        if(absolute<=endUs&&relative>written[target]){info.set(0,size,relative,source.getSampleFlags()&MediaExtractor.SAMPLE_FLAG_SYNC);result.writeSampleData(target,data,info);written[target]=relative;}
                        source.advance();
                    }
                }finally{source.release();}
            }
        }finally{if(result!=null){if(started)result.stop();result.release();}}
    }
    public static byte[] snapshot(String path,long sampleUs) throws Exception {
        MediaMetadataRetriever reader=new MediaMetadataRetriever();android.graphics.Bitmap image=null;
        try { reader.setDataSource(path);image=reader.getFrameAtTime(sampleUs,MediaMetadataRetriever.OPTION_CLOSEST);
            if(image==null)throw new IOException("No video frame available");ByteArrayOutputStream bytes=new ByteArrayOutputStream();image.compress(android.graphics.Bitmap.CompressFormat.JPEG,85,bytes);return bytes.toByteArray();
        }finally{if(image!=null)image.recycle();reader.release();}
    }
    public static double duration(String path) throws Exception {MediaMetadataRetriever reader=new MediaMetadataRetriever();try{reader.setDataSource(path);return Long.parseLong(reader.extractMetadata(MediaMetadataRetriever.METADATA_KEY_DURATION))/1000.0;}finally{reader.release();}}
}
