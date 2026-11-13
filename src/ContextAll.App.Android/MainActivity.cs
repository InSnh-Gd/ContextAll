using Android.App;
using Android.OS;
using Android.Widget;

namespace ContextAll.App.Android;

[Activity(Label = "ContextAll", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var devices = new ListView(this);
        devices.Adapter = new ArrayAdapter<string>(this, global::Android.Resource.Layout.SimpleListItem1, ["Devices"]);
        SetContentView(devices);
        _ = AndroidSession.Run();
    }
}

