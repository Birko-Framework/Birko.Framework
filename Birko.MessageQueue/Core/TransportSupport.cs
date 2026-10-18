using System;

namespace Birko.MessageQueue
{
    /// <summary>
    /// What a transport refuses rather than silently ignores (TASK-155 / TASK-174, decided by the user: refuse loudly,
    /// and lift each refusal when the feature is implemented). Every refusal is a <see cref="NotSupportedException"/>
    /// naming the option and the transport, raised at the call that asked for it.
    /// </summary>
    public static class TransportSupport
    {
        /// <summary>Refuses a non-default <see cref="QueueMessage.Priority"/>: no transport orders by it yet.</summary>
        public static void RefusePriority(QueueMessage message, string transport)
        {
            if (message.Priority != 0)
            {
                throw new NotSupportedException($"The {transport} transport does not order by priority; send the message with Priority 0.");
            }
        }

        /// <summary>Refuses a <see cref="QueueMessage.TimeToLive"/> on a transport that does not expire messages.</summary>
        public static void RefuseTimeToLive(QueueMessage message, string transport)
        {
            if (message.TimeToLive != null)
            {
                throw new NotSupportedException($"The {transport} transport does not expire messages; send the message without TimeToLive.");
            }
        }

        /// <summary>Refuses a non-default <see cref="ConsumerOptions.PrefetchCount"/>: no transport bounds unacknowledged messages yet.</summary>
        public static void RefusePrefetch(ConsumerOptions? options, string transport)
        {
            if (options != null && options.PrefetchCount != new ConsumerOptions().PrefetchCount)
            {
                throw new NotSupportedException($"The {transport} transport does not bound unacknowledged messages; leave PrefetchCount at its default.");
            }
        }
    }
}
