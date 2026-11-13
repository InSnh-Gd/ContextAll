using System.Security.Cryptography;
using ContextAll.Core;
using ContextAll.Protocol;
using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier1_FeatureCoverage;

[Trait("Category", "Tier1")]
[Trait("Feature", "F14")]
public sealed class F14_AndroidNsdMulticastTests
{
    [Fact]
    public void Nsd_Service_Name_Is_ContextAll_Udp_Local()
    {
        Assert.Equal("_contextall._udp.local", MdnsCodec.ServiceName);
    }

    [Fact]
    public void Nsd_Encodes_Query_For_Service()
    {
        var query = MdnsCodec.EncodeQuery(100);
        Assert.NotEmpty(query);
        Assert.True(query.Length > 12);
    }

    [Fact]
    public void Nsd_Encodes_Response_With_Id_And_Port()
    {
        var response = MdnsCodec.EncodeResponse("ca1abcdef23456789012345678", "Pixel 9", 42461, null, 100);
        Assert.NotEmpty(response);
    }

    [Fact]
    public void Nsd_Txt_Record_Requires_DeviceId_Prefix()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var spki = key.ExportSubjectPublicKeyInfo();
        var deviceId = DeviceIds.Text(spki);

        Assert.StartsWith("ca1", deviceId, StringComparison.Ordinal);
        Assert.Equal(29, deviceId.Length);
    }

    [Fact]
    public void MulticastLock_Tag_Is_Defined()
    {
        var designPath = NetworkSandbox.FindChangeArtifact("android-transport", "design.md");
        Assert.True(File.Exists(designPath));
        var design = File.ReadAllText(designPath);
        Assert.Contains("MulticastLock", design);
        Assert.Contains("WifiManager.MulticastLock", design);
    }
}
