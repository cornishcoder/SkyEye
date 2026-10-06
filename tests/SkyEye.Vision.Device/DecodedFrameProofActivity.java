package uk.co.skyeye.companion.nativebridge;

import android.app.Activity;
import android.graphics.Bitmap;
import android.os.*;
import org.json.*;
import java.io.*;

/** Test-package-only activity exercising the production PlayerBridge copy. */
public final class DecodedFrameProofActivity extends Activity {
    private PlayerBridge player;
    private final Handler main=new Handler(Looper.getMainLooper());
    private final JSONArray samples=new JSONArray();
    private long started,lastPts=-1,oldGeneration;private int attempts,nulls;private volatile int phase;
    private static volatile DecodedFrameProofActivity activeProof;
    public static PlayerBridge liveProofPlayer(){DecodedFrameProofActivity active=activeProof;return active!=null&&active.phase==2&&!active.finished?active.player:null;}
    private long sourceOriginUs,transportStarted;private java.net.ServerSocket server;
    private boolean finished;private String error="";
    public void onCreate(Bundle state){super.onCreate(state);activeProof=this;getWindow().addFlags(android.view.WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON);
        player=new PlayerBridge(this,new PlayerBridge.Listener(){public void onError(String message){error=message;}public void onPlaying(boolean playing){}});
        setContentView(player.getView());started=SystemClock.elapsedRealtime();
        try{sourceOriginUs=new JSONObject(new String(java.nio.file.Files.readAllBytes(new File(getExternalFilesDir(null),"decoded-frame-fixture.json").toPath()),java.nio.charset.StandardCharsets.UTF_8)).getLong("sourceOriginUs");}
        catch(Exception failure){finishProof(false,failure.toString());return;}
        player.play(new File(getExternalFilesDir(null),"decoded-frame-fixture.mp4").getAbsolutePath());main.postDelayed(this::sample,30);
    }
    private void sample(){if(finished)return;attempts++;
        try{
            if(phase==2){long gen=new JSONObject(player.playbackMetrics()).getLong("mediaGeneration");
                player.setRecordingClock("0123456789abcdef0123456789abcdef",sourceOriginUs+(SystemClock.elapsedRealtime()-transportStarted)*1000,gen);}
            DecodedFrameSnapshot copy=player.snapshotDecodedFrame();
            if(copy==null)nulls++;
            else try{
                JSONObject receipt=new JSONObject(copy.metadata());Bitmap bitmap=copy.takeBitmap();
                try{
                    long pts=receipt.getLong("presentationUs"),gen=receipt.getLong("mediaGeneration");
                    if(phase==1&&gen==oldGeneration)throw new Exception("Old generation survived reconnect");
                    if(pts!=lastPts){int id=0;for(int bit=0;bit<12;bit++){int pixel=bitmap.getPixel(60+40*bit,180);if(android.graphics.Color.red(pixel)>128)id|=1<<bit;}
                        int expected=(int)Math.round((phase==2?receipt.getLong("sourceUs")-sourceOriginUs:pts)*30/1000000d);
                        if(id!=expected)throw new Exception("Pixels/PTS mismatch: barcode "+id+", timestamp frame "+expected);
                        if(receipt.getLong("surfaceTimestampNs")!=receipt.getLong("releaseTimestampNs"))throw new Exception("Unmatched surface receipt");
                        samples.put(receipt.put("pixelFrameId",id).put("expectedFrameId",expected).put("phase",phase));lastPts=pts;
                    }
                    if(phase==0&&samples.length()>=25){oldGeneration=gen;player.stop();if(player.snapshotDecodedFrame()!=null)throw new Exception("Stopped frame available");
                        phase=1;lastPts=-1;player.play(new File(getExternalFilesDir(null),"decoded-frame-fixture.mp4").getAbsolutePath());}
                    else if(phase==1&&samples.length()>=75){player.stop();phase=2;lastPts=-1;startTransport();}
                }finally{bitmap.recycle();}
            }finally{copy.close();}
            if(samples.length()>=125){finishProof(true,"");return;}
            if(!error.isEmpty())throw new Exception(error);
            if(SystemClock.elapsedRealtime()-started>30000)throw new Exception("Insufficient paired frames: "+samples.length()+"/"+attempts);
            main.postDelayed(this::sample,20);
        }catch(Exception failure){finishProof(false,failure.toString());}
    }
    private void startTransport()throws Exception{
        server=new java.net.ServerSocket(0,2,java.net.InetAddress.getByName("127.0.0.1"));
        Thread serving=new Thread(()->{while(!server.isClosed())try(java.net.Socket client=server.accept()){
            client.setSoTimeout(3000);BufferedReader request=new BufferedReader(new InputStreamReader(client.getInputStream(),java.nio.charset.StandardCharsets.US_ASCII));
            String first=request.readLine();int bytes=first==null?0:first.length();String line;
            while((line=request.readLine())!=null&&!line.isEmpty()){bytes+=line.length();if(bytes>8192)throw new IOException("Request too long");}
            if(first==null||!first.startsWith("GET /live/stream.ts "))continue;
            File video=new File(getExternalFilesDir(null),"decoded-frame-fixture.ts");OutputStream output=client.getOutputStream();
            output.write(("HTTP/1.1 200 OK\r\nContent-Type: video/mp2t\r\nContent-Length: "+video.length()+"\r\nConnection: close\r\n\r\n").getBytes(java.nio.charset.StandardCharsets.US_ASCII));
            java.nio.file.Files.copy(video.toPath(),output);output.flush();
        }catch(IOException ignored){}});serving.setDaemon(true);serving.start();transportStarted=SystemClock.elapsedRealtime();
        player.play("http://127.0.0.1:"+server.getLocalPort()+"/live/stream.ts");
    }
    private void finishProof(boolean passed,String message){finished=true;
        try{JSONObject result=new JSONObject().put("passed",passed).put("error",message).put("samples",samples).put("attempts",attempts).put("unpairedAttempts",nulls)
            .put("runId",getIntent().getStringExtra("proof-run"))
            .put("copyDiagnostics",new JSONObject(player.decodedCopyMetrics()))
            .put("stopAndGenerationTested",phase>=1).put("liveTsSourceClockTested",phase==2).put("fixture","Synthetic 640x360 H.264, 30fps, B frames; local MP4 and loopback live TS, no table accuracy evidence");
            try(FileOutputStream output=new FileOutputStream(new File(getExternalFilesDir(null),"decoded-frame-proof.json"))){output.write(result.toString(2).getBytes(java.nio.charset.StandardCharsets.UTF_8));}
        }catch(Exception failure){android.util.Log.e("SkyEyePairProof","Report unavailable",failure);}player.release();closeTransport();finish();
    }
    private void closeTransport(){if(server!=null)try{server.close();}catch(IOException ignored){}}
    protected void onDestroy(){activeProof=null;main.removeCallbacksAndMessages(null);if(!finished&&player!=null)player.release();closeTransport();super.onDestroy();}
}
