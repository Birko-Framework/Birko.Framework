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
    internal class InMemoryChannel : IDisposable
    {
        private readonly ConcurrentDictionary<string, DestinationState> _destinations = new();
        private readonly int _capacity;
        private readonly TimeProvider _clock;
        private volatile bool _disposed; // SH-L116: written by Dispose, read by every other thread

        public InMemoryChannel(int capacity = 1000, TimeProvider? clock = null)
        {
            _capacity = capacity;
            _clock = clock ?? TimeProvider.System;
        }

        /// <summary>TASK-668: a message past its <see cref="QueueMessage.TimeToLive"/> is dropped, not delivered.</summary>
        private bool Expired(QueueMessage message) => TransportSupport.IsExpired(message, _clock.GetUtcNow());

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
        /// Reads the next message from a destination's channel (pull-based consumption).
        /// Returns null if no message is available within the timeout.
        /// </summary>
        /// <remarks>
        /// CR-L286: a destination is either <b>pull-consumed</b> (this method) or <b>push-consumed</b>
        /// (<see cref="AddSubscriber"/>), not both. Once a subscriber is added, the dispatch loop drains the
        /// destination's channel via <c>ReadAllAsync</c>, so messages would be stolen from a concurrent
        /// <see cref="ReadAsync"/> caller (and vice versa). Don't mix the two modes on one destination.
        /// </remarks>
        public async Task<QueueMessage?> ReadAsync(string destination, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            var state = GetOrCreateDestination(destination);

            if (timeout.HasValue)
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeout.Value);
                try
                {
                    while (true)
                    {
                        var read = await state.Channel.Reader.ReadAsync(cts.Token).ConfigureAwait(false);
                        if (!Expired(read)) return read;
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return null;
                }
            }

            while (state.Channel.Reader.TryRead(out var message))
            {
                if (!Expired(message)) return message;
            }

            return null;
        }

        /// <summary>
        /// Adds a subscriber callback for a destination (push-based consumption).
        /// Returns a registration ID that can be used to remove the subscriber.
        /// </summary>
        /// <remarks>
        /// CR-L286: pushing (subscribers) and pulling (<see cref="ReadAsync"/>) are mutually exclusive per
        /// destination — the dispatch loop drains the channel, so a pull caller on the same destination
        /// would see its messages consumed by the loop.
        /// </remarks>
        public Guid AddSubscriber(string destination, Func<QueueMessage, CancellationToken, Task> handler, int maxInFlight = 1)
        {
            var state = GetOrCreateDestination(destination);
            var id = Guid.NewGuid();

            // CR-L285: make the "first subscriber starts the dispatch loop" decision atomic with respect to
            // the subscriber count. ConcurrentDictionary makes membership thread-safe but not this lifecycle
            // transition — two concurrent adds (or an add racing the last remove) could otherwise start two
            // loops or leave none running. Under the lock, start a loop whenever there isn't one and at least
            // one subscriber exists (which is true right after this TryAdd).
            lock (state.SyncRoot)
            {
                state.Subscribers.TryAdd(id, new Subscriber(handler, maxInFlight));
                if (state.DispatchCts == null)
                {
                    StartDispatching(destination, state);
                }
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
                // CR-L285: stop the dispatch loop atomically when the last subscriber leaves, under the same
                // lock as AddSubscriber so start/stop can't interleave.
                lock (state.SyncRoot)
                {
                    state.Subscribers.TryRemove(subscriberId, out _);

                    if (state.Subscribers.IsEmpty && state.DispatchCts != null)
                    {
                        // SH-L130: cancelled here, disposed by the dispatch loop when it has stopped. Disposing it here
                        // pulled the token from under a handler the loop was still awaiting. A message the loop has
                        // already taken when the last subscriber leaves has no one left to receive it.
                        var cts = state.DispatchCts;
                        state.DispatchCts = null;
                        cts.Cancel();
                    }
                }
            }
        }

        private DestinationState GetOrCreateDestination(string destination)
        {
            return _destinations.GetOrAdd(destination, _ => new DestinationState(_capacity));
        }

        private void StartDispatching(string destination, DestinationState state)
        {
            var dispatchCts = new CancellationTokenSource();
            state.DispatchCts = dispatchCts;
            var ct = dispatchCts.Token;

            _ = Task.Run(async () =>
            {
                try
                {
                    await foreach (var message in state.Channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                    {
                        if (Expired(message))
                        {
                            continue; // TASK-668: past its TimeToLive, so delivered to no one
                        }

                        var subscribers = state.Subscribers.Values;
                        foreach (var subscriber in subscribers)
                        {
                            if (subscriber.Gate == null)
                            {
                                await RunHandlerAsync(subscriber, message, ct).ConfigureAwait(false);
                                continue;
                            }

                            // TASK-668: a PrefetchCount above 1 runs up to that many of this subscriber's handlers at
                            // once; the loop waits for a free slot, so no more than that are ever in flight.
                            await subscriber.Gate.WaitAsync(ct).ConfigureAwait(false);
                            _ = RunGatedAsync(subscriber, message, ct);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when dispatching is stopped
                }
                finally
                {
                    dispatchCts.Dispose(); // SH-L130: the loop owns its token source
                }
            }, ct);
        }

        /// <summary>
        /// Tears down every destination: cancels and disposes its dispatch loop's
        /// <see cref="CancellationTokenSource"/> and completes its channel writer, so no
        /// dispatch tasks or CTS handles leak when the owning queue is disposed.
        /// </summary>
        private static async Task RunHandlerAsync(Subscriber subscriber, QueueMessage message, CancellationToken ct)
        {
            try
            {
                await subscriber.Handler(message, ct).ConfigureAwait(false);
            }
            catch
            {
                // Individual handler failure should not stop other subscribers
            }
        }

        private static async Task RunGatedAsync(Subscriber subscriber, QueueMessage message, CancellationToken ct)
        {
            try
            {
                await RunHandlerAsync(subscriber, message, ct).ConfigureAwait(false);
            }
            finally
            {
                subscriber.Gate!.Release();
            }
        }

        internal sealed class Subscriber
        {
            public Subscriber(Func<QueueMessage, CancellationToken, Task> handler, int maxInFlight)
            {
                Handler = handler;
                Gate = maxInFlight > 1 ? new SemaphoreSlim(maxInFlight, maxInFlight) : null;
            }

            public Func<QueueMessage, CancellationToken, Task> Handler { get; }

            /// <summary>Bounds this subscriber's handlers in flight; null for one at a time (awaited by the loop).</summary>
            public SemaphoreSlim? Gate { get; }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            foreach (var state in _destinations.Values)
            {
                // CR-L285: take the per-state lock so teardown can't race a concurrent Add/RemoveSubscriber.
                lock (state.SyncRoot)
                {
                    var cts = state.DispatchCts;
                    state.DispatchCts = null;
                    if (cts != null)
                    {
                        cts.Cancel();
                        cts.Dispose();
                    }

                    state.Channel.Writer.TryComplete();
                    state.Subscribers.Clear();
                }
            }

            _destinations.Clear();
        }

        internal class DestinationState
        {
            public Channel<QueueMessage> Channel { get; }
            public ConcurrentDictionary<Guid, Subscriber> Subscribers { get; } = new();
            public CancellationTokenSource? DispatchCts { get; set; }

            /// <summary>CR-L285: guards the dispatch-loop start/stop transition against the subscriber count.</summary>
            public object SyncRoot { get; } = new();

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
