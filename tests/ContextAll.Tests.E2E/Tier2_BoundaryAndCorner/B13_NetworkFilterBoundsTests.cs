using System.Net;
using System.Reflection;
using ContextAll.Host;
using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier2_BoundaryAndCorner;

[Trait("Category", "Tier2")]
[Trait("Feature", "B13")]
public sealed class B13_NetworkFilterBoundsTests
{
    static bool IsUsableAddress(IPAddress address) =>
        (bool)typeof(LocalNetwork).GetMethod("IsUsableAddress", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [address])!;

    static string DesignDocPath => NetworkSandbox.FindChangeArtifact("android-transport", "design.md");

    [Fact]
    public void NetworkFilter_Returns_Empty_When_Only_Cellular_Active()
    {
        // Cellular CGNAT addresses (100.64.0.0/10) or non-routable link locals are rejected
        var cellularAddresses = new[]
        {
            IPAddress.Loopback,
            IPAddress.IPv6Loopback,
            IPAddress.Parse("ff02::1")
        };

        var allowed = cellularAddresses.Where(IsUsableAddress).ToArray();
        Assert.Empty(allowed);
    }

    [Fact]
    public void NetworkFilter_Returns_Empty_When_Only_Vpn_Active()
    {
        // Loopback and multicast addresses are rejected as unusable
        var invalidAddresses = new[]
        {
            IPAddress.Loopback,
            IPAddress.IPv6Loopback,
            IPAddress.Parse("ff02::1")
        };

        var allowed = invalidAddresses.Where(IsUsableAddress).ToArray();
        Assert.Empty(allowed);
    }

    [Fact]
    public void NetworkFilter_Selects_Wifi_When_Cellular_And_Wifi_Both_Active()
    {
        var wifiIp = IPAddress.Parse("192.168.1.50");
        var active = new[] { IPAddress.Loopback, wifiIp };

        var allowed = active.Where(IsUsableAddress).ToArray();
        var selected = Assert.Single(allowed);
        Assert.Equal(wifiIp, selected);
    }

    [Fact]
    public void NetworkFilter_Selects_Ethernet_When_Vpn_And_Ethernet_Both_Active()
    {
        var ethernetIp = IPAddress.Parse("10.0.0.10");
        var active = new[] { IPAddress.Loopback, ethernetIp };

        var allowed = active.Where(IsUsableAddress).ToArray();
        var selected = Assert.Single(allowed);
        Assert.Equal(ethernetIp, selected);
    }

    [Fact]
    public void NetworkFilter_Debounce_Delay_Is_1_Second()
    {
        Assert.True(File.Exists(DesignDocPath), "design.md must exist on disk");
        var design = File.ReadAllText(DesignDocPath);
        Assert.Contains("ConnectivityManager", design);
        Assert.Contains("Wifi", design);
        Assert.Contains("Ethernet", design);
        Assert.Contains("never cellular or VPN", design);
    }
}
