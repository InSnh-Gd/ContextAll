namespace ContextAll.Protocol;

public sealed record Frame(
    int PayloadLength,
    byte Type,
    byte Priority,
    ushort Flags,
    uint StreamId,
    ReadOnlyMemory<byte> Payload);
