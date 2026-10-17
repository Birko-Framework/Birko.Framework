using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.EventBus;
using Birko.EventBus.MessageQueue;
using Birko.MessageQueue;
using Birko.MessageQueue.Serialization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Birko.EventBus.MessageQueue.Tests;

/// <summary>
/// TASK-566: the distributed bus resolves DI handlers from one scope per incoming message (as InProcessEventBus since
/// TASK-565), and AutoSubscriber learns the handled event types without resolving handlers from the root.
/// </summary>
public class Task566HandlerScopeTests
{
    public sealed class T566Event : IEvent
    {
        public Guid EventGuid { get; set; } = Guid.NewGuid();
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
        public string Source { get; set; } = "test";
    }

    public sealed class T566ScopedDependency : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }

    public sealed class T566Log
    {
        public List<T566ScopedDependency> Seen { get; } = new();
        public int Constructed;
    }

    public sealed class T566Handler : IEventHandler<T566Event>
    {
        private readonly T566ScopedDependency _dependency;
        private readonly T566Log _log;

        public T566Handler(T566ScopedDependency dependency, T566Log log)
        {
            _dependency = dependency;
            _log = log;
            Interlocked.Increment(ref log.Constructed);
        }

        public Task HandleAsync(T566Event @event, EventContext context, CancellationToken ct = default)
        {
            _dependency.Disposed.Should().BeFalse("the scope lives until dispatch is over");
            lock (_log.Seen) _log.Seen.Add(_dependency);
            return Task.CompletedTask;
        }
    }

    private sealed class CapturingQueue : IMessageQueue, IMessageConsumer, IMessageProducer
    {
        public Dictionary<string, Func<QueueMessage, CancellationToken, Task>> Callbacks { get; } = new();
        public IMessageProducer Producer => this;
        public IMessageConsumer Consumer => this;
        public bool IsConnected => true;
        public Task ConnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task DisconnectAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task<ISubscription> SubscribeAsync(string destination, Func<QueueMessage, CancellationToken, Task> handler, ConsumerOptions? options = null, CancellationToken ct = default)
        {
            Callbacks[destination] = handler;
            return Task.FromResult<ISubscription>(new Subscription(destination));
        }
        public Task<ISubscription> SubscribeAsync<T>(string destination, IMessageHandler<T> handler, ConsumerOptions? options = null, CancellationToken ct = default) where T : class
            => throw new NotSupportedException();
        public Task AcknowledgeAsync(Guid messageId, CancellationToken ct = default) => Task.CompletedTask;
        public Task RejectAsync(Guid messageId, bool requeue = false, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendAsync(string destination, QueueMessage message, CancellationToken ct = default) => Task.CompletedTask;
        public Task SendAsync<T>(string destination, T payload, MessageHeaders? headers = null, CancellationToken ct = default) where T : class => Task.CompletedTask;
        public void Dispose() { }

        private sealed class Subscription : ISubscription
        {
            public Subscription(string destination) => Destination = destination;
            public string Destination { get; }
            public bool IsActive => true;
            public Task UnsubscribeAsync(CancellationToken ct = default) => Task.CompletedTask;
            public void Dispose() { }
        }
    }

    private static readonly JsonMessageSerializer Serializer = new();

    private static ServiceProvider Provider(bool validateScopes)
    {
        var services = new ServiceCollection();
        services.AddSingleton<T566Log>();
        services.AddScoped<T566ScopedDependency>();
        services.AddTransient<IEventHandler<T566Event>, T566Handler>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = validateScopes });
    }

    private static QueueMessage Message()
    {
        var evt = new T566Event();
        var envelope = new EventEnvelope
        {
            EventGuid = evt.EventGuid,
            EventType = typeof(T566Event).AssemblyQualifiedName!,
            Source = evt.Source,
            OccurredAt = evt.OccurredAt,
            Payload = Serializer.Serialize(evt),
        };
        return new QueueMessage { Body = Serializer.Serialize(envelope) };
    }

    private static DistributedEventBus Bus(CapturingQueue queue, IServiceProvider provider)
        => new(queue, new DistributedEventBusOptions { Serializer = Serializer }, provider);

    [Fact]
    public async Task A_handler_with_a_scoped_dependency_is_dispatched_under_ValidateScopes()
    {
        using var provider = Provider(validateScopes: true);
        var queue = new CapturingQueue();
        var bus = Bus(queue, provider);
        await bus.SubscribeToTransportAsync<T566Event>();

        await queue.Callbacks.Values.Should().ContainSingle().Which.Invoking(c => c(Message(), CancellationToken.None))
            .Should().NotThrowAsync();

        provider.GetRequiredService<T566Log>().Seen.Should().ContainSingle();
    }

    [Fact]
    public async Task Each_message_gets_its_own_scope_disposed_after_dispatch()
    {
        using var provider = Provider(validateScopes: false);
        var queue = new CapturingQueue();
        var bus = Bus(queue, provider);
        await bus.SubscribeToTransportAsync<T566Event>();
        var deliver = queue.Callbacks.Values.Should().ContainSingle().Subject;

        await deliver(Message(), CancellationToken.None);
        await deliver(Message(), CancellationToken.None);

        var seen = provider.GetRequiredService<T566Log>().Seen;
        seen.Should().HaveCount(2);
        seen[0].Should().NotBeSameAs(seen[1], "one root-owned instance was shared by every message");
        seen.Should().OnlyContain(d => d.Disposed, "the message's scope is disposed once dispatch is over");
    }

    [Fact]
    public async Task AutoSubscriber_finds_the_handled_type_under_ValidateScopes_without_building_a_handler()
    {
        using var provider = Provider(validateScopes: true);
        var queue = new CapturingQueue();

        await new AutoSubscriber(Bus(queue, provider), provider).SubscribeAllAsync();

        queue.Callbacks.Should().ContainKey(new DistributedEventBusOptions().TopicConvention.GetTopic(typeof(T566Event)));
        provider.GetRequiredService<T566Log>().Constructed.Should().Be(0, "discovery asks the container, it does not build handlers");
    }

    /// <summary>A container that does not answer <see cref="IServiceProviderIsService"/>.</summary>
    private sealed class NoIsServiceProvider : IServiceProvider
    {
        private readonly IServiceProvider _inner;
        public NoIsServiceProvider(IServiceProvider inner) => _inner = inner;
        public object? GetService(Type serviceType)
            => serviceType == typeof(IServiceProviderIsService) ? null : _inner.GetService(serviceType);
    }

    [Fact]
    public async Task AutoSubscriber_asks_inside_a_scope_when_the_container_cannot_answer_IsService()
    {
        using var provider = Provider(validateScopes: true);
        var root = new NoIsServiceProvider(provider);
        var queue = new CapturingQueue();

        await new AutoSubscriber(Bus(queue, root), root).SubscribeAllAsync();

        queue.Callbacks.Should().ContainKey(new DistributedEventBusOptions().TopicConvention.GetTopic(typeof(T566Event)));
    }
}
