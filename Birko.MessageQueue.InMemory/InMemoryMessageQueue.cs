using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.MessageQueue.Serialization;

namespace Birko.MessageQueue.InMemory
{
    /// <summary>
    /// In-memory message queue implementation using System.Threading.Channels.
    /// Suitable for testing and development. Messages are lost on process restart.
    /// </summary>
    public class InMemoryMessageQueue : IMessageQueue
    {
        private readonly InMemoryChannel _channel;
        private bool _disposed;

        public IMessageProducer Producer { get; }
        public IMessageConsumer Consumer { get; }
        public bool IsConnected { get; private set; }

        /// <summary>
        /// Creates a new in-memory message queue.
        /// </summary>
        /// <param name="serializer">Message serializer. Defaults to JsonMessageSerializer.</param>
        /// <param name="channelCapacity">Maximum buffered messages per destination. Default is 1000.</param>
        public InMemoryMessageQueue(IMessageSerializer? serializer = null, int channelCapacity = 1000)
        {
            var ser = serializer ?? new JsonMessageSerializer();
            _channel = new InMemoryChannel(channelCapacity);
            Producer = new InMemoryProducer(_channel, ser);
            Consumer = new InMemoryConsumer(_channel, ser);
        }

        /// <summary>
        /// Creates a new in-memory message queue from <see cref="InMemoryMessageQueueOptions"/>.
        /// </summary>
        /// <param name="options">Options supplying the channel capacity.</param>
        /// <param name="serializer">Message serializer. Defaults to JsonMessageSerializer.</param>
        // CR-L283: wire InMemoryMessageQueueOptions in (it was documented as the config surface but never
        // consumed) by delegating to the raw-capacity ctor.
        public InMemoryMessageQueue(InMemoryMessageQueueOptions options, IMessageSerializer? serializer = null)
            : this(serializer, (options ?? throw new ArgumentNullException(nameof(options))).ChannelCapacity)
        {
        }

        public Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            IsConnected = true;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            IsConnected = false;
            Producer.Dispose();
            Consumer.Dispose();
            // Tear down the shared channel: cancel/dispose dispatch loops and complete
            // writers so subscriptions left open at queue disposal don't leak tasks/CTS handles.
            _channel.Dispose();
        }
    }
}
