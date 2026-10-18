using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Birko.Configuration;
using Birko.MessageQueue;
using Birko.MessageQueue.Mqtt;
using Birko.MessageQueue.Serialization;
using FluentAssertions;
using Moq;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Packets;
using Xunit;

namespace Birko.MessageQueue.MQTT.Tests;

/// <summary>
/// TASK-174 on MQTT: SH-L105 (one message instance shared by every handler), SH-L106 (LoadFrom of non-MQTT settings
/// copied nothing), SH-L107 (null! forced through non-nullable settings) and SH-L108 / SH-L109 / SH-L110 (TimeToLive,
/// Priority and PrefetchCount silently ignored; decided by the user: refuse until implemented).
/// </summary>
public class Task174MqttTests
{
    private static Mock<IMqttClient> Client()
    {
        var client = new Mock<IMqttClient>();
        client.Setup(c => c.SubscribeAsync(It.IsAny<MqttClientSubscribeOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MqttClientSubscribeResult)null!);
        return client;
    }

    [Fact]
    public async Task SH_L105_each_handler_gets_its_own_copy_of_the_message()
    {
        var client = Client();
        var consumer = new MqttConsumer(client.Object, new JsonMessageSerializer(), new MqttSettings());
        var seen = new ConcurrentBag<string>();
        // Each handler records what it got, then changes it: whichever runs second sees the first one's change unless
        // each gets its own copy. (Order-independent, so it is red on a shared instance in any dispatch order.)
        Func<QueueMessage, CancellationToken, Task> recordThenChange = (m, _) =>
        {
            seen.Add(m.Body + "|" + m.Headers.Custom.Count);
            m.Body = "changed";
            m.Headers.Custom["k"] = "v";
            return Task.CompletedTask;
        };
        await consumer.SubscribeAsync("a/#", recordThenChange);
        await consumer.SubscribeAsync("a/#", recordThenChange);

        var message = new MqttApplicationMessageBuilder().WithTopic("a/b").WithPayload(Encoding.UTF8.GetBytes("original")).Build();
        client.Raise(c => c.ApplicationMessageReceivedAsync += null,
            new object[] { new MqttApplicationMessageReceivedEventArgs("c", message, new MqttPublishPacket(), (_, _) => Task.CompletedTask) });
        await Task.Delay(50);

        seen.Should().Equal(new[] { "original|0", "original|0" });
    }

    [Fact]
    public void SH_L106_loading_plain_settings_copies_what_they_carry()
    {
        var mqtt = new MqttSettings();
        mqtt.LoadFrom(new Settings("broker.local", "name"));
        mqtt.Location.Should().Be("broker.local");
    }

    [Fact]
    public void SH_L107_the_host_constructor_leaves_no_null_in_non_nullable_settings()
    {
        var settings = new MqttSettings("broker.local");
        settings.Name.Should().NotBeNull();
        settings.UserName.Should().NotBeNull();
        settings.Password.Should().NotBeNull();
    }

    [Theory]
    [InlineData("ttl")]
    [InlineData("priority")]
    public async Task SH_L108_SH_L109_a_message_option_MQTT_cannot_honour_is_refused(string option)
    {
        var producer = new MqttProducer(Client().Object, new JsonMessageSerializer(), new MqttSettings());
        var message = option == "ttl" ? new QueueMessage { Body = "x", TimeToLive = TimeSpan.FromSeconds(5) } : new QueueMessage { Body = "x", Priority = 3 };
        var send = () => producer.SendAsync("a/b", message, CancellationToken.None);
        await send.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task SH_L110_a_prefetch_count_is_refused()
    {
        var consumer = new MqttConsumer(Client().Object, new JsonMessageSerializer(), new MqttSettings());
        var subscribe = () => consumer.SubscribeAsync("a/b", (_, _) => Task.CompletedTask, new ConsumerOptions { PrefetchCount = 10 });
        await subscribe.Should().ThrowAsync<NotSupportedException>();
    }
}
