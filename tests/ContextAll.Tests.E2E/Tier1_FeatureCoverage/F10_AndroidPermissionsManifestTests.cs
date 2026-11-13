using ContextAll.Tests.E2E.Harness.AndroidHarness;

namespace ContextAll.Tests.E2E.Tier1_FeatureCoverage;

[Trait("Category", "Tier1")]
[Trait("Feature", "F10")]
public sealed class F10_AndroidPermissionsManifestTests
{
    [Fact]
    public void Manifest_Requires_Eight_Mandatory_Permissions()
    {
        Assert.Equal(8, ManifestInspector.RequiredPermissions.Length);
        Assert.Contains("android.permission.INTERNET", ManifestInspector.RequiredPermissions);
        Assert.Contains("android.permission.ACCESS_NETWORK_STATE", ManifestInspector.RequiredPermissions);
        Assert.Contains("android.permission.ACCESS_WIFI_STATE", ManifestInspector.RequiredPermissions);
        Assert.Contains("android.permission.CHANGE_WIFI_MULTICAST_STATE", ManifestInspector.RequiredPermissions);
        Assert.Contains("android.permission.NEARBY_WIFI_DEVICES", ManifestInspector.RequiredPermissions);
        Assert.Contains("android.permission.FOREGROUND_SERVICE", ManifestInspector.RequiredPermissions);
        Assert.Contains("android.permission.FOREGROUND_SERVICE_CONNECTED_DEVICE", ManifestInspector.RequiredPermissions);
        Assert.Contains("android.permission.POST_NOTIFICATIONS", ManifestInspector.RequiredPermissions);
    }

    [Fact]
    public void ExpectedManifest_Passes_All_Audits()
    {
        var sourceResult = ManifestInspector.InspectSourceManifest();
        Assert.True(sourceResult.ServiceFound, "LinkService must be declared in AndroidManifest.xml");
        Assert.True(sourceResult.ServiceExportedFalse, "LinkService must have android:exported='false'");
        Assert.True(sourceResult.ServiceHasConnectedDeviceType, "LinkService must have foregroundServiceType='connectedDevice'");

        // Validate complete permission set parsing via InspectXml
        const string fullXml = """
            <?xml version="1.0" encoding="utf-8"?>
            <manifest xmlns:android="http://schemas.android.com/apk/res/android">
              <uses-permission android:name="android.permission.INTERNET" />
              <uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />
              <uses-permission android:name="android.permission.ACCESS_WIFI_STATE" />
              <uses-permission android:name="android.permission.CHANGE_WIFI_MULTICAST_STATE" />
              <uses-permission android:name="android.permission.NEARBY_WIFI_DEVICES" android:usesPermissionFlags="neverForLocation" />
              <uses-permission android:name="android.permission.FOREGROUND_SERVICE" />
              <uses-permission android:name="android.permission.FOREGROUND_SERVICE_CONNECTED_DEVICE" />
              <uses-permission android:name="android.permission.POST_NOTIFICATIONS" />
              <application>
                <service android:name=".LinkService" android:exported="false" android:foregroundServiceType="connectedDevice" />
              </application>
            </manifest>
            """;
        var parsed = ManifestInspector.InspectXml(fullXml);
        Assert.True(parsed.HasAllRequiredPermissions);
    }

    [Fact]
    public void Manifest_LinkService_Must_Be_Exported_False()
    {
        var result = ManifestInspector.InspectSourceManifest();
        Assert.True(result.ServiceExportedFalse);
    }

    [Fact]
    public void Manifest_LinkService_ForegroundType_Must_Be_ConnectedDevice()
    {
        var result = ManifestInspector.InspectSourceManifest();
        Assert.True(result.ServiceHasConnectedDeviceType);
    }

    [Fact]
    public void Manifest_Inspects_Source_Manifest_Safely()
    {
        Assert.True(File.Exists(ManifestInspector.SourceManifestPath));
        var result = ManifestInspector.InspectSourceManifest();
        Assert.True(result.ServiceFound);
        Assert.False(result.HasWriteExternalStorage);
    }
}
