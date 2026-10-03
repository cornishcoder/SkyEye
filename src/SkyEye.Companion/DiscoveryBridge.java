package uk.co.skyeye.companion.nativebridge;
import android.content.Context;
import android.net.nsd.*;
import android.os.*;
import java.util.concurrent.ConcurrentHashMap;
public final class DiscoveryBridge {
    public interface Listener { void onCamera(String name,String address); void onError(String error); }
    private final NsdManager manager;private final Listener listener;private final Handler main=new Handler(Looper.getMainLooper());
    private boolean running;private final ConcurrentHashMap<String,Boolean> resolving=new ConcurrentHashMap<>();
    private final NsdManager.DiscoveryListener events=new NsdManager.DiscoveryListener(){
        public void onDiscoveryStarted(String type){} public void onDiscoveryStopped(String type){}
        public void onStartDiscoveryFailed(String type,int code){running=false;main.post(()->listener.onError("Discovery unavailable. Enter the camera address."));}
        public void onStopDiscoveryFailed(String type,int code){} public void onServiceLost(NsdServiceInfo info){resolving.remove(info.getServiceName());}
        public void onServiceFound(NsdServiceInfo info){if(!info.getServiceType().contains("_skyeye._tcp")||resolving.putIfAbsent(info.getServiceName(),true)!=null)return;
            manager.resolveService(info,new NsdManager.ResolveListener(){
                public void onResolveFailed(NsdServiceInfo s,int code){resolving.remove(info.getServiceName());}
                public void onServiceResolved(NsdServiceInfo s){String host=s.getHost().getHostAddress();String address="http://"+(host.contains(":")?"["+host+"]":host)+":"+s.getPort();main.post(()->listener.onCamera(s.getServiceName(),address));}
            });
        }
    };
    public DiscoveryBridge(Context context,Listener listener){manager=(NsdManager)context.getSystemService(Context.NSD_SERVICE);this.listener=listener;}
    public void start(){if(running)return;running=true;resolving.clear();manager.discoverServices("_skyeye._tcp.",NsdManager.PROTOCOL_DNS_SD,events);}
    public void stop(){if(running){running=false;try{manager.stopServiceDiscovery(events);}catch(Exception ignored){}}}
}
