using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
using MQTTnet.Server;
using Xunit;

namespace Birko.MessageQueue.MQTT.Tests;

/// <summary>
/// TASK-711. Measured against MQTTnet's broker before the fix: on the default MQTT 3.1.1 connection every
/// <c>SendAsync(QueueMessage)</c> and <c>SendAsync&lt;T&gt;</c> threw "Feature UserProperties requires MQTT version 5.0.0",
/// because the producer always attached the headers as a user property. A TimeToLive sent before ConnectAsync was refused
/// as 3.1.1 under V500 settings, and a null message threw NullReferenceException.
/// </summary>
public class Task711MqttTests
{
    private sealed class Command
    {
        public string Name { get; set; } = string.Empty;
    }

    private static (Mock<IMqttClient> client, Func<MqttApplicationMessage?> captured) Client(MqttProtocolVersion? connected)
    {
        var mock = new Mock<IMqttClient>();
        MqttApplicationMessage? captured = null;
        mock.Setup(c => c.Options).Returns(connected is { } version
            ? new MqttClientOptionsBuilder().WithTcpServer("broker").WithProtocolVersion(version).Build()
            : null!);
        mock.Setup(c => c.PublishAsync(It.IsAny<MqttApplicationMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MqttApplicationMessage, CancellationToken>((m, _) => captured = m)
            .ReturnsAsync((MqttClientPublishResult)null!);
        return (mock, () => captured);
    }

    private static MqttProducer Producer(Mock<IMqttClient> client, MqttProtocol settings = MqttProtocol.V311)
        => new(client.Object, new JsonMessageSerializer(), new MqttSettings { ProtocolVersion = settings });

    [Fact]
    public async Task On_MQTT_3_1_1_a_plain_message_is_published_without_user_properties()
    {
        var (client, captured) = Client(MqttProtocolVersion.V311);

        await Producer(client).SendAsync("a/b", new QueueMessage { Body = "x" }, CancellationToken.None);

        captured().Should().NotBeNull();
        (captured()!.UserProperties ?? new()).Should().BeEmpty("MQTTnet refuses a 3.1.1 publish that has any");
    }

    [Fact]
    public async Task On_MQTT_3_1_1_a_typed_send_is_published_without_user_properties()
    {
        var (client, captured) = Client(MqttProtocolVersion.V311);

        await Producer(client).SendAsync("a/b", new Command { Name = "on" });

        captured().Should().NotBeNull();
        (captured()!.UserProperties ?? new()).Should().BeEmpty();
    }

    [Theory]
    [InlineData("CorrelationId")]
    [InlineData("ReplyTo")]
    [InlineData("GroupId")]
    [InlineData("Custom")]
    [InlineData("ContentType")]
    public async Task On_MQTT_3_1_1_a_header_only_user_properties_could_carry_is_refused(string header)
    {
        var (client, captured) = Client(MqttProtocolVersion.V311);
        var headers = new MessageHeaders();
        switch (header)
        {
            case "CorrelationId": headers.CorrelationId = "c-1"; break;
            case "ReplyTo": headers.ReplyTo = "r/1"; break;
            case "GroupId": headers.GroupId = "g"; break;
            case "Custom": headers.Custom["tenant"] = "acme"; break;
            case "ContentType": headers.ContentType = "text/plain"; break;
        }

        var send = () => Producer(client).SendAsync("a/b", new QueueMessage { Body = "x", Headers = headers }, CancellationToken.None);

        (await send.Should().ThrowAsync<NotSupportedException>()).Which.Message.Should().Contain(header).And.Contain("ProtocolVersion = V500");
        captured().Should().BeNull("nothing is published");
    }

    [Fact]
    public async Task On_MQTT_5_the_metadata_still_travels_as_user_properties()
    {
        var (client, captured) = Client(MqttProtocolVersion.V500);

        await Producer(client).SendAsync("a/b", new Command { Name = "on" }, new MessageHeaders { CorrelationId = "c-1" });

        captured()!.UserProperties.Select(p => p.Name).Should().BeEquivalentTo(new[] { MqttProducer.PayloadTypeProperty, MqttProducer.HeadersProperty });
    }

    [Fact]
    public async Task Before_ConnectAsync_the_settings_decide_the_protocol()
    {
        var (client, captured) = Client(connected: null);

        await Producer(client, MqttProtocol.V500).SendAsync("a/b", new QueueMessage { Body = "x", TimeToLive = TimeSpan.FromSeconds(5) }, CancellationToken.None);

        captured()!.MessageExpiryInterval.Should().Be(5u, "V500 settings connect as MQTT 5, where a TimeToLive is the expiry interval");
    }

    [Fact]
    public async Task Once_connected_the_connection_decides_the_protocol()
    {
        var (client, captured) = Client(MqttProtocolVersion.V311);

        var send = () => Producer(client, MqttProtocol.V500).SendAsync("a/b", new QueueMessage { Body = "x", TimeToLive = TimeSpan.FromSeconds(5) }, CancellationToken.None);

        await send.Should().ThrowAsync<NotSupportedException>();
        captured().Should().BeNull();
    }

    [Fact]
    public async Task A_null_message_is_an_argument_error()
    {
        var (client, _) = Client(MqttProtocolVersion.V311);

        var send = () => Producer(client).SendAsync("a/b", (QueueMessage)null!, CancellationToken.None);

        (await send.Should().ThrowAsync<ArgumentNullException>()).Which.ParamName.Should().Be("message");
    }

    [Fact]
    public async Task A_typed_send_on_the_default_protocol_reaches_a_typed_subscriber_through_a_real_broker()
    {
        var port = FreePort();
        using var server = new MqttFactory().CreateMqttServer(
            new MqttServerOptionsBuilder().WithDefaultEndpoint().WithDefaultEndpointBoundIPAddress(IPAddress.Loopback).WithDefaultEndpointPort(port).Build());
        await server.StartAsync();
        try
        {
            using var queue = new MqttMessageQueue(new MqttSettings("127.0.0.1", port) { AutoReconnect = false });
            await queue.ConnectAsync();
            var received = new Received();
            await queue.Consumer.SubscribeAsync("devices/1/command", received);

            await queue.Producer.SendAsync("devices/1/command", new Command { Name = "on" });

            var command = await received.Got.Task.WaitAsync(TimeSpan.FromSeconds(10));
            command.Name.Should().Be("on");
            await queue.DisconnectAsync();
        }
        finally
        {
            await server.StopAsync();
        }
    }

    private sealed class Received : IMessageHandler<Command>
    {
        public TaskCompletionSource<Command> Got { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task HandleAsync(Command message, MessageContext context, CancellationToken cancellationToken = default)
        {
            Got.TrySetResult(message);
            return Task.CompletedTask;
        }
    }

    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
