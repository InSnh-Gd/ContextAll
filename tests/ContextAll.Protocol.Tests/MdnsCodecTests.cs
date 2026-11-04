using System.Net;
using System.Text;
using ContextAll.Protocol;

namespace ContextAll.Protocol.Tests;

public sealed class MdnsCodecTests
{
    const string DeviceId = "ca1aaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void A_response_round_trips_the_device_id_name_port_and_address()
    {
        var address = IPAddress.Parse("10.0.0.5");
        var packet = MdnsCodec.EncodeResponse(DeviceId, "desk", 42461, address);

        var read = MdnsCodec.Read(packet, IPAddress.Parse("10.0.0.9"));

        var service = Assert.Single(read.Services);
        Assert.Equal(DeviceId, service.DeviceId);
        Assert.Equal("desk", service.DisplayName);
        Assert.Equal(42461, service.Port);
        Assert.Equal(address, service.Address);
        Assert.False(read.ServiceQuery);
    }

    [Fact]
    public void A_query_is_recognised_and_yields_no_services()
    {
        var read = MdnsCodec.Read(MdnsCodec.EncodeQuery(7), IPAddress.Parse("10.0.0.9"));

        Assert.True(read.ServiceQuery);
        Assert.Empty(read.Services);
    }

    [Fact]
    public void A_truncated_packet_is_rejected_without_throwing()
    {
        var packet = MdnsCodec.EncodeResponse(DeviceId, "desk", 42461, IPAddress.Parse("10.0.0.5"));
        var thrown = Record.Exception(() => MdnsCodec.Read(packet.AsSpan(0, 20), IPAddress.Parse("10.0.0.9")));
        var read = MdnsCodec.Read(packet.AsSpan(0, 20), IPAddress.Parse("10.0.0.9"));

        Assert.Null(thrown);
        Assert.Empty(read.Services);
    }

    [Fact]
    public void A_compression_loop_is_rejected_without_hanging()
    {
        var packet = new byte[12 + 2 + 10 + 4];
        packet[3] = 1; // one answer
        packet[12] = 0xC0;
        packet[13] = 0x0C;
        packet[15] = 12;

        var read = MdnsCodec.Read(packet, IPAddress.Parse("10.0.0.9"));

        Assert.Empty(read.Services);
    }

    [Fact]
    public void An_oversized_label_is_rejected()
    {
        var packet = new byte[12 + 1 + 64];
        packet[3] = 1;
        packet[12] = 64;

        var read = MdnsCodec.Read(packet, IPAddress.Parse("10.0.0.9"));

        Assert.Empty(read.Services);
    }

    [Fact]
    public void An_uncompressed_ptr_and_srv_are_assembled()
    {
        var instance = DeviceId + "._contextall._udp.local";
        var packet = BuildSingleAnswer(instance);

        var read = MdnsCodec.Read(packet, IPAddress.Parse("10.0.0.9"));

        var service = Assert.Single(read.Services);
        Assert.Equal(DeviceId, service.DeviceId);
        Assert.Equal(42461, service.Port);
        Assert.Equal(IPAddress.Parse("10.0.0.9"), service.Address);
    }

    static byte[] BuildSingleAnswer(string instance)
    {
        var txt = Encoding.UTF8.GetBytes("id=" + DeviceId);
        var list = new List<byte>();
        WriteU16(list, 0);
        WriteU16(list, 0x8400);
        WriteU16(list, 0);
        WriteU16(list, 2);
        WriteU16(list, 0);
        WriteU16(list, 0);

        WritePtr(list, instance);
        WriteU16(list, 33);
        WriteU16(list, 1);
        WriteU32(list, 120);
        var srv = new List<byte>();
        WriteU16(srv, 0);
        WriteU16(srv, 0);
        WriteU16(srv, 42461);
        WriteName(srv, instance);
        WriteU16(list, (ushort)srv.Count);
        list.AddRange(srv);

        WritePtr(list, instance);
        WriteU16(list, 16);
        WriteU16(list, 1);
        WriteU32(list, 120);
        WriteU16(list, (ushort)(txt.Length + 1));
        list.Add((byte)txt.Length);
        list.AddRange(txt);
        return list.ToArray();
    }

    static void WritePtr(List<byte> list, string instance)
    {
        WriteName(list, instance);
    }

    static void WriteName(List<byte> list, string name)
    {
        foreach (var label in name.Split('.'))
        {
            var bytes = Encoding.ASCII.GetBytes(label);
            list.Add((byte)bytes.Length);
            list.AddRange(bytes);
        }

        list.Add(0);
    }

    static void WriteU16(List<byte> list, ushort value)
    {
        list.Add((byte)(value >> 8));
        list.Add((byte)(value & 0xFF));
    }

    static void WriteU32(List<byte> list, uint value)
    {
        list.Add((byte)(value >> 24));
        list.Add((byte)((value >> 16) & 0xFF));
        list.Add((byte)((value >> 8) & 0xFF));
        list.Add((byte)(value & 0xFF));
    }
}
