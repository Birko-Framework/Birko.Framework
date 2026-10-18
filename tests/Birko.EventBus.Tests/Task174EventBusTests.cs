using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Birko.EventBus;
using Birko.EventBus.Deduplication;
using Birko.EventBus.EventSourcing;
using Birko.EventBus.Local;
using Birko.EventBus.Pipeline;
using Birko.EventBus.Routing;
using Birko.EventBus.Tests.TestResources;
using Birko.Rules;
using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.Tests
{
    /// <summary>
    /// TASK-174 — low findings in the in-process bus, the event context, deduplication, the rule filter and routing:
    /// SH-L117, SH-L118, SH-L124, SH-L125, SH-L126, SH-L127, SH-L129.
    /// </summary>
    public class Task174EventBusTests
    {
        private sealed class ThrowsOwnCancellation : IEventHandler<OrderPlaced>
        {
            public Task HandleAsync(OrderPlaced @event, EventContext context, CancellationToken ct = default)
                => throw new OperationCanceledException("the handler's own internal timeout");
        }

        private sealed class Noop : IEventHandler<OrderPlaced>
        {
            public Task HandleAsync(OrderPlaced @event, EventContext context, CancellationToken ct = default) => Task.CompletedTask;
        }

        [Fact]
        public async Task SH_L117_a_handlers_own_cancellation_is_reported_in_parallel_Continue_mode()
        {
            var reported = new ConcurrentBag<Exception>();
            using var bus = new InProcessEventBus(options: new InProcessEventBusOptions
            {
                MaxConcurrency = 2,
                ErrorHandling = ErrorHandlingMode.Continue,
                OnHandlerError = (_, ex) => reported.Add(ex),
            });
            bus.Subscribe(new ThrowsOwnCancellation());
            bus.Subscribe(new Noop());

            await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m));

            reported.Should().ContainSingle().Which.Should().BeOfType<OperationCanceledException>(
                "only the follow-on cancellation of Stop mode is swallowed, not a handler's own failure");
        }

        [Fact]
        public void SH_L118_a_handle_reports_inactive_once_the_bus_is_disposed_and_a_later_subscribe_is_refused()
        {
            var bus = new InProcessEventBus();
            var handle = bus.Subscribe(new Noop());
            handle.IsActive.Should().BeTrue();

            bus.Dispose();

            handle.IsActive.Should().BeFalse("a disposed bus delivers to no subscription");
            bus.Invoking(b => b.Subscribe(new Noop())).Should().Throw<ObjectDisposedException>();
        }

        public sealed class PlainCorrelatedEvent : ICorrelatedEvent
        {
            public Guid EventGuid { get; } = Guid.NewGuid();
            public DateTime OccurredAt { get; } = DateTime.UtcNow;
            public string Source => "plain";
            public Guid? CorrelationGuid { get; init; }
        }

        [Fact]
        public void SH_L124_correlation_is_read_from_any_correlated_event_not_only_EventBase()
        {
            var correlation = Guid.NewGuid();
            EventContext.From(new PlainCorrelatedEvent { CorrelationGuid = correlation }).CorrelationGuid.Should().Be(correlation);
            EventContext.From(new OrderPlaced(Guid.NewGuid(), 1m) { CorrelationGuid = correlation }).CorrelationGuid.Should().Be(correlation);
        }

        [Fact]
        public async Task SH_L124_the_correlation_enricher_keeps_a_correlated_events_own_correlation()
        {
            var correlation = Guid.NewGuid();
            var context = new EventContext();
            await new Birko.EventBus.Enrichment.CorrelationEventEnricher().EnrichAsync(new PlainCorrelatedEvent { CorrelationGuid = correlation }, context);
            context.CorrelationGuid.Should().Be(correlation, "not a fresh Guid that breaks the trace");
        }

        [Fact]
        public void SH_L125_string_members_a_deserializer_may_leave_unset_default_to_empty_not_null()
        {
            new EventContext().Source.Should().NotBeNull();
            var published = new DomainEventPublished();
            published.DomainEventType.Should().NotBeNull();
            published.EventData.Should().NotBeNull();
        }

        [Fact]
        public void SH_L126_a_null_rule_set_is_refused_at_wiring_time()
        {
            FluentActions.Invoking(() => new RuleFilterBehavior(null!)).Should().Throw<ArgumentNullException>();
            FluentActions.Invoking(() => new RuleFilterBehavior(null!, (_, _) => null!)).Should().Throw<ArgumentNullException>();
        }

        [Fact]
        public async Task SH_L127_marking_alone_still_sweeps_expired_ids()
        {
            var clock = new TestDateTimeProvider(new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero));
            var store = new InMemoryDeduplicationStore(ttl: TimeSpan.FromMinutes(1), clock: clock);
            await store.MarkProcessedAsync(Guid.NewGuid());

            clock.Advance(TimeSpan.FromMinutes(10)); // past the TTL and the sweep interval
            await store.MarkProcessedAsync(Guid.NewGuid());

            var processed = (ConcurrentDictionary<Guid, DateTime>)typeof(InMemoryDeduplicationStore)
                .GetField("_processed", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(store)!;
            processed.Should().HaveCount(1, "the expired id is swept by Mark too, not only by Exists / TryMark");
        }

        [Topic("custom.topic")]
        public sealed record Attributed : EventBase
        {
            public override string Source => "src";
        }

        [Fact]
        public void SH_L129_the_instance_overload_gives_the_same_topics_as_before()
        {
            var convention = new AttributeTopicConvention();
            // Pin: the cached lookup must not change any result.
            convention.GetTopic(new Attributed()).Should().Be("custom.topic");
            convention.GetTopic(new OrderPlaced(Guid.NewGuid(), 1m)).Should().Be(new DefaultTopicConvention().GetTopic(new OrderPlaced(Guid.NewGuid(), 1m)));
        }
    }
}
