package uk.co.skyeye.companion.nativebridge;

import android.Manifest;
import android.content.Context;
import android.content.pm.PackageManager;
import android.os.Handler;
import android.os.Looper;

import org.webrtc.AudioSource;
import org.webrtc.AudioTrack;
import org.webrtc.DataChannel;
import org.webrtc.MediaConstraints;
import org.webrtc.MediaStreamTrack;
import org.webrtc.PeerConnection;
import org.webrtc.PeerConnectionFactory;
import org.webrtc.RtpReceiver;
import org.webrtc.RtpTransceiver;
import org.webrtc.SdpObserver;
import org.webrtc.SessionDescription;
import org.webrtc.audio.JavaAudioDeviceModule;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Collections;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Small Android-native WebRTC session wrapper for Maeve. All callbacks are posted
 * to the main thread so a .NET Activity can update its UI without extra dispatch.
 */
public final class SkyEyeRtcSession {
    public interface Listener {
        void onOfferCreated(String offerSdp);
        void onStateChanged(String state, String detail);
        void onDataChannelEvent(String json);
        void onRemoteAudioActivityChanged(boolean active);
    }

    private static final String DATA_CHANNEL_LABEL = "oai-events";
    private static final Object INITIALIZE_LOCK = new Object();
    private static boolean factoryInitialized;

    private final Context appContext;
    private final Listener listener;
    private final Handler mainHandler = new Handler(Looper.getMainLooper());
    private final AtomicBoolean offerDelivered = new AtomicBoolean();

    private JavaAudioDeviceModule audioDeviceModule;
    private PeerConnectionFactory factory;
    private PeerConnection peerConnection;
    private AudioSource audioSource;
    private AudioTrack audioTrack;
    private DataChannel dataChannel;
    private boolean iceGatheringComplete;
    private boolean started;
    private volatile boolean stopped;

    public SkyEyeRtcSession(Context context, Listener listener) {
        if (context == null) throw new IllegalArgumentException("Context is required.");
        if (listener == null) throw new IllegalArgumentException("Listener is required.");
        this.appContext = context.getApplicationContext();
        this.listener = listener;
    }

    /** Initializes audio capture/playback and starts offer creation. */
    public void start() {
        if (started) throw new IllegalStateException("The Maeve WebRTC session has already started.");
        if (stopped) throw new IllegalStateException("The Maeve WebRTC session has stopped.");
        if (appContext.checkSelfPermission(Manifest.permission.RECORD_AUDIO) != PackageManager.PERMISSION_GRANTED) {
            throw new SecurityException("Microphone permission is required before starting WebRTC.");
        }

        started = true;
        postState("connecting", "");
        try {
            initializeFactory(appContext);
            audioDeviceModule = JavaAudioDeviceModule.builder(appContext)
                    .setUseHardwareAcousticEchoCanceler(true)
                    .setUseHardwareNoiseSuppressor(true)
                    .createAudioDeviceModule();

            factory = PeerConnectionFactory.builder()
                    .setAudioDeviceModule(audioDeviceModule)
                    .createPeerConnectionFactory();

            PeerConnection.RTCConfiguration configuration =
                    new PeerConnection.RTCConfiguration(new ArrayList<>());
            peerConnection = factory.createPeerConnection(configuration, createPeerObserver());
            if (peerConnection == null) throw new IllegalStateException("WebRTC could not create a peer connection.");

            audioSource = factory.createAudioSource(new MediaConstraints());
            audioTrack = factory.createAudioTrack("skyeye-microphone", audioSource);
            audioTrack.setEnabled(true);
            peerConnection.addTrack(audioTrack, Collections.singletonList("skyeye-audio"));

            dataChannel = peerConnection.createDataChannel(DATA_CHANNEL_LABEL, new DataChannel.Init());
            if (dataChannel == null) throw new IllegalStateException("WebRTC could not create its event channel.");
            dataChannel.registerObserver(createDataChannelObserver());

            peerConnection.createOffer(new SdpObserver() {
                @Override public void onCreateSuccess(SessionDescription offer) {
                    if (stopped || peerConnection == null) return;
                    peerConnection.setLocalDescription(new SdpObserver() {
                        @Override public void onSetSuccess() { deliverOfferIfReady(); }
                        @Override public void onCreateSuccess(SessionDescription ignored) { }
                        @Override public void onCreateFailure(String error) { fail("Couldn't create the local offer: " + error); }
                        @Override public void onSetFailure(String error) { fail("Couldn't set the local offer: " + error); }
                    }, offer);
                }

                @Override public void onCreateFailure(String error) { fail("Couldn't create a WebRTC offer: " + error); }
                @Override public void onSetSuccess() { }
                @Override public void onSetFailure(String error) { }
            }, new MediaConstraints());
        } catch (RuntimeException error) {
            fail(error.getMessage() == null ? "WebRTC could not start." : error.getMessage());
            releaseResources();
        }
    }

