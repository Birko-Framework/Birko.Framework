using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.EventSourcing.Events;
using Birko.EventBus;
using Birko.EventBus.Deduplication;
using Birko.EventBus.EventSourcing;
using Birko.EventBus.Local;
using Birko.EventBus.Pipeline;
using Birko.EventBus.Tests.EventSourcing;
using Birko.EventBus.Tests.TestResources;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.Tests
{
    /// <summary>
    /// TASK-155 — SH-M118 (deduplication marked before handlers and kept the mark on failure, so a redelivery of a failed
    /// event was dropped; decided by the user: release on failure), SH-M119 (a publish with no handlers skipped the
    /// pipeline, so deduplication never recorded it) and SH-M122 (a range append that failed part-way said nothing of
    /// which events were left unpublished).
    /// </summary>
    public class Task155EventBusTests
    {
        private sealed class Recorder : IEventHandler<OrderPlaced>
        {
            public int Calls;
            public Task HandleAsync(OrderPlaced @event, EventContext context, CancellationToken ct = default)
            {
                Interlocked.Increment(ref Calls);
                return Task.CompletedTask;
            }
        }

        [Fact]
        public async Task SH_M118_an_event_whose_handler_failed_runs_again_when_redelivered()
        {
            var store = new InMemoryDeduplicationStore();
            var behavior = new DeduplicationBehavior(store);
            var evt = new OrderPlaced(Guid.NewGuid(), 1m);
            var calls = 0;

            await behavior.Invoking(b => b.HandleAsync(evt, EventContext.From(evt), () => { calls++; throw new InvalidOperationException("transient"); }))
                .Should().ThrowAsync<InvalidOperationException>();
            (await store.ExistsAsync(evt.EventGuid)).Should().BeFalse("a failed handling releases the reservation");

            await behavior.HandleAsync(evt, EventContext.From(evt), () => { calls++; return Task.CompletedTask; });
            calls.Should().Be(2, "the redelivery is processed, not dropped as a duplicate");

            await behavior.HandleAsync(evt, EventContext.From(evt), () => { calls++; return Task.CompletedTask; });
            calls.Should().Be(2, "once handled, it is a duplicate");
        }

        [Fact]
        public async Task SH_M119_a_publish_with_no_handler_is_recorded_by_deduplication()
        {
            var store = new InMemoryDeduplicationStore();
            using var bus = new InProcessEventBus(behaviors: new IEventPipelineBehavior[] { new DeduplicationBehavior(store) });
            var evt = new OrderPlaced(Guid.NewGuid(), 1m);

            await bus.PublishAsync(evt);
            (await store.ExistsAsync(evt.EventGuid)).Should().BeTrue("the pipeline saw the publish");

            var recorder = new Recorder();
            bus.Subscribe(recorder);
            await bus.PublishAsync(evt);
            recorder.Calls.Should().Be(0, "the same event id is a duplicate, not first-seen");
        }

        /// <summary>A transport whose second publish fails, as a broker outage mid-range would.</summary>
        private sealed class FailOnSecond : IEventBus
        {
            private int _seen;
            public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
                => Interlocked.Increment(ref _seen) == 2 ? throw new InvalidOperationException("publish 2 failed") : Task.CompletedTask;
            public IEventSubscription Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent => throw new NotSupportedException();
            public void Dispose() { }
        }

        [Fact]
        public async Task SH_M122_a_partial_publish_says_which_appended_events_are_unpublished()
        {
            var store = new TestAsyncEventStore();
            var decorator = new EventStoreEventBus(store, new FailOnSecond());
            var aggregate = Guid.NewGuid();
            var events = Enumerable.Range(1, 4).Select(v => (Birko.Data.EventSourcing.Events.IEvent)new DomainEvent(aggregate, v, "E", "{}")).ToList();

            var append = () => decorator.AppendRangeAsync(events);
            var thrown = (await append.Should().ThrowAsync<InvalidOperationException>()).Which;

            thrown.Data[EventStoreEventBus.UnpublishedEventsKey].Should().BeAssignableTo<IReadOnlyList<Birko.Data.EventSourcing.Events.IEvent>>()
                .Which.Select(e => e.Version).Should().Equal(2, 3, 4);
            (await store.ReadAsync(aggregate)).Should().HaveCount(4, "all four were appended before publishing began");
        }
    }
}
