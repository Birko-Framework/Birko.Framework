using System;
using System.Threading;
using System.Threading.Tasks;
using MQTTnet;
using MQTTnet.Client;

namespace Birko.MessageQueue.Mqtt
{
    /// <summary>
    /// Represents an active MQTT topic subscription.
    /// </summary>
    public class MqttSubscription : ISubscription
    {
        private readonly IMqttClient _client;

        public string Destination { get; }
        public bool IsActive { get; private set; } = true;

        internal MqttSubscription(IMqttClient client, string destination)
        {
            _client = client;
            Destination = destination;
        }

        public async Task UnsubscribeAsync(CancellationToken cancellationToken = default)
        {
            if (!IsActive)
            {
                return;
            }

            var unsubscribeOptions = new MqttClientUnsubscribeOptionsBuilder()
                .WithTopicFilter(Destination)
                .Build();

            await _client.UnsubscribeAsync(unsubscribeOptions, cancellationToken).ConfigureAwait(false);
            IsActive = false;
        }

        public void Dispose()
        {
            if (IsActive)
            {
                IsActive = false;
                // Fire-and-forget unsubscribe on dispose
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var unsubscribeOptions = new MqttClientUnsubscribeOptionsBuilder()
                            .WithTopicFilter(Destination)
                            .Build();
                        await _client.UnsubscribeAsync(unsubscribeOptions).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Best-effort unsubscribe on dispose
                    }
                });
            }
        }
    }
}
