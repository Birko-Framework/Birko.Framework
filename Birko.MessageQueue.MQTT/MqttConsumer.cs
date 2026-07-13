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
        private readonly ConcurrentDictionary<string, Func<QueueMessage, CancellationToken, Task>> _handlers = new();
        private bool _disposed;
        private bool _eventAttached;
        private readonly object _eventAttachLock = new(); // CR-M203: guards the _eventAttached check-and-set

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

            EnsureEventAttached();

            _handlers[destination] = handler;

            var qos = MqttProducer.ToMqttQos(_options.DefaultQualityOfService);
            var subscribeOptions = new MqttClientSubscribeOptionsBuilder()
                .WithTopicFilter(f => f.WithTopic(destination).WithQualityOfServiceLevel(qos))
                .Build();

            await _client.SubscribeAsync(subscribeOptions, cancellationToken).ConfigureAwait(false);

            return new MqttSubscription(_client, destination);
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
            // MQTT has no native reject/requeue mechanism.
            // Application-level reject is a no-op.
            return Task.CompletedTask;
        }

        internal void RemoveHandler(string topicFilter)
        {
            _handlers.TryRemove(topicFilter, out _);
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
            foreach (var (filter, handler) in _handlers)
            {
                if (string.Equals(filter, topic, StringComparison.Ordinal) || MqttTopic.Matches(filter, topic))
                {
                    try
                    {
                        await handler(message, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Individual handler failure should not affect other handlers
                    }
                }
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
