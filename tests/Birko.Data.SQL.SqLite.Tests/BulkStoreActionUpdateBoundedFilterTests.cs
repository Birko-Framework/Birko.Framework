using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.SQL.Stores;
using Birko.Data.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-329 / SH-H002 — the <c>Update(filter, Action&lt;T&gt;)</c> overload on the SQL bulk stores accepted a
/// filter that <b>reduces</b> to every row and rewrote the whole table, silently, on both the sync and the
/// async store.
///
/// <para><b>Measured before the fix</b>, on this fixture's shape (3 rows, an empty <c>ids</c> list, filter
/// <c>x =&gt; !ids.Contains(x.Name)</c>): <c>thrown=NONE | overwritten=3 of 3</c> on the async store and the
/// same on the sync one. The other four filter-based destructive overloads on the same two classes —
/// <c>Update(filter, PropertyUpdate&lt;T&gt;)</c> and <c>Delete(filter)</c>, sync and async — already refused
/// it at the connector via SH-H002's <c>AddRequiredWhere</c>. So each store's <c>Delete</c> guarded beside an
/// <c>Update</c> that rewrote every row, which is precisely the split § Conventions records as the shape this
/// family keeps arriving in.</para>
///
/// <para><b>Why these two and not the other four.</b> This overload is read-then-loop: it issues a
/// <c>SELECT</c> — where an always-true predicate is perfectly legitimate — and then a per-row
/// <c>UPDATE … WHERE Guid = @g</c>, each individually bounded. No conditionless statement is ever emitted, so
/// no guard on the <i>statement</i> can see this path. The check has to be on the <b>expression</b>, which is
/// what <c>Birko.Data.Expressions.PredicateScope</c> answers and what
/// <c>Birko.Data.Expressions.BoundedFilterGuard</c> now enforces for every caller.</para>
///
/// <para><b>Root cause worth carrying:</b> TASK-215 wired <c>RequireBoundedFilter</c> into
/// <c>AbstractBulkStore</c> / <c>AbstractAsyncBulkStore</c>, and the SQL bulk stores derive from
/// <b>neither</b> — they implement <c>IBulkStore&lt;T&gt;</c> / <c>IAsyncBulkStore&lt;T&gt;</c> directly and
/// carry their own copies of the filter-based overloads (which is also why they have their own private
/// <c>RequireFilter</c>). "Wired into the base" says nothing until you check which bases the concrete types
/// actually derive from.</para>
///
/// <para><b>Assertions are counted rows, never the absence of an exception.</b> SQLite accepts a whole-table
/// rewrite happily, so only the surviving values distinguish a refused update from a performed one.</para>
/// </summary>
public class BulkStoreActionUpdateBoundedFilterTests : IDisposable
{
    private readonly string _root;

    public BulkStoreActionUpdateBoundedFilterTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-boundedaction-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    public class Row : AbstractModel
    {
        public string? Name { get; set; }
        public int Amount { get; set; }
    }

    private sealed class RowMapping : IModelMapping<Row>
    {
        public void Configure(ModelMap<Row> map)
        {
            map.ToTable("BoundedActionRows").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
            map.Property(x => x.Amount);
        }
    }

