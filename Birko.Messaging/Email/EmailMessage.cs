using System;
using System.Collections.Generic;

namespace Birko.Messaging.Email;

public enum MessagePriority
{
    Low,
    Normal,
    High
}

public sealed class EmailMessage : IMessage
{
    public string? Id { get; set; }
    public MessageAddress? From { get; set; }
    public IReadOnlyList<MessageAddress> Recipients { get; set; } = Array.Empty<MessageAddress>();
    public IReadOnlyList<MessageAddress> Cc { get; set; } = Array.Empty<MessageAddress>();
    public IReadOnlyList<MessageAddress> Bcc { get; set; } = Array.Empty<MessageAddress>();
    public MessageAddress? ReplyTo { get; set; }
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool IsHtml { get; set; }
    public string? PlainTextBody { get; set; }
    public IReadOnlyList<MessageAttachment> Attachments { get; set; } = Array.Empty<MessageAttachment>();
    public DateTimeOffset? ScheduledAt { get; set; }
    public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    public MessagePriority Priority { get; set; } = MessagePriority.Normal;
    public IDictionary<string, string> Headers { get; set; } = new Dictionary<string, string>();
}
