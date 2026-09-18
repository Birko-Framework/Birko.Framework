namespace Birko.MessageQueue
{
    /// <summary>
    /// Controls how messages are acknowledged after processing.
    /// </summary>
    public enum MessageAckMode
    {
        /// <summary>
        /// Messages are automatically acknowledged when the handler completes without exception.
        /// </summary>
        AutoAck,

        /// <summary>
        /// Messages must be explicitly acknowledged by calling AcknowledgeAsync.
        /// Gives full control over when a message is considered processed.
        /// </summary>
        ManualAck
    }
}
