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
    Android.Widget.TextView? title;
    FrameLayout? container;
    FrozenTableSurface? frozen;
    float density;
    TouchListener? touchListener;
    public NativeMediaHandler() : base(Properties) { }
    protected override FrameLayout CreatePlatformView()
    {
        var activity = Platform.CurrentActivity ?? throw new InvalidOperationException("Android activity required.");
        probe = MauiContext!.Services.GetRequiredService<NativeMediaProbe>();
        session = MauiContext.Services.GetRequiredService<ShellSession>();
        playerListener = new(probe, session); castListener = new(probe, session);
        player = new(activity, playerListener); cast = new(activity, castListener);
        container = new FrameLayout(activity);
        density = activity.Resources!.DisplayMetrics!.Density;
        session.SetAccessibility(activity.Resources.Configuration?.FontScale ?? 1,
            (activity.GetSystemService(Android.Content.Context.AccessibilityService) as Android.Views.Accessibility.AccessibilityManager)?.IsTouchExplorationEnabled == true);
        var videoLayout = new FrameLayout.LayoutParams(-1, -1) { TopMargin = (int)(64 * density), BottomMargin = (int)(160 * density) };
        container.AddView(player.View, videoLayout);
        placeholder = new(activity) { Text = "Your table.\nEvery shot.", Gravity = GravityFlags.Center, TextSize = 27 };
        placeholder.SetTextColor(Android.Graphics.Color.Rgb(172, 192, 196));
        placeholder.ImportantForAccessibility = ImportantForAccessibility.Yes;
        container.AddView(placeholder, new FrameLayout.LayoutParams(-1, -1) { TopMargin = videoLayout.TopMargin, BottomMargin = videoLayout.BottomMargin });
        frozen = new(activity, session) { Clickable = true, Focusable = true, Visibility = ViewStates.Gone };
        container.AddView(frozen, new FrameLayout.LayoutParams(-1, -1) { TopMargin = videoLayout.TopMargin, BottomMargin = videoLayout.BottomMargin });
        title = new Android.Widget.TextView(activity) { Text = "SKYEYE", TextSize = 15, Gravity = GravityFlags.CenterVertical };
        title.SetTextColor(Android.Graphics.Color.White); title.SetPadding((int)(24 * density), 0, 0, 0);
        container.AddView(title, new FrameLayout.LayoutParams(-2, (int)(56 * density), GravityFlags.Top | GravityFlags.Left));
        cast.Configure("", true);
        int touchSize = (int)(56 * activity.Resources!.DisplayMetrics!.Density);
        container.AddView(cast.Button, new FrameLayout.LayoutParams(touchSize, touchSize, GravityFlags.Top | GravityFlags.Right));
        probe.Attach(player, cast);
        touchListener = new(session, density);
        player.View!.SetOnTouchListener(touchListener);
        player.View.Click += Reveal;
        player.View.Clickable = true; player.View.Focusable = true;
        player.View.ContentDescription = "Table video. Tap to reveal controls or swipe up to open them.";
        session.Attach(player, cast); session.Changed += Update;
        player.View.LayoutChange += SceneLayoutChanged;
        Update();
        container.SetOnApplyWindowInsetsListener(new InsetsListener());
        container.RequestApplyInsets();
        return container;
    }
    protected override void DisconnectHandler(FrameLayout platformView)
    {
        if (session is not null) { session.Changed -= Update; if (player is not null) session.Detach(player); }
        if (player is not null) { player.View!.Click -= Reveal; player.View.LayoutChange -= SceneLayoutChanged; player.View.SetOnTouchListener(null); }
        probe?.Detach(); player?.Release(); cast?.Release();
        placeholder = null;
        title = null; container = null; touchListener?.Dispose(); touchListener = null;
        frozen?.Dispose(); frozen = null;
        player?.Dispose(); cast?.Dispose(); playerListener?.Dispose(); castListener?.Dispose();
        base.DisconnectHandler(platformView);
    }
    void Update() => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (placeholder is not null) placeholder.Visibility = session?.HasSource == true ? ViewStates.Gone : ViewStates.Visible;
        if (session is not null && container is not null && player is not null)
        {
            int top = session.ControlsVisible ? (int)(64 * density) : 0;
            int bottom = (int)((session.EditingTable ? session.EditorHeight : session.DockHeight) * density);
            var layout = (FrameLayout.LayoutParams)player.View!.LayoutParameters!;
            if (layout.TopMargin != top || layout.BottomMargin != bottom)
            {
                layout.TopMargin = top; layout.BottomMargin = bottom; player.View.LayoutParameters = layout;
            }
            if (title is not null) title.Visibility = session.ControlsVisible ? ViewStates.Visible : ViewStates.Gone;
            if (frozen is not null)
            {
                var frozenLayout = (FrameLayout.LayoutParams)frozen.LayoutParameters!;
                if (frozenLayout.TopMargin != top || frozenLayout.BottomMargin != bottom)
                { frozenLayout.TopMargin = top; frozenLayout.BottomMargin = bottom; frozen.LayoutParameters = frozenLayout; }
                frozen.Bind(session.FrozenFrame);
            }
            if (cast is not null) cast.Button!.Visibility = session.ControlsVisible ? ViewStates.Visible : ViewStates.Gone;
            session.UpdateFraming(player.View.Width, player.View.Height);
        }
    });
    void Reveal(object? sender, EventArgs args) => session?.RevealControls();
    void SceneLayoutChanged(object? sender, Android.Views.View.LayoutChangeEventArgs args) => Update();
    sealed class TouchListener(ShellSession session, float density) : Java.Lang.Object, Android.Views.View.IOnTouchListener
    {
        float x, y; bool tracking;
        public bool OnTouch(Android.Views.View? view, MotionEvent? motion)
        {
            if (motion is null || view is null) return false;
            if (motion.PointerCount != 1) { tracking = false; return true; }
            switch (motion.ActionMasked)
            {
                case MotionEventActions.Down: x = motion.GetX(); y = motion.GetY(); tracking = true; break;
                case MotionEventActions.Cancel: tracking = false; break;
                case MotionEventActions.Up:
                    if (tracking)
                    {
                        float dx = motion.GetX() - x, dy = motion.GetY() - y;
                        if (dy < -48 * density && Math.Abs(dy) > Math.Abs(dx)) session.SetSheet(true);
                        else if (Math.Abs(dx) < 24 * density && Math.Abs(dy) < 24 * density) view.PerformClick();
                    }
                    tracking = false; break;
            }
            return true;
        }
    }
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
    sealed class CastListener(NativeMediaProbe probe, ShellSession session) : Java.Lang.Object, Bridge.CastBridge.IListener
    {
        public void OnStatus(string? status) { probe.Report(status ?? "Native Cast status changed"); session.ReportCast(status); }
    }
}
