using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Messaging.Email;

public sealed class SmtpEmailSender : IEmailSender, IDisposable
{
    private readonly EmailSettings _settings;
    private readonly SmtpClient? _client;
    private readonly Func<MailMessage, CancellationToken, Task> _sendMail;
    // CR-M210: System.Net.Mail.SmtpClient does not support concurrent SendMailAsync on one instance
    // (a second in-flight send throws "An asynchronous call is already in progress"). This sender is a
    // natural singleton (it owns a disposable client), so concurrent SendAsync calls from different
    // requests are serialized here rather than corrupting the shared client's state.
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public SmtpEmailSender(EmailSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        _client = new SmtpClient
        {
            Host = settings.Location ?? throw new ArgumentException("SMTP host is required.", nameof(settings)),
            Port = settings.Port > 0 ? settings.Port : 587,
            EnableSsl = settings.UseSecure,
            Timeout = settings.Timeout
        };

        if (!string.IsNullOrEmpty(settings.UserName))
        {
            _client.Credentials = new NetworkCredential(settings.UserName, settings.Password);
        }

        _sendMail = _client.SendMailAsync;
    }

    /// <summary>
    /// Test seam: sends via a supplied delegate instead of a live <see cref="SmtpClient"/>, so the
    /// send-serialization (CR-M210) can be verified without a real SMTP server.
    /// </summary>
    internal SmtpEmailSender(EmailSettings settings, Func<MailMessage, CancellationToken, Task> sendMail)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _sendMail = sendMail ?? throw new ArgumentNullException(nameof(sendMail));
        _client = null;
    }

    /// <remarks>
    /// TASK-675: the whole send, including the wait for another send to finish, is abandoned at
    /// <see cref="EmailSettings.Timeout"/> (milliseconds; 0 or less: no deadline). <c>SmtpClient.Timeout</c> bounds only
    /// the synchronous <c>Send</c>, so the async path ignored it: 2000 ms took 4096 ms (measured by Symbio). A send
    /// abandoned at the deadline is a failed, <see cref="MessageFailureKind.Transient"/> result, not the caller's
    /// cancellation, which still throws <see cref="OperationCanceledException"/>. Every failure says whether it is worth
    /// retrying (<see cref="MessageResult.FailureKind"/>, decided by <see cref="SmtpFailureClassifier"/>).
    /// </remarks>
    public async Task<MessageResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        if (message.Recipients.Count == 0)
        {
            return MessageResult.Failed("No recipients specified.", kind: MessageFailureKind.Permanent);
        }

        var from = message.From ?? _settings.DefaultFrom;
        if (from == null)
        {
            return MessageResult.Failed("No sender address specified and no default configured.", kind: MessageFailureKind.Permanent);
        }

        var messageId = message.Id ?? Guid.NewGuid().ToString();

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        if (_settings.Timeout > 0)
        {
            deadline.CancelAfter(_settings.Timeout);
        }
        MailMessage? mailMessage = null;
        try
        {
            mailMessage = BuildMailMessage(message, from);
            // Serialize concurrent sends — SmtpClient is not safe for concurrent SendMailAsync (CR-M210).
            await _sendLock.WaitAsync(deadline.Token).ConfigureAwait(false);
            var send = StartSend(mailMessage, deadline.Token);
            var sent = mailMessage;
            mailMessage = null;
            // A send that ignores the token still returns at the deadline. The lock and the message are released when
            // it really ends, so the next send never meets a client that is still busy.
            _ = send.ContinueWith(_ =>
            {
                sent.Dispose();
                _sendLock.Release();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            await send.WaitAsync(deadline.Token).ConfigureAwait(false);
            return MessageResult.Succeeded(messageId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex) when (deadline.IsCancellationRequested)
        {
            CancelPendingSend();
            return MessageResult.Failed($"SMTP send abandoned after its {_settings.Timeout} ms timeout.", ex,
                kind: MessageFailureKind.Transient);
        }
        catch (SmtpException ex)
        {
            return MessageResult.Failed($"SMTP error: {ex.Message}", ex, kind: SmtpFailureClassifier.Classify(ex));
        }
        catch (InvalidRecipientException ex)
        {
            // CR-L296: a bad address surfaces with its dedicated reason, not the generic failure message.
            return MessageResult.Failed(ex.Message, ex, kind: MessageFailureKind.Permanent);
        }
        catch (Exception ex)
        {
            return MessageResult.Failed($"Failed to send email: {ex.Message}", ex, kind: SmtpFailureClassifier.Classify(ex));
        }
        finally
        {
            mailMessage?.Dispose();
        }
    }

    // Starts the send; a delegate that throws synchronously becomes a faulted task, so the release below still runs.
    private Task StartSend(MailMessage mail, CancellationToken token)
    {
        try
        {
            return _sendMail(mail, token);
        }
        catch (Exception ex)
        {
            return Task.FromException(ex);
        }
    }

    // SendMailAsync cancels through the token already; this also stops a client whose send does not watch it.
    private void CancelPendingSend()
    {
        try
        {
            _client?.SendAsyncCancel();
        }
        catch (Exception)
        {
            // Best effort: the send was abandoned either way.
        }
    }

    public async Task<IReadOnlyList<MessageResult>> SendBatchAsync(
        IEnumerable<EmailMessage> messages, CancellationToken ct = default)
    {
        if (messages == null)
        {
            throw new ArgumentNullException(nameof(messages));
        }

        var results = new List<MessageResult>();
        foreach (var message in messages)
        {
            ct.ThrowIfCancellationRequested();
            // CR-L295: a null element must not abort the batch (which would discard results already
            // collected) — the batch contract is result-collecting, so capture it as a Failed result.
            if (message == null)
            {
                results.Add(MessageResult.Failed("Null message.", kind: MessageFailureKind.Permanent));
                continue;
            }
            var result = await SendAsync(message, ct).ConfigureAwait(false);
            results.Add(result);
        }
        return results;
    }

    public Task<MessageResult> SendAsync(
        MessageAddress from,
        MessageAddress to,
        string subject,
        string body,
        bool isHtml = false,
        CancellationToken ct = default)
    {
        var message = new EmailMessage
        {
            From = from,
            Recipients = new[] { to },
            Subject = subject,
            Body = body,
            IsHtml = isHtml
        };
        return SendAsync(message, ct);
    }

    public void Dispose()
    {
        _client?.Dispose();
        _sendLock.Dispose();
    }

    private static MailMessage BuildMailMessage(EmailMessage message, MessageAddress from)
    {
        var mail = new MailMessage
        {
            From = ToMailAddress(from),
            Subject = message.Subject,
            Body = message.Body,
            IsBodyHtml = message.IsHtml,
            Priority = MapPriority(message.Priority)
        };

        foreach (var to in message.Recipients)
        {
            mail.To.Add(ToMailAddress(to));
        }

        foreach (var cc in message.Cc)
        {
            mail.CC.Add(ToMailAddress(cc));
        }

        foreach (var bcc in message.Bcc)
        {
            mail.Bcc.Add(ToMailAddress(bcc));
        }

        if (message.ReplyTo != null)
        {
            mail.ReplyToList.Add(ToMailAddress(message.ReplyTo));
        }

        // TASK-585: with any AlternateView present, System.Net.Mail ignores IsBodyHtml and sends Body as text/plain,
        // first. So both bodies go out as views: plain, then HTML (RFC 2046: the last alternative is preferred).
        AlternateView? htmlView = null;
        if (message.IsHtml && !string.IsNullOrEmpty(message.PlainTextBody))
        {
            mail.Body = string.Empty;
            mail.IsBodyHtml = false;
            mail.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
                message.PlainTextBody, Encoding.UTF8, MediaTypeNames.Text.Plain));
            htmlView = AlternateView.CreateAlternateViewFromString(message.Body, Encoding.UTF8, MediaTypeNames.Text.Html);
            mail.AlternateViews.Add(htmlView);
        }

        foreach (var attachment in message.Attachments)
        {
            // An inline image belongs to the HTML view, so the part is multipart/related and cid: resolves there.
            if (htmlView != null && attachment.IsInline && !string.IsNullOrEmpty(attachment.ContentId))
            {
                htmlView.LinkedResources.Add(new LinkedResource(attachment.Content, attachment.ContentType)
                {
                    ContentId = attachment.ContentId,
                });
                continue;
            }

            var mailAttachment = new Attachment(attachment.Content, attachment.FileName, attachment.ContentType);
            if (attachment.IsInline && !string.IsNullOrEmpty(attachment.ContentId))
            {
                mailAttachment.ContentId = attachment.ContentId;
                mailAttachment.ContentDisposition!.Inline = true;
            }
            mail.Attachments.Add(mailAttachment);
        }

        foreach (var header in message.Headers)
        {
            mail.Headers.Add(header.Key, header.Value);
        }

        return mail;
    }

    private static MailAddress ToMailAddress(MessageAddress address)
    {
        // CR-L296: MessageAddress only guards a null Value, so an empty/whitespace or malformed value
        // reaches new MailAddress(...) and throws a generic ArgumentException/FormatException. Surface the
        // dedicated InvalidRecipientException so the caller gets a clear reason (SendAsync maps it to a
        // Failed result) instead of the generic "Failed to send email".
        if (string.IsNullOrWhiteSpace(address.Value))
        {
            throw new InvalidRecipientException(address.Value ?? string.Empty);
        }

        try
        {
            return string.IsNullOrEmpty(address.DisplayName)
                ? new MailAddress(address.Value)
                : new MailAddress(address.Value, address.DisplayName);
        }
        catch (FormatException)
        {
            throw new InvalidRecipientException(address.Value);
        }
    }

    private static MailPriority MapPriority(MessagePriority priority) => priority switch
    {
        MessagePriority.Low => MailPriority.Low,
        MessagePriority.High => MailPriority.High,
        _ => MailPriority.Normal
    };
}
