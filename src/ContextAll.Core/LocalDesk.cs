using ContextAll.Protocol;

namespace ContextAll.Core;

public sealed class LocalDesk
{
    readonly HashSet<string> requests = [];

    public string LocalDisplayName { get; set; } = "ContextAll";

    public string DownloadDirectory { get; set; } = "";

    public List<DeviceSnapshotPayload> Devices { get; } = [];

    public PairingSnapshotPayload? Pairing { get; set; }

    public List<FilePromptSnapshotPayload> FilePrompts { get; } = [];

    public List<TransferSnapshotPayload> Transfers { get; } = [];

    public Frame SnapshotFrame()
    {
        var payload = new SnapshotPayload(
            LocalDisplayName,
            DownloadDirectory,
            Devices.ToArray(),
            Pairing,
            FilePrompts.ToArray(),
            Transfers.ToArray());
        return Encode(FrameTypes.Snapshot, 0, payload, ProtocolJsonContext.Default.SnapshotPayload);
    }

    public Frame Handle(Frame frame)
    {
        var command = ControlJson.Decode(frame.Payload.Span, ProtocolJsonContext.Default.LocalCommandPayload);
        if (frame.Type is < 64 or > 75 || command.IsFailure || command.Value is null)
        {
            return Result(command.Value?.RequestId ?? "0", false, "invalid");
        }

        var body = command.Value;
        if (!requests.Add(body.RequestId))
        {
            return Result(body.RequestId, false, "duplicate-request");
        }

        var error = frame.Type switch
        {
            FrameTypes.SendFile when body.Path is null || Directory.Exists(body.Path) => "not-a-file",
            FrameTypes.SetDownloadDirectory when body.Path is { Length: > 0 } && !Directory.Exists(body.Path) => "invalid-path",
            FrameTypes.SetDisplayName when body.DisplayName is null || !DisplayNames.IsLegal(body.DisplayName) => "invalid-name",
            _ => null,
        };
        if (error is null && frame.Type == FrameTypes.SetDisplayName && body.DisplayName is not null)
        {
            LocalDisplayName = body.DisplayName;
        }

        if (error is null && frame.Type == FrameTypes.SetDownloadDirectory)
        {
            DownloadDirectory = body.Path ?? "";
        }

        return Result(body.RequestId, error is null, error);
    }

    static Frame Result(string requestId, bool ok, string? error) =>
        Encode(FrameTypes.CommandResult, 0, new CommandResultPayload(requestId, ok, error), ProtocolJsonContext.Default.CommandResultPayload);

    static Frame Encode<T>(byte type, byte priority, T payload, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> info)
        where T : class
    {
        var json = ControlJson.Encode(payload, info);
        var encoded = FrameCodec.Encode(json.Value ?? [], type, priority, 0, FrameTypes.SessionStream);
        return FrameCodec.Decode(encoded.Value ?? []).Value
            ?? new Frame(0, type, priority, 0, 0, json.Value ?? []);
    }
}
