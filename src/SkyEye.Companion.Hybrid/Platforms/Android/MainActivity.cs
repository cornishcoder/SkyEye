using Android.App;
using Android.Content.PM;
using Android.OS;

namespace SkyEye.Companion.Hybrid;

#if SKYEYE_HYBRID_PRODUCTION
[Activity(Name = "uk.co.skyeye.companion.MainActivity", Theme = "@style/Maui.SplashTheme", MainLauncher = true, Exported = true, ScreenOrientation = ScreenOrientation.UserPortrait, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
#else
[Activity(Name = "uk.co.skyeye.companion.lab.MainActivity", Theme = "@style/Maui.SplashTheme", MainLauncher = true, Exported = true, ScreenOrientation = ScreenOrientation.UserPortrait, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
#endif
public class MainActivity : MauiAppCompatActivity
{
    ShellSession? shell;
    SheetBackCallback? sheetBack;
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        SkyEye.Companion.MigrationAudit.Report(this);
        base.OnCreate(savedInstanceState);
        shell = IPlatformApplication.Current!.Services.GetRequiredService<ShellSession>();
#if SKYEYE_PERFORMANCE
        if (Intent?.GetBooleanExtra("skyeye-performance", false) == true) shell.EnablePerformance();
#endif
        sheetBack = new(shell);
        OnBackPressedDispatcher.AddCallback(this, sheetBack);
        shell.Changed += UpdateBack;
        UpdateBack();
    }
    void UpdateBack() => MainThread.BeginInvokeOnMainThread(() =>
    {
        if (sheetBack is not null) sheetBack.Enabled = shell?.SheetOpen == true || shell?.EditingTable == true;
    });
    protected override void OnDestroy()
    {
        if (shell is not null) shell.Changed -= UpdateBack;
        sheetBack?.Remove(); sheetBack?.Dispose(); sheetBack = null;
        base.OnDestroy();
    }
    sealed class SheetBackCallback(ShellSession shell) : AndroidX.Activity.OnBackPressedCallback(false)
    {
        public override void HandleOnBackPressed() { if (shell.EditingTable) shell.CancelTableEdit(); else shell.SetSheet(false); }
    }
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
