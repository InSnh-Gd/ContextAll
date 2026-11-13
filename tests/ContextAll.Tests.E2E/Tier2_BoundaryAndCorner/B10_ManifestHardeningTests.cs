using ContextAll.Tests.E2E.Harness.AndroidHarness;

namespace ContextAll.Tests.E2E.Tier2_BoundaryAndCorner;

[Trait("Category", "Tier2")]
[Trait("Feature", "B10")]
public sealed class B10_ManifestHardeningTests
{
    [Fact]
    public void Manifest_Rejects_Missing_NeverForLocation_On_NearbyWifi()
    {
        const string badXml = """
        <manifest xmlns:android="http://schemas.android.com/apk/res/android">
            <uses-permission android:name="android.permission.NEARBY_WIFI_DEVICES" />
        </manifest>
        """;
        var result = ManifestInspector.InspectXml(badXml);
        Assert.True(result.HasNearbyWifiDevices);
        Assert.False(result.NearbyWifiHasNeverForLocation);
    }

    [Fact]
    public void Manifest_Rejects_Deprecated_WriteExternalStorage()
    {
        const string badXml = """
        <manifest xmlns:android="http://schemas.android.com/apk/res/android">
            <uses-permission android:name="android.permission.WRITE_EXTERNAL_STORAGE" />
        </manifest>
        """;
        var result = ManifestInspector.InspectXml(badXml);
        Assert.True(result.HasWriteExternalStorage);
        Assert.False(result.HasAllRequiredPermissions);
    }

    [Fact]
    public void Manifest_Rejects_Exported_True_On_Service()
    {
        const string badXml = """
        <manifest xmlns:android="http://schemas.android.com/apk/res/android">
            <application>
                <service android:name=".LinkService" android:exported="true" android:foregroundServiceType="connectedDevice" />
            </application>
        </manifest>
        """;
        var result = ManifestInspector.InspectXml(badXml);
        Assert.False(result.ServiceExportedFalse);
        Assert.False(result.HasAllRequiredPermissions);
    }

    [Fact]
    public void Manifest_Rejects_Wrong_ForegroundServiceType()
    {
        const string badXml = """
        <manifest xmlns:android="http://schemas.android.com/apk/res/android">
            <application>
                <service android:name=".LinkService" android:exported="false" android:foregroundServiceType="dataSync" />
            </application>
        </manifest>
        """;
        var result = ManifestInspector.InspectXml(badXml);
        Assert.False(result.ServiceHasConnectedDeviceType);
    }

    [Fact]
    public void Manifest_Rejects_Incomplete_Permissions_Set()
    {
        // Manifest missing POST_NOTIFICATIONS
        const string incompleteXml = """
        <manifest xmlns:android="http://schemas.android.com/apk/res/android">
            <uses-permission android:name="android.permission.INTERNET" />
            <application>
                <service android:name=".LinkService" android:exported="false" android:foregroundServiceType="connectedDevice" />
            </application>
        </manifest>
        """;
        var result = ManifestInspector.InspectXml(incompleteXml);
        Assert.False(result.HasAllRequiredPermissions);
        Assert.False(result.HasPostNotifications);
    }
}
