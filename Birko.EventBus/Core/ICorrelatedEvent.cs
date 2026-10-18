using System;

namespace Birko.EventBus
{
    /// <summary>
    /// An event that carries the correlation id of the operation that raised it. <see cref="EventContext.From"/> reads
    /// it from any event implementing this, not only from <see cref="EventBase"/> (SH-L124: an event implementing
    /// <see cref="IEvent"/> directly lost its correlation, and the enricher then generated a fresh one, breaking the
    /// trace rather than showing it missing).
    /// </summary>
    public interface ICorrelatedEvent : IEvent
    {
        /// <summary>Correlation id of the operation that raised the event, if any.</summary>
        Guid? CorrelationGuid { get; }
    }
}
