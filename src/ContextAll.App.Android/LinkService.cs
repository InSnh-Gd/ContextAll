using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;

namespace ContextAll.App.Android;

[Service(ForegroundServiceType = ForegroundService.TypeConnectedDevice, Exported = false)]
public sealed class LinkService : Service
{
    public override IBinder? OnBind(Intent? intent) => null;
}
