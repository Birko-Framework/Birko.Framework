using System;

namespace Birko.EventBus
{
    /// <summary>
    /// Represents an active event subscription. Dispose to unsubscribe.
    /// </summary>
    public interface IEventSubscription : IDisposable
    {
        /// <summary>
        /// The event type this subscription handles.
        /// </summary>
        Type EventType { get; }

        /// <summary>
        /// Whether the subscription is still active.
        /// </summary>
        bool IsActive { get; }
    }
}
