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

        internal MqttConsumer(IMqttClient client, IMessageSerializer serializer, MqttSettings options, IDateTimeProvider? clock = null)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _clock = clock ?? new SystemDateTimeProvider();
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
            if (_eventAttached)
            {
                return;
            }

            _client.ApplicationMessageReceivedAsync += OnMessageReceivedAsync;
            _eventAttached = true;
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

            if (_eventAttached)
            {
                _client.ApplicationMessageReceivedAsync -= OnMessageReceivedAsync;
                _eventAttached = false;
            }

            _handlers.Clear();
        }
    }
}
