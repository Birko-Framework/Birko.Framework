using System;
using System.Linq;
using System.Threading.Tasks;
using Birko.EventBus.Outbox;
using Birko.EventBus.Outbox.Stores;
using Birko.Time;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.Outbox.Tests;

/// <summary>
/// TASK-710 on the in-memory store: a mark applies only while the claim it was handed out under is
/// current, and a reclaim counts against the caller's attempt cap.
/// </summary>
public class Task710InMemoryClaimFenceTests
{
    private static OutboxEntry Entry() => new() { EventType = "x", Payload = "{}" };

    private static (InMemoryOutboxStore Store, TestDateTimeProvider Clock) NewStore()
    {
        var clock = new TestDateTimeProvider(new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc));
        return (new InMemoryOutboxStore(TimeSpan.FromMinutes(5), clock), clock);
    }

    [Fact]
    public async Task A_reclaimed_processors_marks_change_nothing()
    {
        var (store, clock) = NewStore();
        var entry = Entry();
        await store.SaveAsync(entry);
        var first = (await store.GetPendingAsync(10)).Single();
        clock.Advance(TimeSpan.FromMinutes(6));
        var second = (await store.GetPendingAsync(10)).Single();

        await store.MarkFailedAsync(first, "slow and failed", maxAttempts: 5);
        (await store.GetPendingAsync(10)).Should().BeEmpty("the second claim still holds it");

        await store.MarkPublishedAsync(first);
        entry.Status.Should().Be(OutboxStatus.Publishing);

        await store.MarkPublishedAsync(second);
        entry.Status.Should().Be(OutboxStatus.Published);
    }

    [Fact]
    public async Task A_reclaim_counts_against_the_callers_cap()
    {
        var (store, clock) = NewStore();
        var entry = Entry();
        await store.SaveAsync(entry);
        await store.GetPendingAsync(10, maxAttempts: 2);

        clock.Advance(TimeSpan.FromMinutes(6));
        (await store.GetPendingAsync(10, maxAttempts: 2)).Should().ContainSingle("one reclaim of two");
        clock.Advance(TimeSpan.FromMinutes(6));
        (await store.GetPendingAsync(10, maxAttempts: 2)).Should().BeEmpty("before, a reclaim was not counted and it came round forever");

        entry.Status.Should().Be(OutboxStatus.Failed);
        entry.Attempts.Should().Be(2);
        entry.LastError.Should().StartWith("Abandoned");
    }
}
