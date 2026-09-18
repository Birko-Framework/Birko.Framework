using System;
using System.Threading.Tasks;
using Birko.EventBus;
using Birko.EventBus.Deduplication;
using Birko.EventBus.Local;
using Birko.EventBus.Tests.TestResources;
using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.Tests
{
    public class DeduplicationTests
    {
        // ---- CR-L248: atomic reserve + mark-before semantics ----

        [Fact]
        public async Task InMemoryStore_TryMarkProcessedAsync_ReservesAtomically()
        {
            var store = new InMemoryDeduplicationStore();
            var id = Guid.NewGuid();

            (await store.TryMarkProcessedAsync(id)).Should().BeTrue("first caller marks the event");
            (await store.TryMarkProcessedAsync(id)).Should().BeFalse("a second caller sees it already marked");
            (await store.ExistsAsync(id)).Should().BeTrue();
        }

        [Fact]
        public async Task DeduplicationBehavior_MarksBeforeHandler_SoAThrowingHandlerStillDedups()
        {
            var store = new InMemoryDeduplicationStore();
            var behavior = new DeduplicationBehavior(store);
            var evt = new OrderPlaced(Guid.NewGuid(), 1m);

            var handlerCalls = 0;
            Func<Task> throwingNext = () =>
            {
                handlerCalls++;
                throw new InvalidOperationException("handler failed");
            };

            // Mark-before (at-most-once): the event is reserved before the handler runs, so even though
            // the handler throws, the event stays marked and a republish is skipped (not reprocessed).
            await behavior.Invoking(b => b.HandleAsync(evt, EventContext.From(evt), throwingNext))
                .Should().ThrowAsync<InvalidOperationException>();
            (await store.ExistsAsync(evt.EventId)).Should().BeTrue("mark happens before the handler");

            await behavior.HandleAsync(evt, EventContext.From(evt), throwingNext); // duplicate → skipped
            handlerCalls.Should().Be(1, "the duplicate must not re-invoke the handler");
        }

        // ---- CR-L249: cleanup evicts expired entries (functional pin over the Interlocked-guarded sweep) ----

        [Fact]
        public async Task InMemoryStore_CleanupEvictsExpiredEntries_AfterInterval()
        {
            var clock = new TestDateTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var store = new InMemoryDeduplicationStore(ttl: TimeSpan.FromMinutes(1), clock: clock);
            var id = Guid.NewGuid();

            await store.MarkProcessedAsync(id);
            (await store.ExistsAsync(id)).Should().BeTrue();

            // Advance past both the TTL and the 5-minute cleanup interval so the next access sweeps it out.
            clock.Advance(TimeSpan.FromMinutes(6));
            (await store.ExistsAsync(id)).Should().BeFalse("the expired entry is swept on the next access");
        }

        [Fact]
        public async Task InMemoryStore_ExistsAsync_ReturnsFalseForNew()
        {
            var store = new InMemoryDeduplicationStore();
            var result = await store.ExistsAsync(Guid.NewGuid());
            result.Should().BeFalse();
        }

        [Fact]
        public async Task InMemoryStore_ExistsAsync_ReturnsTrueAfterMark()
        {
            var store = new InMemoryDeduplicationStore();
            var id = Guid.NewGuid();

            await store.MarkProcessedAsync(id);
            var result = await store.ExistsAsync(id);

            result.Should().BeTrue();
        }

        [Fact]
        public async Task DeduplicationBehavior_SkipsDuplicate()
        {
            var store = new InMemoryDeduplicationStore();
            var behavior = new DeduplicationBehavior(store);
            var handler = new OrderPlacedHandler();

            using var bus = new InProcessEventBus(behaviors: [behavior]);
            bus.Subscribe(handler);

            var evt = new OrderPlaced(Guid.NewGuid(), 1m);

            await bus.PublishAsync(evt);
            await bus.PublishAsync(evt); // Same EventId — duplicate

            handler.ReceivedEvents.Should().ContainSingle();
        }

        [Fact]
        public async Task DeduplicationBehavior_AllowsDifferentEvents()
        {
            var store = new InMemoryDeduplicationStore();
            var behavior = new DeduplicationBehavior(store);
            var handler = new OrderPlacedHandler();

            using var bus = new InProcessEventBus(behaviors: [behavior]);
            bus.Subscribe(handler);

            await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 1m));
            await bus.PublishAsync(new OrderPlaced(Guid.NewGuid(), 2m)); // Different EventId

            handler.ReceivedEvents.Should().HaveCount(2);
        }
    }
}
