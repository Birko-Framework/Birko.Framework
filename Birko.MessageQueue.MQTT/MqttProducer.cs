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
        private readonly IMqttClient _client;
        private readonly IMessageSerializer _serializer;
        private readonly MqttOptions _options;
        private bool _disposed;

        internal MqttProducer(IMqttClient client, IMessageSerializer serializer, MqttOptions options)
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

            var mqttMessage = new MqttApplicationMessageBuilder()
                .WithTopic(destination)
                .WithPayload(message.Body)
                .WithQualityOfServiceLevel(ToMqttQos(_options.DefaultQualityOfService))
                .WithRetainFlag(false)
                .Build();

            await _client.PublishAsync(mqttMessage, cancellationToken).ConfigureAwait(false);
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
