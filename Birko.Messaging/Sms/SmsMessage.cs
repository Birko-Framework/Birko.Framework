using System;
using System.Collections.Generic;

namespace Birko.Messaging.Sms;

public sealed class SmsMessage : IMessage
{
    public string? Id { get; set; }
    public MessageAddress? From { get; set; }
    public IReadOnlyList<MessageAddress> Recipients { get; set; } = Array.Empty<MessageAddress>();
    public string Body { get; set; } = string.Empty;
    public DateTimeOffset? ScheduledAt { get; set; }
    public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
}
