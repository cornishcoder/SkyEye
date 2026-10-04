using Microsoft.Maui.Handlers;
using Android.Views;
using Android.Widget;
using Bridge = UK.CO.Skyeye.Companion.Nativebridge;
using SkyEye.Companion.Hybrid.Probes;

namespace SkyEye.Companion.Hybrid;

public sealed class NativeMediaView : Microsoft.Maui.Controls.View { }

public sealed class NativeMediaHandler : ViewHandler<NativeMediaView, FrameLayout>
{
    public static readonly IPropertyMapper<NativeMediaView, NativeMediaHandler> Properties = new PropertyMapper<NativeMediaView, NativeMediaHandler>(ViewMapper);
    Bridge.PlayerBridge? player;
    Bridge.CastBridge? cast;
    PlayerListener? playerListener;
    CastListener? castListener;
    NativeMediaProbe? probe;
    public NativeMediaHandler() : base(Properties) { }
    protected override FrameLayout CreatePlatformView()
    {
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("Android activity required.");
        probe = MauiContext!.Services.GetRequiredService<NativeMediaProbe>();
        playerListener = new(probe); castListener = new(probe);
        player = new(activity, playerListener); cast = new(activity, castListener);
        var container = new FrameLayout(activity);
        container.AddView(player.View, new FrameLayout.LayoutParams(-1, -1));
        cast.Configure("", true);
        int touchSize = (int)(56 * activity.Resources!.DisplayMetrics!.Density);
        container.AddView(cast.Button, new FrameLayout.LayoutParams(touchSize, touchSize, GravityFlags.Top | GravityFlags.Right));
        probe.Attach(player, cast);
        return container;
    }
    protected override void DisconnectHandler(FrameLayout platformView)
    {
        probe?.Detach(); player?.Release(); cast?.Release();
        player?.Dispose(); cast?.Dispose(); playerListener?.Dispose(); castListener?.Dispose();
        base.DisconnectHandler(platformView);
    }
    sealed class PlayerListener(NativeMediaProbe probe) : Java.Lang.Object, Bridge.PlayerBridge.IListener
    {
        public void OnError(string? error) => probe.Report(error ?? "Native playback interrupted");
        public void OnPlaying(bool playing) => probe.Report(playing ? "Native ExoPlayer playing · silent" : "Native player waiting");
    }
    sealed class CastListener(NativeMediaProbe probe) : Java.Lang.Object, Bridge.CastBridge.IListener
    {
        public void OnStatus(string? status) => probe.Report(status ?? "Native Cast status changed");
    }
}
