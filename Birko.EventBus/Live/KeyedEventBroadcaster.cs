using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;

namespace Birko.EventBus.Live
{
    /// <summary>One event as a live subscriber receives it: its per-key sequence number and the event.</summary>
    /// <remarks>
    /// Sequences of a key start at 1 and rise by one per published event, so a subscriber that sees 7 after 4 missed
    /// 5 and 6. The first event a subscriber reads may have any number: it attached mid-stream.
    /// </remarks>
    public readonly record struct LiveEvent<TEvent>(long Sequence, TEvent Event);

    /// <summary>
    /// Fans the events of each key out to that key's live subscribers (TASK-533): a run's progress to the SSE and
    /// WebSocket clients watching that run. <see cref="Publish"/> is synchronous and never blocks or throws; each
    /// subscriber has its own bounded buffer that drops its OLDEST event when full, so a slow subscriber loses events,
    /// visibly, instead of stalling the publisher or the other subscribers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is not <see cref="IEventBus"/>: the bus routes by event type and awaits each handler, which suits domain
    /// events and would stall a stream. Backported from DraCode's <c>KoboldRunEventSource</c>, with its two gaps closed:
    /// the sequence was stamped under the lock but written outside it, so two concurrent publishers could deliver
    /// sequence 6 before 5; and a drop was visible only as a gap, now also counted on the subscription
    /// (<see cref="LiveEventSubscription{TEvent}.Dropped"/>).
    /// </para>
    /// <para>
    /// A subscriber may attach before the key's first event. A key exists while it has subscribers: an event for a key
    /// nobody watches is discarded (and numbers nothing), and when the last subscriber leaves the key is forgotten, so
    /// a later subscriber starts again from sequence 1. <see cref="Complete"/> ends every current subscriber's stream.
    /// Events are not stored: there is no replay.
    /// </para>
    /// </remarks>
    public sealed class KeyedEventBroadcaster<TKey, TEvent> where TKey : notnull
    {
        /// <summary>The default per-subscriber buffer.</summary>
        public const int DefaultCapacity = 256;

        private readonly object _gate = new();
        private readonly Dictionary<TKey, KeyState> _keys;
        private readonly int _capacity;

        public KeyedEventBroadcaster(int capacity = DefaultCapacity, IEqualityComparer<TKey>? comparer = null)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "A subscriber buffer holds at least one event.");
            _capacity = capacity;
            _keys = new Dictionary<TKey, KeyState>(comparer);
        }

        /// <summary>
        /// Delivers <paramref name="event"/> to every current subscriber of <paramref name="key"/>, stamped with the
        /// key's next sequence. Never blocks, never throws.
        /// </summary>
        /// <returns><c>false</c> when the key has no subscriber, so the event went nowhere.</returns>
        public bool Publish(TKey key, TEvent @event)
        {
            KeyState? state;
            lock (_gate)
            {
                if (!_keys.TryGetValue(key, out state)) return false;
            }

            // Stamp and write under the key's lock: a bounded DropOldest TryWrite never waits, and writing inside the
            // lock is what keeps every subscriber's sequence in publication order.
            lock (state)
            {
                if (state.Subscribers.Count == 0) return false;
                var live = new LiveEvent<TEvent>(++state.Sequence, @event);
                foreach (var subscriber in state.Subscribers)
                    subscriber.Channel.Writer.TryWrite(live);
                return true;
            }
        }

        /// <summary>Attaches a subscriber to <paramref name="key"/>; it receives the events published from now on.</summary>
        public LiveEventSubscription<TEvent> Subscribe(TKey key)
        {
            lock (_gate)
            {
                if (!_keys.TryGetValue(key, out var state))
                {
                    state = new KeyState();
                    _keys[key] = state;
                }
                var subscription = new LiveEventSubscription<TEvent>(_capacity, s => Detach(key, state, s));
                lock (state) state.Subscribers.Add(subscription);
                return subscription;
            }
        }

        /// <summary>The number of current subscribers of <paramref name="key"/>.</summary>
        public int SubscriberCount(TKey key)
        {
            lock (_gate)
            {
                if (!_keys.TryGetValue(key, out var state)) return 0;
                lock (state) return state.Subscribers.Count;
            }
        }

        /// <summary>
        /// Ends the stream of every current subscriber of <paramref name="key"/> (with <paramref name="error"/>, if given,
        /// thrown to readers after the buffered events) and forgets the key. Call it when the run behind the key ends,
        /// including when it fails.
        /// </summary>
        public void Complete(TKey key, Exception? error = null)
        {
            KeyState? state;
            lock (_gate)
            {
                if (!_keys.Remove(key, out state)) return;
            }
            lock (state)
            {
                foreach (var subscriber in state.Subscribers)
                    subscriber.Channel.Writer.TryComplete(error);
                state.Subscribers.Clear();
            }
        }

        private void Detach(TKey key, KeyState state, LiveEventSubscription<TEvent> subscription)
        {
            lock (_gate)
            {
                lock (state)
                {
                    state.Subscribers.Remove(subscription);
                    if (state.Subscribers.Count == 0 && _keys.TryGetValue(key, out var current) && ReferenceEquals(current, state))
                        _keys.Remove(key);
                }
            }
        }

        private sealed class KeyState
        {
            public readonly List<LiveEventSubscription<TEvent>> Subscribers = new();
            public long Sequence;
        }
    }

    /// <summary>
    /// A live subscription of <see cref="KeyedEventBroadcaster{TKey, TEvent}"/>. Read it with <see cref="ReadAllAsync"/>
    /// or <see cref="Reader"/>; dispose it to detach. The stream ends when the key is completed.
    /// </summary>
    public sealed class LiveEventSubscription<TEvent> : IDisposable
    {
        private readonly Action<LiveEventSubscription<TEvent>> _detach;
        private long _dropped;
        private int _disposed;

        internal LiveEventSubscription(int capacity, Action<LiveEventSubscription<TEvent>> detach)
        {
            _detach = detach;
            Channel = System.Threading.Channels.Channel.CreateBounded<LiveEvent<TEvent>>(
                new BoundedChannelOptions(capacity)
                {
                    FullMode = BoundedChannelFullMode.DropOldest,
                    SingleReader = false,
                    SingleWriter = false,
                },
                _ => Interlocked.Increment(ref _dropped));
        }

        internal Channel<LiveEvent<TEvent>> Channel { get; }

        /// <summary>The subscriber's buffer.</summary>
        public ChannelReader<LiveEvent<TEvent>> Reader => Channel.Reader;

        /// <summary>
        /// How many events this subscriber's full buffer discarded, oldest first. Each also shows as a gap in the
        /// sequence numbers it reads.
        /// </summary>
        public long Dropped => Interlocked.Read(ref _dropped);

        /// <summary>Reads until the key is completed or the subscription disposed.</summary>
        public IAsyncEnumerable<LiveEvent<TEvent>> ReadAllAsync(CancellationToken cancellationToken = default)
            => Channel.Reader.ReadAllAsync(cancellationToken);

        /// <summary>Detaches from the key and ends this subscriber's stream. Idempotent.</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            _detach(this);
            Channel.Writer.TryComplete();
        }
    }
}
