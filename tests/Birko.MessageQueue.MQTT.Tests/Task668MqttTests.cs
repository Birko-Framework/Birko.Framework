using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.MessageQueue;
using Birko.MessageQueue.Mqtt;
using Birko.MessageQueue.Serialization;
using FluentAssertions;
using Moq;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using Xunit;

namespace Birko.MessageQueue.MQTT.Tests;

/// <summary>
/// TASK-668 on MQTT: a TimeToLive is sent as the MQTT 5 message expiry interval on an MQTT 5 connection (SH-L108's
/// refusal lifted there), and stays refused on MQTT 3.1.1, and for a TTL that is not a whole number of seconds. Priority
/// and PrefetchCount stay refused (Task174MqttTests): MQTT's receive maximum is per connection, not per subscription.
/// </summary>
public class Task668MqttTests
{
    private static (Mock<IMqttClient> client, Func<MqttApplicationMessage?> captured) Client(MqttProtocolVersion version)
    {
        var mock = new Mock<IMqttClient>();
        MqttApplicationMessage? captured = null;
        mock.Setup(c => c.Options).Returns(new MqttClientOptionsBuilder().WithTcpServer("broker").WithProtocolVersion(version).Build());
        mock.Setup(c => c.PublishAsync(It.IsAny<MqttApplicationMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MqttApplicationMessage, CancellationToken>((m, _) => captured = m)
            .ReturnsAsync((MqttClientPublishResult)null!);
        return (mock, () => captured);
    }

    private static MqttProducer Producer(Mock<IMqttClient> client) => new(client.Object, new JsonMessageSerializer(), new MqttSettings());

    [Fact]
    public async Task On_MQTT_5_a_time_to_live_is_the_message_expiry_interval()
    {
        var (client, captured) = Client(MqttProtocolVersion.V500);

        await Producer(client).SendAsync("a/b", new QueueMessage { Body = "x", TimeToLive = TimeSpan.FromMinutes(2) }, CancellationToken.None);

        captured()!.MessageExpiryInterval.Should().Be(120u);
    }

    [Fact]
    public async Task Without_a_time_to_live_no_expiry_is_sent()
    {
        var (client, captured) = Client(MqttProtocolVersion.V500);

        await Producer(client).SendAsync("a/b", new QueueMessage { Body = "x" }, CancellationToken.None);

        captured()!.MessageExpiryInterval.Should().Be(0u, "0 means the message does not expire");
    }

    [Theory]
    [InlineData(500)]   // under a second: the interval would round to 0, which means "never"
    [InlineData(1500)]  // not whole seconds
    public async Task A_time_to_live_MQTT_cannot_express_exactly_is_refused(int milliseconds)
    {
        var (client, captured) = Client(MqttProtocolVersion.V500);

        var send = () => Producer(client).SendAsync("a/b", new QueueMessage { Body = "x", TimeToLive = TimeSpan.FromMilliseconds(milliseconds) }, CancellationToken.None);

        await send.Should().ThrowAsync<NotSupportedException>();
        captured().Should().BeNull("nothing is published");
    }

    [Fact]
    public async Task On_MQTT_3_1_1_a_time_to_live_is_still_refused()
    {
        var (client, captured) = Client(MqttProtocolVersion.V311);

        var send = () => Producer(client).SendAsync("a/b", new QueueMessage { Body = "x", TimeToLive = TimeSpan.FromSeconds(5) }, CancellationToken.None);

        (await send.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain("ProtocolVersion = V500");
        captured().Should().BeNull();
    }

    [Fact]
    public void The_protocol_version_is_copied_with_the_settings_and_defaults_to_3_1_1()
    {
        new MqttSettings().ProtocolVersion.Should().Be(MqttProtocol.V311);
        var copy = new MqttSettings();
        copy.LoadFrom(new MqttSettings { ProtocolVersion = MqttProtocol.V500 });
        copy.ProtocolVersion.Should().Be(MqttProtocol.V500);
    }
}
