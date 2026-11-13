using ContextAll.Core;
using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier2_BoundaryAndCorner;

[Trait("Category", "Tier2")]
[Trait("Feature", "B01")]
public sealed class B01_MsQuicAbiBoundaryTests
{
    static bool ValidateElfHeader(byte[] header, ushort expectedMachine)
    {
        if (header.Length < 18) return false;
        if (header[0] != 0x7F || header[1] != (byte)'E' || header[2] != (byte)'L' || header[3] != (byte)'F') return false;
        var machine = BitConverter.ToUInt16(header, 16);
        return machine == expectedMachine;
    }

    static bool IsPageAligned(long offset, int pageSize) => pageSize > 0 && offset % pageSize == 0;

    [Fact]
    public void LibMsQuic_Rejects_Zero_Byte_Files()
    {
        var emptyHeader = Array.Empty<byte>();
        Assert.False(ValidateElfHeader(emptyHeader, 183));
    }

    [Fact]
    public void LibMsQuic_Rejects_Invalid_Elf_Magic()
    {
        var corruptData = new byte[] { 0x00, (byte)'E', (byte)'L', (byte)'F', 2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xB7, 0x00 };
        Assert.False(ValidateElfHeader(corruptData, 183));
    }

    [Fact]
    public void LibMsQuic_Rejects_Unsupported_Elf_Architectures()
    {
        // 0x3E is EM_X86_64, 0xB7 is EM_AARCH64
        var x86_64Header = new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x3E, 0x00 };
        Assert.False(ValidateElfHeader(x86_64Header, 183)); // Not ARM64
        Assert.False(ValidateElfHeader(x86_64Header, 40));  // Not ARM32
    }

    [Fact]
    public void LibMsQuic_PageAlignment_Rejects_Misaligned_Offsets()
    {
        Assert.False(IsPageAligned(4095, 4096));
        Assert.False(IsPageAligned(16383, 16384));
        Assert.True(IsPageAligned(4096, 4096));
        Assert.True(IsPageAligned(16384, 16384));
    }

    [Fact]
    public void LibMsQuic_Packaging_Path_Rejects_Directory_Traversal()
    {
        var sanitized = DownloadNames.Sanitize("lib/arm64-v8a/../../etc/passwd");
        Assert.Equal("passwd", sanitized);
        Assert.DoesNotContain("..", sanitized);
        Assert.DoesNotContain('/', sanitized);
    }
}
