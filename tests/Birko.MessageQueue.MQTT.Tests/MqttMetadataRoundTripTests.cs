using System;
using System.Reflection;
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
/// Regressions for CR-H120: a typed MQTT publish/subscribe round-trip must preserve
/// QueueMessage.PayloadType and Headers via MQTT5 user properties (previously dropped).
/// The MQTT sources compile into this test assembly via shared projitems, so the internal
/// producer/consumer ctors are reachable; the injected IMqttClient is mocked.
/// </summary>
public class MqttMetadataRoundTripTests
{
    private sealed class TestPayload
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    private const string Topic = "test/topic";

    private static (Mock<IMqttClient> client, Func<MqttApplicationMessage?> captured) MockClient()
    {
        var mock = new Mock<IMqttClient>();
        MqttApplicationMessage? captured = null;

        mock.Setup(c => c.PublishAsync(It.IsAny<MqttApplicationMessage>(), It.IsAny<CancellationToken>()))
            .Callback<MqttApplicationMessage, CancellationToken>((m, _) => captured = m)
            .ReturnsAsync((MqttClientPublishResult)null!);

        mock.Setup(c => c.SubscribeAsync(It.IsAny<MqttClientSubscribeOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((MqttClientSubscribeResult)null!);

        return (mock, () => captured);
    }

    private static Task DeliverAsync(MqttConsumer consumer, MqttApplicationMessage message)
    {
        // The receive path is wired to the MQTT client's event; invoke it directly so the
        // returned Task can be awaited deterministically (no event-raise race).
        var evt = new MqttApplicationMessageReceivedEventArgs(
            "client-id", message, new MqttPublishPacket(), (_, _) => Task.CompletedTask);

        var method = typeof(MqttConsumer).GetMethod(
            "OnMessageReceivedAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (Task)method.Invoke(consumer, new object[] { evt })!;
    }

    [Fact]
    public async Task OnHandlerError_InvokedWhenHandlerThrows()
    {
        // CR-L289: a throwing handler no longer fails silently — the OnHandlerError hook observes it.
        var (mock, _) = MockClient();
        var serializer = new JsonMessageSerializer();
        var consumer = new MqttConsumer(mock.Object, serializer, new MqttSettings());

        Exception? seen = null;
        QueueMessage? seenMessage = null;
        consumer.OnHandlerError = (ex, msg) => { seen = ex; seenMessage = msg; return Task.CompletedTask; };

        await consumer.SubscribeAsync(Topic, (msg, ct) => throw new InvalidOperationException("boom"));

        var appMessage = new MqttApplicationMessageBuilder().WithTopic(Topic).WithPayload("body").Build();
        await DeliverAsync(consumer, appMessage);

        seen.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("boom");
        seenMessage.Should().NotBeNull();
    }

    [Fact]
    public async Task OnHandlerError_HookThatThrows_DoesNotBreakDispatch()
    {
        // CR-L289: an exception from the error hook itself is swallowed (dispatch must survive).
        var (mock, _) = MockClient();
        var serializer = new JsonMessageSerializer();
        var consumer = new MqttConsumer(mock.Object, serializer, new MqttSettings());
        consumer.OnHandlerError = (ex, msg) => throw new InvalidOperationException("hook failed");

        await consumer.SubscribeAsync(Topic, (msg, ct) => throw new InvalidOperationException("handler failed"));

        var appMessage = new MqttApplicationMessageBuilder().WithTopic(Topic).WithPayload("body").Build();
        var act = async () => await DeliverAsync(consumer, appMessage);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Producer_AttachesPayloadTypeAndHeaders_AsUserProperties()
    {
        var (mock, captured) = MockClient();
        var serializer = new JsonMessageSerializer();
        var producer = new MqttProducer(mock.Object, serializer, new MqttSettings());

        var headers = new MessageHeaders { CorrelationId = "corr-1", ContentType = serializer.ContentType };
        await producer.SendAsync(Topic, new TestPayload { Name = "a", Value = 7 }, headers);

        var message = captured();
        message.Should().NotBeNull();
        message!.UserProperties.Should().Contain(p => p.Name == "payload_type");
        message.UserProperties.Should().Contain(p => p.Name == "headers");
        message.UserProperties.Should().Contain(p =>
            p.Name == "payload_type" && p.Value == typeof(TestPayload).AssemblyQualifiedName);
    }

    [Fact]
    public async Task RoundTrip_PreservesPayloadTypeAndHeaders()
    {
        var (mock, captured) = MockClient();
        var serializer = new JsonMessageSerializer();
        var producer = new MqttProducer(mock.Object, serializer, new MqttSettings());
        var consumer = new MqttConsumer(mock.Object, serializer, new MqttSettings());

        var headers = new MessageHeaders
        {
            CorrelationId = "corr-1",
            ReplyTo = "reply/here",
            GroupId = "g1",
            ContentType = serializer.ContentType,
        };
        headers.Custom["tenant"] = "acme";

        await producer.SendAsync(Topic, new TestPayload { Name = "a", Value = 7 }, headers);

        QueueMessage? received = null;
        await consumer.SubscribeAsync(Topic, (msg, _) => { received = msg; return Task.CompletedTask; });

        await DeliverAsync(consumer, captured()!);

        received.Should().NotBeNull();
        received!.PayloadType.Should().Be(typeof(TestPayload).AssemblyQualifiedName);
        received.Headers.CorrelationId.Should().Be("corr-1");
        received.Headers.ReplyTo.Should().Be("reply/here");
        received.Headers.GroupId.Should().Be("g1");
        received.Headers.Custom.Should().ContainKey("tenant").WhoseValue.Should().Be("acme");
    }

    [Fact]
    public async Task RoundTrip_TypedSubscribe_HandlerSeesMetadataViaContext()
    {
        var (mock, captured) = MockClient();
        var serializer = new JsonMessageSerializer();
        var producer = new MqttProducer(mock.Object, serializer, new MqttSettings());
        var consumer = new MqttConsumer(mock.Object, serializer, new MqttSettings());

        var headers = new MessageHeaders { CorrelationId = "corr-9", ContentType = serializer.ContentType };
        await producer.SendAsync(Topic, new TestPayload { Name = "b", Value = 42 }, headers);

        string? seenCorrelation = null;
        TestPayload? seenPayload = null;
        var handler = new DelegateHandler<TestPayload>((payload, context, _) =>
        {
            seenPayload = payload;
            seenCorrelation = context.Message.Headers.CorrelationId;
            return Task.CompletedTask;
        });

        await consumer.SubscribeAsync(Topic, handler);
        await DeliverAsync(consumer, captured()!);

        seenPayload.Should().NotBeNull();
        seenPayload!.Value.Should().Be(42);
        seenCorrelation.Should().Be("corr-9");
    }

    [Fact]
    public async Task Consumer_WithoutUserProperties_KeepsDefaults()
    {
        var (mock, _) = MockClient();
        var serializer = new JsonMessageSerializer();
        var consumer = new MqttConsumer(mock.Object, serializer, new MqttSettings());

        QueueMessage? received = null;
        await consumer.SubscribeAsync(Topic, (msg, _) => { received = msg; return Task.CompletedTask; });

        // A non-Birko / MQTT 3.1.1 publisher: no user properties on the wire.
        var plain = new MqttApplicationMessageBuilder().WithTopic(Topic).WithPayload("{}").Build();
        await DeliverAsync(consumer, plain);

        received.Should().NotBeNull();
        received!.PayloadType.Should().BeNull();
        received.Headers.Should().NotBeNull();
        received.Headers.CorrelationId.Should().BeNull();
    }

    private sealed class DelegateHandler<T> : IMessageHandler<T> where T : class
    {
        private readonly Func<T, MessageContext, CancellationToken, Task> _handle;
        public DelegateHandler(Func<T, MessageContext, CancellationToken, Task> handle) => _handle = handle;
        public Task HandleAsync(T message, MessageContext context, CancellationToken cancellationToken = default)
            => _handle(message, context, cancellationToken);
    }
}