    private static void Register()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new RowMapping());
        registry.ApplyToDatabase();
    }

    /// <summary>An EMPTY collection: <c>!Empty.Contains(x)</c> is true of every entity, by C# semantics.</summary>
    private static readonly List<string> Empty = new();

    private async Task<AsyncSQLiteStore<Row>> SeededAsync()
    {
        Register();
        var store = new AsyncSQLiteStore<Row>();
        store.SetSettings(new SqLiteSettings(_root, $"async-{Guid.NewGuid():N}.db"));
        for (var i = 1; i <= 3; i++)
        {
            await store.CreateAsync(new Row { Guid = Guid.NewGuid(), Name = $"r{i}", Amount = i * 10 });
        }

        (await store.ReadAsync(CancellationToken.None)).Should().HaveCount(3, "seed");
        return store;
    }

    private SQLiteStore<Row> SeededSync()
    {
        Register();
        var store = new SQLiteStore<Row>();
        store.SetSettings(new SqLiteSettings(_root, $"sync-{Guid.NewGuid():N}.db"));
        for (var i = 1; i <= 3; i++)
        {
            store.Create(new Row { Guid = Guid.NewGuid(), Name = $"r{i}", Amount = i * 10 });
        }

        SyncRows(store).Should().HaveCount(3, "seed");
        return store;
    }

    // The bulk Read(filter, orderBy, limit, offset) overload hides the single-result Read(filter), so this is
    // the collection — see § Conventions. Named rather than inlined so no assertion below can accidentally
    // read a single entity and pass vacuously.
    private static List<Row> SyncRows(SQLiteStore<Row> store) => store.Read(null, null, null, null).ToList();

    private static async Task<List<Row>> AsyncRows(AsyncSQLiteStore<Row> store)
        => (await store.ReadAsync(CancellationToken.None)).ToList();

    // ── the defect: Update(filter, Action<T>) ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Async_UpdateWithAnAction_AndAFilterThatReducesToEveryRow_IsRefusedAndRewritesNothing()
    {
        var store = await SeededAsync();

        var act = async () => await store.UpdateAsync(
            x => !Empty.Contains(x.Name!), r => r.Name = "WIPED", CancellationToken.None);

        await act.Should().ThrowAsync<Birko.Data.Exceptions.WholeTableWriteException>();

        var rows = await AsyncRows(store);
        rows.Should().HaveCount(3);
        rows.Should().NotContain(r => r.Name == "WIPED",
            "before the fix this rewrote 3 of 3 rows and threw nothing");
        rows.Select(r => r.Name).Should().BeEquivalentTo(new[] { "r1", "r2", "r3" });
    }

    [Fact]
    public void Sync_UpdateWithAnAction_AndAFilterThatReducesToEveryRow_IsRefusedAndRewritesNothing()
    {
        var store = SeededSync();

        var act = () => store.Update(x => !Empty.Contains(x.Name!), r => r.Name = "WIPED");

        act.Should().Throw<Birko.Data.Exceptions.WholeTableWriteException>();

        var rows = SyncRows(store);
        rows.Should().HaveCount(3);
        rows.Should().NotContain(r => r.Name == "WIPED");
        rows.Select(r => r.Name).Should().BeEquivalentTo(new[] { "r1", "r2", "r3" });
    }

    [Fact]
    public async Task Async_UpdateWithAnAction_AndACollapsedOrChain_IsAlsoRefused()
    {
        // `A OR TRUE` is TRUE, so this covers every row just as surely as the sole term does — and it is the
        // shape a partial fix (one that only recognised a lone always-true predicate) would let through.
        var store = await SeededAsync();

        var act = async () => await store.UpdateAsync(
            x => x.Amount > 20 || !Empty.Contains(x.Name!), r => r.Name = "WIPED", CancellationToken.None);

        await act.Should().ThrowAsync<Birko.Data.Exceptions.WholeTableWriteException>();
        (await AsyncRows(store)).Should().NotContain(r => r.Name == "WIPED");
    }

    [Fact]
    public void Sync_UpdateWithAnAction_AndACollapsedOrChain_IsAlsoRefused()
    {
        var store = SeededSync();

        var act = () => store.Update(x => x.Amount > 20 || !Empty.Contains(x.Name!), r => r.Name = "WIPED");

        act.Should().Throw<Birko.Data.Exceptions.WholeTableWriteException>();
        SyncRows(store).Should().NotContain(r => r.Name == "WIPED");
    }

    // ── the refusal names the door THIS caller has (§ SH-H037 / TASK-215) ───────────────────────────────

    [Fact]
    public async Task Async_TheRefusalNamesTheAsyncDoor_NotOneThatWouldNotCompile()
    {
        var store = await SeededAsync();

        var act = async () => await store.UpdateAsync(
            x => !Empty.Contains(x.Name!), r => r.Name = "WIPED", CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<Birko.Data.Exceptions.WholeTableWriteException>();
        thrown.Which.Message.Should().Contain("UpdateAllAsync(updates)",
            "an async store has no UpdateAll(updates) — a refusal pointing at one is an opt-out that does "
            + "not compile, which is a wall wearing a door's label");
        thrown.Which.Operation.Should().Be("update");
        thrown.Which.TableName.Should().Be(nameof(Row));
    }

    [Fact]
    public void Sync_TheRefusalNamesTheSyncDoor()
    {
        var store = SeededSync();

        var act = () => store.Update(x => !Empty.Contains(x.Name!), r => r.Name = "WIPED");

        var thrown = act.Should().Throw<Birko.Data.Exceptions.WholeTableWriteException>();
        thrown.Which.Message.Should().Contain("UpdateAll(updates)");
        thrown.Which.Message.Should().NotContain("UpdateAllAsync",
            "the sync store's door is the synchronous spelling; naming the async one sends the reader to a "
            + "method their store does not have");
    }

    // ── the doors that must stay open: without these the fix is a blanket refusal ───────────────────────

    [Fact]
    public async Task Async_UpdateWithAnAction_AndAnExplicitTruePredicate_StillRewritesEveryRow()
    {
        // `x => true` is the documented all-rows synonym (§ Conventions), and it is the only escape hatch the
        // Action<T> overload has — neither store declares an UpdateAll(Action<T>). So this is not a nicety:
        // § SH-H037 makes the opt-out part of the fix, and it is executed here rather than reasoned about.
        var store = await SeededAsync();

        await store.UpdateAsync(x => true, r => r.Name = "ALL", CancellationToken.None);

        var rows = await AsyncRows(store);
        rows.Should().HaveCount(3);
        rows.Should().OnlyContain(r => r.Name == "ALL");
    }

    [Fact]
    public void Sync_UpdateWithAnAction_AndAnExplicitTruePredicate_StillRewritesEveryRow()
    {
        var store = SeededSync();

        store.Update(x => true, r => r.Name = "ALL");

        var rows = SyncRows(store);
        rows.Should().HaveCount(3);
        rows.Should().OnlyContain(r => r.Name == "ALL");
    }

    [Fact]
    public async Task Async_UpdateWithAnAction_AndABoundedFilter_StillRewritesOnlyItsOwnRows()
    {
        var store = await SeededAsync();

        await store.UpdateAsync(x => x.Amount > 20, r => r.Name = "HIT", CancellationToken.None);

        var rows = await AsyncRows(store);
        rows.Should().HaveCount(3);
        rows.Where(r => r.Name == "HIT").Should().HaveCount(1);
        rows.Single(r => r.Name == "HIT").Amount.Should().Be(30);
    }

    [Fact]
    public void Sync_UpdateWithAnAction_AndABoundedFilter_StillRewritesOnlyItsOwnRows()
    {
        var store = SeededSync();

        store.Update(x => x.Amount > 20, r => r.Name = "HIT");

        var rows = SyncRows(store);
        rows.Should().HaveCount(3);
        rows.Where(r => r.Name == "HIT").Should().HaveCount(1);
        rows.Single(r => r.Name == "HIT").Amount.Should().Be(30);
    }

    [Fact]
    public async Task Async_UpdateWithAnAction_AndABoundedFilterBesideAnAlwaysTrueTerm_StillRewritesItsRows()
    {
        // `A AND TRUE` is `A`. The reduction must not make a bounded update look unbounded — a false refusal
        // breaks working code, which PredicateScope rates worse than the hole it closes.
        var store = await SeededAsync();

        await store.UpdateAsync(
            x => x.Amount > 20 && !Empty.Contains(x.Name!), r => r.Name = "HIT", CancellationToken.None);

        (await AsyncRows(store)).Where(r => r.Name == "HIT").Should().HaveCount(1);
    }

    [Fact]
    public void Sync_UpdateWithAnAction_AndANullFilter_IsStillRefusedByRequireFilter()
    {
        // Contract pin, NOT evidence for this fix: SH-M023's RequireFilter already covered null, and the new
        // guard deliberately ignores null so one mistake does not produce two different messages. It is here
        // because the two guards now sit adjacent and a later edit could collapse them.
        var store = SeededSync();

        var act = () => store.Update((Expression<Func<Row, bool>>)null!, r => r.Name = "WIPED");

        act.Should().Throw<ArgumentNullException>();
        SyncRows(store).Should().HaveCount(3).And.NotContain(r => r.Name == "WIPED");
    }

    // ── the neighbours the connector already covered: contract pins, not provers ────────────────────────

    [Fact]
    public void Sync_DeleteWithAFilterThatReducesToEveryRow_WasAlreadyRefusedAtTheConnector()
    {
        // Measured before the fix: this one already threw. It is asserted because § Conventions requires the
        // whole verb family to agree — a store whose Delete refuses beside an Update that rewrites every row
        // is the split this task closed, and the only way to notice the split reopening is to pin both ends.
        var store = SeededSync();

        var act = () => store.Delete(x => !Empty.Contains(x.Name!));

        act.Should().Throw<Birko.Data.Exceptions.WholeTableWriteException>();
        SyncRows(store).Should().HaveCount(3);
    }

    [Fact]
    public void Sync_UpdateWithAPropertyUpdate_AndAFilterThatReducesToEveryRow_WasAlreadyRefused()
    {
        var store = SeededSync();

        var act = () => store.Update(
            x => !Empty.Contains(x.Name!), new PropertyUpdate<Row>().Set(x => x.Name, "WIPED"));

        act.Should().Throw<Birko.Data.Exceptions.WholeTableWriteException>();
        SyncRows(store).Should().NotContain(r => r.Name == "WIPED");
    }

    // ── structural pin: a NEW filter-based destructive overload must be wired deliberately ──────────────

    /// <summary>
    /// The recurrence risk here is not drift, it is <b>absence</b> — this defect existed because a guard was
    /// added to one hierarchy and the SQL stores were never enumerated. So the enumeration is pinned: if a
    /// filter-based destructive overload is added to either SQL bulk store, this fails and the author has to
    /// decide about the guard rather than inherit nothing by default.
    /// </summary>
    /// <remarks>
    /// ⚠ <b>Do not satisfy a failure here by editing the expected list.</b> The point is the decision, and
    /// the decision is: wire <c>RequireBoundedFilter</c> unless the new overload provably reaches
    /// <c>AddRequiredWhere</c> with a conditionless statement — <i>measured</i>, per this task's own record
    /// that four of the six existing overloads do and two do not.
    /// </remarks>
    [Fact]
    public void TheSqlBulkStores_FilterBasedDestructiveOverloads_AreTheSixThatWereMeasured()
    {
        static string[] FilterOverloads(Type t) => t
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.Name is "Update" or "UpdateAsync" or "Delete" or "DeleteAsync")
            .Where(m => m.GetParameters().Length > 0
                     && m.GetParameters()[0].ParameterType.IsGenericType
                     && m.GetParameters()[0].ParameterType.GetGenericTypeDefinition() == typeof(Expression<>))
            .Select(m => t.Name.Split('`')[0] + "." + m.Name
                + "(" + string.Join(", ", m.GetParameters().Skip(1).Select(p => p.ParameterType.Name)) + ")")
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var found = FilterOverloads(typeof(AsyncDataBaseBulkStore<,>))
            .Concat(FilterOverloads(typeof(DataBaseBulkStore<,>)))
            .ToArray();

        found.Should().BeEquivalentTo(new[]
        {
            // async — a CancellationToken trails each of these
            "AsyncDataBaseBulkStore.DeleteAsync(CancellationToken)",
            "AsyncDataBaseBulkStore.UpdateAsync(Action`1, CancellationToken)",
            "AsyncDataBaseBulkStore.UpdateAsync(PropertyUpdate`1, CancellationToken)",
            // sync
            "DataBaseBulkStore.Delete()",
            "DataBaseBulkStore.Update(Action`1)",
            "DataBaseBulkStore.Update(PropertyUpdate`1)",
        }, "a seventh filter-based destructive overload must be wired to BoundedFilterGuard deliberately, "
         + "not silently inherit no guard the way these two did");
    }
}
