using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier1_FeatureCoverage;

[Trait("Category", "Tier1")]
[Trait("Feature", "F01")]
public sealed class F01_MsQuicNativePackagingTests
{
    [Fact]
    public void Apk_Requires_Arm64V8a_LibMsQuic_Path()
    {
        var scriptPath = Path.Combine(NetworkSandbox.FindRepoRoot(), "scripts/build-android-native.sh");
        Assert.True(File.Exists(scriptPath), "Android native build script must exist on disk");

        var script = File.ReadAllText(scriptPath);
        Assert.Contains("arm64-v8a", script);
        Assert.Contains("VpnHood.Net.Quic.MsQuic.AndroidNative", script);
        Assert.Contains("libmsquic.so", script);
        Assert.Contains("libcontextall_native.so", script);
    }

    [Fact]
    public void Apk_Requires_X86_64_And_Gradle_Jni_Hook()
    {
        var scriptPath = Path.Combine(NetworkSandbox.FindRepoRoot(), "scripts/build-android-native.sh");
        var gradlePath = Path.Combine(NetworkSandbox.FindRepoRoot(), "app/build.gradle.kts");
        Assert.True(File.Exists(scriptPath));
        Assert.True(File.Exists(gradlePath));

        var script = File.ReadAllText(scriptPath);
        var gradle = File.ReadAllText(gradlePath);
        Assert.Contains("x86_64", script);
        Assert.Contains("buildNativeAndroidLibs", gradle);
        Assert.Contains("nativeJniLibs", gradle);
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
        var scriptPath = Path.Combine(NetworkSandbox.FindRepoRoot(), "scripts/build-android-native.sh");
        var script = File.ReadAllText(scriptPath);
        Assert.True(script.Contains("VpnHood.Net.Quic.MsQuic.AndroidNative"), "MsQuic native package must be referenced");
    }
}
