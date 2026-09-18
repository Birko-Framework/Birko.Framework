using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.InMemory.Tests;

/// <summary>
/// SH-M023 (TASK-109) — the portable half. <c>AbstractBulkStore</c> / <c>AbstractAsyncBulkStore</c> declared
/// <c>filter</c> non-nullable on their filter-based destructive overloads and never checked it. They are
/// read-then-loop: <c>Delete(null!)</c> called <c>Read(null, …)</c>, where a null filter legitimately means
/// <b>read everything</b>, and deleted the entire result.
///
/// <para>These overloads emit no conditionless statement — every write they issue is per-row and carries its
/// own key — so no backend's query guard can see the problem. The damage is "affected every row", never "a
/// statement with no predicate", which is why it has to be refused at the store boundary.</para>
///
/// <para>Asserted through <c>AsyncInMemoryStore</c> / <c>InMemoryStore</c>, the canonical test doubles. The
/// guards live on the shared bases, so this covers the behaviour inherited by all 8 portable backends
/// (CosmosDB, ElasticSearch, InMemory, InfluxDB, JSON, MongoDB, RavenDB, XML).</para>
/// </summary>
public class PortableBulkFilterGuardTests
{
    private class Row : AbstractModel
    {
        public string? Name { get; set; }
        public int Amount { get; set; }
    }

    private static async Task<AsyncInMemoryStore<Row>> SeededAsync()
    {
        var store = new AsyncInMemoryStore<Row>();
        for (var i = 1; i <= 3; i++)
        {
            await store.CreateAsync(new Row { Guid = Guid.NewGuid(), Name = $"r{i}", Amount = i * 10 });
        }
        return store;
    }

    private static InMemoryStore<Row> Seeded()
    {
        var store = new InMemoryStore<Row>();
        for (var i = 1; i <= 3; i++)
        {
            store.Create(new Row { Guid = Guid.NewGuid(), Name = $"r{i}", Amount = i * 10 });
        }
        return store;
    }

    private static async Task<int> CountAsync(AsyncInMemoryStore<Row> store)
        => (await store.ReadAsync(CancellationToken.None)).Count();

    // ---- the defect: a null filter must not mean "every row" ----

    [Fact]
    public async Task NullFilter_DeleteAsync_Throws_AndKeepsEveryRow()
    {
        var store = await SeededAsync();

        var act = async () => await store.DeleteAsync((Expression<Func<Row, bool>>)null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
        (await CountAsync(store)).Should().Be(3);
    }

    [Fact]
    public async Task NullFilter_UpdateAsyncWithAction_Throws_AndChangesNothing()
    {
        var store = await SeededAsync();

        var act = async () => await store.UpdateAsync((Expression<Func<Row, bool>>)null!, r => r.Name = "clobbered");

        await act.Should().ThrowAsync<ArgumentNullException>();
        (await store.ReadAsync(CancellationToken.None)).Select(r => r.Name)
            .Should().BeEquivalentTo(new[] { "r1", "r2", "r3" });
    }

    [Fact]
    public async Task NullFilter_UpdateAsyncWithPropertyUpdate_Throws_AndChangesNothing()
    {
        var store = await SeededAsync();

        var act = async () => await store.UpdateAsync(
            (Expression<Func<Row, bool>>)null!, new PropertyUpdate<Row>().Set(r => r.Name, "clobbered"));

        await act.Should().ThrowAsync<ArgumentNullException>();
        (await store.ReadAsync(CancellationToken.None)).Select(r => r.Name)
            .Should().BeEquivalentTo(new[] { "r1", "r2", "r3" });
    }

    [Fact]
    public void NullFilter_Delete_Sync_Throws_AndKeepsEveryRow()
    {
        var store = Seeded();

        var act = () => store.Delete((Expression<Func<Row, bool>>)null!);

        act.Should().Throw<ArgumentNullException>();
        store.Read().Should().HaveCount(3);
    }

    [Fact]
    public void NullFilter_Update_Sync_Throws_AndChangesNothing()
    {
        var store = Seeded();

        ((Action)(() => store.Update((Expression<Func<Row, bool>>)null!, r => r.Name = "clobbered")))
            .Should().Throw<ArgumentNullException>();
        store.Read().Select(r => r.Name).Should().BeEquivalentTo(new[] { "r1", "r2", "r3" });
    }

    [Fact]
    public async Task TheRefusal_NamesTheExplicitAlternative()
    {
        var store = await SeededAsync();

        var act = async () => await store.DeleteAsync((Expression<Func<Row, bool>>)null!);

        // A guard that blocks without saying how to do the legitimate thing just gets worked around.
        (await act.Should().ThrowAsync<ArgumentNullException>()).WithMessage("*DeleteAllAsync()*");
    }

    // ---- the explicit all-rows door ----

    [Fact]
    public async Task DeleteAllAsync_EmptiesTheStore()
    {
        var store = await SeededAsync();

        await store.DeleteAllAsync();

        (await CountAsync(store)).Should().Be(0);
    }

    [Fact]
    public async Task UpdateAllAsync_TouchesEveryRow()
    {
        var store = await SeededAsync();

        await store.UpdateAllAsync(new PropertyUpdate<Row>().Set(r => r.Name, "migrated"));

        (await store.ReadAsync(CancellationToken.None)).Select(r => r.Name).Should().AllBe("migrated");
    }

    [Fact]
    public void DeleteAll_Sync_EmptiesTheStore()
    {
        var store = Seeded();

        store.DeleteAll();

        store.Read().Should().BeEmpty();
    }

    [Fact]
    public void UpdateAll_Sync_TouchesEveryRow()
    {
        var store = Seeded();

        store.UpdateAll(new PropertyUpdate<Row>().Set(r => r.Name, "migrated"));

        store.Read().Select(r => r.Name).Should().AllBe("migrated");
    }

    // ---- what must keep working ----

    [Fact]
    public async Task ARealFilter_DeletesExactlyTheMatchingRows()
    {
        var store = await SeededAsync();

        await store.DeleteAsync(x => x.Amount > 10);

        (await store.ReadAsync(CancellationToken.None)).Select(r => r.Name).Should().BeEquivalentTo(new[] { "r1" });
    }

    [Fact]
    public async Task AnExplicitTruePredicate_StillMeansEveryRow_OnThePortablePath()
    {
        // The portable path compiles the delegate rather than translating it, so `x => true` needs no special
        // case here — it simply matches everything. Pinned so the guard cannot be tightened into refusing it
        // on one layer while the SQL layer accepts it.
        var store = await SeededAsync();

        await store.DeleteAsync(x => true);

        (await CountAsync(store)).Should().Be(0);
    }

    [Fact]
    public async Task AFilterMatchingNothing_DeletesNothing()
    {
        var store = await SeededAsync();

        await store.DeleteAsync(x => x.Amount > 9999);

        (await CountAsync(store)).Should().Be(3);
    }
}
