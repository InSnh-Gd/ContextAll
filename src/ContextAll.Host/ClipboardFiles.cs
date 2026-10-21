using System.Text.Json.Serialization;
using ContextAll.Core;

namespace ContextAll.Host;

public sealed class ClipboardFileModel
{
    public string Text { get; set; } = "";

    public long? Milliseconds { get; set; }

    public uint? Counter { get; set; }

    public string? Origin { get; set; }

    public Dictionary<string, long> Vector { get; set; } = [];

    public long? LastMilliseconds { get; set; }

    public uint? LastCounter { get; set; }

    public string? LastOrigin { get; set; }

    public string AppliedText { get; set; } = "";

    public long Sequence { get; set; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ClipboardFileModel))]
[JsonSerializable(typeof(Dictionary<string, long>))]
public partial class ClipboardJsonContext : JsonSerializerContext;

public static class ClipboardFiles
{
    public static string PathFor(string? xdgStateHome, string home) =>
        Path.Combine(LinuxPaths.StateDirectory(xdgStateHome, home), "clipboard.json");

    public static bool Listen(string path, string? plain, bool isPlain)
    {
        ClipboardDocument? stored = null;
        var missing = !File.Exists(path);
        if (!missing)
        {
            try
            {
                stored = Read(path);
            }
            catch (IOException)
            {
                missing = true;
            }
            catch (System.Text.Json.JsonException)
            {
                missing = true;
            }
        }

        var plan = ClipboardRestart.Plan(stored, missing, plain, isPlain);
        if (missing || plan.NewLocalChange || plan.Document.AppliedText != stored?.AppliedText)
        {
            Write(path, plan.Document);
        }

        return plan.Listening;
    }

    public static ClipboardDocument Read(string path)
    {
        var model = System.Text.Json.JsonSerializer.Deserialize(File.ReadAllBytes(path), ClipboardJsonContext.Default.ClipboardFileModel)
            ?? new ClipboardFileModel();
        return new ClipboardDocument(
            model.Text,
            model.Milliseconds is long ms && model.Origin is not null
                ? new HybridClock(ms, model.Counter ?? 0, model.Origin)
                : null,
            model.Vector,
            model.LastMilliseconds is long last && model.LastOrigin is not null
                ? new HybridClock(last, model.LastCounter ?? 0, model.LastOrigin)
                : null,
            model.AppliedText,
            model.Sequence);
    }

    public static void Write(string path, ClipboardDocument document)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
            IdentityStore.SetDirectoryMode(directory);
        }

        var model = new ClipboardFileModel
        {
            Text = document.Text,
            Milliseconds = document.Clock?.Milliseconds,
            Counter = document.Clock?.Counter,
            Origin = document.Clock?.Origin,
            Vector = document.Vector,
            LastMilliseconds = document.Last?.Milliseconds,
            LastCounter = document.Last?.Counter,
            LastOrigin = document.Last?.Origin,
            AppliedText = document.AppliedText,
            Sequence = document.Sequence,
        };
        var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(model, ClipboardJsonContext.Default.ClipboardFileModel);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, json);
        using (var stream = new FileStream(temp, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            stream.Flush(true);
        }

        File.Move(temp, path, overwrite: true);
        IdentityStore.SetFileMode(path);
    }
}
