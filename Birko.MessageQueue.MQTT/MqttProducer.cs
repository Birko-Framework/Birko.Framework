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
            ArgumentNullException.ThrowIfNull(message);   // TASK-711: was a NullReferenceException
            // SH-M113 (decided by the user 2026-10-18: refuse loudly): MQTT has no delayed delivery, and a message with
            // Delay was published at once. Only the in-memory transport honours Delay.
            if (message.Delay != null)
            {
                throw new NotSupportedException("The MQTT transport cannot delay a message; send it without Delay, or delay it before sending.");
            }
            var mqtt5 = IsMqtt5();
            var expirySeconds = MessageExpiryInterval(message, mqtt5);
            TransportSupport.RefusePriority(message, "MQTT");   // SH-L109
            if (!mqtt5)
            {
                RefuseMetadataMqtt311CannotCarry(message);
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

            // Carry QueueMessage metadata as MQTT5 user properties so a typed publish/subscribe round-trip preserves
            // PayloadType + Headers (parity with the Redis / InMemory backends). TASK-711: MQTT 3.1.1 has no user
            // properties and MQTTnet refuses a publish that has any, so there the message goes without them; what only
            // they could carry was refused above.
            if (mqtt5)
            {
                if (!string.IsNullOrEmpty(message.PayloadType))
                {
                    builder.WithUserProperty(PayloadTypeProperty, message.PayloadType);
                }
                if (message.Headers != null)
                {
                    builder.WithUserProperty(HeadersProperty, _serializer.Serialize(message.Headers));
                }
            }

            await _client.PublishAsync(builder.Build(), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// TASK-711: the protocol of the connection, or, before one is made, the one <see cref="MqttSettings.ProtocolVersion"/>
        /// will connect with (a TimeToLive sent before ConnectAsync was refused as 3.1.1 under V500 settings).
        /// </summary>
        private bool IsMqtt5() => _client.Options is { } connected
            ? connected.ProtocolVersion == MQTTnet.Formatter.MqttProtocolVersion.V500
            : _options.ProtocolVersion == MqttProtocol.V500;

        /// <summary>
        /// TASK-711: on MQTT 3.1.1 the only metadata a message can carry is its body. A correlation id, reply-to, group
        /// id, custom header or a content type other than the serializer's would be lost on the way, so it is refused
        /// (rule 41). The payload type is not refused: a typed subscriber deserializes the body as its own type.
        /// </summary>
        private void RefuseMetadataMqtt311CannotCarry(QueueMessage message)
        {
            var headers = message.Headers;
            if (headers == null)
            {
                return;
            }
            var lost = new System.Collections.Generic.List<string>();
            if (headers.CorrelationId != null) lost.Add(nameof(MessageHeaders.CorrelationId));
            if (headers.ReplyTo != null) lost.Add(nameof(MessageHeaders.ReplyTo));
            if (headers.GroupId != null) lost.Add(nameof(MessageHeaders.GroupId));
            if (headers.Custom is { Count: > 0 }) lost.Add(nameof(MessageHeaders.Custom));
            if (headers.ContentType != _serializer.ContentType && headers.ContentType != new MessageHeaders().ContentType)
            {
                lost.Add(nameof(MessageHeaders.ContentType));
            }
            if (lost.Count > 0)
            {
                throw new NotSupportedException(
                    $"MQTT 3.1.1 cannot carry message headers, so {string.Join(", ", lost)} would be lost; send the message without them. " +
                    "An MQTT 5 connection (MqttSettings.ProtocolVersion = V500) sends them as user properties.");
            }
        }

        /// <summary>
        /// TASK-668: a TimeToLive is the MQTT 5 message expiry interval, which the broker enforces. It is refused on an
        /// MQTT 3.1.1 connection (no expiry exists there; SH-L108), and unless it is a whole number of seconds from 1 up,
        /// since the interval counts whole seconds and 0 would mean "never expires".
        /// </summary>
        private static uint? MessageExpiryInterval(QueueMessage message, bool mqtt5)
        {
            if (message.TimeToLive is not { } ttl)
            {
                return null;
            }
            if (!mqtt5)
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
