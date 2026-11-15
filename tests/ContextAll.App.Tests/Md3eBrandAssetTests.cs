using System.Buffers.Binary;
using ContextAll.App.Controls;

namespace ContextAll.App.Tests;

[Collection("AvaloniaUI")]
public sealed class Md3eBrandAssetTests
{
    [Fact]
    public void Embedded_app_icon_is_valid_256px_png_replacing_placeholder()
    {
        using var stream = typeof(ContextAllLogo).Assembly.GetManifestResourceStream("ContextAll.App.Assets.contextall.png");
        Assert.NotNull(stream);

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        var bytes = ms.ToArray();

        Assert.True(bytes.Length > 1024, $"Expected full-res PNG icon, found {bytes.Length} bytes.");

        // Verify PNG signature + IHDR chunk dimensions (256x256).
        ReadOnlySpan<byte> pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Assert.True(bytes.AsSpan(0, 8).SequenceEqual(pngSignature));
        Assert.Equal("IHDR", System.Text.Encoding.ASCII.GetString(bytes, 12, 4));
        var width = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20, 4));
        Assert.Equal(256, width);
        Assert.Equal(256, height);
    }

    [Fact]
    public void ContextAllLogo_exposes_exact_android_vector_paths_and_default_size()
    {
        var logo = new ContextAllLogo();
        Assert.Equal(28, logo.LogoSize);
        Assert.Contains("M30,33", ContextAllLogo.LeftDevicePathData);
        Assert.Contains("M58,41", ContextAllLogo.RightDevicePathData);
        Assert.Contains("M54,46", ContextAllLogo.BadgeCirclePathData);
        Assert.Contains("M50,53", ContextAllLogo.ArrowPathData);
    }
}
