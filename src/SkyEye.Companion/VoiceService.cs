using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace SkyEye.Companion;

[Service(Exported = false, ForegroundServiceType = ForegroundService.TypeMicrophone)]
public sealed class VoiceService : Service
{
    public override IBinder? OnBind(Intent? intent) => null;
    public override void OnCreate()
    {
        base.OnCreate(); var manager = (NotificationManager)GetSystemService(NotificationService)!;
        manager.CreateNotificationChannel(new NotificationChannel("maeve", "Maeve conversation", NotificationImportance.Low));
        var open = PendingIntent.GetActivity(this, 2, new Intent(this, typeof(MainActivity)), PendingIntentFlags.Immutable | PendingIntentFlags.UpdateCurrent);
        StartForeground(2001, new Notification.Builder(this, "maeve").SetContentTitle("Maeve is with you").SetContentText("Open SkyEye to mute or end the conversation.").SetSmallIcon(Android.Resource.Drawable.IcMenuCall).SetContentIntent(open).SetOngoing(true).Build(), ForegroundService.TypeMicrophone);
    }
    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId) => StartCommandResult.NotSticky;
}
