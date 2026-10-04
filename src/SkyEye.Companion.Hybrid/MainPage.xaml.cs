namespace SkyEye.Companion.Hybrid;

public partial class MainPage : ContentPage
{
	readonly ShellSession session;
	void WebViewInitialized(object? sender, Microsoft.AspNetCore.Components.WebView.BlazorWebViewInitializedEventArgs args)
		=> args.WebView.SetBackgroundColor(Android.Graphics.Color.Transparent);
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
		// The native scene stays attached at full size while the contextual region changes.
		blazorWebView.HeightRequest = session.SheetOpen ? Math.Min(560, Math.Max(160, Height * .78)) : 160;
	}
}
