namespace Birko.Messaging;

/// <summary>
/// Whether a failed send is worth retrying (TASK-675), so a caller decides without parsing
/// <see cref="MessageResult.Error"/>. Every sender can report it; one that cannot tell says <see cref="Unknown"/>.
/// </summary>
public enum MessageFailureKind
{
    /// <summary>The send succeeded.</summary>
    None = 0,

    /// <summary>The same message may succeed later: the transport timed out, refused or dropped the connection, or
    /// answered with a temporary (4xx) refusal.</summary>
    Transient = 1,

    /// <summary>Sending the same message again fails the same way: a rejected or malformed recipient, a permanent (5xx)
    /// refusal, or a message the sender could not build.</summary>
    Permanent = 2,

    /// <summary>The sender could not tell. Retry with a limit, if at all.</summary>
    Unknown = 3,
}
