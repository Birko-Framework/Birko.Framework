using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.MessageQueue.Serialization;
using Birko.Time;
using MQTTnet;
using MQTTnet.Client;

namespace Birko.MessageQueue.Mqtt
{
    /// <summary>
    /// MQTT message consumer. Subscribes to MQTT topics.
    /// </summary>
    public class MqttConsumer : IMessageConsumer
    {
        private readonly IMqttClient _client;
        private readonly IMessageSerializer _serializer;
        private readonly MqttSettings _options;
        private readonly IDateTimeProvider _clock;
        // SH-M106: every handler of a filter, by subscription. Keyed by filter alone, a second subscriber replaced the
        // first, which stopped receiving while its subscription still reported IsActive.
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, Func<QueueMessage, CancellationToken, Task>>> _handlers = new();
        private readonly object _handlersLock = new(); // adding to and emptying a filter's handlers are one step each
        private bool _disposed;
        private bool _eventAttached;
        private readonly object _eventAttachLock = new(); // CR-M203: guards the _eventAttached check-and-set

        /// <summary>
        /// CR-L289: optional callback invoked when a message handler throws, so callers can observe/log
        /// failures instead of losing them silently. Other handlers still run, and exceptions thrown by the
        /// callback itself are swallowed (it must not break dispatch). Set it before subscribing.
        /// </summary>
        public Func<Exception, QueueMessage, Task>? OnHandlerError { get; set; }

        internal MqttConsumer(IMqttClient client, IMessageSerializer serializer, MqttSettings options, IDateTimeProvider? clock = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _clock = clock ?? new SystemDateTimeProvider();

            // CR-M204: with CleanSession (the default) the broker drops all subscriptions on
            // disconnect, so after an auto-reconnect the in-memory handlers would silently receive
            // nothing. Replay every subscription on each (re)connect.
            _client.ConnectedAsync += OnClientConnectedAsync;
        }

        private async Task OnClientConnectedAsync(MqttClientConnectedEventArgs args)
        {
            await ResubscribeAllAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Re-issues the broker SUBSCRIBE for every registered handler topic. Invoked on each
        /// (re)connect so subscriptions survive an auto-reconnect (CR-M204). Best-effort: a failed
        /// resubscribe is swallowed and retried on the next reconnect.
        /// </summary>
        internal async Task ResubscribeAllAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed || _handlers.IsEmpty)
            {
                return;
            }

            var qos = MqttProducer.ToMqttQos(_options.DefaultQualityOfService);
            foreach (var filter in _handlers.Keys)
            {
                var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                    .WithTopicFilter(f => f.WithTopic(filter).WithQualityOfServiceLevel(qos))
                    .Build();
                try
                {
                    await _client.SubscribeAsync(subscribeOptions, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // Best-effort replay; the next reconnect will retry.
                }
            }
        }

        public async Task<ISubscription> SubscribeAsync(string destination, Func<QueueMessage, CancellationToken, Task> handler, ConsumerOptions? options = null, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!MqttTopic.IsValidSubscribeFilter(destination))
            {
                throw new ArgumentException($"Invalid MQTT subscribe filter: '{destination}'.", nameof(destination));
            }

            RefuseUnsupported(options);
            EnsureEventAttached();

            var subscriptionId = Guid.NewGuid();
            lock (_handlersLock)
            {
                _handlers.GetOrAdd(destination, _ => new ConcurrentDictionary<Guid, Func<QueueMessage, CancellationToken, Task>>())[subscriptionId] = handler;
            }

            var qos = MqttProducer.ToMqttQos(_options.DefaultQualityOfService);
            var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(f => f.WithTopic(destination).WithQualityOfServiceLevel(qos))
                .Build();

            await _client.SubscribeAsync(subscribeOptions, cancellationToken).ConfigureAwait(false);

            return new MqttSubscription(this, destination, subscriptionId);
        }

