using System.Text;
using ContextAll.Session.Abstractions;

namespace ContextAll.Host;

internal static class ClipboardMime
{
    public const string UriList = "text/uri-list";

    public static ClipboardAttachment? Decode(string mime, byte[] data)
    {
        if (mime == ClipboardAttachment.Image)
        {
            var image = new ClipboardAttachment(mime, "clipboard.png", data);
            return image.IsValid() ? image : null;
        }

        var lines = Encoding.UTF8.GetString(data).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(line => !line.StartsWith('#')).Take(2).ToArray();
        if (mime != UriList || lines.Length != 1 || !Uri.TryCreate(lines[0], UriKind.Absolute, out var uri) ||
            !uri.IsFile || uri.IsUnc || (uri.Host.Length > 0 && uri.Host != "localhost")) return null;
        return ClipboardAttachment.ReadFile(uri.LocalPath);
    }

    public static (string Mime, byte[] Data) Encode(ClipboardAttachment attachment, string directory) =>
        attachment.Mime == ClipboardAttachment.Image ? (attachment.Mime, attachment.Bytes) :
            (UriList, Encoding.UTF8.GetBytes(new Uri(attachment.PublishFile(directory)).AbsoluteUri + "\r\n"));
}
