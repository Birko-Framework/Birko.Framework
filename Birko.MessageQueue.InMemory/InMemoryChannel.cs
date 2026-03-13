using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Birko.MessageQueue.InMemory
{
    /// <summary>
    /// Manages in-memory channels for destinations (queues/topics).
    /// Each destination has a bounded channel and a list of subscriber callbacks.
    /// </summary>
    internal class InMemoryChannel
    {
        private readonly ConcurrentDictionary<string, DestinationState> _destinations = new();
        private readonly int _capacity;

        public InMemoryChannel(int capacity = 1000)
        {
            _capacity = capacity;
        }

        /// <summary>
        /// Writes a message to a destination, delivering to all active subscribers.
        /// If no subscribers, the message is buffered in the channel for later consumption.
        /// </summary>
        public async Task WriteAsync(string destination, QueueMessage message, CancellationToken cancellationToken = default)
        {
            var state = GetOrCreateDestination(destination);
            await state.Channel.Writer.WriteAsync(message, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads the next message from a destination's channel.
        /// Returns null if no message is available within the timeout.
        /// </summary>
        public async Task<QueueMessage?> ReadAsync(string destination, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            var state = GetOrCreateDestination(destination);

            if (timeout.HasValue)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout.Value);
                try
                {
                    return await state.Channel.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return null;
                }
            }

            if (state.Channel.Reader.TryRead(out var message))
            {
                return message;
            }

            return null;
        }

        /// <summary>
        /// Adds a subscriber callback for a destination.
        /// Returns a registration ID that can be used to remove the subscriber.
        /// </summary>
        public Guid AddSubscriber(string destination, Func<QueueMessage, CancellationToken, Task> handler)
        {
            var state = GetOrCreateDestination(destination);
            var id = Guid.NewGuid();
            state.Subscribers.TryAdd(id, handler);

            // Start dispatching if this is the first subscriber
            if (state.Subscribers.Count == 1 && state.DispatchCts == null)
            {
                StartDispatching(destination, state);
            }

            return id;
        }

        /// <summary>
        /// Removes a subscriber by registration ID.
        /// </summary>
        public void RemoveSubscriber(string destination, Guid subscriberId)
        {
            if (_destinations.TryGetValue(destination, out var state))
            {
                state.Subscribers.TryRemove(subscriberId, out _);

                // Stop dispatching if no more subscribers
                if (state.Subscribers.IsEmpty)
                {
                    state.DispatchCts?.Cancel();
                    state.DispatchCts = null;
                }
            }
        }

        private DestinationState GetOrCreateDestination(string destination)
        {
            return _destinations.GetOrAdd(destination, _ => new DestinationState(_capacity));
        }

        private void StartDispatching(string destination, DestinationState state)
        {
            state.DispatchCts = new CancellationTokenSource();
            var ct = state.DispatchCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var message in state.Channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                    {
                        var subscribers = state.Subscribers.Values;
                        foreach (var handler in subscribers)
                        {
                            try
                            {
                                await handler(message, ct).ConfigureAwait(false);
                            }
                            catch
                            {
                                // Individual handler failure should not stop other subscribers
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when dispatching is stopped
                }
            }, ct);
        }

        internal class DestinationState
        {
            public Channel<QueueMessage> Channel { get; }
            public ConcurrentDictionary<Guid, Func<QueueMessage, CancellationToken, Task>> Subscribers { get; } = new();
            public CancellationTokenSource? DispatchCts { get; set; }

            public DestinationState(int capacity)
            {
                Channel = System.Threading.Channels.Channel.CreateBounded<QueueMessage>(
                    new BoundedChannelOptions(capacity)
                    {
                        FullMode = BoundedChannelFullMode.Wait,
                        SingleReader = false,
                        SingleWriter = false
                    });
            }
        }
    }
}
