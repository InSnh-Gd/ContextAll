using System.Net;
using ContextAll.Host;
using ContextAll.Protocol;

namespace ContextAll.Tests.E2E.Tier2_BoundaryAndCorner;

[Trait("Category", "Tier2")]
[Trait("Feature", "B14")]
public sealed class B14_NsdRobustnessTests
{
    [Fact]
    public void Mdns_Reads_Empty_Packet_Without_Throwing()
    {
        var read = MdnsCodec.Read([], IPAddress.Loopback);
        Assert.Empty(read.Services);
        Assert.False(read.ServiceQuery);
    }

    [Fact]
    public void Mdns_Reads_Truncated_Packet_Without_Throwing()
    {
        var truncated = new byte[] { 0, 1, 0x84, 0x00, 0, 0, 0, 1 }; // Header only 8 bytes < 12
        var read = MdnsCodec.Read(truncated, IPAddress.Loopback);
        Assert.Empty(read.Services);
        Assert.False(read.ServiceQuery);
    }

    [Fact]
    public void Mdns_Handles_Corrupted_Record_Count_Gracefully()
    {
        var packet = new byte[64];
        packet[6] = 0xFF; // Declared 65535 answers
        packet[7] = 0xFF;

        var read = MdnsCodec.Read(packet, IPAddress.Loopback);
        Assert.NotNull(read);
    }

    [Fact]
    public void Mdns_Parses_Valid_Encoded_Response()
    {
        const string deviceId = "ca1aaaaaaaaaaaaaaaaaaaaaaaaaa";
        var response = MdnsCodec.EncodeResponse(deviceId, "DeviceA", 42461, IPAddress.Parse("192.168.1.100"));
        var read = MdnsCodec.Read(response, IPAddress.Parse("192.168.1.100"));

        Assert.NotEmpty(read.Services);
        Assert.Equal(deviceId, read.Services[0].DeviceId);
        Assert.Equal(42461, read.Services[0].Port);
    }

    [Fact]
    public void Mdns_Service_Port_Is_Strictly_5353()
    {
        Assert.Equal(5353, MdnsDiscovery.Port);
    }
}
