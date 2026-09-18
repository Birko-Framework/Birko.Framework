using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.EventBus.Tests.TestResources
{
    public class OrderPlacedHandler : IEventHandler<OrderPlaced>
    {
        public List<OrderPlaced> ReceivedEvents { get; } = [];

        public Task HandleAsync(OrderPlaced @event, EventContext context, CancellationToken cancellationToken = default)
        {
            ReceivedEvents.Add(@event);
            return Task.CompletedTask;
        }
    }

    public class SecondOrderHandler : IEventHandler<OrderPlaced>
    {
        public List<OrderPlaced> ReceivedEvents { get; } = [];

        public Task HandleAsync(OrderPlaced @event, EventContext context, CancellationToken cancellationToken = default)
        {
            ReceivedEvents.Add(@event);
            return Task.CompletedTask;
        }
    }

    public class ThrowingHandler : IEventHandler<OrderPlaced>
    {
        public Task HandleAsync(OrderPlaced @event, EventContext context, CancellationToken cancellationToken = default)
        {
            throw new System.InvalidOperationException("Handler failed");
        }
    }

    public class ContextCapturingHandler : IEventHandler<OrderPlaced>
    {
        public EventContext? CapturedContext { get; private set; }

        public Task HandleAsync(OrderPlaced @event, EventContext context, CancellationToken cancellationToken = default)
        {
            CapturedContext = context;
            return Task.CompletedTask;
        }
    }
}
