using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Widget;

namespace SkyEye.Camera;

[Activity(Name = "uk.co.skyeye.camera.MainActivity", Label = "SkyEye Camera", MainLauncher = true, Exported = true, Theme = "@android:style/Theme.Material.NoActionBar")]
public sealed class MainActivity : Activity
{
    TextView status = null!;
    Handler handler = null!;
    bool visible;
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var content = new LinearLayout(this) { Orientation = Orientation.Vertical };
        content.SetPadding(32, 56, 32, 32); content.SetBackgroundColor(Color.Rgb(11, 20, 28));
        var title = new TextView(this) { Text = "SkyEye\nMounted camera", TextSize = 30 }; title.SetTextColor(Color.White); content.AddView(title);
        var help = new TextView(this) { Text = "Arm once before mounting. Your companion starts, pauses and stops recording. You can switch this display off.", TextSize = 17 }; help.SetTextColor(Color.Rgb(180, 205, 210)); content.AddView(help);
        var arm = new Button(this) { Text = "Arm camera" }; arm.Click += (_, _) => Arm(); content.AddView(arm);
        status = new TextView(this) { TextSize = 18 }; status.SetTextColor(Color.Rgb(80, 220, 185)); content.AddView(status);
        SetContentView(content); handler = new Handler(Looper.MainLooper!);
        if (Intent?.GetBooleanExtra("arm", false) == true) Arm();
    }
    void Arm()
    {
        string[] permissions = [Manifest.Permission.Camera, Manifest.Permission.RecordAudio];
        var missing = permissions.Where(x => CheckSelfPermission(x) != Permission.Granted).ToArray();
        if (missing.Length > 0) { RequestPermissions(missing, 1); return; }
        if (CheckSelfPermission(Manifest.Permission.PostNotifications) != Permission.Granted && !GetPreferences(FileCreationMode.Private)!.GetBoolean("notificationAsked", false)) { GetPreferences(FileCreationMode.Private)!.Edit()!.PutBoolean("notificationAsked", true)!.Apply(); RequestPermissions([Manifest.Permission.PostNotifications], 3); }
        if (global::SkyEye.Camera.CameraService.Instance?.Authority.State == SkyEye.Core.CaptureState.Faulted)
        {
            StopService(new Intent(this, typeof(global::SkyEye.Camera.CameraService)));
            handler.PostDelayed(StartWhenStopped, 200);
        }
        else if (global::SkyEye.Camera.CameraService.Instance is null) StartForegroundService(new Intent(this, typeof(global::SkyEye.Camera.CameraService)));
    }
    void StartWhenStopped()
    {
        if (!visible) return;
        if (global::SkyEye.Camera.CameraService.Instance is not null) { handler.PostDelayed(StartWhenStopped, 200); return; }
        StartForegroundService(new Intent(this, typeof(global::SkyEye.Camera.CameraService)));
    }
    protected override void OnNewIntent(Intent? intent) { base.OnNewIntent(intent); if (intent?.GetBooleanExtra("arm", false) == true) Arm(); }
    public override void OnRequestPermissionsResult(int code, string[] permissions, Permission[] results) { base.OnRequestPermissionsResult(code, permissions, results); if (code == 1 && CheckSelfPermission(Manifest.Permission.Camera) == Permission.Granted && CheckSelfPermission(Manifest.Permission.RecordAudio) == Permission.Granted) Arm(); }
    protected override void OnResume() { base.OnResume(); visible = true; Refresh(); }
    protected override void OnPause() { visible = false; base.OnPause(); }
    void Refresh() { if (!visible) return; status.Text = global::SkyEye.Camera.CameraService.Instance?.DisplayStatus ?? "Camera is not armed."; handler.PostDelayed(Refresh, 1500); }
}
