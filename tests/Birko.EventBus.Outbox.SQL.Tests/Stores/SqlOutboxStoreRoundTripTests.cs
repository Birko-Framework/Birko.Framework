using System;
using System.IO;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Stores;
using Birko.EventBus.Outbox.SQL.Models;
using FluentAssertions;
using Xunit;

namespace Birko.EventBus.Outbox.SQL.Tests.Stores;

/// <summary>
/// End-to-end coverage for <see cref="SqlOutboxStore{DB}"/> against a real SQLite file.
/// </summary>
/// <remarks>
/// <para>
/// Filed as TASK-231. Before this suite existed, <c>Birko.EventBus.Outbox.SQL</c> was registered in no
/// solution, no workspace and no build-validation aggregator, so <b>nothing in the family compiled it</b>
/// and no test exercised it. The project happened to still build, which is luck rather than a guarantee:
/// a change to <see cref="IOutboxStore"/> or to <c>Birko.Data.SQL</c> would have broken it with nothing
/// to notice.
/// </para>
/// <para>
/// A real file, not an in-memory connector object, because the whole point of this store is that entries
/// <i>outlive the process</i> — the class doc says an outbox that does not is "a queue with extra steps".
/// A test that never writes a byte to disk cannot observe that.
/// </para>
/// </remarks>
public sealed class SqlOutboxStoreRoundTripTests : IDisposable
{
    private readonly string _dir;
    private readonly SqLiteSettings _settings;

    public SqlOutboxStoreRoundTripTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "birko_outbox_sql_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _settings = new SqLiteSettings(_dir, "outbox.db");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* the temp dir is not the assertion */ }
    }

    /// <summary>
    /// Builds the store through the <b>pre-built store</b> constructor, not the settings one.
    /// </summary>
    /// <remarks>
    /// This is forced, not a preference. <c>SqlOutboxStore(SqlSettings)</c> needs
    /// <see cref="SqlSettings"/>, and <see cref="SqLiteSettings"/> does not derive from it — the settings
    /// chain hangs SQLite off <c>PasswordSettings</c> instead, because it has no host, port or user. So
    /// SQLite is the one provider whose settings constructor cannot be used, which is exactly what the
    /// store's own doc comment means by "SQLite passes one in".
    /// </remarks>
    private SqlOutboxStore<SqLiteConnector> NewStore()
    {
        var inner = new AsyncSQLiteStore<OutboxEntryModel>();
        inner.SetSettings(_settings);
        return new SqlOutboxStore<SqLiteConnector>(inner);
    }

    private static OutboxEntry Entry(string type = "OrderPlaced", string payload = "{}") => new()
    {
        EventId   = Guid.NewGuid(),
        EventType = type,
        Payload   = payload,
        Source    = "tests",
    };

    [Fact]
    public async Task A_saved_entry_comes_back_as_pending()
    {
        var store = NewStore();
        var entry = Entry();

        await store.SaveAsync(entry);
        var pending = await store.GetPendingAsync(10);

        pending.Should().HaveCount(1);
        pending[0].Id.Should().Be(entry.Id);
        pending[0].EventType.Should().Be("OrderPlaced");
        pending[0].EventId.Should().Be(entry.EventId);
    }

    [Fact]
    public async Task Entries_survive_a_new_store_instance_over_the_same_file()
    {
        var entry = Entry("Persisted");
        await NewStore().SaveAsync(entry);

        // A completely separate store object over the same settings — the closest thing to a restart
        // that a single process can stage, and the property the class doc exists for.
        var pending = await NewStore().GetPendingAsync(10);

        pending.Should().ContainSingle(e => e.Id == entry.Id && e.EventType == "Persisted");
    }

    [Fact]
    public async Task A_claimed_entry_is_not_handed_to_a_second_caller()
    {
        var store = NewStore();
        await store.SaveAsync(Entry());

        var first  = await store.GetPendingAsync(10);
        var second = await store.GetPendingAsync(10);

        first.Should().HaveCount(1, "the entry was pending");
        second.Should().BeEmpty(
            "GetPendingAsync CLAIMS what it returns — without that, two processors read the same rows " +
            "and every event is published twice");
    }

    [Fact]
    public async Task MarkPublished_removes_the_entry_from_the_pending_set()
    {
        var store = NewStore();
        var entry = Entry();
        await store.SaveAsync(entry);
        await store.GetPendingAsync(10);

        await store.MarkPublishedAsync(entry.Id);

        (await store.GetPendingAsync(10)).Should().BeEmpty();
    }

    [Fact]
    public async Task MarkFailed_below_the_attempt_ceiling_returns_the_entry_to_pending()
    {
        var store = NewStore();
        var entry = Entry();
        await store.SaveAsync(entry);
        await store.GetPendingAsync(10);

        await store.MarkFailedAsync(entry.Id, "transport down", maxAttempts: 3);

        var pending = await store.GetPendingAsync(10);
        pending.Should().ContainSingle(e => e.Id == entry.Id);
        pending[0].Attempts.Should().Be(1);
        pending[0].LastError.Should().Be("transport down");
    }

    [Fact]
    public async Task MarkFailed_at_the_attempt_ceiling_stops_returning_the_entry()
    {
        var store = NewStore();
        var entry = Entry();
        await store.SaveAsync(entry);

        for (var i = 0; i < 3; i++)
        {
            await store.GetPendingAsync(10);
            await store.MarkFailedAsync(entry.Id, "still down", maxAttempts: 3);
        }

        (await store.GetPendingAsync(10)).Should().BeEmpty(
            "an entry that exhausted its attempts must stop being retried forever");
    }

    [Fact]
    public async Task GetPending_honours_the_batch_size()
    {
        var store = NewStore();
        for (var i = 0; i < 5; i++) await store.SaveAsync(Entry($"Event{i}"));

        var batch = await store.GetPendingAsync(2);

        batch.Should().HaveCount(2);
    }

    [Fact]
    public async Task SaveAsync_rejects_a_null_entry()
    {
        var act = () => NewStore().SaveAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("entry");
    }

    [Fact]
    public async Task Cleanup_does_not_remove_an_entry_that_is_still_pending()
    {
        var store = NewStore();
        var entry = Entry();
        await store.SaveAsync(entry);

        await store.CleanupAsync(DateTime.UtcNow.AddDays(1));

        (await store.GetPendingAsync(10)).Should().ContainSingle(e => e.Id == entry.Id,
            "cleanup retires PUBLISHED history — deleting unpublished work would lose events, which is " +
            "the exact failure the outbox pattern exists to prevent");
    }

    [Fact]
    public void The_store_rejects_a_null_inner_store()
    {
        var act = () => new SqlOutboxStore<SqLiteConnector>((AsyncDataBaseBulkStore<SqLiteConnector, OutboxEntryModel>)null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("store");
    }
}
