using System.Xml.Linq;

namespace ContextAll.Tests.E2E.Harness.AndroidHarness;

public sealed class ManifestInspectionResult
{
    public bool HasInternet { get; set; }
    public bool HasAccessNetworkState { get; set; }
    public bool HasAccessWifiState { get; set; }
    public bool HasChangeWifiMulticastState { get; set; }
    public bool HasNearbyWifiDevices { get; set; }
    public bool NearbyWifiHasNeverForLocation { get; set; }
    public bool HasForegroundService { get; set; }
    public bool HasForegroundServiceConnectedDevice { get; set; }
    public bool HasPostNotifications { get; set; }
    public bool HasWriteExternalStorage { get; set; }
    public bool ServiceFound { get; set; }
    public bool ServiceExportedFalse { get; set; }
    public bool ServiceHasConnectedDeviceType { get; set; }
    public List<string> DeclaredPermissions { get; } = [];
    public List<string> MissingPermissions { get; } = [];

    public bool HasAllRequiredPermissions =>
        HasInternet &&
        HasAccessNetworkState &&
        HasAccessWifiState &&
        HasChangeWifiMulticastState &&
        HasNearbyWifiDevices &&
        NearbyWifiHasNeverForLocation &&
        HasForegroundService &&
        HasForegroundServiceConnectedDevice &&
        HasPostNotifications &&
        !HasWriteExternalStorage &&
        ServiceFound &&
        ServiceExportedFalse &&
        ServiceHasConnectedDeviceType;
}

public static class ManifestInspector
{
    static readonly XNamespace AndroidNs = "http://schemas.android.com/apk/res/android";

    public static readonly string[] RequiredPermissions =
    [
        "android.permission.INTERNET",
        "android.permission.ACCESS_NETWORK_STATE",
        "android.permission.ACCESS_WIFI_STATE",
        "android.permission.CHANGE_WIFI_MULTICAST_STATE",
        "android.permission.NEARBY_WIFI_DEVICES",
        "android.permission.FOREGROUND_SERVICE",
        "android.permission.FOREGROUND_SERVICE_CONNECTED_DEVICE",
        "android.permission.POST_NOTIFICATIONS"
    ];

    public static string SourceManifestPath =>
        Path.Combine(NetworkSandbox.FindRepoRoot(), "app/src/main/AndroidManifest.xml");

    public static string? FindLatestMergedManifestPath()
    {
        var repoRoot = NetworkSandbox.FindRepoRoot();
        var intermediatesDir = Path.Combine(repoRoot, "app/build/intermediates");
        if (!Directory.Exists(intermediatesDir))
        {
            return null;
        }

        var candidates = Directory.GetFiles(intermediatesDir, "AndroidManifest.xml", SearchOption.AllDirectories)
            .Where(f => f.Contains("merged_manifest", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .ToArray();

        return candidates.FirstOrDefault();
    }

    public static ManifestInspectionResult InspectFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Manifest file not found: " + filePath, filePath);
        }

        var content = File.ReadAllText(filePath);
        return InspectXml(content);
    }

    public static ManifestInspectionResult InspectSourceManifest() =>
        InspectFile(SourceManifestPath);

    public static ManifestInspectionResult InspectXml(string xmlContent)
    {
        var doc = XDocument.Parse(xmlContent);
        var result = new ManifestInspectionResult();

        // 1. Inspect uses-permission tags
        foreach (var elem in doc.Descendants("uses-permission"))
        {
            var name = (string?)elem.Attribute(AndroidNs + "name") ?? (string?)elem.Attribute("name");
            if (string.IsNullOrEmpty(name)) continue;

            result.DeclaredPermissions.Add(name);

            switch (name)
            {
                case "android.permission.INTERNET":
                    result.HasInternet = true;
                    break;
                case "android.permission.ACCESS_NETWORK_STATE":
                    result.HasAccessNetworkState = true;
                    break;
                case "android.permission.ACCESS_WIFI_STATE":
                    result.HasAccessWifiState = true;
                    break;
                case "android.permission.CHANGE_WIFI_MULTICAST_STATE":
                    result.HasChangeWifiMulticastState = true;
                    break;
                case "android.permission.NEARBY_WIFI_DEVICES":
                    result.HasNearbyWifiDevices = true;
                    var flags = (string?)elem.Attribute(AndroidNs + "usesPermissionFlags") ?? (string?)elem.Attribute("usesPermissionFlags");
                    if (flags != null && flags.Contains("neverForLocation", StringComparison.OrdinalIgnoreCase))
                    {
                        result.NearbyWifiHasNeverForLocation = true;
                    }
                    break;
                case "android.permission.FOREGROUND_SERVICE":
                    result.HasForegroundService = true;
                    break;
                case "android.permission.FOREGROUND_SERVICE_CONNECTED_DEVICE":
                    result.HasForegroundServiceConnectedDevice = true;
                    break;
                case "android.permission.POST_NOTIFICATIONS":
                    result.HasPostNotifications = true;
                    break;
                case "android.permission.WRITE_EXTERNAL_STORAGE":
                    result.HasWriteExternalStorage = true;
                    break;
            }
        }

        // Check for missing permissions
        foreach (var req in RequiredPermissions)
        {
            if (!result.DeclaredPermissions.Contains(req, StringComparer.Ordinal))
            {
                result.MissingPermissions.Add(req);
            }
        }

        // 2. Inspect LinkService declaration
        var serviceElem = doc.Descendants("service").FirstOrDefault(s =>
        {
            var name = (string?)s.Attribute(AndroidNs + "name") ?? (string?)s.Attribute("name");
            return name is not null && name.EndsWith("LinkService", StringComparison.Ordinal);
        });

        if (serviceElem is not null)
        {
            result.ServiceFound = true;
            var exported = (string?)serviceElem.Attribute(AndroidNs + "exported") ?? (string?)serviceElem.Attribute("exported");
            result.ServiceExportedFalse = string.Equals(exported, "false", StringComparison.OrdinalIgnoreCase);

            var fgType = (string?)serviceElem.Attribute(AndroidNs + "foregroundServiceType") ?? (string?)serviceElem.Attribute("foregroundServiceType");
            result.ServiceHasConnectedDeviceType = string.Equals(fgType, "connectedDevice", StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }
}
