using System;
using System.IO;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.EventBus.Outbox.SQL.Models;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.Outbox.SQL.Tests.Stores;

/// <summary>
/// TASK-710: the claim, the reclaim, the abandon and both marks are each guarded by the claim they act on.
/// A store that runs a hook just before its next conditional update lets a second processor act in the
/// gap between a claim's read and its write, which is where every one of these races lived.
/// </summary>
public sealed class Task710ClaimFenceTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(50);
    private readonly string _dir;
    private readonly SqLiteSettings _settings;

    public Task710ClaimFenceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "birko_t710_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _settings = new SqLiteSettings(_dir, "outbox.db");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* the temp dir is not the assertion */ }
    }

    private sealed class GapStore : AsyncSQLiteStore<OutboxEntryModel>
    {
        public Func<Task>? BeforeNextConditionalUpdate;

        public override async Task UpdateAsync(
            Expression<Func<OutboxEntryModel, bool>> filter, PropertyUpdate<OutboxEntryModel> updates, CancellationToken ct = default)
        {
            var hook = BeforeNextConditionalUpdate;
            BeforeNextConditionalUpdate = null;
            if (hook != null) await hook();
            await base.UpdateAsync(filter, updates, ct);
        }
    }

    private (SqlOutboxStore<SqLiteConnector> Outbox, GapStore Inner) NewStore()
    {
        var inner = new GapStore();
        inner.SetSettings(_settings);
        return (new SqlOutboxStore<SqLiteConnector>(inner, Timeout), inner);
    }

    private async Task<OutboxEntryModel> Row(Guid id)
        => (await NewStore().Inner.ReadFirstAsync(e => e.Guid == id))!;

    private static OutboxEntry Entry() => new() { EventGuid = Guid.NewGuid(), EventType = "OrderPlaced", Payload = "{}", Source = "tests" };

    private async Task<(OutboxEntry Entry, OutboxEntry Claim)> SavedAndClaimed(SqlOutboxStore<SqLiteConnector> store)
    {
        var entry = Entry();
        await store.SaveAsync(entry);
        var claimed = await store.GetPendingAsync(10);
        claimed.Should().ContainSingle();
        return (entry, claimed[0]);
    }

    [Fact]
    public async Task A_stale_entry_is_reclaimed_by_one_processor_only()
    {
        var (a, aInner) = NewStore();
        var (b, _) = NewStore();
        await SavedAndClaimed(a);
        await Task.Delay(Timeout * 3);

        var bGot = -1;
        aInner.BeforeNextConditionalUpdate = async () => bGot = (await b.GetPendingAsync(10)).Count;
        var aGot = (await a.GetPendingAsync(10)).Count;

        bGot.Should().Be(1, "B reclaimed it in the gap");
        aGot.Should().Be(0, "the row is no longer stale once B reclaimed it, so A's reclaim must not match: before, both published");
    }

    [Fact]
    public async Task A_late_pending_claim_does_not_win_over_an_attempt_made_in_between()
    {
        var (a, aInner) = NewStore();
        var (b, _) = NewStore();
        var entry = Entry();
        await a.SaveAsync(entry);

        aInner.BeforeNextConditionalUpdate = async () =>
        {
            var got = await b.GetPendingAsync(10);
            await b.MarkFailedAsync(got[0], "transport down", maxAttempts: 5);
        };
        var aGot = await a.GetPendingAsync(10);

        aGot.Should().BeEmpty("A read the entry at 0 attempts; it has 1 now");
        var row = await Row(entry.Guid);
        row.Attempts.Should().Be(1, "the late claim wound it back to 0");
        row.Status.Should().Be((int)OutboxStatus.Pending);
    }

    [Fact]
    public async Task A_reclaimed_processors_failure_does_not_return_the_entry_to_pending()
    {
        var (a, _) = NewStore();
        var (b, _) = NewStore();
        var (c, _) = NewStore();
        var (entry, aClaim) = await SavedAndClaimed(a);
        await Task.Delay(Timeout * 3);
        var bClaim = await b.GetPendingAsync(10);
        bClaim.Should().ContainSingle();

        await a.MarkFailedAsync(aClaim, "slow and failed", maxAttempts: 5);

        (await c.GetPendingAsync(10)).Should().BeEmpty("B still holds it: before, C claimed it a third time");
        var row = await Row(entry.Guid);
        row.Status.Should().Be((int)OutboxStatus.Publishing);
        row.ClaimToken.Should().Be(bClaim[0].ClaimToken!.Value);
    }

    [Fact]
    public async Task A_reclaimed_processors_failure_does_not_undo_the_new_holders_publish()
    {
        var (a, _) = NewStore();
        var (b, _) = NewStore();
        var (entry, aClaim) = await SavedAndClaimed(a);
        await Task.Delay(Timeout * 3);
        var bClaim = await b.GetPendingAsync(10);

        await b.MarkPublishedAsync(bClaim[0]);
        await a.MarkFailedAsync(aClaim, "slow and failed", maxAttempts: 5);

        (await Row(entry.Guid)).Status.Should().Be((int)OutboxStatus.Published, "before, it went back to Pending and was published again");
    }

    [Fact]
    public async Task A_reclaimed_processors_publish_leaves_the_mark_to_the_new_holder()
    {
        var (a, _) = NewStore();
        var (b, _) = NewStore();
        var (entry, aClaim) = await SavedAndClaimed(a);
        await Task.Delay(Timeout * 3);
        var bClaim = await b.GetPendingAsync(10);

        await a.MarkPublishedAsync(aClaim);
        (await Row(entry.Guid)).Status.Should().Be((int)OutboxStatus.Publishing, "B holds the claim");

        await b.MarkFailedAsync(bClaim[0], "transport down", maxAttempts: 5);
        var row = await Row(entry.Guid);
        row.Status.Should().Be((int)OutboxStatus.Pending);
        row.Attempts.Should().Be(2, "the reclaim and B's failure each count");
    }

    [Fact]
    public async Task Abandoning_an_entry_does_not_overwrite_a_reclaim_made_in_between()
    {
        var (a, aInner) = NewStore();
        var (b, _) = NewStore();
        var (entry, _) = await SavedAndClaimed(a);
        await Task.Delay(Timeout * 3);

        aInner.BeforeNextConditionalUpdate = async () => (await b.GetPendingAsync(10, maxAttempts: 10)).Should().ContainSingle();
        (await a.GetPendingAsync(10, maxAttempts: 1)).Should().BeEmpty();

        var row = await Row(entry.Guid);
        row.Status.Should().Be((int)OutboxStatus.Publishing, "B reclaimed it first; A's abandon wrote the whole row back over that");
        row.ClaimToken.Should().NotBeNull();
    }

    [Fact]
    public async Task The_reclaim_cap_is_the_one_the_caller_passes()
    {
        var (store, _) = NewStore();
        var (entry, _) = await SavedAndClaimed(store);
        await Task.Delay(Timeout * 3);

        (await store.GetPendingAsync(10, maxAttempts: 1)).Should().BeEmpty("the store was built with the default cap of 5");

        var row = await Row(entry.Guid);
        row.Status.Should().Be((int)OutboxStatus.Failed);
        row.Attempts.Should().Be(1);
        row.LastError.Should().StartWith("Abandoned");
    }

    [Fact]
    public async Task An_entry_with_no_claim_cannot_be_marked()
    {
        var (store, _) = NewStore();
        var entry = Entry();
        await store.SaveAsync(entry);

        await FluentActions.Awaiting(() => store.MarkPublishedAsync(entry)).Should().ThrowAsync<ArgumentException>();
        await FluentActions.Awaiting(() => store.MarkFailedAsync(entry, "x", 5)).Should().ThrowAsync<ArgumentException>();
        (await Row(entry.Guid)).Status.Should().Be((int)OutboxStatus.Pending);
    }
}
