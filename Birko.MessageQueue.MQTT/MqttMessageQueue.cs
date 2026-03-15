using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.MessageQueue.Serialization;
using MQTTnet;
using MQTTnet.Client;

namespace Birko.MessageQueue.Mqtt
{
    /// <summary>
    /// MQTT message queue implementation using MQTTnet.
    /// Supports QoS 0/1/2, persistent sessions, topic wildcards,
    /// retained messages, Last Will, TLS, and automatic reconnection.
    /// </summary>
    public class MqttMessageQueue : IMessageQueue
    {
        private readonly MqttSettings _options;
        private readonly IMqttClient _client;
        private readonly MqttProducer _producer;
        private readonly MqttConsumer _consumer;
        private CancellationTokenSource? _reconnectCts;
        private bool _disposed;

        public IMessageProducer Producer => _producer;
        public IMessageConsumer Consumer => _consumer;
        public bool IsConnected => _client.IsConnected;

        /// <summary>
        /// Fired when the client connects or reconnects to the broker.
        /// </summary>
        public event Func<Task>? Connected;

        /// <summary>
        /// Fired when the client disconnects from the broker.
        /// </summary>
        public event Func<Task>? Disconnected;

        /// <summary>
        /// Creates a new MQTT message queue.
        /// </summary>
        /// <param name="options">MQTT connection options.</param>
        /// <param name="serializer">Message serializer. Defaults to JsonMessageSerializer.</param>
        public MqttMessageQueue(MqttSettings options, IMessageSerializer? serializer = null)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            var ser = serializer ?? new JsonMessageSerializer();

            var factory = new MqttFactory();
            _client = factory.CreateMqttClient();

            _producer = new MqttProducer(_client, ser, _options);
            _consumer = new MqttConsumer(_client, ser, _options);

            _client.DisconnectedAsync += OnDisconnectedAsync;
            _client.ConnectedAsync += OnConnectedAsync;
        }

        public async Task ConnectAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            var optionsBuilder = new MqttClientOptionsBuilder()
                .WithTcpServer(_options.Location, _options.Port)
                .WithClientId(_options.ClientId ?? $"birko-{Guid.NewGuid():N}")
                .WithCleanSession(_options.CleanSession)
                .WithKeepAlivePeriod(_options.KeepAlive)
                .WithTimeout(_options.ConnectionTimeout);

            if (!string.IsNullOrEmpty(_options.UserName))
            {
                optionsBuilder.WithCredentials(_options.UserName, _options.Password);
            }

            if (_options.UseSecure)
            {
                optionsBuilder.WithTlsOptions(tls =>
                {
                    tls.UseTls(true);

                    if (_options.AllowUntrustedCertificates)
                    {
                        tls.WithAllowUntrustedCertificates(true);
                    }

                    if (_options.ClientCertificate != null)
                    {
                        tls.WithClientCertificates(new[] { _options.ClientCertificate });
                    }
                });
            }

            if (_options.LastWill != null)
            {
                optionsBuilder
                    .WithWillTopic(_options.LastWill.Topic)
                    .WithWillPayload(_options.LastWill.Payload)
                    .WithWillQualityOfServiceLevel(MqttProducer.ToMqttQos(_options.LastWill.QualityOfService))
                    .WithWillRetain(_options.LastWill.Retain);
            }

            await _client.ConnectAsync(optionsBuilder.Build(), cancellationToken).ConfigureAwait(false);
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            _reconnectCts?.Cancel();
            _reconnectCts = null;

            if (_client.IsConnected)
            {
                var disconnectOptions = new MqttClientDisconnectOptionsBuilder()
                    .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                    .Build();

                await _client.DisconnectAsync(disconnectOptions, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task OnConnectedAsync(MqttClientConnectedEventArgs args)
        {
            if (Connected != null)
            {
                await Connected.Invoke().ConfigureAwait(false);
            }
        }

        private async Task OnDisconnectedAsync(MqttClientDisconnectedEventArgs args)
        {
            if (Disconnected != null)
            {
                await Disconnected.Invoke().ConfigureAwait(false);
            }

            if (_disposed || !_options.AutoReconnect)
            {
                return;
            }

            _reconnectCts?.Cancel();
            _reconnectCts = new CancellationTokenSource();
            var ct = _reconnectCts.Token;

            _ = Task.Run(async () =>
            {
                var attempts = 0;
                while (!ct.IsCancellationRequested && !_client.IsConnected)
                {
                    if (_options.MaxReconnectAttempts > 0 && attempts >= _options.MaxReconnectAttempts)
                    {
                        break;
                    }

                    try
                    {
                        await Task.Delay(_options.ReconnectDelay, ct).ConfigureAwait(false);
                        await ConnectAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        attempts++;
                    }
                }
            }, ct);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _reconnectCts?.Cancel();
            _reconnectCts?.Dispose();

            _client.DisconnectedAsync -= OnDisconnectedAsync;
            _client.ConnectedAsync -= OnConnectedAsync;

            _producer.Dispose();
            _consumer.Dispose();
            _client.Dispose();
        }
    }
}