    /** Applies the SDP answer returned by the GPT-Live session endpoint. */
    public void setRemoteAnswer(String answerSdp) {
        if (stopped || peerConnection == null) throw new IllegalStateException("The WebRTC session is not active.");
        if (answerSdp == null || answerSdp.trim().isEmpty()) throw new IllegalArgumentException("An SDP answer is required.");

        peerConnection.setRemoteDescription(new SdpObserver() {
            @Override public void onSetSuccess() { postState("connected", ""); }
            @Override public void onSetFailure(String error) { fail("Couldn't apply the GPT-Live answer: " + error); }
            @Override public void onCreateSuccess(SessionDescription ignored) { }
            @Override public void onCreateFailure(String error) { fail("Couldn't apply the GPT-Live answer: " + error); }
        }, new SessionDescription(SessionDescription.Type.ANSWER, answerSdp));
    }

    /** Mutes or unmutes the outgoing microphone track. */
    public void setMuted(boolean muted) {
        if (audioTrack != null && !stopped) {
            audioTrack.setEnabled(!muted);
            postState(muted ? "muted" : "listening", "");
        }
    }

    /** Sends a GPT-Live client event over the oai-events data channel. */
    public void sendDataChannelEvent(String json) {
        if (stopped || dataChannel == null || dataChannel.state() != DataChannel.State.OPEN) {
            throw new IllegalStateException("The GPT-Live event channel is not open.");
        }
        byte[] bytes = json.getBytes(StandardCharsets.UTF_8);
        dataChannel.send(new DataChannel.Buffer(ByteBuffer.wrap(bytes), false));
    }

    /** Closes the media session and releases all native resources. */
    public void stop() {
        if (stopped) return;
        stopped = true;
        releaseResources();
        postState("disconnected", "");
    }

    private static void initializeFactory(Context context) {
        synchronized (INITIALIZE_LOCK) {
            if (factoryInitialized) return;
            PeerConnectionFactory.initialize(
                    PeerConnectionFactory.InitializationOptions.builder(context).createInitializationOptions());
            factoryInitialized = true;
        }
    }

