using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Exceptions;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// SH-M028 (TASK-629). A bulk collection write outside any boundary committed item by item, so a failure at item k left
/// items 0..k-1 written. Measured by rows on a real database, because the defect is what is left behind.
/// </summary>
public class AtomicBulkWriteTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-atomic-{Guid.NewGuid():N}");
    private int _db;

    public AtomicBulkWriteTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        SqlitePool.ClearForDirectory(_root, 2);
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Table("Atomic629")]
    public class Row : AbstractModel
    {
        public string? Name { get; set; }
    }

    private SqLiteSettings Settings() => new(_root, $"a{++_db}.db");

    private DataBaseBulkStore<SqLiteConnector, Row> Store(SqLiteSettings settings)
    {
        var store = new DataBaseBulkStore<SqLiteConnector, Row>();
        store.SetSettings(settings);
        return store;
    }

    private AsyncDataBaseBulkStore<SqLiteConnector, Row> AsyncStore(SqLiteSettings settings)
    {
        var store = new AsyncDataBaseBulkStore<SqLiteConnector, Row>();
        store.SetSettings(settings);
        return store;
    }

    [Fact]
    public void A_bulk_create_that_fails_part_way_leaves_nothing_written()
    {
        var store = Store(Settings());
        var stored = new Row { Name = "stored" };
        store.Create(stored);

        var batch = new[] { new Row { Name = "first" }, new Row { Name = "second" }, new Row { Guid = stored.Guid, Name = "duplicate" } };
        var act = () => store.Create(batch);

        act.Should().Throw<StoreException>();
        store.Read(x => x.Name != null, null, null, null).Select(r => r.Name).Should().Equal("stored");
    }

    [Fact]
    public async Task An_async_bulk_create_that_fails_part_way_leaves_nothing_written()
    {
        var store = AsyncStore(Settings());
        var stored = new Row { Name = "stored" };
        await store.CreateAsync(stored);

        var batch = new[] { new Row { Name = "first" }, new Row { Name = "second" }, new Row { Guid = stored.Guid, Name = "duplicate" } };
        var act = () => store.CreateAsync(batch);

        await act.Should().ThrowAsync<StoreException>();
        (await store.ReadAsync(x => x.Name != null, null, null, null)).Select(r => r.Name).Should().Equal("stored");
    }

    [Fact]
    public async Task A_bulk_delete_that_is_cancelled_part_way_leaves_every_row()
    {
        var store = AsyncStore(Settings());
        for (var i = 0; i < 5; i++) await store.CreateAsync(new Row { Name = "r" + i });
        var rows = (await store.ReadAsync(x => x.Name != null, null, null, null)).ToList();

        using var cts = new CancellationTokenSource();
        var cancelling = rows.Select((r, i) => { if (i == 3) cts.Cancel(); return r; });
        var act = () => store.DeleteAsync(cancelling, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        (await store.ReadAsync(x => x.Name != null, null, null, null)).Should().HaveCount(5);
    }

    /// <summary>TASK-555 is kept: a collection update writes every stored item, then reports the missing ones.</summary>
    [Fact]
    public void A_bulk_update_with_a_missing_item_still_writes_the_stored_ones()
    {
        var store = Store(Settings());
        var a = new Row { Name = "a" };
        store.Create(a);
        a.Name = "a2";

        var act = () => store.Update(new[] { a, new Row { Guid = Guid.NewGuid(), Name = "missing" } });

        act.Should().Throw<EntityNotFoundException>();
        store.ReadFirst(x => x.Guid == a.Guid)!.Name.Should().Be("a2");
    }

    [Fact]
    public void A_successful_bulk_create_commits_every_row()
    {
        var store = Store(Settings());
        store.Create(Enumerable.Range(0, 4).Select(i => new Row { Name = "r" + i }).ToList());
        store.Read(x => x.Name != null, null, null, null).Should().HaveCount(4);
    }
}
