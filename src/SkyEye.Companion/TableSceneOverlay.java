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
            int schema=value.optInt("schema",1);if(schema!=1&&schema!=2)return false;
            JSONArray paths=array(value,"paths",16),markers=array(value,"markers",32),annotations=array(value,"annotations",24);
            boolean projected=false;
            for(int i=0;i<paths.length();i++){JSONObject p=paths.getJSONObject(i);ink(p.getInt("ink"));int role=p.optInt("role",0);if(role<0||role>3||schema==1&&role!=0)return false;projected|=role==3;JSONArray points=array(p,"points",32);if(points.length()<2)return false;for(int j=0;j<points.length();j++)point(points.getJSONObject(j));}
            for(int i=0;i<markers.length();i++){JSONObject m=markers.getJSONObject(i);point(m.getJSONObject("point"));ink(m.getInt("ink"));double r=m.getDouble("radius");int kind=m.getInt("kind");if(!Double.isFinite(r)||r<.001||r>.1||kind<0||kind>(schema==1?2:4))return false;}
            JSONArray corridors=value.optJSONArray("corridors");if(corridors!=null){if(corridors.length()>4||schema==1&&corridors.length()!=0)return false;for(int i=0;i<corridors.length();i++){JSONObject corridor=corridors.getJSONObject(i);ink(corridor.getInt("ink"));JSONArray boundary=array(corridor,"boundary",8);if(boundary.length()<3)return false;for(int j=0;j<boundary.length();j++)point(boundary.getJSONObject(j));}}
            JSONObject hud=value.optJSONObject("shotHud");if(hud!=null){if(schema!=2||!hud.getString("provenance").equals("Geometric"))return false;text(hud.getString("candidateId"),128);text(hud.getString("shotType"),24);text(hud.getString("model"),48);int number=hud.getInt("number");if(number<0||number>3)return false;JSONArray assumptions=array(hud,"assumptions",4);for(int i=0;i<assumptions.length();i++)text(assumptions.getString(i),72);if(!hud.isNull("projectionUnavailable"))text(hud.getString("projectionUnavailable"),96);}
            if(projected&&(hud==null||!hud.optString("model").equals("geometric-stun-v1")||!hud.getJSONArray("assumptions").toString().contains("Stun at impact; ideal equal-mass collision")))return false;
            for(int i=0;i<annotations.length();i++){JSONObject a=annotations.getJSONObject(i);point(a.getJSONObject("point"));text(a.getString("text"),48);}
            JSONObject score=value.optJSONObject("score");if(score!=null){text(score.getString("leftName"),24);text(score.getString("rightName"),24);if(score.getInt("leftScore")<0||score.getInt("leftScore")>999||score.getInt("rightScore")<0||score.getInt("rightScore")>999)return false;}
            data=value;revision=version;return true;
        }catch(Exception ignored){return false;}
    }
    JSONObject snapshot(long now){return data!=null&&now<data.optLong("expiresAt")?data:null;}
    JSONObject metrics(JSONObject value,long generation,long position){JSONObject result=new JSONObject();try{return result.put("sceneVersion",value==null?0:value.getLong("version")).put("mediaGeneration",generation).put("tableVersion",value==null?0:value.getLong("tableVersion")).put("sourcePositionMs",position).put("paths",value==null?0:value.getJSONArray("paths").length()).put("markers",value==null?0:value.getJSONArray("markers").length());}catch(Exception ignored){return result;}}
    void geometry(Canvas c,int w,int h,float aspect,JSONObject value){if(value==null)return;Paint p=new Paint(Paint.ANTI_ALIAS_FLAG);p.setStyle(Paint.Style.STROKE);
        JSONArray bands=value.optJSONArray("corridors");if(bands!=null)for(int i=0;i<bands.length();i++){JSONObject band=bands.optJSONObject(i);JSONArray boundary=band.optJSONArray("boundary");Path polygon=new Path();for(int j=0;j<boundary.length();j++){JSONObject at=boundary.optJSONObject(j);float x=(float)at.optDouble("x")*w,y=(float)at.optDouble("y")*h;if(j==0)polygon.moveTo(x,y);else polygon.lineTo(x,y);}polygon.close();p.setStyle(Paint.Style.FILL);p.setColor(ink(band.optInt("ink")));p.setAlpha(28);c.drawPath(polygon,p);p.setAlpha(255);p.setStyle(Paint.Style.STROKE);}
        JSONArray paths=value.optJSONArray("paths");for(int i=0;i<paths.length();i++){JSONObject path=paths.optJSONObject(i);JSONArray points=path.optJSONArray("points");float stroke=Math.max(2,w/600f)*(path.optBoolean("selected")?1.3f:1);
            int role=path.optInt("role",0);p.setStrokeCap(Paint.Cap.ROUND);p.setPathEffect(role==3?new DashPathEffect(new float[]{stroke*2,stroke*1.6f},0):null);
            p.setColor(0xa0000000);p.setStrokeWidth(stroke+2);lines(c,p,points,w,h);p.setColor(role==3?0xffe6bd6a:role==2?0xff64d8cd:ink(path.optInt("ink")));p.setStrokeWidth(stroke);lines(c,p,points,w,h);p.setPathEffect(null);
            if(role!=0)arrow(c,p,points,w,h,stroke*2.7f);}
        JSONArray markers=value.optJSONArray("markers");for(int i=0;i<markers.length();i++){JSONObject marker=markers.optJSONObject(i);JSONObject at=marker.optJSONObject("point");float x=(float)at.optDouble("x")*w,y=(float)at.optDouble("y")*h,ry=(float)marker.optDouble("radius")*h,rx=(float)marker.optDouble("radius")*w/aspect;
            int kind=marker.optInt("kind");p.setStyle(Paint.Style.STROKE);p.setPathEffect(kind==2?new DashPathEffect(new float[]{5,4},0):null);
            if(kind==3){p.setStyle(Paint.Style.FILL);p.setColor(0xffe6bd6a);Path diamond=new Path();diamond.moveTo(x,y-ry-2);diamond.lineTo(x+rx+2,y);diamond.lineTo(x,y+ry+2);diamond.lineTo(x-rx-2,y);diamond.close();c.drawPath(diamond,p);p.setStyle(Paint.Style.STROKE);continue;}
            p.setColor(Color.BLACK);p.setStrokeWidth(6);c.drawOval(x-rx,y-ry,x+rx,y+ry,p);p.setColor(ink(marker.optInt("ink")));p.setStrokeWidth(3);c.drawOval(x-rx,y-ry,x+rx,y+ry,p);
            if(kind==1||kind==4){c.drawLine(x-rx,y,x+rx,y,p);c.drawLine(x,y-ry,x,y+ry,p);}p.setPathEffect(null);}
    }
    void textLayer(Canvas c,int w,int h,Matrix imageMatrix,JSONObject value){if(value==null)return;Paint p=new Paint(Paint.ANTI_ALIAS_FLAG);JSONObject hud=value.optJSONObject("shotHud");p.setTextSize(Math.max(16,Math.min(w,h)/(hud==null?25f:hud.optBoolean("detailed")?34f:40f)));float pad=Math.max(10,Math.min(w,h)/60f),line=p.getTextSize()+pad;
        String caption=value.optString("caption");if(!caption.isEmpty())banner(c,p,caption,pad+Math.min(w,h)*.1f,pad+line,w);
        if(hud!=null){String label=hud.optString("model").equals("geometric-stun-v1")?(hud.isNull("projectionUnavailable")?"Projected stun line · direction only":"Stun line unavailable · "+hud.optString("projectionUnavailable")):"Geometric · candidate rank";banner(c,p,label,pad+Math.min(w,h)*.1f,pad+line*2,w);if(hud.optString("model").equals("geometric-stun-v1"))banner(c,p,"Assumes stun at impact · no finish estimate",pad+Math.min(w,h)*.1f,pad+line*3,w);}
        JSONObject score=value.optJSONObject("score");if(score!=null)banner(c,p,score.optString("leftName")+"  "+score.optInt("leftScore")+"     ·     "+score.optInt("rightScore")+"  "+score.optString("rightName"),pad,h-pad,w);
        JSONArray notes=value.optJSONArray("annotations");for(int i=0;i<notes.length();i++){JSONObject note=notes.optJSONObject(i),at=note.optJSONObject("point");float[] xy={(float)at.optDouble("x")*w,(float)at.optDouble("y")*h};imageMatrix.mapPoints(xy);float x=Math.max(pad,Math.min(w-pad-p.measureText(note.optString("text")),xy[0])),y=Math.max(line,Math.min(h-pad-line,xy[1]));banner(c,p,note.optString("text"),x,y,w);}
    }
    private static void banner(Canvas c,Paint p,String label,float x,float y,int width){while(!label.isEmpty()&&p.measureText(label)>width-x-10)label=label.substring(0,label.length()-1);p.setStyle(Paint.Style.FILL);p.setColor(0xd0000000);c.drawRoundRect(x-6,y-p.getTextSize()-4,x+p.measureText(label)+6,y+5,5,5,p);p.setColor(Color.WHITE);c.drawText(label,x,y,p);}
    private static void lines(Canvas c,Paint p,JSONArray a,int w,int h){for(int i=1;i<a.length();i++){JSONObject from=a.optJSONObject(i-1),to=a.optJSONObject(i);c.drawLine((float)from.optDouble("x")*w,(float)from.optDouble("y")*h,(float)to.optDouble("x")*w,(float)to.optDouble("y")*h,p);}}
    private static void arrow(Canvas c,Paint p,JSONArray points,int w,int h,float size){JSONObject a=points.optJSONObject(points.length()-2),b=points.optJSONObject(points.length()-1);float x=(float)b.optDouble("x")*w,y=(float)b.optDouble("y")*h,dx=x-(float)a.optDouble("x")*w,dy=y-(float)a.optDouble("y")*h,length=(float)Math.hypot(dx,dy);if(length<1)return;dx/=length;dy/=length;c.drawLine(x,y,x-dx*size-dy*size*.5f,y-dy*size+dx*size*.5f,p);c.drawLine(x,y,x-dx*size+dy*size*.5f,y-dy*size-dx*size*.5f,p);}
    private static JSONArray array(JSONObject o,String key,int max)throws JSONException{JSONArray a=o.getJSONArray(key);if(a.length()>max)throw new JSONException("Too large");return a;}
    private static void point(JSONObject p)throws JSONException{double x=p.getDouble("x"),y=p.getDouble("y");if(!Double.isFinite(x)||!Double.isFinite(y)||x<0||x>1||y<0||y>1)throw new JSONException("Invalid point");}
    private static void text(String s,int max)throws JSONException{if(s.length()>max)throw new JSONException("Text too long");for(int i=0;i<s.length();i++)if(Character.isISOControl(s.charAt(i)))throw new JSONException("Control text");}
    private static int ink(int value){switch(value){case 0:return Color.WHITE;case 1:return Color.CYAN;case 2:return Color.YELLOW;case 3:return Color.RED;case 4:return Color.GREEN;default:throw new IllegalArgumentException("Invalid ink");}}
}
