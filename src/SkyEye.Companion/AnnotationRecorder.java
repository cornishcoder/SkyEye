package uk.co.skyeye.companion.nativebridge;

import androidx.media3.common.C;
import androidx.media3.common.util.TimestampAdjuster;
import org.json.*;
import java.util.ArrayDeque;

/** Associates actually drawn content with released decoder frames. Status is
 * used only to disambiguate the TS wrap epoch, never to place scene events. */
final class AnnotationRecorder {
    private volatile TimestampAdjuster adjuster;
    private final ArrayDeque<long[]> frames=new ArrayDeque<>();
    private JSONArray observations=new JSONArray();
    private String timeline="";private long referenceUs,referenceAt,generation;
    private long phoneUs=-1,tvUs=-1;private boolean overflow;
    synchronized void reset(long generation){this.generation=generation;adjuster=null;frames.clear();timeline="";phoneUs=tvUs=-1;}
    synchronized void extractor(TimestampAdjuster value,long generation){if(generation==this.generation)adjuster=value;}
    synchronized void clock(String timeline,long sourceUs,long generation){
        if(generation!=this.generation||timeline==null||!timeline.matches("[a-fA-F0-9]{32}")||sourceUs<0)return;
        if(!this.timeline.isEmpty()&&!this.timeline.equals(timeline)){frames.clear();phoneUs=tvUs=-1;adjuster=null;}
        this.timeline=timeline;referenceUs=sourceUs;referenceAt=android.os.SystemClock.elapsedRealtime();
    }
    synchronized void frame(long presentationUs,long releaseNs){
        TimestampAdjuster a=adjuster;if(a==null||timeline.isEmpty()||android.os.SystemClock.elapsedRealtime()-referenceAt>10000)return;
        long offset=a.getTimestampOffsetUs();if(offset==C.TIME_UNSET)return;
        long raw=presentationUs-offset;
        // Camera TsWriter stores hardware monotonic microseconds as 33-bit 90kHz PTS.
        double wrap=0x200000000L*1000000d/90000d;
        long source=raw+Math.round(Math.rint((referenceUs-raw)/wrap)*wrap);
        if(source<referenceUs-30000000L||source>referenceUs+10000000L||source<0)return;
        frames.add(new long[]{source,releaseNs});while(frames.size()>16)frames.removeFirst();
    }
    synchronized void drawn(JSONObject scene,String geometry,boolean tv,long mediaGeneration){
        if(mediaGeneration!=generation||timeline.isEmpty()||android.os.SystemClock.elapsedRealtime()-referenceAt>10000)return;
        long now=System.nanoTime();long[] selected=null;
        for(long[] f:frames)if(f[1]<=now)selected=f;
        // A frozen/buffering texture cannot extend recorded annotation lifetime.
        if(selected==null||now-selected[1]>250000000L)return;
        long previous=tv?tvUs:phoneUs;if(selected[0]<=previous)return;
        if(tv)tvUs=selected[0];else phoneUs=selected[0];
        try{
            if(observations.length()>=256){observations=new JSONArray();overflow=true;}
            observations.put(new JSONObject().put("timeline",timeline).put("sourceUs",selected[0])
                .put("target",tv?1:0).put("geometry",geometry==null?"":geometry)
                .put("scene",scene==null?JSONObject.NULL:scene));
        }catch(JSONException ignored){}
    }
    synchronized String drain(){String result=observations.toString();observations=new JSONArray();return result;}
    synchronized String metrics(){try{return new JSONObject().put("mapped",!timeline.isEmpty()&&!frames.isEmpty())
        .put("phoneSourceUs",phoneUs).put("tvSourceUs",tvUs).put("pending",observations.length()).put("overflow",overflow)
        .put("sourceAgeEstimateMs",phoneUs<0?JSONObject.NULL:(referenceUs-phoneUs)/1000d+android.os.SystemClock.elapsedRealtime()-referenceAt)
        .put("clockSampleAgeMs",android.os.SystemClock.elapsedRealtime()-referenceAt).toString();}catch(Exception ignored){return "{}";}}
}
