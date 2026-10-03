package uk.co.skyeye.companion.nativebridge;
import android.content.Context;
import androidx.media3.common.*;
import androidx.media3.effect.ScaleAndRotateTransformation;
import androidx.media3.transformer.*;
import java.util.Collections;

public final class PortraitExporter {
    public interface Listener { void onComplete(); void onError(String error); }
    private Transformer transformer;
    public void start(Context context,String input,String output,int rotation,Listener listener){
        // Explicit encoding settings prevent 90-degree rotation being reduced to container metadata.
        transformer=new Transformer.Builder(context).setPortraitEncodingEnabled(true).setEncoderFactory(new DefaultEncoderFactory.Builder(context).setRequestedVideoEncoderSettings(new VideoEncoderSettings.Builder().setBitrate(6000000).build()).setEnableFallback(false).build()).setVideoMimeType(MimeTypes.VIDEO_H264).setAudioMimeType(MimeTypes.AUDIO_AAC).addListener(new Transformer.Listener(){
            public void onCompleted(Composition composition,ExportResult result){
                android.media.MediaExtractor check=new android.media.MediaExtractor();
                try{check.setDataSource(output);boolean portrait=false;for(int i=0;i<check.getTrackCount();i++){android.media.MediaFormat f=check.getTrackFormat(i);if(f.getString(android.media.MediaFormat.KEY_MIME).startsWith("video/")){portrait=f.getInteger(android.media.MediaFormat.KEY_HEIGHT)>f.getInteger(android.media.MediaFormat.KEY_WIDTH)&&(f.containsKey("rotation-degrees")?f.getInteger("rotation-degrees"):0)==0;}}
                    if(!portrait)throw new java.io.IOException("Encoder did not produce portrait pixels");listener.onComplete();
                }catch(Exception e){listener.onError("Portrait export failed: "+e.getMessage());}finally{check.release();}
            }
            public void onError(Composition composition,ExportResult result,ExportException exception){listener.onError("Portrait export failed: "+exception.getMessage());}
        }).build();
        EditedMediaItem item=new EditedMediaItem.Builder(MediaItem.fromUri(android.net.Uri.fromFile(new java.io.File(input)))).setEffects(new Effects(Collections.emptyList(),Collections.singletonList(new ScaleAndRotateTransformation.Builder().setRotationDegrees(rotation).build()))).build();
        transformer.start(item,output);
    }
    public void cancel(){if(transformer!=null)transformer.cancel();}
}