        public Task<ISubscription> SubscribeAsync<T>(string destination, IMessageHandler<T> handler, ConsumerOptions? options = null, CancellationToken cancellationToken = default) where T : class
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            return SubscribeAsync(destination, async (message, ct) =>
            {
                var payload = DeserializePayload<T>(message);
                if (payload != null)
                {
                    var context = new MessageContext(message, destination, this);
                    await handler.HandleAsync(payload, context, ct).ConfigureAwait(false);
                }
            }, options, cancellationToken);
        }

        public Task AcknowledgeAsync(Guid messageId, CancellationToken cancellationToken = default)
        {
            // MQTT handles acknowledgment at the protocol level (QoS 1/2).
            // Application-level ack is a no-op.
            return Task.CompletedTask;
        }

        public Task RejectAsync(Guid messageId, bool requeue = false, CancellationToken cancellationToken = default)
        {
            // SH-M107 (decided by the user 2026-10-18: refuse loudly): MQTT cannot redeliver a message on request, so a
            // requeue is refused rather than reported done. A reject without requeue is what already happened.
            if (requeue)
            {
                throw new NotSupportedException("The MQTT transport cannot requeue a message: the broker has already delivered it.");
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// SH-M107: the options MQTT cannot honour are refused instead of dropped. Acknowledgement is the protocol's (QoS),
        /// so ManualAck has nothing to drive; a subscription receives only what is published after it, so FromBeginning
        /// cannot be met; and a consumer group is not part of this transport. PrefetchCount has no effect and is ignored.
        /// </summary>
        private static void RefuseUnsupported(ConsumerOptions? options)
        {
            if (options == null) return;
            if (options.AckMode == MessageAckMode.ManualAck)
                throw new NotSupportedException("The MQTT transport acknowledges at the protocol level (QoS); ManualAck is not supported.");
            if (options.FromBeginning)
                throw new NotSupportedException("An MQTT subscription receives only messages published after it; FromBeginning is not supported.");
            if (options.GroupId != null)
                throw new NotSupportedException("The MQTT transport has no consumer groups; GroupId is not supported.");
        }

        /// <summary>
        /// Removes one subscription's handler (SH-M105: a released subscription stayed registered and was re-subscribed
        /// on the next reconnect). Returns true when it was the filter's last handler, so the broker subscription can go.
        /// </summary>
        internal bool RemoveHandler(string topicFilter, Guid subscriptionId)
        {
            lock (_handlersLock)
            {
                if (!_handlers.TryGetValue(topicFilter, out var handlers) || !handlers.TryRemove(subscriptionId, out _))
                {
                    return false;
                }
                if (!handlers.IsEmpty)
                {
                    return false;
                }
                _handlers.TryRemove(topicFilter, out _);
                return true;
            }
        }

        /// <summary>Issues the broker UNSUBSCRIBE for a filter no handler needs any more.</summary>
        internal Task UnsubscribeFromBrokerAsync(string topicFilter, CancellationToken cancellationToken = default)
        {
            var unsubscribeOptions = new MqttClientUnsubscribeOptionsBuilder().WithTopicFilter(topicFilter).Build();
            return _client.UnsubscribeAsync(unsubscribeOptions, cancellationToken);
        }

        private void EnsureEventAttached()
        {
            // CR-M203: SubscribeAsync is async and can run concurrently from multiple threads; an
            // unsynchronized check-then-set let two callers both subscribe OnMessageReceivedAsync,
            // double-dispatching every received message (and Dispose only detaches once). Guard the
            // check-and-set so the handler is attached exactly once.
            if (_eventAttached)
            {
                return;
            }

            lock (_eventAttachLock)
            {
                if (_eventAttached)
                {
                    return;
                }

                _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
                _eventAttached = true;
            }
        }

        private async Task OnMessageReceivedAsync(MqttApplicationMessageReceivedEventArgs args)
        {
            var topic = args.ApplicationMessage.Topic;
            var body = args.ApplicationMessage.PayloadSegment.Count > 0
                ? Encoding.UTF8.GetString(args.ApplicationMessage.PayloadSegment)
                : string.Empty;

            var message = new QueueMessage
            {
                Body = body,
                CreatedAt = _clock.OffsetUtcNow
            };

            // Reconstruct metadata the producer attached as MQTT5 user properties
            // (PayloadType + Headers). Absent on MQTT 3.1.1 or non-Birko publishers → defaults kept.
            var userProperties = args.ApplicationMessage.UserProperties;
            if (userProperties != null)
            {
                foreach (var property in userProperties)
                {
                    if (string.IsNullOrEmpty(property.Value))
                    {
                        continue;
                    }

                    if (string.Equals(property.Name, MqttProducer.PayloadTypeProperty, StringComparison.Ordinal))
                    {
                        message.PayloadType = property.Value;
                    }
                    else if (string.Equals(property.Name, MqttProducer.HeadersProperty, StringComparison.Ordinal))
                    {
                        var parsed = _serializer.Deserialize<MessageHeaders>(property.Value);
                        if (parsed != null)
                        {
                            message.Headers = parsed;
                        }
                    }
                }
            }

            // Try exact match first, then wildcard matches
            foreach (var (filter, handlers) in _handlers)
            {
                if (!string.Equals(filter, topic, StringComparison.Ordinal) && !MqttTopic.Matches(filter, topic))
                {
                    continue;
                }

                foreach (var handler in handlers.Values)
                {
                    try
                    {
                        await handler(message, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        // Individual handler failure should not affect other handlers. CR-L289: surface it
                        // through the optional OnHandlerError hook instead of losing it silently.
                        await InvokeHandlerErrorAsync(ex, message).ConfigureAwait(false);
                    }
                }
            }
        }

        private async Task InvokeHandlerErrorAsync(Exception ex, QueueMessage message)
        {
            var hook = OnHandlerError;
            if (hook == null)
            {
                return;
            }

            try
            {
                await hook(ex, message).ConfigureAwait(false);
            }
            catch
            {
                // The error hook must never break dispatch of the remaining handlers.
            }
        }

        private T? DeserializePayload<T>(QueueMessage message) where T : class
        {
            if (string.IsNullOrEmpty(message.Body))
            {
                return null;
            }

            return _serializer.Deserialize<T>(message.Body);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            _client.ConnectedAsync -= OnClientConnectedAsync;

            if (_eventAttached)
            {
                _client.ApplicationMessageReceivedAsync -= OnMessageReceivedAsync;
                _eventAttached = false;
            }

            _handlers.Clear();
        }
    }
}
