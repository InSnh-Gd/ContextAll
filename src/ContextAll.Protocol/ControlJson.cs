using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ContextAll.Protocol;

public static class ControlJson
{
    public static FrameOutcome<byte[]> Encode<T>(T value, JsonTypeInfo<T> type)
        where T : class
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, type);
        if (bytes.Length > FrameLimits.MaxPayloadBytes)
        {
            return FrameOutcome<byte[]>.Failure("payload-length");
        }

        return FrameOutcome<byte[]>.Success(bytes);
    }

    public static FrameOutcome<T> Decode<T>(ReadOnlySpan<byte> json, JsonTypeInfo<T> type)
        where T : class
    {
        try
        {
            var value = JsonSerializer.Deserialize(json, type);
            if (value is null)
            {
                return FrameOutcome<T>.Failure("json");
            }

            return FrameOutcome<T>.Success(value);
        }
        catch (JsonException)
        {
            return FrameOutcome<T>.Failure("json");
        }
    }
}
