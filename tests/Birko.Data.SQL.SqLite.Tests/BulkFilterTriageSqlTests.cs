using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>TASK-170 on the SQL bulk stores, against a real SQLite database.</summary>
public class BulkFilterTriageSqlTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-bf170-{Guid.NewGuid():N}");
    private int _db;

    public BulkFilterTriageSqlTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        SqlitePool.ClearForDirectory(_root, 2);
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Table("Bf170")]
    public class Row : AbstractModel
    {
        public int A { get; set; }
        public string? Name { get; set; }
        [IgnoreField] public int Scratch { get; set; }
    }

    private DataBaseBulkStore<SqLiteConnector, Row> Store()
    {
        var store = new DataBaseBulkStore<SqLiteConnector, Row>();
        store.SetSettings(new SqLiteSettings(_root, $"b{++_db}.db"));
        return store;
    }

    private AsyncDataBaseBulkStore<SqLiteConnector, Row> AsyncStore()
    {
        var store = new AsyncDataBaseBulkStore<SqLiteConnector, Row>();
        store.SetSettings(new SqLiteSettings(_root, $"b{++_db}.db"));
        return store;
    }

    /// <summary>SH-M031: a store with no settings reported every delete, update and read as done (measured).</summary>
    [Fact]
    public async Task An_unconfigured_store_refuses_instead_of_reporting_success()
    {
        var sync = new DataBaseBulkStore<SqLiteConnector, Row>();
        ((Action)(() => sync.Delete(x => x.A == 1))).Should().Throw<InvalidOperationException>().WithMessage("*SetSettings*");
        ((Action)(() => sync.Update(x => x.A == 1, new PropertyUpdate<Row>().Set(x => x.A, 2)))).Should().Throw<InvalidOperationException>();
        ((Action)(() => sync.Read(x => x.A == 1, null, null, null).ToList())).Should().Throw<InvalidOperationException>();

        var async = new AsyncDataBaseBulkStore<SqLiteConnector, Row>();
        await ((Func<Task>)(() => async.DeleteAsync(x => x.A == 1))).Should().ThrowAsync<InvalidOperationException>();
        await ((Func<Task>)(async () => (await async.ReadAsync(x => x.A == 1, null, null, null)).ToList())).Should().ThrowAsync<InvalidOperationException>();
    }

    /// <summary>SH-M025: the sync read handed out the connector's live iterator, connection and reader still open.</summary>
    [Fact]
    public void A_sync_read_is_materialised_so_a_write_inside_the_loop_runs_on_its_own()
    {
        var store = Store();
        store.Create(new Row { A = 1 });
        store.Create(new Row { A = 2 });

        var rows = store.Read(x => x.A > 0, null, null, null);
        rows.Should().BeAssignableTo<ICollection<Row>>();
        foreach (var row in rows)
        {
            row.A += 10;
            store.Update(row);
        }
        store.Read(x => x.A > 10, null, null, null).Count().Should().Be(2);
    }

    /// <summary>SH-M027: Set on an [IgnoreField] property threw "Sequence contains no elements" from inside the store.</summary>
    [Fact]
    public void Set_of_an_unmapped_property_names_it()
    {
        var store = Store();
        store.Create(new Row { A = 1 });
        var act = () => store.Update(x => x.A == 1, new PropertyUpdate<Row>().Set(x => x.Scratch, 5));
        act.Should().Throw<ArgumentException>().WithMessage("*Scratch*not a mapped column*");
    }

    /// <summary>SH-M030: two Sets of one property threw on the duplicate parameter; the portable path is last-wins.</summary>
    [Fact]
    public async Task Two_sets_of_one_property_are_last_wins_as_on_the_portable_path()
    {
        var store = Store();
        store.Create(new Row { A = 1 });
        store.Update(x => x.A == 1, new PropertyUpdate<Row>().Set(x => x.Name, "first").Set(x => x.Name, "last"));
        store.ReadFirst(x => x.A == 1)!.Name.Should().Be("last");

        var asyncStore = AsyncStore();
        await asyncStore.CreateAsync(new Row { A = 1 });
        await asyncStore.UpdateAsync(x => x.A == 1, new PropertyUpdate<Row>().Set(x => x.Name, "first").Set(x => x.Name, "last"));
        (await asyncStore.ReadFirstAsync(x => x.A == 1))!.Name.Should().Be("last");
    }

    /// <summary>SH-M434 on the SQL store's own loop: a row deleted after the read is skipped; the rest are written.</summary>
    [Fact]
    public void A_row_deleted_during_a_filtered_update_is_skipped_and_the_rest_are_written()
    {
        var store = Store();
        for (var i = 1; i <= 3; i++) store.Create(new Row { A = i });
        var deleted = false;

        store.Update(x => x.A > 0, row =>
        {
            if (!deleted) { deleted = true; store.Delete(store.ReadFirst(x => x.Guid != row.Guid)!); }
            row.A = 9;
        });

        store.Read(x => x.A > 0, null, null, null).Select(x => x.A).Should().Equal(9, 9);
    }
}