    private PeerConnection.Observer createPeerObserver() {
        return new PeerConnection.Observer() {
            @Override public void onSignalingChange(PeerConnection.SignalingState state) { }
            @Override public void onIceConnectionChange(PeerConnection.IceConnectionState state) {
                switch (state) {
                    case CHECKING: postState("connecting", ""); break;
                    case CONNECTED:
                    case COMPLETED: postState("listening", ""); break;
                    case DISCONNECTED: postState("disconnected", ""); break;
                    case FAILED: fail("The WebRTC network connection failed."); break;
                    case CLOSED: postState("disconnected", ""); break;
                    default: break;
                }
            }
            @Override public void onIceConnectionReceivingChange(boolean receiving) { }
            @Override public void onIceGatheringChange(PeerConnection.IceGatheringState state) {
                if (state == PeerConnection.IceGatheringState.COMPLETE) {
                    iceGatheringComplete = true;
                    deliverOfferIfReady();
                }
            }
            @Override public void onIceCandidate(org.webrtc.IceCandidate candidate) { }
            @Override public void onIceCandidatesRemoved(org.webrtc.IceCandidate[] candidates) { }
            @Override public void onAddStream(org.webrtc.MediaStream stream) { }
            @Override public void onRemoveStream(org.webrtc.MediaStream stream) { }
            @Override public void onDataChannel(DataChannel channel) { }
            @Override public void onRenegotiationNeeded() { }
            @Override public void onAddTrack(RtpReceiver receiver, org.webrtc.MediaStream[] streams) {
                // WebRTC's Java audio device module renders the remote audio track.
            }
            @Override public void onTrack(RtpTransceiver transceiver) {
                // WebRTC's Java audio device module renders the remote audio track.
                // The old binding used here predates AudioTrackSink, so activity
                // metering is intentionally left to the watch UI rather than
                // attaching a sink that is absent from the packaged binding.
            }
        };
    }

    private DataChannel.Observer createDataChannelObserver() {
        return new DataChannel.Observer() {
            @Override public void onBufferedAmountChange(long previousAmount) { }
            @Override public void onStateChange() {
                DataChannel channel = dataChannel;
                if (channel == null) return;
                String state = channel.state().toString().toLowerCase();
                postState("data_channel_" + state, "");
            }
            @Override public void onMessage(DataChannel.Buffer buffer) {
                if (buffer.binary) return;
                ByteBuffer data = buffer.data.slice();
                byte[] bytes = new byte[data.remaining()];
                data.get(bytes);
                String json = new String(bytes, StandardCharsets.UTF_8);
                mainHandler.post(() -> {
                    if (!stopped) listener.onDataChannelEvent(json);
                });
            }
        };
    }

    private void deliverOfferIfReady() {
        if (stopped || !iceGatheringComplete || peerConnection == null || !offerDelivered.compareAndSet(false, true)) return;
        SessionDescription local = peerConnection.getLocalDescription();
        if (local == null || local.description == null || local.description.isEmpty()) {
            fail("WebRTC finished gathering without a local SDP offer.");
            return;
        }
        String sdp = local.description;
        mainHandler.post(() -> {
            if (!stopped) listener.onOfferCreated(sdp);
        });
    }

    private void fail(String detail) {
        postState("error", detail == null ? "WebRTC failed." : detail);
    }

    private void postState(String state, String detail) {
        mainHandler.post(() -> {
            if (!stopped || "disconnected".equals(state)) listener.onStateChanged(state, detail);
        });
    }

    private void releaseResources() {
        if (dataChannel != null) {
            try { dataChannel.unregisterObserver(); } catch (RuntimeException ignored) { }
            try { dataChannel.close(); } catch (RuntimeException ignored) { }
            try { dataChannel.dispose(); } catch (RuntimeException ignored) { }
            dataChannel = null;
        }
        if (peerConnection != null) {
            try { peerConnection.close(); } catch (RuntimeException ignored) { }
            try { peerConnection.dispose(); } catch (RuntimeException ignored) { }
            peerConnection = null;
        }
        if (audioTrack != null) {
            try { audioTrack.dispose(); } catch (RuntimeException ignored) { }
            audioTrack = null;
        }
        if (audioSource != null) {
            try { audioSource.dispose(); } catch (RuntimeException ignored) { }
            audioSource = null;
        }
        if (factory != null) {
            try { factory.dispose(); } catch (RuntimeException ignored) { }
            factory = null;
        }
        if (audioDeviceModule != null) {
            try { audioDeviceModule.release(); } catch (RuntimeException ignored) { }
            audioDeviceModule = null;
        }
    }
}
