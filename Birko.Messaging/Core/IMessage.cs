using System;
using System.Collections.Generic;

namespace Birko.Messaging;

public interface IMessage
{
    string? Id { get; }
    IReadOnlyList<MessageAddress> Recipients { get; }
    string Body { get; }
    DateTimeOffset? ScheduledAt { get; }
    IDictionary<string, string> Metadata { get; }
}
