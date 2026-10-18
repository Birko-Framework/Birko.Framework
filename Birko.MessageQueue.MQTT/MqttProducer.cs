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
            // SH-M113 (decided by the user 2026-10-18: refuse loudly): MQTT has no delayed delivery, and a message with
            // Delay was published at once. Only the in-memory transport honours Delay.
            if (message?.Delay != null)
            {
                throw new NotSupportedException("The MQTT transport cannot delay a message; send it without Delay, or delay it before sending.");
            }
            uint? expirySeconds = null;
            if (message != null)
            {
                expirySeconds = MessageExpiryInterval(message);
                TransportSupport.RefusePriority(message, "MQTT");   // SH-L109
            }

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

            if (expirySeconds is { } seconds)
            {
                builder.WithMessageExpiryInterval(seconds);
            }

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

        /// <summary>
        /// TASK-668: a TimeToLive is the MQTT 5 message expiry interval, which the broker enforces. It is refused on an
        /// MQTT 3.1.1 connection (no expiry exists there; SH-L108), and unless it is a whole number of seconds from 1 up,
        /// since the interval counts whole seconds and 0 would mean "never expires".
        /// </summary>
        private uint? MessageExpiryInterval(QueueMessage message)
        {
            if (message.TimeToLive is not { } ttl)
            {
                return null;
            }
            if (_client.Options?.ProtocolVersion != MQTTnet.Formatter.MqttProtocolVersion.V500)
            {
                TransportSupport.RefuseTimeToLive(message, "MQTT 3.1.1",
                    "An MQTT 5 connection (MqttSettings.ProtocolVersion = V500) sends it as the message expiry interval.");
            }
            if (ttl.Ticks % TimeSpan.TicksPerSecond != 0 || ttl < TimeSpan.FromSeconds(1) || ttl.TotalSeconds > uint.MaxValue)
            {
                throw new NotSupportedException(
                    $"MQTT expires messages in whole seconds; a TimeToLive of {ttl} cannot be sent exactly. Use a whole number of seconds from 1.");
            }
            return (uint)ttl.TotalSeconds;
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
