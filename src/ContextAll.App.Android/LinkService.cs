using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace ContextAll.App.Android;

[Service(Name = "app.contextall.LinkService", ForegroundServiceType = ForegroundService.TypeConnectedDevice, Exported = false)]
public sealed class LinkService : Service
{
    public const string ChannelId = "contextall_link";
    public const int NotificationId = 42461;

    static volatile bool isForegroundRunning;

    public static bool IsForegroundRunning => isForegroundRunning;

    public override void OnCreate()
    {
        base.OnCreate();
        PromoteToForeground();
        AndroidSession.Start(ApplicationContext ?? this);
        isForegroundRunning = true;
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        PromoteToForeground();
        AndroidSession.Start(ApplicationContext ?? this);
        isForegroundRunning = true;
        return StartCommandResult.Sticky;
    }

    public override void OnDestroy()
    {
        isForegroundRunning = false;
        AndroidSession.Stop();
        base.OnDestroy();
    }

    public override IBinder? OnBind(Intent? intent) => null;

    void PromoteToForeground()
    {
        var manager = GetSystemService(NotificationService) as NotificationManager;
        if (manager is not null && manager.GetNotificationChannel(ChannelId) is null)
        {
            using var channel = new NotificationChannel(ChannelId, "ContextAll", NotificationImportance.Low)
            {
                Description = "ContextAll LAN session",
            };
            manager.CreateNotificationChannel(channel);
        }

        using var builder = new Notification.Builder(this, ChannelId)
            .SetContentTitle("ContextAll")
            .SetContentText("LAN QUIC session active")
            .SetSmallIcon(global::Android.Resource.Drawable.StatNotifySync)
            .SetOngoing(true);
        var notification = builder.Build();
        StartForeground(NotificationId, notification, ForegroundService.TypeConnectedDevice);
    }
}
