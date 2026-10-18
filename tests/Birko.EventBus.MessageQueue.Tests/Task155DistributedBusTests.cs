using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.EventBus;
using Birko.EventBus.MessageQueue;
using Birko.EventBus.Outbox.Extensions;
using Birko.MessageQueue;
using Birko.MessageQueue.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace Birko.EventBus.MessageQueue.Tests;

/// <summary>
/// TASK-155 — the distributed bus: SH-M114 (composing it with the outbox killed host startup), SH-M116 (an undecodable
/// message was acked and dropped in silence; decided by the user: retry an unknown event type, report a corrupt
/// message) and SH-M117 (the consume path never applied the IEventScopeAccessor, so handlers lost the tenant scope).
/// </summary>
public class Task155DistributedBusTests
{
    public sealed class T155Event : IEvent
    {
        public Guid EventGuid { get; set; } = Guid.NewGuid();
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
        public string Source { get; set; } = "test";
    }

    private sealed class Handler : IEventHandler<T155Event>
    {
        public readonly ConcurrentBag<Guid?> Tenants = new();
        public Func<Guid?>? Ambient;
        public Task HandleAsync(T155Event @event, EventContext context, CancellationToken ct = default)
        {
            Tenants.Add(Ambient?.Invoke());
            return Task.CompletedTask;
        }
    }

    private sealed class Queue : IMessageQueue, IMessageConsumer, IMessageProducer, ISubscription
    {
        public Func<QueueMessage, CancellationToken, Task>? Callback;
        public IMessageProducer Producer => this;
        public IMessageConsumer Consumer => this;
        public bool IsConnected => true;
        public string Destination => "t";
        public bool IsActive => true;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<ISubscription> SubscribeAsync(string destination, Func<QueueMessage, CancellationToken, Task> handler, ConsumerOptions? options = null, CancellationToken ct = default)
        {
            Callback = handler;
            return Task.FromResult<ISubscription>(this);
        }
        public Task<ISubscription> SubscribeAsync<T>(string destination, IMessageHandler<T> handler, ConsumerOptions? options = null, CancellationToken ct = default) where T : class
            => throw new NotSupportedException();
        public Task AcknowledgeAsync(Guid messageId, CancellationToken ct = default) => Task.CompletedTask;
        public Task RejectAsync(Guid messageId, bool requeue = false, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendAsync(string destination, QueueMessage message, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendAsync<T>(string destination, T payload, MessageHeaders? headers = null, CancellationToken ct = default) where T : class => Task.CompletedTask;
        public Task UnsubscribeAsync(CancellationToken ct = default) => Task.CompletedTask;
        public void Dispose() { }
    }

    /// <summary>A scope bridge that makes the context's tenant ambient for the body, as the tenant bridge does.</summary>
    private sealed class AmbientTenant : IEventScopeAccessor
    {
        public static readonly AsyncLocal<Guid?> Current = new();
        public async Task RunWithScopeAsync(EventContext context, Func<Task> body, CancellationToken cancellationToken = default)
        {
            var previous = Current.Value;
            Current.Value = context.TenantGuid;
            try { await body(); }
            finally { Current.Value = previous; }
        }
    }

    private static readonly IMessageSerializer Serializer = new JsonMessageSerializer();

    private static QueueMessage Message(string eventType, string payload, Guid? tenant = null)
        => new() { Body = Serializer.Serialize(new EventEnvelope { EventGuid = Guid.NewGuid(), EventType = eventType, Source = "t", OccurredAt = DateTime.UtcNow, Payload = payload, TenantGuid = tenant }) };

    private static async Task<(DistributedEventBus Bus, Queue Queue, Handler Handler)> Bus(IServiceProvider? services = null)
    {
        var queue = new Queue();
        var bus = new DistributedEventBus(queue, new DistributedEventBusOptions { Serializer = Serializer }, services);
        var handler = new Handler { Ambient = () => AmbientTenant.Current.Value };
        bus.Subscribe(handler);
        await bus.SubscribeToTransportAsync<T155Event>();
        return (bus, queue, handler);
    }

    [Fact]
    public void SH_M114_the_outbox_over_the_distributed_bus_starts()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMessageQueue>(new Queue());
        services.AddDistributedEventBus();
        services.AddInMemoryOutbox();
        services.AddOutboxEventBus();
        using var provider = services.BuildServiceProvider();

        var build = () => provider.GetServices<IHostedService>().ToList();
        build.Should().NotThrow("the documented outbox-over-distributed composition must construct its hosted services");
        provider.GetRequiredService<IEventBus>().Should().NotBeOfType<DistributedEventBus>("the outbox decorates the bus");
    }

    [Fact]
    public async Task SH_M116_an_unknown_event_type_is_left_for_redelivery()
    {
        var (_, queue, handler) = await Bus();
        var deliver = () => queue.Callback!(Message("Some.Newer.Event, Some.Newer.Assembly", "{}"), CancellationToken.None);
        await deliver.Should().ThrowAsync<InvalidOperationException>("a newer consumer may know the type, so the transport redelivers it");
        handler.Tenants.Should().BeEmpty();
    }

    [Fact]
    public async Task SH_M116_a_corrupt_message_is_reported_and_dropped()
    {
        var (bus, queue, _) = await Bus();
        var dropped = new ConcurrentBag<string>();
        bus.MessageDropped += (_, reason) => dropped.Add(reason);

        await queue.Callback!(new QueueMessage { Body = "null" }, CancellationToken.None);
        await queue.Callback!(Message(typeof(T155Event).AssemblyQualifiedName!, "null"), CancellationToken.None);
        await queue.Callback!(Message(typeof(string).AssemblyQualifiedName!, "\"x\""), CancellationToken.None);

        dropped.Should().HaveCount(3, "each undecodable message is reported, then acked: it can never succeed");
    }

    [Fact]
    public async Task SH_M117_handlers_run_in_the_scope_the_event_was_published_under()
    {
        var services = new ServiceCollection().AddSingleton<IEventScopeAccessor, AmbientTenant>().BuildServiceProvider();
        var (_, queue, handler) = await Bus(services);
        var tenant = Guid.NewGuid();

        await queue.Callback!(Message(typeof(T155Event).AssemblyQualifiedName!, Serializer.Serialize(new T155Event()), tenant), CancellationToken.None);

        handler.Tenants.Should().Equal(tenant);
    }
}
