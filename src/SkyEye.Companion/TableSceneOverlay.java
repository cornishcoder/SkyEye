package uk.co.skyeye.companion.nativebridge;

import android.graphics.*;
import org.json.*;

/** One validated scene snapshot, independently mapped to phone and landscape encoder.
 * Geometry follows the image matrix; captions and annotation text stay upright. */
final class TableSceneOverlay {
    private JSONObject data;
    private long revision;
    void clear(){data=null;} // Keep revision: delayed earlier publications cannot restore cleared advice.
    void reset(){clear();revision=0;}
    boolean set(String json,long generation){
        try {
            if(json==null||json.length()>32768)return false;
            JSONObject value=new JSONObject(json);long version=value.getLong("version"),expires=value.getLong("expiresAt"),now=System.currentTimeMillis();
            if(version<=revision||value.getLong("mediaGeneration")!=generation||value.getLong("tableVersion")<0||expires<=now||expires>now+60000)return false;
            text(value.getString("caption"),96);
            JSONArray paths=array(value,"paths",16),markers=array(value,"markers",32),annotations=array(value,"annotations",8);
            for(int i=0;i<paths.length();i++){JSONObject p=paths.getJSONObject(i);ink(p.getInt("ink"));JSONArray points=array(p,"points",32);if(points.length()<2)return false;for(int j=0;j<points.length();j++)point(points.getJSONObject(j));}
            for(int i=0;i<markers.length();i++){JSONObject m=markers.getJSONObject(i);point(m.getJSONObject("point"));ink(m.getInt("ink"));double r=m.getDouble("radius");int kind=m.getInt("kind");if(!Double.isFinite(r)||r<.001||r>.1||kind<0||kind>2)return false;}
            for(int i=0;i<annotations.length();i++){JSONObject a=annotations.getJSONObject(i);point(a.getJSONObject("point"));text(a.getString("text"),48);}
            JSONObject score=value.optJSONObject("score");if(score!=null){text(score.getString("leftName"),24);text(score.getString("rightName"),24);if(score.getInt("leftScore")<0||score.getInt("leftScore")>999||score.getInt("rightScore")<0||score.getInt("rightScore")>999)return false;}
            data=value;revision=version;return true;
        }catch(Exception ignored){return false;}
    }
    JSONObject snapshot(long now){return data!=null&&now<data.optLong("expiresAt")?data:null;}
    JSONObject metrics(JSONObject value,long generation,long position){JSONObject result=new JSONObject();try{return result.put("sceneVersion",value==null?0:value.getLong("version")).put("mediaGeneration",generation).put("tableVersion",value==null?0:value.getLong("tableVersion")).put("sourcePositionMs",position).put("paths",value==null?0:value.getJSONArray("paths").length()).put("markers",value==null?0:value.getJSONArray("markers").length());}catch(Exception ignored){return result;}}
    void geometry(Canvas c,int w,int h,float aspect,JSONObject value){if(value==null)return;Paint p=new Paint(Paint.ANTI_ALIAS_FLAG);p.setStyle(Paint.Style.STROKE);
        JSONArray paths=value.optJSONArray("paths");for(int i=0;i<paths.length();i++){JSONObject path=paths.optJSONObject(i);JSONArray points=path.optJSONArray("points");float stroke=Math.max(3,w/240f)*(path.optBoolean("selected")?1.8f:1);
            p.setColor(Color.BLACK);p.setStrokeWidth(stroke+3);lines(c,p,points,w,h);p.setColor(ink(path.optInt("ink")));p.setStrokeWidth(stroke);lines(c,p,points,w,h);}
        JSONArray markers=value.optJSONArray("markers");for(int i=0;i<markers.length();i++){JSONObject marker=markers.optJSONObject(i);JSONObject at=marker.optJSONObject("point");float x=(float)at.optDouble("x")*w,y=(float)at.optDouble("y")*h,ry=(float)marker.optDouble("radius")*h,rx=(float)marker.optDouble("radius")*w/aspect;
            p.setColor(Color.BLACK);p.setStrokeWidth(6);c.drawOval(x-rx,y-ry,x+rx,y+ry,p);p.setColor(ink(marker.optInt("ink")));p.setStrokeWidth(3);c.drawOval(x-rx,y-ry,x+rx,y+ry,p);
            if(marker.optInt("kind")==1){c.drawLine(x-rx,y,x+rx,y,p);c.drawLine(x,y-ry,x,y+ry,p);}}
    }
    void textLayer(Canvas c,int w,int h,Matrix imageMatrix,JSONObject value){if(value==null)return;Paint p=new Paint(Paint.ANTI_ALIAS_FLAG);p.setTextSize(Math.max(16,Math.min(w,h)/25f));float pad=Math.max(10,Math.min(w,h)/50f),line=p.getTextSize()+pad;
        String caption=value.optString("caption");if(!caption.isEmpty())banner(c,p,caption,pad,pad+line,w);
        JSONObject score=value.optJSONObject("score");if(score!=null)banner(c,p,score.optString("leftName")+"  "+score.optInt("leftScore")+"     ·     "+score.optInt("rightScore")+"  "+score.optString("rightName"),pad,h-pad,w);
        JSONArray notes=value.optJSONArray("annotations");for(int i=0;i<notes.length();i++){JSONObject note=notes.optJSONObject(i),at=note.optJSONObject("point");float[] xy={(float)at.optDouble("x")*w,(float)at.optDouble("y")*h};imageMatrix.mapPoints(xy);float x=Math.max(pad,Math.min(w-pad-p.measureText(note.optString("text")),xy[0])),y=Math.max(line,Math.min(h-pad-line,xy[1]));banner(c,p,note.optString("text"),x,y,w);}
    }
    private static void banner(Canvas c,Paint p,String label,float x,float y,int width){while(!label.isEmpty()&&p.measureText(label)>width-x-10)label=label.substring(0,label.length()-1);p.setStyle(Paint.Style.FILL);p.setColor(0xd0000000);c.drawRoundRect(x-6,y-p.getTextSize()-4,x+p.measureText(label)+6,y+5,5,5,p);p.setColor(Color.WHITE);c.drawText(label,x,y,p);}
    private static void lines(Canvas c,Paint p,JSONArray a,int w,int h){for(int i=1;i<a.length();i++){JSONObject from=a.optJSONObject(i-1),to=a.optJSONObject(i);c.drawLine((float)from.optDouble("x")*w,(float)from.optDouble("y")*h,(float)to.optDouble("x")*w,(float)to.optDouble("y")*h,p);}}
    private static JSONArray array(JSONObject o,String key,int max)throws JSONException{JSONArray a=o.getJSONArray(key);if(a.length()>max)throw new JSONException("Too large");return a;}
    private static void point(JSONObject p)throws JSONException{double x=p.getDouble("x"),y=p.getDouble("y");if(!Double.isFinite(x)||!Double.isFinite(y)||x<0||x>1||y<0||y>1)throw new JSONException("Invalid point");}
    private static void text(String s,int max)throws JSONException{if(s.length()>max)throw new JSONException("Text too long");for(int i=0;i<s.length();i++)if(Character.isISOControl(s.charAt(i)))throw new JSONException("Control text");}
    private static int ink(int value){switch(value){case 0:return Color.WHITE;case 1:return Color.CYAN;case 2:return Color.YELLOW;case 3:return Color.RED;case 4:return Color.GREEN;default:throw new IllegalArgumentException("Invalid ink");}}
}
