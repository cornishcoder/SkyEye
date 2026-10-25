namespace SkyEye.Companion.Hybrid;

public partial class MainPage : ContentPage
{
	readonly ShellSession session;
	void WebViewInitialized(object? sender, Microsoft.AspNetCore.Components.WebView.BlazorWebViewInitializedEventArgs args)
	{
		args.WebView.SetBackgroundColor(Android.Graphics.Color.Transparent);
		args.WebView.Settings!.TextZoom = (int)(session.FontScale * 100);
		// Keep controls above the TextureView-backed video, including the live Home dock.
		args.WebView.Elevation = 12 * (Platform.CurrentActivity?.Resources?.DisplayMetrics?.Density ?? 1);
		args.WebView.BringToFront();
	}
	public MainPage()
	{
		InitializeComponent();
		session = IPlatformApplication.Current!.Services.GetRequiredService<ShellSession>();
		SizeChanged += (_, _) => ResizeControls();
	}
	protected override void OnAppearing()
	{
		base.OnAppearing(); session.Changed += OnChanged; ResizeControls();
	}
	protected override void OnDisappearing()
	{
		session.Changed -= OnChanged; base.OnDisappearing();
	}
	void OnChanged() => MainThread.BeginInvokeOnMainThread(ResizeControls);
	void ResizeControls()
	{
		session.SetEditorLimit(Height * .6);
		// The native scene stays attached at full size while the contextual region changes.
		blazorWebView.HeightRequest = session.EditingTable ? session.EditorHeight
			: session.SheetOpen ? Math.Min(560 * session.FontScale, Math.Max(session.DockHeight, Height * .78)) : session.DockHeight;
	}
	protected override bool OnBackButtonPressed()
	{
		if (session.EditingTable) { session.CancelTableEdit(); return true; }
		if (!session.SheetOpen) return base.OnBackButtonPressed();
		session.SetSheet(false); return true;
	}
}
