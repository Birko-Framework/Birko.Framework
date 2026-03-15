using System;
using System.Collections.Generic;

namespace Birko.EventBus
{
    /// <summary>
    /// Context passed to event handlers alongside the event.
    /// Contains metadata about the delivery (correlation, tenant, retry count, headers).
    /// </summary>
    public class EventContext
    {
        /// <summary>
        /// The event's unique identifier.
        /// </summary>
        public Guid EventId { get; init; }

        /// <summary>
        /// Source module or component that raised the event.
        /// </summary>
        public string Source { get; init; } = null!;

        /// <summary>
        /// Correlation ID for tracing related operations.
        /// </summary>
        public Guid? CorrelationId { get; set; }

        /// <summary>
        /// Tenant identifier, if multi-tenancy is enabled.
        /// </summary>
        public Guid? TenantGuid { get; set; }

        /// <summary>
        /// Number of times this event has been delivered (1 = first attempt).
        /// </summary>
        public int DeliveryCount { get; set; } = 1;

        /// <summary>
        /// Additional metadata headers.
        /// </summary>
        public IDictionary<string, string> Metadata { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Creates an EventContext from an IEvent.
        /// </summary>
        public static EventContext From(IEvent @event, Guid? tenantGuid = null, int deliveryCount = 1, IDictionary<string, string>? metadata = null)
        {
            return new EventContext
            {
                EventId = @event.EventId,
                Source = @event.Source,
                CorrelationId = @event is EventBase eb ? eb.CorrelationId : null,
                TenantGuid = tenantGuid,
                DeliveryCount = deliveryCount,
                Metadata = metadata ?? new Dictionary<string, string>()
            };
        }
    }
}
