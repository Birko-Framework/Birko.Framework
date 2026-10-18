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

        /// <summary>
        /// The clock a message's <see cref="QueueMessage.TimeToLive"/> is measured on (TASK-668). Default
        /// <see cref="System.TimeProvider.System"/>.
        /// </summary>
        public System.TimeProvider? Clock { get; set; }
    }
}
