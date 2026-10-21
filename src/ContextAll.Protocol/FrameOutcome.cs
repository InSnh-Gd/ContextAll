namespace ContextAll.Protocol;

public sealed class FrameOutcome<T>
    where T : class
{
    FrameOutcome(T? value, string? reason)
    {
        Value = value;
        Reason = reason;
    }

    public bool IsFailure => Reason is not null;

    public T? Value { get; }

    public string? Reason { get; }

    public static FrameOutcome<T> Success(T value) => new(value, null);

    public static FrameOutcome<T> Failure(string reason) => new(null, reason);
}
