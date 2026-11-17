using ContextAll.Protocol;

namespace ContextAll.Core;

public static class LocalOperator
{
    public static bool RequestIdOk(string id) =>
        id.Length is >= 1 and <= 64 && id.All(ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f');

    public static string? Remember(ISet<string> seen, string requestId) =>
        seen.Add(requestId) ? null : "duplicate-request";

    public static string? Admit(string requestId, ISet<string> seen)
    {
        if (!RequestIdOk(requestId))
        {
            return "invalid";
        }

        return Remember(seen, requestId);
    }

    public static string? Validate(byte type, LocalCommandPayload body)
    {
        if (!LocalFrameTypes.IsCommand(type))
        {
            return "invalid";
        }

        return type switch
        {
            FrameTypes.SendFile when body.Path is null || !File.Exists(body.Path) || Directory.Exists(body.Path) => "not-a-file",
            FrameTypes.SetDownloadDirectory when body.Path is { Length: > 0 } && !Directory.Exists(body.Path) => "invalid-path",
            FrameTypes.SetDisplayName when body.DisplayName is null || !DisplayNames.IsLegal(body.DisplayName) => "invalid-name",
            FrameTypes.ConnectEndpoint => ValidateConnectEndpoint(body),
            FrameTypes.PushClipboardText => ValidatePushClipboard(body),
            _ => null,
        };
    }

    public static string? ValidateConnectEndpoint(LocalCommandPayload body)
    {
        if (body.Host is not { } rawHost || string.IsNullOrWhiteSpace(rawHost) || body.Port is null or < 1 or > 65535)
        {
            return "invalid-endpoint";
        }

        var host = rawHost.Trim();
        if (host.Length > 255)
        {
            return "invalid-endpoint";
        }

        foreach (var ch in host)
        {
            if (char.IsWhiteSpace(ch) || char.IsControl(ch))
            {
                return "invalid-endpoint";
            }
        }

        if (!System.Net.IPAddress.TryParse(host, out _) && Uri.CheckHostName(host) == UriHostNameType.Unknown)
        {
            return "invalid-endpoint";
        }

        return null;
    }

    public static string? ValidatePushClipboard(LocalCommandPayload body)
    {
        if (body.TotalBytes is > FrameLimits.MaxClipboardBytes)
        {
            return "too-large";
        }

        if (body.Text is not null)
        {
            var bytes = System.Text.Encoding.UTF8.GetByteCount(body.Text);
            if (bytes > FrameLimits.MaxClipboardBytes)
            {
                return "too-large";
            }

            if (body.TotalBytes is int expected && expected != bytes)
            {
                return "invalid";
            }

            return null;
        }

        if (body.ChunkBase64 is not null)
        {
            if (body.TotalBytes is not int total || total <= 0 || body.Offset is not int offset || offset < 0 || offset >= total)
            {
                return "invalid";
            }

            var decoded = new byte[(body.ChunkBase64.Length * 3 + 3) / 4];
            if (!Convert.TryFromBase64String(body.ChunkBase64, decoded, out var written) || written == 0 || offset + written > total)
            {
                return "invalid";
            }

            return null;
        }

        return "invalid";
    }
}
