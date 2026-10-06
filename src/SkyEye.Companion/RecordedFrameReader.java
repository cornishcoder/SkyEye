package uk.co.skyeye.companion.nativebridge;

import android.media.MediaExtractor;
import android.media.MediaFormat;
import android.media.MediaMetadataRetriever;
import android.graphics.Bitmap;
import java.io.ByteArrayOutputStream;
import org.json.JSONObject;

// Offline source pixels, independent of the current live player/transform.
// Seek the extractor first so the journal records the actual keyframe PTS,
// rather than pretending a requested thumbnail timestamp was decoded exactly.
public final class RecordedFrameReader implements java.io.Closeable {
    private final MediaExtractor extractor = new MediaExtractor();
    private final MediaMetadataRetriever reader = new MediaMetadataRetriever();
    private long durationUs, actualTimeUs;
    private int encodedWidth, encodedHeight, rotation, frameWidth, frameHeight;
    private boolean closed;
    public RecordedFrameReader(String path) throws Exception {
        try {
            extractor.setDataSource(path); reader.setDataSource(path);
            boolean video=false;
            for(int i=0;i<extractor.getTrackCount();i++) {
                MediaFormat format=extractor.getTrackFormat(i);
                if(format.getString(MediaFormat.KEY_MIME).startsWith("video/")) {
                    encodedWidth=format.getInteger(MediaFormat.KEY_WIDTH); encodedHeight=format.getInteger(MediaFormat.KEY_HEIGHT);
                    durationUs=format.containsKey(MediaFormat.KEY_DURATION)?format.getLong(MediaFormat.KEY_DURATION):Long.parseLong(reader.extractMetadata(MediaMetadataRetriever.METADATA_KEY_DURATION))*1000;
                    rotation=format.containsKey("rotation-degrees")?format.getInteger("rotation-degrees"):0;
                    extractor.selectTrack(i); video=true; break;
                }
            }
            if(!video||durationUs<=0||durationUs>605000000L)throw new java.io.IOException("Unsupported recorded video extent");
        } catch(Exception error) {close();throw error;}
    }
    public String metadata() throws Exception {
        return new JSONObject().put("durationUs",durationUs).put("encodedWidth",encodedWidth).put("encodedHeight",encodedHeight)
            .put("rotation",rotation).put("frameWidth",frameWidth).put("frameHeight",frameHeight).put("actualTimeUs",actualTimeUs).toString();
    }
    public byte[] frame(long requestedUs) throws Exception {
        if(requestedUs<0||requestedUs>=durationUs)throw new IllegalArgumentException("Recorded frame outside source");
        extractor.seekTo(requestedUs,MediaExtractor.SEEK_TO_CLOSEST_SYNC);
        actualTimeUs=extractor.getSampleTime();
        if(actualTimeUs<0||actualTimeUs>=durationUs)throw new java.io.IOException("Recorded keyframe unavailable");
        Bitmap bitmap=reader.getFrameAtTime(actualTimeUs,MediaMetadataRetriever.OPTION_CLOSEST_SYNC);
        if(bitmap==null)throw new java.io.IOException("Recorded frame unavailable");
        Bitmap scaled=null;
        try {
            frameWidth=bitmap.getWidth();frameHeight=bitmap.getHeight();
            // The detector performs its own resize. Preserve source detail first
            // to avoid double resampling/JPEG degradation of pocket boundaries.
            double scale=Math.min(1,1920.0/Math.max(frameWidth,frameHeight));
            scaled=Bitmap.createScaledBitmap(bitmap,Math.max(1,(int)Math.round(frameWidth*scale)),Math.max(1,(int)Math.round(frameHeight*scale)),true);
            ByteArrayOutputStream output=new ByteArrayOutputStream();scaled.compress(Bitmap.CompressFormat.JPEG,95,output);return output.toByteArray();
        } finally {if(scaled!=null&&scaled!=bitmap)scaled.recycle();bitmap.recycle();}
    }
    public void close(){if(closed)return;closed=true;try{extractor.release();}finally{try{reader.release();}catch(java.io.IOException ignored){}}}
}
