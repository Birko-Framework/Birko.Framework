using System;
using System.Collections.Generic;

namespace Birko.Messaging.Push;

public sealed class PushMessage : IMessage
{
    public string? Id { get; set; }
    public IReadOnlyList<MessageAddress> Recipients { get; set; } = Array.Empty<MessageAddress>();
    public string Title { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public string? ClickAction { get; set; }
    public IDictionary<string, string> Data { get; set; } = new Dictionary<string, string>();
    public DateTimeOffset? ScheduledAt { get; set; }
    public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();
    public int? Badge { get; set; }
    public string? Sound { get; set; }
}
