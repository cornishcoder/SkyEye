package uk.co.skyeye.companion.nativebridge;

import android.content.Context;
import org.webrtc.PeerConnectionFactory;

/** Loads the production WebRTC JNI library without opening a microphone or session. */
public final class NativeCompatibilityProbe {
    public static String checkWebRtc(Context context) {
        PeerConnectionFactory.initialize(PeerConnectionFactory.InitializationOptions.builder(context).createInitializationOptions());
        PeerConnectionFactory factory = PeerConnectionFactory.builder().createPeerConnectionFactory();
        factory.dispose();
        return "WebRTC factory created and disposed";
    }
}
