using System.Net;
using System.Reflection;
using ContextAll.Host;

namespace ContextAll.Tests.E2E.Tier1_FeatureCoverage;

[Trait("Category", "Tier1")]
[Trait("Feature", "F13")]
public sealed class F13_NetworkFilterTests
{
    static bool IsUsableAddress(IPAddress address) =>
        (bool)typeof(LocalNetwork).GetMethod("IsUsableAddress", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [address])!;

    static bool IsLinkLocal(IPAddress address) =>
        (bool)typeof(LocalNetwork).GetMethod("IsLinkLocal", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [address])!;

    [Fact]
    public void NetworkFilter_Rejects_Loopback()
    {
        Assert.False(IsUsableAddress(IPAddress.Loopback));
        Assert.False(IsUsableAddress(IPAddress.IPv6Loopback));
    }

    [Fact]
    public void NetworkFilter_Accepts_Lan_Addresses()
    {
        Assert.True(IsUsableAddress(IPAddress.Parse("192.168.1.100")));
        Assert.True(IsUsableAddress(IPAddress.Parse("10.0.0.5")));
    }

    [Fact]
    public void NetworkFilter_Detects_LinkLocal_Addresses()
    {
        Assert.True(IsLinkLocal(IPAddress.Parse("169.254.1.1")));
        Assert.False(IsLinkLocal(IPAddress.Parse("192.168.1.1")));
    }

    [Fact]
    public void NetworkFilter_Rejects_IPv6_Multicast()
    {
        Assert.False(IsUsableAddress(IPAddress.Parse("ff02::1")));
    }

    [Fact]
    public void NetworkFilter_Selects_Only_Allowed_Networks_From_List()
    {
        var candidates = new[]
        {
            IPAddress.Loopback,
            IPAddress.Parse("192.168.1.50"),
            IPAddress.Parse("ff02::1"),
            IPAddress.Parse("10.0.0.2")
        };

        var usable = candidates.Where(IsUsableAddress).Select(a => a.ToString()).ToArray();
        Assert.Equal(["192.168.1.50", "10.0.0.2"], usable);
    }
}
