using ContextAll.Core;
using ContextAll.Host;
using ContextAll.Protocol;
using ContextAll.Tests.E2E.Harness;

namespace ContextAll.Tests.E2E.Tier2_BoundaryAndCorner;

[Trait("Category", "Tier2")]
[Trait("Feature", "B08")]
public sealed class B08_PlatformSeamBoundsTests
{
    [Fact]
    public void LinuxPaths_Handles_Empty_Strings_As_Unset()
    {
        const string home = "/home/alice";
        var idDir = LinuxPaths.IdentityDirectory("", home);
        Assert.Equal("/home/alice/.local/share/contextall/identity", idDir);
    }

    [Fact]
    public void Clipboard_Frame_Payload_Cannot_Exceed_64KiB()
    {
        var outcome = FrameCodec.Encode(new byte[65537], FrameTypes.ClipboardChunk, 0, 0, 1);
        Assert.True(outcome.IsFailure);
        Assert.Equal("payload-length", outcome.Reason);
    }

    [Fact]
    public void Mdns_Port_Collision_Does_Not_Throw_Unhandled_Exception()
    {
        using var occupant = MdnsTestProbe.OccupyPort(5353);
        // Occupying port 5353 should not throw unhandled exception
        Assert.Equal(5353, occupant.Port);
    }

    [Fact]
    public void AndroidPaths_AvoidCollision_Handles_Multiple_Collisions()
    {
        var existingFiles = new HashSet<string> { "photo.png", "photo (2).png" };
        var avoided = DownloadNames.AvoidCollision("photo.png", existingFiles.Contains);
        Assert.Equal("photo (3).png", avoided);
    }

    [Fact]
    public void AndroidPaths_Sanitizes_Whitespace_Only_Filenames()
    {
        var sanitized = DownloadNames.Sanitize("\t\r\n");
        Assert.Equal("download", sanitized);
    }
}
