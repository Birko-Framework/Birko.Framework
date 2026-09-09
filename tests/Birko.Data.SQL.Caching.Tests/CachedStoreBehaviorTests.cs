using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.IO;
using System.Threading.Tasks;
using Birko.Caching;
using Birko.Caching.Memory;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Stores;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Caching.Tests;

/// <summary>
/// CR-H085: the SQL.Caching store surface was untested. These SQLite-backed tests cover the
/// caching behavior of CachedAsyncDataBaseBulkStore itself: cache hit (a stale result is served
/// after an out-of-band delete), write-through invalidation, and Enabled=false passthrough.
/// </summary>
public class CachedStoreBehaviorTests : IDisposable
{
    private readonly string _root;
    private readonly SqLiteSettings _settings;

    public CachedStoreBehaviorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-sqlcache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _settings = new SqLiteSettings(_root, "cache.db");

        var registry = new ModelMapRegistry();
        registry.Register(new WidgetMapping());
        registry.ApplyToDatabase();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    public class Widget : AbstractModel
    {
        public string? Name { get; set; }

        // SH-H007 needs TWO columns to observe a lost update: one the concurrent writer changes and one
        // the update action changes. With a single column the action overwrites it either way and the
        // defect is invisible — which is part of why it went unnoticed.
        public string? Body { get; set; }
    }

    private sealed class WidgetMapping : IModelMapping<Widget>
    {
        public void Configure(ModelMap<Widget> map)
        {
            map.ToTable("Widgets").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
            map.Property(x => x.Body).HasPrecision(100);
        }
    }

    private CachedAsyncDataBaseBulkStore<SqLiteConnector, Widget> NewStore(SqlCacheOptions options)
    {
        var store = new CachedAsyncDataBaseBulkStore<SqLiteConnector, Widget>(new MemoryCache(), options);
        store.SetSettings(_settings);
        return store;
    }

