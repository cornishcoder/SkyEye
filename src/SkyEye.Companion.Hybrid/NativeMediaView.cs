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
    ShellSession? session;
    Android.Widget.TextView? placeholder;
    public NativeMediaHandler() : base(Properties) { }
    protected override FrameLayout CreatePlatformView()
    {
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("Android activity required.");
        probe = MauiContext!.Services.GetRequiredService<NativeMediaProbe>();
        session = MauiContext.Services.GetRequiredService<ShellSession>();
        playerListener = new(probe, session); castListener = new(probe);
        player = new(activity, playerListener); cast = new(activity, castListener);
        var container = new FrameLayout(activity);
        var density = activity.Resources!.DisplayMetrics!.Density;
        var videoLayout = new FrameLayout.LayoutParams(-1, -1) { TopMargin = (int)(64 * density), BottomMargin = (int)(160 * density) };
        container.AddView(player.View, videoLayout);
        placeholder = new(activity) { Text = "Your table.\nEvery shot.", Gravity = GravityFlags.Center, TextSize = 27 };
        placeholder.SetTextColor(Android.Graphics.Color.Rgb(172, 192, 196));
        placeholder.ImportantForAccessibility = ImportantForAccessibility.Yes;
        container.AddView(placeholder, new FrameLayout.LayoutParams(-1, -1) { TopMargin = videoLayout.TopMargin, BottomMargin = videoLayout.BottomMargin });
        var title = new Android.Widget.TextView(activity) { Text = "SKYEYE", TextSize = 15, Gravity = GravityFlags.CenterVertical };
        title.SetTextColor(Android.Graphics.Color.White); title.SetPadding((int)(24 * density), 0, 0, 0);
        container.AddView(title, new FrameLayout.LayoutParams(-2, (int)(56 * density), GravityFlags.Top | GravityFlags.Left));
        cast.Configure("", true);
        int touchSize = (int)(56 * activity.Resources!.DisplayMetrics!.Density);
        container.AddView(cast.Button, new FrameLayout.LayoutParams(touchSize, touchSize, GravityFlags.Top | GravityFlags.Right));
        probe.Attach(player, cast);
        session.Attach(player, cast); session.Changed += Update;
        Update();
        container.SetOnApplyWindowInsetsListener(new InsetsListener());
        container.RequestApplyInsets();
        return container;
    }
    protected override void DisconnectHandler(FrameLayout platformView)
    {
        if (session is not null) { session.Changed -= Update; if (player is not null) session.Detach(player); }
        probe?.Detach(); player?.Release(); cast?.Release();
        player?.Dispose(); cast?.Dispose(); playerListener?.Dispose(); castListener?.Dispose();
        base.DisconnectHandler(platformView);
    }
    void Update() => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (placeholder is not null) placeholder.Visibility = session?.HasSource == true ? ViewStates.Gone : ViewStates.Visible;
    });
    sealed class InsetsListener : Java.Lang.Object, Android.Views.View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(Android.Views.View? view, WindowInsets? insets)
        {
            if (view is not null && insets is not null)
            {
                var safe = insets.GetInsets(WindowInsets.Type.SystemBars() | WindowInsets.Type.DisplayCutout())!;
                view.SetPadding(safe.Left, safe.Top, safe.Right, safe.Bottom);
            }
            return insets!;
        }
    }
    sealed class PlayerListener(NativeMediaProbe probe, ShellSession session) : Java.Lang.Object, Bridge.PlayerBridge.IListener
    {
        public void OnError(string? error) { probe.Report(error ?? "Native playback interrupted"); session.ReportError(); }
        public void OnPlaying(bool playing) { probe.Report(playing ? "Native ExoPlayer playing · silent" : "Native player waiting"); session.ReportPlaying(playing); }
    }
    sealed class CastListener(NativeMediaProbe probe) : Java.Lang.Object, Bridge.CastBridge.IListener
    {
        public void OnStatus(string? status) => probe.Report(status ?? "Native Cast status changed");
    }
}
