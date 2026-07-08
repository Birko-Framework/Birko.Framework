using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.MessageQueue.Serialization;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;

namespace Birko.MessageQueue.Mqtt
{
    /// <summary>
    /// MQTT message producer. Publishes messages to MQTT topics.
    /// </summary>
    public class MqttProducer : IMessageProducer
    {
        /// <summary>MQTT5 user-property name carrying <see cref="QueueMessage.PayloadType"/>.</summary>
        internal const string PayloadTypeProperty = "payload_type";

        /// <summary>MQTT5 user-property name carrying the serialized <see cref="MessageHeaders"/>.</summary>
        internal const string HeadersProperty = "headers";

        private readonly IMqttClient _client;
        private readonly IMessageSerializer _serializer;
        private readonly MqttSettings _options;
        private bool _disposed;

        internal MqttProducer(IMqttClient client, IMessageSerializer serializer, MqttSettings options)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task SendAsync(string destination, QueueMessage message, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!MqttTopic.IsValidPublishTopic(destination))
            {
                throw new ArgumentException($"Invalid MQTT publish topic: '{destination}'. Wildcards are not allowed in publish topics.", nameof(destination));
            }

            var builder = new MqttApplicationMessageBuilder()
                .WithTopic(destination)
                .WithPayload(message.Body)
                .WithQualityOfServiceLevel(ToMqttQos(_options.DefaultQualityOfService))
                .WithRetainFlag(false);

            // Carry QueueMessage metadata as MQTT5 user properties so a typed publish/subscribe
            // round-trip preserves PayloadType + Headers (parity with the Redis / InMemory backends).
            // NOTE: user properties are an MQTT5 feature — on an MQTT 3.1.1 connection the broker
            // ignores them and the metadata is (as before) not carried on the wire.
            if (!string.IsNullOrEmpty(message.PayloadType))
            {
                builder.WithUserProperty(PayloadTypeProperty, message.PayloadType);
            }

            if (message.Headers != null)
            {
                builder.WithUserProperty(HeadersProperty, _serializer.Serialize(message.Headers));
            }

            await _client.PublishAsync(builder.Build(), cancellationToken).ConfigureAwait(false);
        }

        public async Task SendAsync<T>(string destination, T payload, MessageHeaders? headers = null, CancellationToken cancellationToken = default) where T : class
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var body = _serializer.Serialize(payload);
            var message = new QueueMessage
            {
                Body = body,
                PayloadType = typeof(T).AssemblyQualifiedName,
                Headers = headers ?? new MessageHeaders { ContentType = _serializer.ContentType }
            };

            await SendAsync(destination, message, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Publishes a message with explicit QoS and retain flag.
        /// </summary>
        public async Task PublishAsync(string topic, string payload, MqttQualityOfService qos = MqttQualityOfService.AtLeastOnce, bool retain = false, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!MqttTopic.IsValidPublishTopic(topic))
            {
                throw new ArgumentException($"Invalid MQTT publish topic: '{topic}'.", nameof(topic));
            }

            var mqttMessage = new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(ToMqttQos(qos))
                .WithRetainFlag(retain)
                .Build();

            await _client.PublishAsync(mqttMessage, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Publishes a typed payload with explicit QoS and retain flag.
        /// </summary>
        public async Task PublishAsync<T>(string topic, T payload, MqttQualityOfService qos = MqttQualityOfService.AtLeastOnce, bool retain = false, CancellationToken cancellationToken = default) where T : class
        {
            var body = _serializer.Serialize(payload);
            await PublishAsync(topic, body, qos, retain, cancellationToken).ConfigureAwait(false);
        }

        internal static MqttQualityOfServiceLevel ToMqttQos(MqttQualityOfService qos)
        {
            return qos switch
            {
                MqttQualityOfService.AtMostOnce => MqttQualityOfServiceLevel.AtMostOnce,
                MqttQualityOfService.AtLeastOnce => MqttQualityOfServiceLevel.AtLeastOnce,
                MqttQualityOfService.ExactlyOnce => MqttQualityOfServiceLevel.ExactlyOnce,
                _ => MqttQualityOfServiceLevel.AtLeastOnce
            };
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }
}
