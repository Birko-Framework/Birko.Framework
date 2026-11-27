using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Tests.Shared.StoreContract;

/// <summary>An enum stored as its integer, set by an update and used as a key.</summary>
public enum BulkBindStage { Draft = 0, Live = 2 }

/// <summary>The row TASK-1007's update half writes: a nullable DateTime and an enum.</summary>
[Table("T1007Rows")]
public class BulkBindRow : AbstractModel
{
    public int Seq { get; set; }

    public DateTime? Stamp { get; set; }

    public BulkBindStage Stage { get; set; }
}

/// <summary>A composite key holding a DateTime and an enum, so a delete must match on both.</summary>
[Table("T1007Keyed")]
public class BulkBindKeyed : AbstractModel
{
    public int Seq { get; set; }

    [PrimaryField]
    public DateTime Stamp { get; set; }

    [PrimaryField]
    public BulkBindStage Stage { get; set; }
}

/// <summary>
/// TASK-1007 — a collection write (the connector's native <c>BulkUpdate</c> / <c>BulkDelete</c>) binds a value as the
/// one-entity write binds it, through <c>AddParameter</c>, so it stores what a single update stores and matches the
/// rows a single delete matches. SQL Server's bulk paths built placeholders and assigned <c>Value</c> per row, which
/// skipped <c>ConvertParameterValue</c> / <c>CreateParameter</c>: a written DateTime lost precision and a DateTime key
/// matched no row (TASK-854). MySQL, PostgreSQL and SQLite kept that shape.
/// <para>Shared source, linked into each SQL provider's test project, which supplies fresh, empty stores.</para>
/// </summary>
public abstract class BulkBindingContract
{
    protected virtual bool Ready() => true;

    protected abstract IAsyncBulkStore<BulkBindRow> NewAsyncRowStore();
    protected abstract IBulkStore<BulkBindRow> NewSyncRowStore();
    protected abstract IAsyncBulkStore<BulkBindKeyed> NewAsyncKeyedStore();
    protected abstract IBulkStore<BulkBindKeyed> NewSyncKeyedStore();

    /// <summary>Seven fractional digits and a UTC kind: the precision and the kind both have to survive the binding.</summary>
    protected static readonly DateTime Precise = new DateTime(2026, 10, 17, 12, 34, 56, DateTimeKind.Utc).AddTicks(1234567);

    private static List<BulkBindRow> Rows()
        => Enumerable.Range(1, 3).Select(i => new BulkBindRow { Guid = Guid.NewGuid(), Seq = i, Stage = BulkBindStage.Draft }).ToList();

    private static List<BulkBindKeyed> Keyed()
        => Enumerable.Range(1, 4).Select(i => new BulkBindKeyed
        {
            Guid = Guid.NewGuid(), Seq = i, Stamp = Precise.AddSeconds(i), Stage = BulkBindStage.Live,
        }).ToList();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_bulk_update_stores_what_a_single_update_stores(bool async)
    {
        if (!Ready()) return;
        var rows = Rows();
        void Change()
        {
            foreach (var row in rows)
            {
                row.Stamp = Precise;
                row.Stage = BulkBindStage.Live;
            }
        }

        List<BulkBindRow> stored;
        if (async)
        {
            var store = NewAsyncRowStore();
            foreach (var row in rows) await store.CreateAsync(row);
            Change();
            await store.UpdateAsync(rows[0]);
            await store.UpdateAsync(rows.Skip(1).ToList());
            stored = (await store.ReadAsync(x => x.Seq > 0, null, null, null)).OrderBy(x => x.Seq).ToList();
        }
        else
        {
            var store = NewSyncRowStore();
            foreach (var row in rows) store.Create(row);
            Change();
            store.Update(rows[0]);
            store.Update(rows.Skip(1).ToList());
            stored = store.Read(x => x.Seq > 0, null, null, null).OrderBy(x => x.Seq).ToList();
        }

        stored.Should().HaveCount(3);
        stored[0].Stamp.Should().NotBeNull("the single update wrote it");
        stored[1].Stamp.Should().Be(stored[0].Stamp, "a bulk update stores the value a single update stores");
        stored[2].Stamp.Should().Be(stored[0].Stamp, "a bulk update stores the value a single update stores");
        stored.Select(x => x.Stage).Should().AllBeEquivalentTo(BulkBindStage.Live);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_bulk_delete_matches_a_DateTime_and_enum_key_as_a_single_delete_does(bool async)
    {
        if (!Ready()) return;
        var rows = Keyed();
        List<int> left;
        if (async)
        {
            var store = NewAsyncKeyedStore();
            foreach (var row in rows) await store.CreateAsync(row);
            await store.DeleteAsync(rows[0]);
            await store.DeleteAsync(new List<BulkBindKeyed> { rows[1], rows[2] });
            left = (await store.ReadAsync(x => x.Seq > 0, null, null, null)).Select(x => x.Seq).OrderBy(x => x).ToList();
        }
        else
        {
            var store = NewSyncKeyedStore();
            foreach (var row in rows) store.Create(row);
            store.Delete(rows[0]);
            store.Delete(new List<BulkBindKeyed> { rows[1], rows[2] });
            left = store.Read(x => x.Seq > 0, null, null, null).Select(x => x.Seq).OrderBy(x => x).ToList();
        }

        left.Should().Equal(new[] { 4 }, "the single delete removed row 1 and the bulk delete rows 2 and 3, by the same key");
    }
}
