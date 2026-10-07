namespace SkyEye.Vision.Device;

[global::Android.App.Activity(Name="uk.co.skyeye.visionchecks.HudPreviewActivity",Exported=true)]
public sealed class HudPreviewActivity : global::Android.App.Activity
{
    global::Android.Graphics.Bitmap? bitmap;
    protected override void OnCreate(global::Android.OS.Bundle? state)
    {
        base.OnCreate(state);Window!.AddFlags(global::Android.Views.WindowManagerFlags.KeepScreenOn);
        var name=Intent?.GetStringExtra("image")??"hud-phone-2047.png";
        if(name!=Path.GetFileName(name)||!name.StartsWith("hud-"))throw new InvalidOperationException("Unknown HUD preview");
        bitmap=global::Android.Graphics.BitmapFactory.DecodeFile(Path.Combine(GetExternalFilesDir(null)!.AbsolutePath,name));
        var image=new global::Android.Widget.ImageView(this);image.SetBackgroundColor(global::Android.Graphics.Color.Black);
        image.SetScaleType(global::Android.Widget.ImageView.ScaleType.FitCenter);image.SetImageBitmap(bitmap);SetContentView(image);
    }
    protected override void OnDestroy(){bitmap?.Recycle();bitmap?.Dispose();base.OnDestroy();}
}
