namespace Birko.MessageQueue.InMemory
{
    /// <summary>
    /// Configuration options for the in-memory message queue.
    /// </summary>
    public class InMemoryMessageQueueOptions
    {
        /// <summary>
        /// Maximum number of buffered messages per destination.
        /// When the buffer is full, producers will wait. Default is 1000.
        /// </summary>
        public int ChannelCapacity { get; set; } = 1000;
    }
}
