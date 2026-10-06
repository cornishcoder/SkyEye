package uk.co.skyeye.companion.nativebridge;

import androidx.media3.common.util.TimestampAdjuster;
import org.json.*;

public final class AnnotationClockProof {
    public static String run(){try{
        String timeline="0123456789abcdef0123456789abcdef";
        long wrapPts=0x200000000L;JSONArray cases=new JSONArray();
        for(long ticks:new long[]{90000L,wrapPts+360000L,3*wrapPts-90000L,3*wrapPts+90000L}){
            long source=TimestampAdjuster.ptsToUs(ticks);
            AnnotationRecorder recorder=new AnnotationRecorder();recorder.reset(1);recorder.clock(timeline,source+5000000,1);
            TimestampAdjuster adjuster=new TimestampAdjuster(0);recorder.extractor(adjuster,1);
            long adjusted=adjuster.adjustTsTimestamp(ticks&(wrapPts-1));
            recorder.frame(adjusted,System.nanoTime()-1000000);recorder.drawn(null,"",false,1);
            JSONArray observed=new JSONArray(recorder.drain());
            if(observed.length()!=1||Math.abs(observed.getJSONObject(0).getLong("sourceUs")-source)>12)throw new Exception("TS epoch/offset mapping differs from raw source");
            recorder.drawn(null,"",false,1);if(new JSONArray(recorder.drain()).length()!=0)throw new Exception("Duplicate frame recorded");
            cases.put(new JSONObject().put("sourceUs",source).put("observedUs",observed.getJSONObject(0).getLong("sourceUs")));
        }
        AnnotationRecorder recorder=new AnnotationRecorder();recorder.reset(1);recorder.clock(timeline,1000000,1);
        TimestampAdjuster adjuster=new TimestampAdjuster(0);long adjusted=adjuster.adjustTsTimestamp(90000);
        recorder.extractor(adjuster,2);recorder.frame(adjusted,System.nanoTime()-1000000);recorder.drawn(null,"",false,1);
        if(new JSONArray(recorder.drain()).length()!=0)throw new Exception("Unrelated media extractor accepted");
        recorder.extractor(adjuster,1);recorder.frame(adjusted,System.nanoTime()-1000000000);recorder.drawn(null,"",false,1);
        if(new JSONArray(recorder.drain()).length()!=0)throw new Exception("Frozen frame extended journal");
        recorder.reset(2);recorder.clock(timeline,1000000,2);recorder.extractor(adjuster,2);
        recorder.frame(adjusted,System.nanoTime()+1000000000);recorder.drawn(null,"",false,2);
        if(new JSONArray(recorder.drain()).length()!=0)throw new Exception("Future frame accepted before release");
        return new JSONObject().put("passed",true).put("rawPtsCases",cases).put("duplicateAndUnrelatedAndFrozenAndFutureRejected",true).toString();
    }catch(Exception e){try{return new JSONObject().put("passed",false).put("error",e.toString()).toString();}catch(Exception ignored){return "{}";}}}
}
