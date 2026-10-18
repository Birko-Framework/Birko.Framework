using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.EventBus;
using Birko.EventBus.MessageQueue;
using Birko.MessageQueue;
using Birko.MessageQueue.InMemory;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Birko.EventBus.MessageQueue.Tests;

/// <summary>
/// TASK-174 on the distributed bus: SH-L112 (AutoSubscribe decided at registration, so a later change was ignored),
/// SH-L114 (RetryPolicy / DeadLetterOptions advertised behaviour nothing applies; decided by the user: refuse until
/// implemented), SH-L115 (a non-atomic subscription Dispose) and SH-L125 (envelope fields typed non-null left null).
/// </summary>
public class Task174DistributedBusTests
{
    public sealed class T174Event : IEvent
    {
        public Guid EventGuid { get; set; } = Guid.NewGuid();
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
        public string Source { get; set; } = "t";
    }

    private sealed class Handler : IEventHandler<T174Event>
    {
        public Task HandleAsync(T174Event @event, EventContext context, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public void SH_L112_AutoSubscribe_is_read_when_the_host_starts()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMessageQueue>(new InMemoryMessageQueue());
        services.AddDistributedEventBus(o => o.AutoSubscribe = false);
        using var provider = services.BuildServiceProvider();

        provider.GetServices<IHostedService>().OfType<DistributedEventBusHostedService>()
            .Should().ContainSingle("the hosted service is registered whatever AutoSubscribe was at registration; StartAsync reads it");
    }

    [Fact]
    public void SH_L114_retry_and_dead_letter_are_off_by_default_and_refused_where_deliveries_cannot_be_counted()
    {
        // Inverted at TASK-668, which implements them: refused only on a transport that cannot say how a failed delivery
        // comes back (no IRedeliveryCapability); in-memory accepts them (Task668BusRetryTests).
        new DistributedEventBusOptions().RetryPolicy.Should().BeNull("off unless asked for");
        new DistributedEventBusOptions().DeadLetterOptions.Should().BeNull();

        using var inner = new InMemoryMessageQueue();
        var withRetry = () => new DistributedEventBus(new UncountedQueue(inner), new DistributedEventBusOptions { RetryPolicy = new global::Birko.RetryPolicy() });
        withRetry.Should().Throw<NotSupportedException>();
        var withDlq = () => new DistributedEventBus(new UncountedQueue(inner), new DistributedEventBusOptions { DeadLetterOptions = new global::Birko.MessageQueue.Retry.DeadLetterOptions() });
        withDlq.Should().Throw<NotSupportedException>();
        new DistributedEventBus(new InMemoryMessageQueue(), new DistributedEventBusOptions { RetryPolicy = new global::Birko.RetryPolicy() }).Dispose();
    }

    /// <summary>A transport that does not implement IRedeliveryCapability.</summary>
    private sealed class UncountedQueue(InMemoryMessageQueue inner) : IMessageQueue
    {
        public IMessageProducer Producer => inner.Producer;
        public IMessageConsumer Consumer => inner.Consumer;
        public bool IsConnected => inner.IsConnected;
        public Task ConnectAsync(CancellationToken cancellationToken = default) => inner.ConnectAsync(cancellationToken);
        public Task DisconnectAsync(CancellationToken cancellationToken = default) => inner.DisconnectAsync(cancellationToken);
        public void Dispose() { }
    }

    [Fact]
    public void SH_L115_concurrent_disposes_of_a_subscription_unsubscribe_once()
    {
        using var bus = new DistributedEventBus(new InMemoryMessageQueue());
        var handler = new Handler();
        for (var round = 0; round < 50; round++)
        {
            var subscription = bus.Subscribe(handler);
            var other = bus.Subscribe(handler); // the same handler twice: a second Remove would take this one
            Parallel.For(0, 8, _ => subscription.Dispose());
            other.IsActive.Should().BeTrue();
            subscription.IsActive.Should().BeFalse();
            other.Dispose();
        }
    }

    [Fact]
    public void SH_L125_an_envelope_never_holds_null_in_a_non_nullable_field()
    {
        var envelope = new EventEnvelope();
        envelope.EventType.Should().NotBeNull();
        envelope.Source.Should().NotBeNull();
        envelope.Payload.Should().NotBeNull();
    }
}
