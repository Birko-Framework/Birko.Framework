using System;

namespace Birko.MessageQueue
{
    /// <summary>
    /// What a transport refuses rather than silently ignores (TASK-155 / TASK-174, decided by the user: refuse loudly,
    /// and lift each refusal when the feature is implemented). Every refusal is a <see cref="NotSupportedException"/>
    /// naming the option and the transport, raised at the call that asked for it.
    /// </summary>
    /// <remarks>
    /// Which transport honours what since TASK-668:
    /// <list type="bullet">
    /// <item><see cref="QueueMessage.TimeToLive"/>: Redis and in-memory always; MQTT on an MQTT 5 connection, in whole
    /// seconds; refused on MQTT 3.1.1.</item>
    /// <item><see cref="ConsumerOptions.PrefetchCount"/>: Redis (the read batch) and in-memory (handlers in flight);
    /// refused on MQTT, whose receive maximum is per connection, not per subscription.</item>
    /// <item><see cref="QueueMessage.Priority"/>: refused everywhere (decided by the user: kept for a transport that can
    /// order by it, such as RabbitMQ).</item>
    /// </list>
    /// </remarks>
    public static class TransportSupport
    {
        /// <summary>Refuses a non-default <see cref="QueueMessage.Priority"/>: no current transport orders by it.</summary>
        public static void RefusePriority(QueueMessage message, string transport)
        {
            if (message.Priority != 0)
            {
                throw new NotSupportedException($"The {transport} transport does not order by priority; send the message with Priority 0.");
            }
        }

        /// <summary>Refuses a <see cref="QueueMessage.TimeToLive"/> on a transport that does not expire messages.</summary>
        public static void RefuseTimeToLive(QueueMessage message, string transport, string? hint = null)
        {
            if (message.TimeToLive != null)
            {
                throw new NotSupportedException($"The {transport} transport does not expire messages; send the message without TimeToLive.{(hint == null ? "" : " " + hint)}");
            }
        }

        /// <summary>Refuses a non-default <see cref="ConsumerOptions.PrefetchCount"/> on a transport that cannot bound it.</summary>
        public static void RefusePrefetch(ConsumerOptions? options, string transport, string? reason = null)
        {
            if (options != null && options.PrefetchCount != new ConsumerOptions().PrefetchCount)
            {
                throw new NotSupportedException($"The {transport} transport does not bound unacknowledged messages per subscription; leave PrefetchCount at its default.{(reason == null ? "" : " " + reason)}");
            }
        }

        /// <summary>
        /// The prefetch a subscription asked for, validated: at least 1 (TASK-668). The default 1 is what a transport that
        /// handles one message at a time already does.
        /// </summary>
        public static int PrefetchOf(ConsumerOptions? options)
        {
            var prefetch = options?.PrefetchCount ?? new ConsumerOptions().PrefetchCount;
            if (prefetch < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(options), prefetch, "PrefetchCount must be at least 1.");
            }
            return prefetch;
        }

        /// <summary>
        /// Whether <paramref name="message"/> has outlived its <see cref="QueueMessage.TimeToLive"/> at <paramref name="now"/>,
        /// counted from <see cref="QueueMessage.CreatedAt"/> (as the Redis transport counts it).
        /// </summary>
        public static bool IsExpired(QueueMessage message, DateTimeOffset now)
            => message.TimeToLive is { } ttl && now - message.CreatedAt > ttl;
    }
}
