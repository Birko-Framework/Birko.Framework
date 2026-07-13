using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Birko.MessageQueue.Mqtt;
using Birko.MessageQueue.Serialization;
using FluentAssertions;
using Moq;
using MQTTnet.Client;
using Xunit;

namespace Birko.MessageQueue.MQTT.Tests;

/// <summary>
/// CR-M204: with CleanSession the broker drops subscriptions on disconnect, so after an auto-reconnect
/// the consumer must replay its subscriptions or it silently receives nothing. The consumer now
/// re-issues SUBSCRIBE for every handler topic on (re)connect (ResubscribeAllAsync). Driven over a
/// mocked IMqttClient (the full ConnectedAsync-event path needs a live broker).
/// </summary>
public class MqttResubscribeTests
{
    [Fact]
    public async Task ResubscribeAllAsync_ReissuesSubscribeForEveryHandlerTopic()
    {
        var subscribedTopics = new ConcurrentBag<string>();
        var client = new Mock<IMqttClient>();
        client.Setup(c => c.SubscribeAsync(It.IsAny<MqttClientSubscribeOptions>(), It.IsAny<CancellationToken>()))
            .Callback<MqttClientSubscribeOptions, CancellationToken>((o, _) =>
            {
                foreach (var f in o.TopicFilters)
                {
                    subscribedTopics.Add(f.Topic);
                }
            })
            .ReturnsAsync((MqttClientSubscribeResult)null!);

        var consumer = new MqttConsumer(client.Object, new JsonMessageSerializer(), new MqttSettings());
        await consumer.SubscribeAsync("a/b", (_, _) => Task.CompletedTask);
        await consumer.SubscribeAsync("c/d", (_, _) => Task.CompletedTask);

        subscribedTopics.Clear(); // ignore the initial subscribes

        // Simulate a reconnect replay.
        await consumer.ResubscribeAllAsync();

        subscribedTopics.Should().BeEquivalentTo(new[] { "a/b", "c/d" },
            "every registered handler topic must be re-subscribed after a reconnect");
    }

    [Fact]
    public async Task ResubscribeAllAsync_NoHandlers_IsNoOp()
    {
        var subscribeCalls = 0;
        var client = new Mock<IMqttClient>();
        client.Setup(c => c.SubscribeAsync(It.IsAny<MqttClientSubscribeOptions>(), It.IsAny<CancellationToken>()))
            .Callback(() => subscribeCalls++)
            .ReturnsAsync((MqttClientSubscribeResult)null!);

        var consumer = new MqttConsumer(client.Object, new JsonMessageSerializer(), new MqttSettings());

        await consumer.ResubscribeAllAsync();

        subscribeCalls.Should().Be(0);
    }
}
