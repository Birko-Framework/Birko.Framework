using System;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.EventBus.Enrichment
{
    /// <summary>
    /// Ensures a CorrelationId is present on the event context.
    /// If the event has a CorrelationId, uses it. Otherwise generates a new one.
    /// </summary>
    public class CorrelationEventEnricher : IEventEnricher
    {
        public Task EnrichAsync(IEvent @event, EventContext context, CancellationToken cancellationToken = default)
        {
            if (context.CorrelationGuid == null)
            {
                context.CorrelationGuid = @event is ICorrelatedEvent eb && eb.CorrelationGuid.HasValue // TASK-174 (SH-L124): any correlated event
                    ? eb.CorrelationGuid
                    : Guid.NewGuid();
            }

            return Task.CompletedTask;
        }
    }
}
