using System;
using System.IO;
using System.Linq;
using System.Net.Mail;
using System.Net.Sockets;

namespace Birko.Messaging.Email;

/// <summary>
/// Decides whether an SMTP failure is worth retrying (TASK-675). <see cref="SmtpException"/> covers both a host that
/// refused or dropped the connection and a rejected mailbox, so the answer comes from the status code, the failed
/// recipient and the inner exception, never from the type alone.
/// </summary>
public static class SmtpFailureClassifier
{
    /// <summary>The kind of <paramref name="error"/>, thrown by a send.</summary>
    public static MessageFailureKind Classify(Exception error)
    {
        switch (error)
        {
            case InvalidRecipientException:
                return MessageFailureKind.Permanent;
            case SmtpFailedRecipientsException several:
                // Retrying resends to every recipient, so it is worth it only if one refusal was temporary.
                var kinds = (several.InnerExceptions ?? Array.Empty<SmtpFailedRecipientException>())
                    .Select(e => ByStatus(e.StatusCode)).ToList();
                if (kinds.Count == 0) return ByStatus(several.StatusCode) ?? ByInner(several.InnerException);
                return kinds.Any(k => k == MessageFailureKind.Transient) ? MessageFailureKind.Transient
                    : kinds.All(k => k == MessageFailureKind.Permanent) ? MessageFailureKind.Permanent
                    : MessageFailureKind.Unknown;
            case SmtpException smtp:
                return ByStatus(smtp.StatusCode) ?? ByInner(smtp.InnerException);
            default:
                return ByInner(error);
        }
    }

    // A reply code says it outright: 4xx is temporary, 5xx permanent (RFC 5321 § 4.2.1). GeneralFailure (-1) and the
    // others below 400 are not replies but the client's own failures, which the inner exception explains.
    private static MessageFailureKind? ByStatus(SmtpStatusCode code)
    {
        var value = (int)code;
        if (value >= 400 && value < 500) return MessageFailureKind.Transient;
        if (value >= 500 && value < 600) return MessageFailureKind.Permanent;
        return null;
    }

    private static MessageFailureKind ByInner(Exception? inner)
    {
        for (var e = inner; e != null; e = e.InnerException)
        {
            if (e is SocketException or IOException or TimeoutException)
            {
                return MessageFailureKind.Transient;
            }
        }
        return MessageFailureKind.Unknown;
    }
}
