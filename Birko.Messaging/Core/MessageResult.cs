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

    /// <summary>
    /// TASK-675: whether a failure is worth retrying. <see cref="MessageFailureKind.None"/> on success; a sender that
    /// cannot tell reports <see cref="MessageFailureKind.Unknown"/>.
    /// </summary>
    public MessageFailureKind FailureKind { get; }

    /// <summary>True for a failure the same message may survive later (<see cref="MessageFailureKind.Transient"/>).</summary>
    public bool IsTransientFailure => FailureKind == MessageFailureKind.Transient;

    private MessageResult(bool success, string? messageId, string? error, Exception? exception, MessageFailureKind kind, IDateTimeProvider? clock = null)
    {
        Success = success;
        MessageId = messageId;
        Error = error;
        Exception = exception;
        FailureKind = kind;
        Timestamp = (clock ?? new SystemDateTimeProvider()).OffsetUtcNow;
    }

    public static MessageResult Succeeded(string? messageId = null, IDateTimeProvider? clock = null) =>
        new(true, messageId, null, null, MessageFailureKind.None, clock);

    /// <param name="error">What failed, for people.</param>
    /// <param name="exception">The exception behind it, if any.</param>
    /// <param name="clock">The clock stamping <see cref="Timestamp"/>.</param>
    /// <param name="kind">Whether it is worth retrying; <see cref="MessageFailureKind.None"/> is refused.</param>
    public static MessageResult Failed(string error, Exception? exception = null, IDateTimeProvider? clock = null,
        MessageFailureKind kind = MessageFailureKind.Unknown)
    {
        if (kind == MessageFailureKind.None)
        {
            throw new ArgumentException("A failed result needs a failure kind; None means it succeeded.", nameof(kind));
        }
        return new(false, null, error, exception, kind, clock);
    }
}
