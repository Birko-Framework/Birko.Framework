using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Messaging.Email;

public sealed class SmtpEmailSender : IEmailSender, IDisposable
{
    private readonly EmailSettings _settings;
    private readonly SmtpClient _client;

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
    }

    public async Task<MessageResult> SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (message == null)
        {
            throw new ArgumentNullException(nameof(message));
        }

        if (message.Recipients.Count == 0)
        {
            return MessageResult.Failed("No recipients specified.");
        }

        var from = message.From ?? _settings.DefaultFrom;
        if (from == null)
        {
            return MessageResult.Failed("No sender address specified and no default configured.");
        }

        var messageId = message.Id ?? Guid.NewGuid().ToString();

        try
        {
            using var mailMessage = BuildMailMessage(message, from);
            await _client.SendMailAsync(mailMessage, ct).ConfigureAwait(false);
            return MessageResult.Succeeded(messageId);
        }
        catch (SmtpException ex)
        {
            return MessageResult.Failed($"SMTP error: {ex.Message}", ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return MessageResult.Failed($"Failed to send email: {ex.Message}", ex);
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

    public void Dispose() => _client.Dispose();

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

        if (message.IsHtml && !string.IsNullOrEmpty(message.PlainTextBody))
        {
            var plainView = AlternateView.CreateAlternateViewFromString(
                message.PlainTextBody, null, "text/plain");
            mail.AlternateViews.Add(plainView);
        }

        foreach (var attachment in message.Attachments)
        {
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

    private static MailAddress ToMailAddress(MessageAddress address) =>
        string.IsNullOrEmpty(address.DisplayName)
            ? new MailAddress(address.Value)
            : new MailAddress(address.Value, address.DisplayName);

    private static MailPriority MapPriority(MessagePriority priority) => priority switch
    {
        MessagePriority.Low => MailPriority.Low,
        MessagePriority.High => MailPriority.High,
        _ => MailPriority.Normal
    };
}
