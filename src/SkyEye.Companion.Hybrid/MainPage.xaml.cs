namespace SkyEye.Companion.Hybrid;

public partial class MainPage : ContentPage
{
	void WebViewInitialized(object? sender, Microsoft.AspNetCore.Components.WebView.BlazorWebViewInitializedEventArgs args)
		=> args.WebView.SetBackgroundColor(Android.Graphics.Color.Transparent);
	public MainPage()
	{
		InitializeComponent();
	}
}
