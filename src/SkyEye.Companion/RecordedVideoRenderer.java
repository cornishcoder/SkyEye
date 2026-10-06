package uk.co.skyeye.companion.nativebridge;

import android.content.Context;
import android.graphics.Matrix;
import android.media.MediaExtractor;
import android.media.MediaFormat;
import androidx.media3.common.*;
import androidx.media3.common.util.Size;
import androidx.media3.effect.MatrixTransformation;
import androidx.media3.transformer.*;
import org.json.*;
import java.util.Collections;
import java.nio.ByteBuffer;
import java.security.MessageDigest;

public final class RecordedVideoRenderer {
    public interface Listener {void onComplete(String report);void onError(String error);}
    private Transformer transformer;
    public void start(Context context,String input,String output,String planJson,boolean watermark,Listener listener) throws Exception {
        JSONObject plan=new JSONObject(planJson),source=plan.getJSONObject("source");
        int width=plan.getInt("width"),height=plan.getInt("height");
        JSONArray spans=plan.getJSONArray("spans");
        if(plan.getInt("version")!=1||source.getInt("rotation")!=0||width<16||height<16||width>3840||height>3840||width%2!=0||height%2!=0||spans.length()<1||spans.length()>512)
            throw new IllegalArgumentException("Invalid recorded render plan");
        long[] starts=new long[spans.length()],ends=new long[spans.length()];boolean[] corrected=new boolean[spans.length()];Matrix[] matrices=new Matrix[spans.length()];
        long cursor=0;
        for(int i=0;i<spans.length();i++){
            JSONObject span=spans.getJSONObject(i);starts[i]=span.getLong("startUs");ends[i]=span.getLong("endUs");corrected[i]=span.getBoolean("corrected");
            if(starts[i]!=cursor||ends[i]<=cursor)throw new IllegalArgumentException("Invalid render interval");cursor=ends[i];
            JSONArray values=span.getJSONArray("matrix");if(values.length()!=9)throw new IllegalArgumentException("Invalid perspective matrix");
            float[] matrix=new float[9];for(int j=0;j<9;j++){matrix[j]=(float)values.getDouble(j);if(!Float.isFinite(matrix[j]))throw new IllegalArgumentException("Invalid perspective value");}
            matrices[i]=new Matrix();matrices[i].setValues(matrix);
        }
        if(cursor!=source.getLong("durationUs"))throw new IllegalArgumentException("Render plan extent differs from source");
        JSONObject before=inspect(input);if(before.getInt("width")!=source.getInt("width")||before.getInt("height")!=source.getInt("height")||before.getInt("rotation")!=0)
            throw new IllegalArgumentException("Decoder dimensions differ from recorded geometry");
        long[] counts={0,0};
        MatrixTransformation effect=new MatrixTransformation(){
            public Size configure(int inputWidth,int inputHeight){if(inputWidth!=source.optInt("width")||inputHeight!=source.optInt("height"))throw new IllegalArgumentException("Decoded source shape changed");return new Size(width,height);}
            public Matrix getMatrix(long time){for(int i=0;i<starts.length;i++)if(time>=starts[i]&&time<ends[i]){counts[corrected[i]?0:1]++;return matrices[i];}throw new IllegalArgumentException("Decoder timestamp outside recorded plan");}
        };
        transformer=new Transformer.Builder(context).setPortraitEncodingEnabled(true)
            .setEncoderFactory(new DefaultEncoderFactory.Builder(context).setRequestedVideoEncoderSettings(new VideoEncoderSettings.Builder().setBitrate(8000000).build()).setEnableFallback(false).build())
            .setVideoMimeType(MimeTypes.VIDEO_H264).setAudioMimeType(MimeTypes.AUDIO_AAC)
            .addListener(new Transformer.Listener(){
                public void onCompleted(Composition composition,ExportResult result){try{
                    JSONObject after=inspect(output);
                    if(after.getInt("width")!=width||after.getInt("height")!=height||after.getInt("rotation")!=0||after.getLong("videoSamples")<=0
                        ||Math.abs(after.getLong("videoDurationUs")-before.getLong("videoDurationUs"))>100000)
                        throw new java.io.IOException("Rendered video verification failed");
                    if(before.has("audioSha256")&&(!after.has("audioSha256")||!before.getString("audioSha256").equals(after.getString("audioSha256"))
                        ||Math.abs(after.getLong("audioStartUs")-before.getLong("audioStartUs"))>1000||Math.abs(after.getLong("audioEndUs")-before.getLong("audioEndUs"))>1000))
                        throw new java.io.IOException("Rendered room audio changed unexpectedly");
                    listener.onComplete(new JSONObject().put("before",before).put("after",after).put("correctedFrameCallbacks",counts[0]).put("fallbackFrameCallbacks",counts[1]).put("watermarked",watermark).toString());
                }catch(Exception e){listener.onError("Recorded render verification failed: "+e.getMessage());}}
                public void onError(Composition composition,ExportResult result,ExportException error){listener.onError("Recorded render failed: "+error.getMessage());}
            }).build();
        EditedMediaItem item=new EditedMediaItem.Builder(MediaItem.fromUri(android.net.Uri.fromFile(new java.io.File(input))))
            .setEffects(new Effects(Collections.emptyList(),watermark?java.util.Arrays.asList(effect,WatermarkRenderer.effect(context,width,height)):Collections.singletonList(effect))).build();
        transformer.start(item,output);
    }
    public void cancel(){if(transformer!=null)transformer.cancel();}
    private static JSONObject inspect(String path) throws Exception {
        JSONObject report=new JSONObject();MediaExtractor reader=new MediaExtractor();
        try{reader.setDataSource(path);ByteBuffer buffer=ByteBuffer.allocateDirect(4*1024*1024);
            for(int i=0;i<reader.getTrackCount();i++){
                MediaFormat f=reader.getTrackFormat(i);String mime=f.getString(MediaFormat.KEY_MIME);boolean video=mime.startsWith("video/"),audio=mime.startsWith("audio/");
                if(!video&&!audio)continue;reader.selectTrack(i);reader.seekTo(0,MediaExtractor.SEEK_TO_PREVIOUS_SYNC);long count=0,start=-1,end=-1;MessageDigest digest=MessageDigest.getInstance("SHA-256");
                while(reader.getSampleTrackIndex()>=0){buffer.clear();int n=reader.readSampleData(buffer,0);if(n<0)break;long time=reader.getSampleTime();if(start<0)start=time;end=time;count++;
                    if(audio){byte[] bytes=new byte[n];buffer.position(0);buffer.get(bytes);digest.update(bytes);}reader.advance();}
                reader.unselectTrack(i);
                if(video){report.put("width",f.getInteger(MediaFormat.KEY_WIDTH)).put("height",f.getInteger(MediaFormat.KEY_HEIGHT)).put("rotation",f.containsKey("rotation-degrees")?f.getInteger("rotation-degrees"):0)
                    .put("videoDurationUs",f.getLong(MediaFormat.KEY_DURATION)).put("videoSamples",count).put("videoStartUs",start).put("videoEndUs",end);}
                else{StringBuilder hash=new StringBuilder();for(byte b:digest.digest())hash.append(String.format(java.util.Locale.ROOT,"%02X",b));report.put("audioSha256",hash.toString()).put("audioSamples",count).put("audioStartUs",start).put("audioEndUs",end).put("audioMime",mime);}
            }
        }finally{reader.release();}return report;
    }
}
