using System.Text.Json.Serialization;

namespace ContextAll.Protocol;

public sealed record HelloPayload(string Intent, string DisplayName);

public sealed record DisplayNamePayload(string DisplayName);

public sealed record PathAnnouncePayload(string[] Addresses);

public sealed record FileOfferPayload(
    string TransferId,
    uint StreamId,
    long TotalSize,
    int SegmentSize,
    long SegmentCount,
    string RootSha256,
    string FileName);

public sealed record FileAcceptPayload(string TransferId);

public sealed record FileRejectPayload(string TransferId, string Reason);

public sealed record FileResumePayload(string TransferId, uint StreamId);

public sealed record FileAckPayload(string TransferId, long SegmentIndex);

public sealed record FileBitmapPayload(string TransferId, long BitOffset, string Bits);

public sealed record FileCancelPayload(string TransferId);

public sealed record DonePayload(bool Done);

public sealed record ClockPayload(long Milliseconds, uint Counter, string Origin);

public sealed record VectorPayload(Dictionary<string, long> Versions, ClockPayload? Clock, bool? ClipboardSync = null);

public sealed record DeltaPayload(
    string Origin,
    long Seq,
    long Milliseconds,
    uint Counter,
    string? Text,
    uint? StreamId,
    long? ByteCount,
    string? Sha256);

public sealed record CommandResultPayload(string RequestId, bool Ok, string? Error);

public sealed record LocalCommandPayload(
    string RequestId,
    string? DeviceId,
    string? DisplayName,
    string? Path,
    bool? Enabled,
    string? TransferId);

public sealed record DeviceSnapshotPayload(
    string DeviceId,
    string DisplayName,
    bool Trusted,
    string Presence,
    bool? ClipboardSync,
    bool? AskBeforeSave);

public sealed record PairingSnapshotPayload(
    string DeviceId,
    string DisplayName,
    string Code,
    long RemainingMilliseconds);

public sealed record FilePromptSnapshotPayload(
    string TransferId,
    string DeviceId,
    string FileName,
    long TotalSize,
    long RemainingMilliseconds);

public sealed record TransferSnapshotPayload(
    string TransferId,
    string DeviceId,
    string Direction,
    string FileName,
    long CompletedSegments,
    long SegmentCount,
    bool MoveBlocked);

public sealed record SnapshotPayload(
    string LocalDisplayName,
    string DownloadDirectory,
    DeviceSnapshotPayload[] Devices,
    PairingSnapshotPayload? Pairing,
    FilePromptSnapshotPayload[] FilePrompts,
    TransferSnapshotPayload[] Transfers);

public sealed record DeviceEventPayload(
    string DeviceId,
    string DisplayName,
    bool Trusted,
    string Presence,
    bool? ClipboardSync,
    bool? AskBeforeSave);

public sealed record PairingEndedPayload(string DeviceId, string Outcome);

public sealed record TransferProgressPayload(
    string TransferId,
    string DeviceId,
    string Direction,
    string FileName,
    long CompletedSegments,
    long SegmentCount,
    bool MoveBlocked);

public sealed record TransferEndedPayload(string TransferId, string Outcome);

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(HelloPayload))]
[JsonSerializable(typeof(DisplayNamePayload))]
[JsonSerializable(typeof(PathAnnouncePayload))]
[JsonSerializable(typeof(FileOfferPayload))]
[JsonSerializable(typeof(FileAcceptPayload))]
[JsonSerializable(typeof(FileRejectPayload))]
[JsonSerializable(typeof(FileResumePayload))]
[JsonSerializable(typeof(FileAckPayload))]
[JsonSerializable(typeof(FileBitmapPayload))]
[JsonSerializable(typeof(FileCancelPayload))]
[JsonSerializable(typeof(DonePayload))]
[JsonSerializable(typeof(ClockPayload))]
[JsonSerializable(typeof(VectorPayload))]
[JsonSerializable(typeof(DeltaPayload))]
[JsonSerializable(typeof(CommandResultPayload))]
[JsonSerializable(typeof(LocalCommandPayload))]
[JsonSerializable(typeof(DeviceSnapshotPayload))]
[JsonSerializable(typeof(PairingSnapshotPayload))]
[JsonSerializable(typeof(FilePromptSnapshotPayload))]
[JsonSerializable(typeof(TransferSnapshotPayload))]
[JsonSerializable(typeof(SnapshotPayload))]
[JsonSerializable(typeof(DeviceEventPayload))]
[JsonSerializable(typeof(PairingEndedPayload))]
[JsonSerializable(typeof(TransferProgressPayload))]
[JsonSerializable(typeof(TransferEndedPayload))]
[JsonSerializable(typeof(Dictionary<string, long>))]
[JsonSerializable(typeof(string[]))]
public partial class ProtocolJsonContext : JsonSerializerContext;
