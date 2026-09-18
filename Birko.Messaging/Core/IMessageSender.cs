using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Messaging;

public interface IMessageSender<in TMessage> where TMessage : IMessage
{
    Task<MessageResult> SendAsync(TMessage message, CancellationToken ct = default);

    Task<IReadOnlyList<MessageResult>> SendBatchAsync(
        IEnumerable<TMessage> messages, CancellationToken ct = default);
}
