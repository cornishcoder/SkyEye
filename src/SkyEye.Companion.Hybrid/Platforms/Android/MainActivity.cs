using Android.App;
using Android.Content.PM;
using Android.OS;

namespace SkyEye.Companion.Hybrid;

[Activity(Name = "uk.co.skyeye.companion.lab.MainActivity", Theme = "@style/Maui.SplashTheme", MainLauncher = true, Exported = true, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
}