    private void DeleteAllRowsOutOfBand()
    {
        var connector = Birko.Data.SQL.DataBase.GetConnector<SqLiteConnector>(_settings);
        using var conn = connector.CreateConnection(connector.Settings);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Widgets";
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task Read_IsServedFromCache_ThenInvalidatedByWrite()
    {
        var store = NewStore(new SqlCacheOptions { Enabled = true });
        Expression<Func<Widget, bool>> filter = x => x.Name == "a";

        await store.CreateAsync(new[] { new Widget { Guid = Guid.NewGuid(), Name = "a" } });

        (await store.ReadAsync(filter)).Should().HaveCount(1);   // miss -> DB -> cached

        DeleteAllRowsOutOfBand();                                // bypass the cache

        (await store.ReadAsync(filter)).Should().HaveCount(1, "the result is served from cache");

        // A write through the store invalidates the table's cache entries.
        await store.CreateAsync(new[] { new Widget { Guid = Guid.NewGuid(), Name = "b" } });

        (await store.ReadAsync(filter)).Should().BeEmpty("the cache was invalidated, so the DB is re-read");
    }

    [Fact]
    public async Task Disabled_Cache_AlwaysHitsTheDatabase()
    {
        var store = NewStore(new SqlCacheOptions { Enabled = false });
        Expression<Func<Widget, bool>> filter = x => x.Name == "a";

        await store.CreateAsync(new[] { new Widget { Guid = Guid.NewGuid(), Name = "a" } });
        (await store.ReadAsync(filter)).Should().HaveCount(1);

        DeleteAllRowsOutOfBand();

        (await store.ReadAsync(filter)).Should().BeEmpty("with caching disabled every read hits the DB");
    }

    // ---- SH-H007: a read-then-write loop must not read through the cache ----
    //
    // These live in this class rather than a new one on purpose: ModelMapRegistry.ApplyToDatabase mutates
    // global table registration, and two parallel test classes each doing that is the shared-global-state
    // family TASK-276 exists for. Reusing this fixture mutates it once.

    private void SetBodyOutOfBand(string body)
    {
        var connector = Birko.Data.SQL.DataBase.GetConnector<SqLiteConnector>(_settings);
        using var conn = connector.CreateConnection(connector.Settings);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Widgets SET Body = $b";
        var p = cmd.CreateParameter();
        p.ParameterName = "$b";
        p.Value = body;
        cmd.Parameters.Add(p);
        cmd.ExecuteNonQuery();
    }

    private string? ReadBodyOutOfBand()
    {
        var connector = Birko.Data.SQL.DataBase.GetConnector<SqLiteConnector>(_settings);
        using var conn = connector.CreateConnection(connector.Settings);
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Body FROM Widgets LIMIT 1";
        var value = cmd.ExecuteScalar();
        return value == null || value is DBNull ? null : (string?)value;
    }

    [Fact]
    public async Task FilterUpdate_DoesNotRevertAConcurrentWritersColumn()
    {
        // THE defect, end to end. The base filter-Update is ReadAsync(filter) then a per-item
        // UpdateAsync, and UpdateCoreAsync writes EVERY mapped column. When that read came from the
        // cache, the loop wrote back a stale Body and silently undid the concurrent writer.
        var store = NewStore(new SqlCacheOptions { Enabled = true });
        Expression<Func<Widget, bool>> filter = x => x.Name == "a";

        await store.CreateAsync(new[] { new Widget { Guid = Guid.NewGuid(), Name = "a", Body = "original" } });

        // Populate the cache for this filter.
        (await store.ReadAsync(filter)).Should().HaveCount(1);

        // A concurrent writer changes a column this update does not touch.
        SetBodyOutOfBand("written-by-someone-else");

        // The update action only sets Name-adjacent state; it must not resurrect the stale Body.
        await store.UpdateAsync(filter, w => w.Name = "a-updated");

        ReadBodyOutOfBand().Should().Be("written-by-someone-else",
            "the loop must read the CURRENT row, not a cached snapshot — otherwise the full-row UPDATE "
          + "silently reverts whatever another writer changed");
    }

    [Fact]
    public async Task FilterUpdate_StillAppliesItsOwnChange()
    {
        // Contract pin: bypassing the cache for the read must not stop the update working.
        var store = NewStore(new SqlCacheOptions { Enabled = true });
        Expression<Func<Widget, bool>> filter = x => x.Name == "a";

        await store.CreateAsync(new[] { new Widget { Guid = Guid.NewGuid(), Name = "a", Body = "b" } });
        (await store.ReadAsync(filter)).Should().HaveCount(1);

        await store.UpdateAsync(filter, w => w.Name = "renamed");

        (await store.ReadAsync(x => x.Name == "renamed")).Should().HaveCount(1);
    }

    [Fact]
    public async Task FilterUpdate_LeavesTheCacheUsableAfterwards()
    {
        // The read bypass is flow-scoped, so it must not stick: an ordinary read after the update flow is
        // cached again. Before the AsyncLocal scope this was the risk of a plain flag — TASK-270 records
        // the same mistake leaving a guard permanently on.
        var store = NewStore(new SqlCacheOptions { Enabled = true });
        Expression<Func<Widget, bool>> filter = x => x.Name == "a";

        await store.CreateAsync(new[] { new Widget { Guid = Guid.NewGuid(), Name = "a", Body = "b" } });
        await store.UpdateAsync(filter, w => w.Body = "updated");

        (await store.ReadAsync(filter)).Should().HaveCount(1);   // repopulates the cache
        DeleteAllRowsOutOfBand();
        (await store.ReadAsync(filter)).Should().HaveCount(1,
            "caching is on again after the update flow, so this read is served from the cache");
    }

    // ---- SH-H004, through the store rather than the key builder ----

    [Fact]
    public async Task A_captured_filter_value_is_not_served_another_values_rows()
    {
        // The store-level version of the tenant leak. Both filters differ only in a CAPTURED local, which
        // is exactly the shape that used to collide; the existing tests in this class all use inline
        // literals, which is why none of them caught it.
        var store = NewStore(new SqlCacheOptions { Enabled = true });

        await store.CreateAsync(new[]
        {
            new Widget { Guid = Guid.NewGuid(), Name = "tenant-a", Body = "a" },
            new Widget { Guid = Guid.NewGuid(), Name = "tenant-b", Body = "b" },
        });

        var first = "tenant-a";
        var second = "tenant-b";
        Expression<Func<Widget, bool>> byFirst = x => x.Name == first;
        Expression<Func<Widget, bool>> bySecond = x => x.Name == second;

        var a = await store.ReadAsync(byFirst);
        a.Should().HaveCount(1).And.OnlyContain(w => w.Name == "tenant-a");

        var b = await store.ReadAsync(bySecond);
        b.Should().HaveCount(1).And.OnlyContain(w => w.Name == "tenant-b",
            "before the fix this was served the first filter's cached rows");
    }

    [Fact]
    public async Task A_set_membership_filter_reads_correctly_by_not_being_cached()
    {
        // TryDescribeFilter refuses this shape, so the store bypasses the cache entirely. The observable
        // consequence is that it behaves like an uncached read — which is the safe direction, and is
        // asserted here so the refusal is visible as behaviour rather than only as a unit-level bool.
        var store = NewStore(new SqlCacheOptions { Enabled = true });

        await store.CreateAsync(new[] { new Widget { Guid = Guid.NewGuid(), Name = "a", Body = "b" } });

        var names = new List<string> { "a" };
        Expression<Func<Widget, bool>> inSet = x => names.Contains(x.Name!);

        (await store.ReadAsync(inSet)).Should().HaveCount(1);

        DeleteAllRowsOutOfBand();

        (await store.ReadAsync(inSet)).Should().BeEmpty(
            "an unkeyable filter is not cached, so this read goes to the database and sees the deletion");
    }
    // ---- TASK-329: the decorator is the ONLY override of the guarded overload ----

    /// <summary>
    /// TASK-329 — <c>CachedAsyncDataBaseBulkStore</c> is the one class in the framework that overrides
    /// <c>UpdateAsync(filter, Action&lt;T&gt;)</c>, measured across every SQL provider. It inherits the new
    /// bounded-filter guard only because its override delegates to <c>base.UpdateAsync</c> rather than
    /// re-implementing the read-then-loop.
    /// </summary>
    /// <remarks>
    /// So this is a pin on the delegation, not on the guard: § Conventions records four times over that a
    /// funnel with overrides is not a funnel, and a later edit that inlined the loop here to save a hop
    /// would silently reopen a whole-table rewrite behind a decorator whose own tests were all green.
    /// The rows are the assertion, because SQLite performs a whole-table rewrite without complaint.
    /// </remarks>
    [Fact]
    public async Task A_filter_that_reduces_to_every_row_is_refused_THROUGH_the_caching_decorator()
    {
        var store = NewStore(new SqlCacheOptions { Enabled = true });
        for (var i = 1; i <= 3; i++)
        {
            await store.CreateAsync(new[] { new Widget { Guid = Guid.NewGuid(), Name = $"r{i}", Body = "keep" } });
        }

        var empty = new List<string>();
        var act = async () => await store.UpdateAsync(
            x => !empty.Contains(x.Name!), w => w.Body = "WIPED");

        await act.Should().ThrowAsync<Birko.Data.Exceptions.WholeTableWriteException>();

        var rows = (await store.ReadAsync(x => true)).ToList();
        rows.Should().HaveCount(3);
        rows.Should().OnlyContain(w => w.Body == "keep",
            "before TASK-329 the base loop rewrote 3 of 3 rows and threw nothing, and the decorator "
            + "passed the filter straight down to it");
    }

    [Fact]
    public async Task An_explicit_all_rows_predicate_still_updates_every_row_through_the_decorator()
    {
        // The opt-out has to work at every layer it passes through, or the refusal above is a wall
        // (§ SH-H037). Executed rather than reasoned about.
        var store = NewStore(new SqlCacheOptions { Enabled = true });
        for (var i = 1; i <= 3; i++)
        {
            await store.CreateAsync(new[] { new Widget { Guid = Guid.NewGuid(), Name = $"r{i}", Body = "keep" } });
        }

        await store.UpdateAsync(x => true, w => w.Body = "ALL");

        var rows = (await store.ReadAsync(x => true)).ToList();
        rows.Should().HaveCount(3);
        rows.Should().OnlyContain(w => w.Body == "ALL");
    }
}
