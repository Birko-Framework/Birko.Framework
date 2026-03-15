using System;

namespace Birko.Messaging;

public sealed class MessageResult
{
    public bool Success { get; }
    public string? MessageId { get; }
    public string? Error { get; }
    public Exception? Exception { get; }
    public DateTimeOffset Timestamp { get; }

    private MessageResult(bool success, string? messageId, string? error, Exception? exception)
    {
        Success = success;
        MessageId = messageId;
        Error = error;
        Exception = exception;
        Timestamp = DateTimeOffset.UtcNow;
    }

    public static MessageResult Succeeded(string? messageId = null) =>
        new(true, messageId, null, null);

    public static MessageResult Failed(string error, Exception? exception = null) =>
        new(false, null, error, exception);
}
