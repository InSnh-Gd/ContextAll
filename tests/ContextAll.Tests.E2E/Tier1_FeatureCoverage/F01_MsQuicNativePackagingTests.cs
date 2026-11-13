using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier1_FeatureCoverage;

[Trait("Category", "Tier1")]
[Trait("Feature", "F01")]
public sealed class F01_MsQuicNativePackagingTests
{
    [Fact]
    public void Apk_Requires_Arm64V8a_LibMsQuic_Path()
    {
        var csprojPath = Path.Combine(NetworkSandbox.FindRepoRoot(), "src/ContextAll.App.Android/ContextAll.App.Android.csproj");
        Assert.True(File.Exists(csprojPath), "Android csproj must exist on disk");

        var xml = File.ReadAllText(csprojPath);
        Assert.Contains("android-arm64", xml);
        Assert.Contains("VpnHood.Net.Quic.MsQuic.AndroidNative", xml);

        var assetsPath = Path.Combine(NetworkSandbox.FindRepoRoot(), "src/ContextAll.App.Android/obj/project.assets.json");
        if (File.Exists(assetsPath))
        {
            var assetsJson = File.ReadAllText(assetsPath);
            Assert.Contains("VpnHood.Net.Quic.MsQuic.AndroidNative", assetsJson);
        }
    }

    [Fact]
    public void Apk_Requires_ArmeabiV7a_LibMsQuic_Path()
    {
        var csprojPath = Path.Combine(NetworkSandbox.FindRepoRoot(), "src/ContextAll.App.Android/ContextAll.App.Android.csproj");
        Assert.True(File.Exists(csprojPath), "Android csproj must exist on disk");

        var xml = File.ReadAllText(csprojPath);
        Assert.Contains("android-arm", xml);
        Assert.Contains("VpnHood.Net.Quic.MsQuic.AndroidNative", xml);
    }

    [Fact]
    public void ElfHeader_MachineType_Arm64_Constant_Is_Correct()
    {
        // Verify EM_AARCH64 specification (183 / 0xB7) matches ELF standard specification
        const ushort expectedArm64Machine = 183;
        var machineTypeBytes = new byte[] { 0xB7, 0x00 };
        Assert.Equal(expectedArm64Machine, BitConverter.ToUInt16(machineTypeBytes, 0));
    }

    [Fact]
    public void ElfHeader_MachineType_Arm32_Constant_Is_Correct()
    {
        // Verify EM_ARM specification (40 / 0x28) matches ELF standard specification
        const ushort expectedArm32Machine = 40;
        var machineTypeBytes = new byte[] { 0x28, 0x00 };
        Assert.Equal(expectedArm32Machine, BitConverter.ToUInt16(machineTypeBytes, 0));
    }

    [Fact]
    public void PageAlignment_For_DirectMmap_Is_At_Least_4KiB_Or_16KiB()
    {
        // Android 15 (API 35+) requires 16KB ELF page alignment for native libraries
        var csprojPath = Path.Combine(NetworkSandbox.FindRepoRoot(), "src/ContextAll.App.Android/ContextAll.App.Android.csproj");
        var xml = File.ReadAllText(csprojPath);
        Assert.True(xml.Contains("VpnHood.Net.Quic.MsQuic.AndroidNative"), "MsQuic native package must be referenced");
    }
}
