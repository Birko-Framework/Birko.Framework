using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
/// TASK-155 — the MQTT transport: SH-M105 (a disposed subscription came back on reconnect), SH-M106 (a second handler
/// on one filter replaced the first), SH-M107 / SH-M113 (options and Delay the transport cannot honour were silently
/// dropped; decided by the user: refuse them) and SH-M108 (a throwing Disconnected subscriber stopped auto-reconnect).
/// Driven over a mocked IMqttClient; no broker.
/// </summary>
public class Task155MqttTests
{
    private static (Mock<IMqttClient> Client, ConcurrentBag<string> Subscribed, ConcurrentBag<string> Unsubscribed) Client()
    {
        var subscribed = new ConcurrentBag<string>();
        var unsubscribed = new ConcurrentBag<string>();
        var client = new Mock<IMqttClient>();
        client.Setup(c => c.SubscribeAsync(It.IsAny<MqttClientSubscribeOptions>(), It.IsAny<CancellationToken>()))
            .Callback<MqttClientSubscribeOptions, CancellationToken>((o, _) => { foreach (var f in o.TopicFilters) subscribed.Add(f.Topic); })
            .ReturnsAsync((MqttClientSubscribeResult)null!);
        client.Setup(c => c.UnsubscribeAsync(It.IsAny<MqttClientUnsubscribeOptions>(), It.IsAny<CancellationToken>()))
            .Callback<MqttClientUnsubscribeOptions, CancellationToken>((o, _) => { foreach (var f in o.TopicFilters) unsubscribed.Add(f); })
            .ReturnsAsync((MqttClientUnsubscribeResult)null!);
        return (client, subscribed, unsubscribed);
    }

    private static Task Deliver(Mock<IMqttClient> client, string topic, string body)
    {
        var message = new MqttApplicationMessageBuilder().WithTopic(topic).WithPayload(Encoding.UTF8.GetBytes(body)).Build();
        var args = new MqttApplicationMessageReceivedEventArgs("c", message, new MqttPublishPacket(), (_, _) => Task.CompletedTask);
        client.Raise(c => c.ApplicationMessageReceivedAsync += null, new object[] { args });
        return Task.Delay(50);
    }

    [Fact]
    public async Task SH_M105_an_unsubscribed_handler_is_not_resubscribed_on_reconnect()
    {
        var (client, subscribed, _) = Client();
        var consumer = new MqttConsumer(client.Object, new JsonMessageSerializer(), new MqttSettings());
        var a = await consumer.SubscribeAsync("a/b", (_, _) => Task.CompletedTask);
        var c = await consumer.SubscribeAsync("c/d", (_, _) => Task.CompletedTask);
        await a.UnsubscribeAsync();
        c.Dispose();

        subscribed.Clear();
        await consumer.ResubscribeAllAsync();
        subscribed.Should().BeEmpty("a released subscription does not come back on the next reconnect");
    }

    [Fact]
    public async Task SH_M106_two_handlers_on_one_filter_both_receive_and_one_can_leave()
    {
        var (client, _, unsubscribed) = Client();
        var consumer = new MqttConsumer(client.Object, new JsonMessageSerializer(), new MqttSettings());
        var first = new ConcurrentBag<string>();
        var second = new ConcurrentBag<string>();
        var s1 = await consumer.SubscribeAsync("sensors/#", (m, _) => { first.Add(m.Body); return Task.CompletedTask; });
        var s2 = await consumer.SubscribeAsync("sensors/#", (m, _) => { second.Add(m.Body); return Task.CompletedTask; });

        await Deliver(client, "sensors/t1", "one");
        first.Should().Equal("one");
        second.Should().Equal("one");

        await s1.UnsubscribeAsync();
        unsubscribed.Should().BeEmpty("the other handler still needs the broker subscription");
        await Deliver(client, "sensors/t1", "two");
        first.Should().Equal("one");
        second.Should().BeEquivalentTo(new[] { "one", "two" });

        await s2.UnsubscribeAsync();
        unsubscribed.Should().Equal("sensors/#");
    }

    [Theory]
    [InlineData("manual")]
    [InlineData("from-beginning")]
    [InlineData("group")]
    public async Task SH_M107_an_option_MQTT_cannot_honour_is_refused(string option)
    {
        var (client, _, _) = Client();
        var consumer = new MqttConsumer(client.Object, new JsonMessageSerializer(), new MqttSettings());
        var options = option switch
        {
            "manual" => new ConsumerOptions { AckMode = MessageAckMode.ManualAck },
            "from-beginning" => new ConsumerOptions { FromBeginning = true },
            _ => new ConsumerOptions { GroupId = "workers" },
        };
        var subscribe = () => consumer.SubscribeAsync("a/b", (_, _) => Task.CompletedTask, options);
        await subscribe.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task SH_M107_a_requeue_is_refused_not_reported_done()
    {
        var (client, _, _) = Client();
        var consumer = new MqttConsumer(client.Object, new JsonMessageSerializer(), new MqttSettings());
        await consumer.Invoking(c => c.RejectAsync(Guid.NewGuid(), requeue: true)).Should().ThrowAsync<NotSupportedException>();
        await consumer.Invoking(c => c.RejectAsync(Guid.NewGuid(), requeue: false)).Should().NotThrowAsync();
    }

    [Fact]
    public async Task SH_M113_a_delayed_message_is_refused_rather_than_sent_at_once()
    {
        var (client, _, _) = Client();
        var producer = new MqttProducer(client.Object, new JsonMessageSerializer(), new MqttSettings());
        var send = () => producer.SendAsync("a/b", new QueueMessage { Body = "x", Delay = TimeSpan.FromSeconds(5) }, CancellationToken.None);
        await send.Should().ThrowAsync<NotSupportedException>();
    }

    [Fact]
    public async Task SH_M108_a_throwing_Disconnected_subscriber_does_not_stop_auto_reconnect()
    {
        using var queue = new MqttMessageQueue(new MqttSettings { AutoReconnect = true, ReconnectDelay = TimeSpan.FromHours(1) });
        queue.Disconnected += () => throw new InvalidOperationException("subscriber bug");

        var handler = typeof(MqttMessageQueue).GetMethod("OnDisconnectedAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var argsType = handler.GetParameters()[0].ParameterType;
        var ctor = argsType.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
        var args = ctor.Invoke(ctor.GetParameters().Select(p => p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null).ToArray());

        var invoke = () => (Task)handler.Invoke(queue, new[] { args })!;
        await invoke.Should().NotThrowAsync("a subscriber's exception must not escape into the client's event");
        typeof(MqttMessageQueue).GetField("_reconnectCts", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(queue)
            .Should().NotBeNull("the reconnect loop was started");
    }
}
