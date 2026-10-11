using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.EventBus.Extensions;
using Birko.EventBus.Outbox;
using Birko.EventBus.Outbox.Extensions;
using Birko.EventBus.Outbox.Publishing;
using Birko.EventBus.Outbox.Stores;
using Birko.EventBus.Tests.TestResources;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Birko.EventBus.Tests
{
    /// <summary>
    /// TASK-565 / SH-M120 — <c>AddEventBus</c> registers the bus as a singleton over the ROOT provider, so handlers
    /// that depend on a scoped service either throw (<c>ValidateScopes</c>, i.e. ASP.NET Development) or get one
    /// root-resolved instance shared by every dispatch for the life of the process and never disposed (everywhere
    /// else). The outbox makes that path unconditional: <see cref="OutboxProcessor"/> publishes from a background
    /// loop where no request scope exists. Each publish must therefore run its handlers in a scope of its own.
    /// </summary>
    public class HandlerScopeTests
    {
        private static ServiceProvider Build(bool validateScopes, Action<IServiceCollection>? extra = null)
        {
            var services = new ServiceCollection();
            services.AddEventBus();
            services.AddSingleton<ScopeLog>();
            services.AddScoped<ScopedProbe>();
            services.AddEventHandler<OrderPlaced, ScopedProbeHandler>();
            extra?.Invoke(services);
            return services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateScopes = validateScopes,
                ValidateOnBuild = validateScopes,
            });
        }

        [Fact]
        public async Task A_handler_with_a_scoped_dependency_runs_under_scope_validation()
        {
            await using var root = Build(validateScopes: true);
            var bus = root.GetRequiredService<IEventBus>();

            await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m));

            root.GetRequiredService<ScopeLog>().Seen.Should().ContainSingle(
                "the handler must be resolved from a scope, not from the root the bus was built with");
        }

        [Fact]
        public async Task Each_publish_gets_its_own_scoped_instance_and_disposes_it()
        {
            await using var root = Build(validateScopes: false);
            var bus = root.GetRequiredService<IEventBus>();

            await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m));
            await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 2m));

            var seen = root.GetRequiredService<ScopeLog>().Seen;
            seen.Should().HaveCount(2);
            seen[0].Should().NotBeSameAs(seen[1],
                "a root-resolved scoped service is one captive instance shared by every dispatch");
            seen.Should().OnlyContain(p => p.Disposed,
                "the publish's scope ends with the publish, so its scoped services are disposed");
        }

        [Fact]
        public async Task Handlers_of_one_publish_share_that_publish_scope()
        {
            await using var root = Build(validateScopes: true,
                s => s.AddEventHandler<OrderPlaced, SecondScopedProbeHandler>());
            var bus = root.GetRequiredService<IEventBus>();

            await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m));

            var seen = root.GetRequiredService<ScopeLog>().Seen;
            seen.Should().HaveCount(2);
            seen[0].Should().BeSameAs(seen[1]);
        }

        [Fact]
        public async Task An_outbox_dispatch_runs_a_scoped_handler_under_scope_validation()
        {
            await using var root = Build(validateScopes: true, s =>
            {
                s.AddInMemoryOutbox();
                s.AddOutboxEventBus();
            });
            var bus = root.GetRequiredService<IEventBus>();
            bus.Should().BeOfType<OutboxEventBus>();

            await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m));
            await root.GetRequiredService<OutboxProcessor>().ProcessBatchAsync();

            var store = (InMemoryOutboxStore)root.GetRequiredService<IOutboxStore>();
            store.GetAll().Should().ContainSingle().Which.Status.Should().Be(OutboxStatus.Published,
                "the processor resolves handlers off-request, where only a scope of the bus's own can supply them");
            root.GetRequiredService<ScopeLog>().Seen.Should().ContainSingle();
        }

        [Fact]
        public async Task A_bus_without_a_provider_still_dispatches_manual_subscriptions()
        {
            using var bus = new Birko.EventBus.Local.InProcessEventBus();
            var handler = new OrderPlacedHandler();
            bus.Subscribe(handler);

            await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m));

            handler.ReceivedEvents.Should().ContainSingle();
        }

        private sealed class ScopeLog
        {
            private readonly List<ScopedProbe> _seen = [];
            public IReadOnlyList<ScopedProbe> Seen { get { lock (_seen) return _seen.ToList(); } }
            public void Add(ScopedProbe probe) { lock (_seen) _seen.Add(probe); }
        }

        private sealed class ScopedProbe : IAsyncDisposable
        {
            public bool Disposed { get; private set; }
            public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
        }

        private sealed class ScopedProbeHandler(ScopedProbe probe, ScopeLog log) : IEventHandler<OrderPlaced>
        {
            public Task HandleAsync(OrderPlaced @event, EventContext context, CancellationToken cancellationToken = default)
            {
                log.Add(probe);
                return Task.CompletedTask;
            }
        }

        private sealed class SecondScopedProbeHandler(ScopedProbe probe, ScopeLog log) : IEventHandler<OrderPlaced>
        {
            public Task HandleAsync(OrderPlaced @event, EventContext context, CancellationToken cancellationToken = default)
            {
                log.Add(probe);
                return Task.CompletedTask;
            }
        }
    }
}
