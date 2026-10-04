using Android.App;
using Android.Content.PM;
using Android.OS;

namespace SkyEye.Companion.Hybrid;

[Activity(Name = "uk.co.skyeye.companion.lab.MainActivity", Theme = "@style/Maui.SplashTheme", MainLauncher = true, Exported = true, ScreenOrientation = ScreenOrientation.UserPortrait, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnPause()
    {
        IPlatformApplication.Current?.Services.GetService<Probes.NativeMediaProbe>()?.Suspend();
        IPlatformApplication.Current?.Services.GetService<ShellSession>()?.Suspend();
        base.OnPause();
    }
    protected override void OnResume()
    {
        base.OnResume();
        IPlatformApplication.Current?.Services.GetService<Probes.NativeMediaProbe>()?.Resume();
        IPlatformApplication.Current?.Services.GetService<ShellSession>()?.Resume();
    }
}
