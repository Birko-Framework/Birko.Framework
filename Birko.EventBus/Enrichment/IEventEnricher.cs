using System.Threading;
using System.Threading.Tasks;

namespace Birko.EventBus.Enrichment
{
    /// <summary>
    /// Enriches an event context before publishing (e.g., adds TenantId, CorrelationId, custom headers).
    /// Enrichers run in registration order before the event is dispatched to handlers or transport.
    /// </summary>
    public interface IEventEnricher
    {
        /// <summary>
        /// Enriches the event context with additional metadata.
        /// </summary>
        /// <param name="event">The event being published.</param>
        /// <param name="context">The context to enrich.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task EnrichAsync(IEvent @event, EventContext context, CancellationToken cancellationToken = default);
    }
}
