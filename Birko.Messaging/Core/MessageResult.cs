using System;
using Birko.Time;

namespace Birko.Messaging;

public sealed class MessageResult
{
    public bool Success { get; }
    public string? MessageId { get; }
    public string? Error { get; }
    public Exception? Exception { get; }
    public DateTimeOffset Timestamp { get; }

    private MessageResult(bool success, string? messageId, string? error, Exception? exception, IDateTimeProvider? clock = null)
    {
        Success = success;
        MessageId = messageId;
        Error = error;
        Exception = exception;
        Timestamp = (clock ?? new SystemDateTimeProvider()).OffsetUtcNow;
    }

    public static MessageResult Succeeded(string? messageId = null, IDateTimeProvider? clock = null) =>
        new(true, messageId, null, null, clock);

    public static MessageResult Failed(string error, Exception? exception = null, IDateTimeProvider? clock = null) =>
        new(false, null, error, exception, clock);
}
