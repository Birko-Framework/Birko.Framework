using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Birko.EventBus.Local;
using Birko.EventBus.Outbox;
using Birko.EventBus.Outbox.Publishing;
using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.Tests.Outbox
{
    /// <summary>
    /// CR-L259: the background loop used to run CleanupAsync on every poll (every PollingInterval, default
    /// 5s) — a full retention scan/delete far more often than needed. CleanupIfDueAsync now throttles the
    /// prune to OutboxOptions.CleanupInterval. These pin the throttling with a controllable clock.
    /// </summary>
    public class OutboxProcessorCleanupCadenceTests
    {
        private sealed class CleanupCountingStore : IOutboxStore
        {
            public int CleanupCalls { get; private set; }

            public Task SaveAsync(OutboxEntry entry, CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task<IReadOnlyList<OutboxEntry>> GetPendingAsync(int batchSize, CancellationToken cancellationToken = default)
                => Task.FromResult<IReadOnlyList<OutboxEntry>>(Array.Empty<OutboxEntry>());

            public Task MarkPublishedAsync(Guid entryId, CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task MarkFailedAsync(Guid entryId, string error, int maxAttempts, CancellationToken cancellationToken = default) => Task.CompletedTask;

            public Task CleanupAsync(DateTime cutoffDate, CancellationToken cancellationToken = default)
            {
                CleanupCalls++;
                return Task.CompletedTask;
            }
        }

        private static OutboxProcessor NewProcessor(CleanupCountingStore store, IDateTimeProvider clock)
        {
            var bus = new InProcessEventBus();
            return new OutboxProcessor(store, bus, new OutboxOptions { CleanupInterval = TimeSpan.FromHours(1) }, clock: clock);
        }

        [Fact]
        public async Task CleanupIfDueAsync_FirstCall_RunsCleanup()
        {
            var store = new CleanupCountingStore();
            var processor = NewProcessor(store, new TestDateTimeProvider());

            var ran = await processor.CleanupIfDueAsync();

            ran.Should().BeTrue();
            store.CleanupCalls.Should().Be(1);
        }

        [Fact]
        public async Task CleanupIfDueAsync_WithinInterval_SkipsCleanup()
        {
            var store = new CleanupCountingStore();
            var clock = new TestDateTimeProvider();
            var processor = NewProcessor(store, clock);

            await processor.CleanupIfDueAsync(); // first: runs
            clock.Advance(TimeSpan.FromMinutes(30));
            var ran = await processor.CleanupIfDueAsync(); // within 1h: skip

            ran.Should().BeFalse();
            store.CleanupCalls.Should().Be(1);
        }

        [Fact]
        public async Task CleanupIfDueAsync_AfterInterval_RunsAgain()
        {
            var store = new CleanupCountingStore();
            var clock = new TestDateTimeProvider();
            var processor = NewProcessor(store, clock);

            await processor.CleanupIfDueAsync();          // runs (t0)
            clock.Advance(TimeSpan.FromMinutes(30));
            await processor.CleanupIfDueAsync();          // skip
            clock.Advance(TimeSpan.FromMinutes(31));      // 61 min since last run
            var ran = await processor.CleanupIfDueAsync(); // runs again

            ran.Should().BeTrue();
            store.CleanupCalls.Should().Be(2);
        }

        [Fact]
        public async Task CleanupIfDueAsync_ManyPollsWithinInterval_CleansUpOnce()
        {
            // Simulates the background loop polling every 5s (default PollingInterval): 20 polls span 100s,
            // well under the 1h CleanupInterval, so cleanup must run exactly once — not per poll (CR-L259).
            var store = new CleanupCountingStore();
            var clock = new TestDateTimeProvider();
            var processor = NewProcessor(store, clock);

            for (var i = 0; i < 20; i++)
            {
                await processor.CleanupIfDueAsync();
                clock.Advance(TimeSpan.FromSeconds(5));
            }

            store.CleanupCalls.Should().Be(1);
        }
    }
}
