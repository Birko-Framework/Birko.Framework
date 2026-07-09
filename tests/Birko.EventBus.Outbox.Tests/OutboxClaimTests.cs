using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.EventBus.Outbox;
using Birko.EventBus.Outbox.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.Outbox.Tests;

/// <summary>
/// CR-H116: OutboxStatus.Publishing ("locked by processor") was never set — GetPendingAsync left rows
/// Pending, so two processors could read and publish the same entries (duplicates). GetPendingAsync
/// now atomically claims the batch (Pending → Publishing) and reclaims stale claims.
/// </summary>
public class OutboxClaimTests
{
    private static OutboxEntry NewEntry() => new() { EventType = "x", Payload = "{}" };

    [Fact]
    public async Task GetPending_ClaimsBatch_SoASecondCallDoesNotReturnTheSameEntries()
    {
        var store = new InMemoryOutboxStore();
        await store.SaveAsync(NewEntry());
        await store.SaveAsync(NewEntry());

        var first = await store.GetPendingAsync(10);
        var second = await store.GetPendingAsync(10);

        first.Should().HaveCount(2);
        second.Should().BeEmpty("the first call claimed both entries (Publishing), so they are not re-returned");
    }

    [Fact]
    public async Task GetPending_TransitionsEntriesToPublishing()
    {
        var store = new InMemoryOutboxStore();
        var entry = NewEntry();
        await store.SaveAsync(entry);

        await store.GetPendingAsync(10);

        entry.Status.Should().Be(OutboxStatus.Publishing);
        entry.ClaimedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task StaleClaim_IsReclaimed_OnNextGetPending()
    {
        // Zero timeout => any prior claim is immediately considered stale and reclaimed.
        var store = new InMemoryOutboxStore(staleClaimTimeout: TimeSpan.Zero);
        var entry = NewEntry();
        await store.SaveAsync(entry);

        (await store.GetPendingAsync(10)).Should().HaveCount(1);   // claimed
        (await store.GetPendingAsync(10)).Should().HaveCount(1, "a stale claim is reclaimed and re-returned");
    }

    [Fact]
    public async Task ConcurrentGetPending_NeverClaimsTheSameEntryTwice()
    {
        var store = new InMemoryOutboxStore();
        for (int i = 0; i < 200; i++) await store.SaveAsync(NewEntry());

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => Task.Run(() => store.GetPendingAsync(50))));

        var allIds = results.SelectMany(r => r.Select(e => e.Id)).ToList();
        allIds.Should().OnlyHaveUniqueItems("no entry may be claimed by two concurrent processors");
    }
}
