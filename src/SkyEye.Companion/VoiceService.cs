using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace SkyEye.Companion;

[Service(Name = "crc648b11b8ca3be5d4a4.VoiceService", Exported = false, ForegroundServiceType = ForegroundService.TypeMicrophone)]
public sealed class VoiceService : Service
{
    public override IBinder? OnBind(Intent? intent) => null;
    public override void OnCreate()
    {
        base.OnCreate(); var manager = (NotificationManager)GetSystemService(NotificationService)!;
        // Keep the installed channel ID and its user notification preferences.
        manager.CreateNotificationChannel(new NotificationChannel("maeve", "Sky conversation", NotificationImportance.Low));
        var openIntent = PackageManager?.GetLaunchIntentForPackage(PackageName ?? string.Empty);
        var notification = new Notification.Builder(this, "maeve")
            .SetContentTitle("Sky is with you")
            .SetContentText("Open SkyEye to mute or end the conversation.")
            .SetSmallIcon(Android.Resource.Drawable.IcMenuCall)
            .SetOngoing(true);
        if (openIntent is not null)
        {
            var open = PendingIntent.GetActivity(this, 2, openIntent, PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
            notification.SetContentIntent(open);
        }
        StartForeground(2001, notification.Build(), ForegroundService.TypeMicrophone);
    }
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId) => StartCommandResult.NotSticky;
}
