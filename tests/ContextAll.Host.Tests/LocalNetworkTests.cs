using System.Net;
using System.Net.NetworkInformation;
using ContextAll.Host;

namespace ContextAll.Host.Tests;

public sealed class LocalNetworkTests
{
    [Fact]
    public void Down_and_loopback_addresses_are_not_eligible()
    {
        Assert.True(LocalNetwork.IsEligible(OperationalStatus.Up));
        Assert.False(LocalNetwork.IsEligible(OperationalStatus.Down));
        Assert.False(LocalNetwork.IsUsableAddress(IPAddress.Loopback));
        Assert.False(LocalNetwork.IsUsableAddress(IPAddress.IPv6Loopback));
        Assert.True(LocalNetwork.IsUsableAddress(IPAddress.Parse("10.0.0.5")));
        Assert.True(LocalNetwork.IsUsableAddress(IPAddress.Parse("2001:db8::1")));
        Assert.False(LocalNetwork.IsUsableAddress(IPAddress.Parse("ff02::1")));
    }

    [Fact]
    public void Link_local_addresses_are_marked_for_both_families()
    {
        Assert.True(LocalNetwork.IsLinkLocal(IPAddress.Parse("169.254.1.5")));
        Assert.False(LocalNetwork.IsLinkLocal(IPAddress.Parse("10.0.0.5")));
        Assert.True(LocalNetwork.IsLinkLocal(IPAddress.Parse("fe80::1%2")));
        Assert.False(LocalNetwork.IsLinkLocal(IPAddress.Parse("2001:db8::1")));
    }

    [Fact]
    public void Enumerated_prefixes_have_an_index_and_never_loopback()
    {
        var prefixes = LocalNetwork.Enumerate();

        Assert.All(prefixes, prefix => Assert.False(IPAddress.IsLoopback(IPAddress.Parse(prefix.Address))));
        Assert.All(prefixes, prefix => Assert.True(prefix.InterfaceIndex >= 0));
    }

    [Fact]
    public void A_scoped_link_local_address_becomes_a_scoped_endpoint()
    {
        Assert.True(IPAddress.TryParse("fe80::1%2", out var address));
        Assert.Equal(2u, address.ScopeId);
        Assert.Equal("fe80::1%2", LocalNetwork.Text(address));

        var endpoint = new IPEndPoint(address, 42461);
        Assert.Equal(2u, endpoint.Address.ScopeId);
        Assert.Equal("fe80::1%2", endpoint.Address.ToString());
    }
}
