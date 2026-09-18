using System;
using System.Threading;
using System.Threading.Tasks;
using Birko.EventBus;
using Birko.EventBus.MessageQueue;
using Birko.MessageQueue;
using Birko.MessageQueue.Serialization;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.MessageQueue.Tests;

/// <summary>
/// CR-H114: the transport delivery callback swallowed every handler exception, so failed messages
/// were always acked and never retried/dead-lettered. Failures now surface out of the callback (the
/// transport drives retry/DLQ by whether it faults), while still dispatching to every handler.
/// </summary>
public class HandlerFailurePropagationTests
{
    private class TestEvent : IEvent
    {
        public Guid EventId { get; set; } = Guid.NewGuid();
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
        public string Source { get; set; } = "test";
    }

    private sealed class DelegatingHandler : IEventHandler<TestEvent>
    {
        private readonly Func<Task> _action;
        public int Calls;
        public DelegatingHandler(Func<Task> action) => _action = action;
        public Task HandleAsync(TestEvent @event, EventContext context, CancellationToken ct = default)
        {
            Calls++;
            return _action();
        }
    }

    // ---- Minimal in-memory transport that captures the delivery callback ----

    private sealed class CapturingQueue : IMessageQueue
    {
        public Func<QueueMessage, CancellationToken, Task>? Callback;
        public IMessageProducer Producer { get; } = new NoopProducer();
        public IMessageConsumer Consumer { get; }
        public bool IsConnected => true;
        public CapturingQueue() => Consumer = new CapturingConsumer(this);
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class CapturingConsumer : IMessageConsumer
    {
        private readonly CapturingQueue _queue;
        public CapturingConsumer(CapturingQueue queue) => _queue = queue;
        public Task<ISubscription> SubscribeAsync(string destination, Func<QueueMessage, CancellationToken, Task> handler, ConsumerOptions? options = null, CancellationToken ct = default)
        {
            _queue.Callback = handler;
            return Task.FromResult<ISubscription>(new NoopSubscription(destination));
        }
        public Task<ISubscription> SubscribeAsync<T>(string destination, IMessageHandler<T> handler, ConsumerOptions? options = null, CancellationToken ct = default) where T : class
            => throw new NotSupportedException();
        public Task AcknowledgeAsync(Guid messageId, CancellationToken ct = default) => Task.CompletedTask;
        public Task RejectAsync(Guid messageId, bool requeue = false, CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class NoopProducer : IMessageProducer
    {
        public Task SendAsync(string destination, QueueMessage message, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendAsync<T>(string destination, T payload, MessageHeaders? headers = null, CancellationToken ct = default) where T : class => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class NoopSubscription : ISubscription
    {
        public NoopSubscription(string destination) => Destination = destination;
        public string Destination { get; }
        public bool IsActive => true;
        public Task UnsubscribeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    private static QueueMessage BuildMessage(IMessageSerializer serializer, TestEvent evt)
    {
        var envelope = new EventEnvelope
        {
            EventId = evt.EventId,
            EventType = typeof(TestEvent).AssemblyQualifiedName!,
            Source = evt.Source,
            OccurredAt = evt.OccurredAt,
            Payload = serializer.Serialize(evt),
        };
        return new QueueMessage { Body = serializer.Serialize(envelope) };
    }

    [Fact]
    public async Task HandlerException_FaultsDeliveryCallback()
    {
        var serializer = new JsonMessageSerializer();
        var queue = new CapturingQueue();
        var bus = new DistributedEventBus(queue, new DistributedEventBusOptions { Serializer = serializer });

        var handler = new DelegatingHandler(() => throw new InvalidOperationException("handler failed"));
        bus.Subscribe<TestEvent>(handler);

        await bus.SubscribeToTransportAsync<TestEvent>();
        queue.Callback.Should().NotBeNull();

        var message = BuildMessage(serializer, new TestEvent());

        // The failure must propagate so the transport can retry / dead-letter.
        await queue.Callback!.Invoke(message, CancellationToken.None)
            .Awaiting(t => t).Should().ThrowAsync<InvalidOperationException>();
        handler.Calls.Should().Be(1);
    }

    [Fact]
    public async Task AllHandlersRun_EvenWhenOneThrows()
    {
        var serializer = new JsonMessageSerializer();
        var queue = new CapturingQueue();
        var bus = new DistributedEventBus(queue, new DistributedEventBusOptions { Serializer = serializer });

        var failing = new DelegatingHandler(() => throw new InvalidOperationException("boom"));
        var succeeding = new DelegatingHandler(() => Task.CompletedTask);
        bus.Subscribe<TestEvent>(failing);
        bus.Subscribe<TestEvent>(succeeding);

        await bus.SubscribeToTransportAsync<TestEvent>();
        var message = BuildMessage(serializer, new TestEvent());

        await queue.Callback!.Invoke(message, CancellationToken.None)
            .Awaiting(t => t).Should().ThrowAsync<Exception>();

        failing.Calls.Should().Be(1);
        succeeding.Calls.Should().Be(1, "error isolation: every handler still runs");
    }

    [Fact]
    public async Task SuccessfulHandlers_DoNotFault()
    {
        var serializer = new JsonMessageSerializer();
        var queue = new CapturingQueue();
        var bus = new DistributedEventBus(queue, new DistributedEventBusOptions { Serializer = serializer });

        bus.Subscribe<TestEvent>(new DelegatingHandler(() => Task.CompletedTask));

        await bus.SubscribeToTransportAsync<TestEvent>();
        var message = BuildMessage(serializer, new TestEvent());

        await queue.Callback!.Invoke(message, CancellationToken.None)
            .Awaiting(t => t).Should().NotThrowAsync();
    }
}
