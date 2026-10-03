package uk.co.skyeye.companion.nativebridge;

import android.graphics.*;
import org.json.*;

/** Shared normalized-image drawing for the phone and the encoded Cast feed. */
final class CandidateOverlay {
    private volatile JSONObject data=new JSONObject();
    void set(String json) { try { data=new JSONObject(json); } catch(JSONException e) { data=new JSONObject(); } }
    String metrics(){JSONObject value=data,result=new JSONObject();JSONArray shots=value.optJSONArray("shots");try{result.put("enabled",value.optBoolean("enabled"));result.put("candidateCount",shots==null?0:shots.length());result.put("capturedAt",value.optLong("capturedAt"));result.put("updatedAt",value.optLong("updatedAt"));result.put("expiresAt",value.optLong("expiresAt"));result.put("candidateIndex",shots==null||shots.length()==0||System.currentTimeMillis()>=value.optLong("expiresAt")?-1:(int)(Math.max(0,android.os.SystemClock.elapsedRealtime()-value.optLong("origin"))/2000%shots.length()));}catch(JSONException ignored){}return result.toString();}
    void draw(Canvas canvas,int width,int height) {
        JSONObject value=data;if(!value.optBoolean("enabled"))return;
        JSONArray shots=value.optJSONArray("shots");
        long now=System.currentTimeMillis();
        String label=value.optString("message","Checking table…");
        Paint p=new Paint(Paint.ANTI_ALIAS_FLAG);p.setStrokeWidth(Math.max(3,width/240f));p.setStyle(Paint.Style.STROKE);
        if(shots!=null&&shots.length()>0&&now<value.optLong("expiresAt")) {
            int index=(int)(Math.max(0,android.os.SystemClock.elapsedRealtime()-value.optLong("origin"))/2000%shots.length());
            JSONObject shot=shots.optJSONObject(index);
            if(shot!=null) {
                path(canvas,p,shot.optJSONArray("cue"),width,height,Color.WHITE);
                path(canvas,p,shot.optJSONArray("target"),width,height,Color.CYAN);
                JSONArray ghost=shot.optJSONArray("ghost");
                if(ghost!=null){p.setColor(Color.CYAN);canvas.drawCircle((float)ghost.optDouble(0)*width,(float)ghost.optDouble(1)*height,Math.max(5,width/180f),p);}
                label="Candidate "+(index+1)+" / "+shots.length()+" · "+shot.optString("type")+" · pocket "+shot.optInt("pocket");
            }
        } else if(shots!=null&&shots.length()>0) label="Waiting for a fresh table view…";
        p.setStyle(Paint.Style.FILL);p.setTextSize(Math.max(16,width/40f));
        float pad=width/80f,baseline=pad+p.getTextSize();
        p.setColor(0xb0000000);canvas.drawRect(0,0,width,baseline+pad,p);
        p.setColor(Color.WHITE);canvas.drawText(label,pad,baseline,p);
    }
    private static void path(Canvas c,Paint p,JSONArray points,int w,int h,int colour) {
        if(points==null)return;p.setColor(Color.BLACK);p.setStrokeWidth(Math.max(6,w/120f));lines(c,p,points,w,h);
        p.setColor(colour);p.setStrokeWidth(Math.max(3,w/240f));lines(c,p,points,w,h);
        for(int i=1;i<points.length()-1;i++){JSONArray v=points.optJSONArray(i);if(v!=null)c.drawCircle((float)v.optDouble(0)*w,(float)v.optDouble(1)*h,Math.max(5,w/180f),p);}
    }
    private static void lines(Canvas c,Paint p,JSONArray a,int w,int h) {
        for(int i=1;i<a.length();i++){JSONArray from=a.optJSONArray(i-1),to=a.optJSONArray(i);if(from!=null&&to!=null)c.drawLine((float)from.optDouble(0)*w,(float)from.optDouble(1)*h,(float)to.optDouble(0)*w,(float)to.optDouble(1)*h,p);}
    }
}
