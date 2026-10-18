using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.EventBus;
using Birko.EventBus.Outbox;
using Birko.EventBus.Outbox.Publishing;
using Birko.EventBus.Outbox.Stores;
using Birko.MessageQueue.Serialization;
using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.Outbox.Tests;

/// <summary>
/// TASK-174 — the outbox: SH-L119 (the publish method was re-resolved per entry), SH-L120 (Subscribe after Dispose
/// reached the disposed inner bus), SH-L122 (state read and written outside the claim lock) and SH-L123 (store and
/// entry timestamps ignored the injected clock).
/// </summary>
public class Task174OutboxTests
{
    private sealed class TestEvent : IEvent
    {
        public Guid EventGuid { get; set; } = Guid.NewGuid();
        public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
        public string Source { get; set; } = "test";
    }

    private sealed class CountingBus : IEventBus
    {
        public int Published;
        public bool Disposed;
        public Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default) where TEvent : IEvent
        {
            Interlocked.Increment(ref Published);
            return Task.CompletedTask;
        }
        public IEventSubscription Subscribe<TEvent>(IEventHandler<TEvent> handler) where TEvent : IEvent
            => Disposed ? throw new ObjectDisposedException("inner") : throw new NotSupportedException();
        public void Dispose() => Disposed = true;
    }

    private sealed class Handler : IEventHandler<TestEvent>
    {
        public Task HandleAsync(TestEvent @event, EventContext context, CancellationToken ct = default) => Task.CompletedTask;
    }

    [Fact]
    public void SH_L120_subscribe_after_dispose_is_refused_by_the_outbox_bus_itself()
    {
        var bus = new OutboxEventBus(new CountingBus(), new InMemoryOutboxStore());
        bus.Dispose();
        bus.Invoking(b => b.Subscribe(new Handler())).Should().Throw<ObjectDisposedException>()
            .Which.ObjectName.Should().Contain(nameof(OutboxEventBus));
    }

    [Fact]
    public async Task SH_L123_the_injected_clock_stamps_entries_and_drives_claims()
    {
        var clock = new TestDateTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var store = new InMemoryOutboxStore(staleClaimTimeout: TimeSpan.FromMinutes(5), clock: clock);
        var bus = new OutboxEventBus(new CountingBus(), store, clock: clock);

        await bus.PublishAsync(new TestEvent());
        var entry = store.GetAll().Single();
        entry.CreatedAt.Should().Be(clock.UtcNow, "the entry is stamped by the outbox's clock");

        (await store.GetPendingAsync(10)).Should().ContainSingle();
        entry.ClaimedAt.Should().Be(clock.UtcNow);

        clock.Advance(TimeSpan.FromMinutes(1));
        (await store.GetPendingAsync(10)).Should().BeEmpty("the claim is one minute old by the store's clock");
        clock.Advance(TimeSpan.FromMinutes(5));
        (await store.GetPendingAsync(10)).Should().ContainSingle("past the stale timeout by the same clock, it is reclaimed");

        await store.MarkPublishedAsync(entry.Guid);
        entry.PublishedAt.Should().Be(clock.UtcNow);
    }

    [Fact]
    public async Task SH_L119_SH_L122_a_batch_publishes_every_entry_and_concurrent_cleanup_keeps_claims()
    {
        // Pin: the cached publish method and the locked store must not change what a batch does.
        var store = new InMemoryOutboxStore();
        var serializer = new JsonMessageSerializer();
        var inner = new CountingBus();
        var processor = new OutboxProcessor(store, inner, new OutboxOptions { BatchSize = 50 }, serializer);
        for (var i = 0; i < 40; i++)
            await store.SaveAsync(new OutboxEntry { EventType = typeof(TestEvent).AssemblyQualifiedName!, Payload = serializer.Serialize(new TestEvent()) });

        var cleanup = Task.Run(async () => { for (var i = 0; i < 50; i++) await store.CleanupAsync(DateTime.UtcNow.AddDays(1)); });
        await processor.ProcessBatchAsync();
        await cleanup;

        inner.Published.Should().Be(40);
    }
}
