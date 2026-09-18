namespace Birko.EventBus.Outbox
{
    /// <summary>
    /// Status of an outbox entry.
    /// </summary>
    public enum OutboxStatus
    {
        /// <summary>
        /// Waiting to be published.
        /// </summary>
        Pending,

        /// <summary>
        /// Currently being published (locked by processor).
        /// </summary>
        Publishing,

        /// <summary>
        /// Successfully published to the event bus.
        /// </summary>
        Published,

        /// <summary>
        /// Failed after exhausting all retry attempts.
        /// </summary>
        Failed
    }
}
