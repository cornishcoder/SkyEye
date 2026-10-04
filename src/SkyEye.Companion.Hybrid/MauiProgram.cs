using Microsoft.Extensions.Logging;
namespace SkyEye.Companion.Hybrid;
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();
        builder.ConfigureMauiHandlers(handlers => handlers.AddHandler<NativeMediaView, NativeMediaHandler>());
        builder.Services.AddSingleton<Probes.NativeMediaProbe>();
        builder.Services.AddMauiBlazorWebView();
#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif
        return builder.Build();
    }
}
