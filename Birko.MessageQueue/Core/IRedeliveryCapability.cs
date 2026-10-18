namespace Birko.MessageQueue
{
    /// <summary>What a transport does with a delivery whose handler failed (TASK-668).</summary>
    public enum RedeliveryMode
    {
        /// <summary>
        /// The transport neither redelivers a failed message nor counts deliveries, so a retry or dead-letter rule
        /// built on top of it would never fire (MQTT reports every delivery as the first).
        /// </summary>
        None,

        /// <summary>
        /// The transport redelivers a failed message itself and stamps the <c>x-delivery-count</c> header (Redis with a
        /// consumer group reclaims the pending entry). A retry is waiting for the redelivery; its delay is the transport's.
        /// </summary>
        Redelivered,

        /// <summary>
        /// The transport drops a failed delivery but honours <see cref="QueueMessage.Delay"/>, so a retry is a re-send
        /// carrying the count in <c>x-delivery-count</c> (in-memory).
        /// </summary>
        Resend,
    }

    /// <summary>
    /// Implemented by a transport that can tell a retry rule how failed deliveries come back. A transport that does not
    /// implement it is treated as <see cref="RedeliveryMode.None"/>, so a retry policy over it is refused, not ignored.
    /// </summary>
    public interface IRedeliveryCapability
    {
        /// <summary>The mode for a subscription made with <paramref name="options"/> (its consumer group, for one).</summary>
        RedeliveryMode GetRedeliveryMode(ConsumerOptions? options);
    }
}
