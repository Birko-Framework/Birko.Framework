using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Exceptions;
using Birko.Data.InMemory.Stores;
using Birko.Data.Models;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.InMemory.Tests;

/// <summary>
/// TASK-215 — the scope half of SH-M023, on the portable path. Sibling of
/// <see cref="PortableBulkFilterGuardTests"/>, which covers the <b>null</b> filter; this one covers a filter
/// that is <b>present and constrains nothing</b>.
///
/// <para><b>The mechanism.</b> <c>x =&gt; !empty.Contains(x.Field)</c> over an empty collection is true of
/// every entity by C# semantics. The portable path compiles the predicate and runs it as a delegate, so
/// there is no translation layer that could notice — measured before the fix: <c>Delete</c> left
/// <b>0 of 3</b> rows and <c>Update</c> clobbered <b>3 of 3</b>, in both cases with no exception and no log
/// entry. This is the third backend instance of the family behind § Conventions' "a scope guard tests what
/// the statement MEANS": SQL's <c>1 = 1</c> (TASK-137), MongoDB's <c>$nin: []</c> (TASK-212), and here,
/// where the shape is not even translated.</para>
///
/// <para><b>Why the guard had to go on the base, not just on InMemory's overrides.</b> InMemory overrides
/// only <c>Delete(filter)</c>/<c>DeleteAsync(filter)</c>; its four <c>Update(filter, …)</c> paths are
/// <see cref="AbstractBulkStore{T}"/>'s own, and those were unguarded too. Guarding only the overrides would
/// have shipped a store whose <c>Delete</c> refuses and whose <c>Update</c> rewrites every row. The base
/// guard also fixes every portable backend that overrides nothing (JSON, XML, RavenDB, CosmosDB, InfluxDB)
/// by construction — see <c>Birko.Data.JSON.Tests.BaseBulkUnboundedFilterGuardTests</c>, which asserts that
/// from outside this project.</para>
/// </summary>
public class PortableUnboundedFilterGuardTests
{
    private class Row : AbstractModel
    {
        public string? Name { get; set; }
        public int Amount { get; set; }
    }

    private static readonly List<int> Empty = new();
    private static readonly int[] EmptyArray = Array.Empty<int>();
    private static readonly List<int> Some = new() { 10 };

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

    private static async Task<IEnumerable<Row>> RowsAsync(AsyncInMemoryStore<Row> store)
        => await store.ReadAsync(CancellationToken.None);

    // ── the defect: InMemory's own overrides ──────────────────────────────────────────────────────────

    [Fact]
    public void UnboundedFilter_Delete_IsRefused_AndKeepsEveryRow()
    {
        var store = Seeded();

        var act = () => store.Delete(x => !Empty.Contains(x.Amount));

        act.Should().Throw<WholeTableWriteException>();
        store.Read().Should().HaveCount(3, "the measured behaviour before the guard was 0 of 3 left");
    }

    [Fact]
    public async Task UnboundedFilter_DeleteAsync_IsRefused_AndKeepsEveryRow()
    {
        var store = await SeededAsync();

        var act = async () => await store.DeleteAsync(x => !Empty.Contains(x.Amount));

        await act.Should().ThrowAsync<WholeTableWriteException>();
        (await RowsAsync(store)).Should().HaveCount(3);
    }

    [Fact]
    public void UnboundedFilter_AsAnEmptyArray_IsRefusedToo()
    {
        // On .NET 9+ an array binds MemoryExtensions.Contains, not Enumerable.Contains, so the collection
        // arrives wrapped in a ReadOnlySpan conversion. PredicateScope unwraps it; without that unwrap the
        // analyser silently declines and every array-typed caller stays unguarded.
        var store = Seeded();

        var act = () => store.Delete(x => !EmptyArray.Contains(x.Amount));

        act.Should().Throw<WholeTableWriteException>();
        store.Read().Should().HaveCount(3);
    }

    // ── the defect: the four Update paths InMemory does NOT override, i.e. the base's own ─────────────

    [Fact]
    public void UnboundedFilter_UpdateWithPropertyUpdate_IsRefused_AndChangesNothing()
    {
        var store = Seeded();

        var act = () => store.Update(
            x => !Empty.Contains(x.Amount), new PropertyUpdate<Row>().Set(r => r.Name, "clobbered"));

        act.Should().Throw<WholeTableWriteException>();
        store.Read().Select(r => r.Name).Should().BeEquivalentTo(new[] { "r1", "r2", "r3" });
    }

    [Fact]
    public void UnboundedFilter_UpdateWithAction_IsRefused_AndChangesNothing()
    {
        var store = Seeded();

        var act = () => store.Update(x => !Empty.Contains(x.Amount), r => r.Name = "clobbered");

        act.Should().Throw<WholeTableWriteException>();
        store.Read().Select(r => r.Name).Should().BeEquivalentTo(new[] { "r1", "r2", "r3" });
    }

    [Fact]
    public async Task UnboundedFilter_UpdateAsyncWithPropertyUpdate_IsRefused_AndChangesNothing()
    {
        var store = await SeededAsync();

        var act = async () => await store.UpdateAsync(
            x => !Empty.Contains(x.Amount), new PropertyUpdate<Row>().Set(r => r.Name, "clobbered"));

        await act.Should().ThrowAsync<WholeTableWriteException>();
        (await RowsAsync(store)).Select(r => r.Name).Should().BeEquivalentTo(new[] { "r1", "r2", "r3" });
    }

    [Fact]
    public async Task UnboundedFilter_UpdateAsyncWithAction_IsRefused_AndChangesNothing()
    {
        var store = await SeededAsync();

        var act = async () => await store.UpdateAsync(x => !Empty.Contains(x.Amount), r => r.Name = "clobbered");

        await act.Should().ThrowAsync<WholeTableWriteException>();
        (await RowsAsync(store)).Select(r => r.Name).Should().BeEquivalentTo(new[] { "r1", "r2", "r3" });
    }

    // ── the explicit door, EXECUTED rather than mentioned (§ SH-H037) ─────────────────────────────────

    [Fact]
    public void TheRefusal_NamesADoorThatWorks_Sync()
    {
        var store = Seeded();

        var thrown = ((Action)(() => store.Delete(x => !Empty.Contains(x.Amount))))
            .Should().Throw<WholeTableWriteException>().Which;

        thrown.Message.Should().Contain("DeleteAll()");
        // …and that door opens. A refusal pointing at a method that throws — or, as the async twin did
        // before TASK-215, at one that does not exist — is a wall wearing a door's label.
        store.DeleteAll();
        store.Read().Should().BeEmpty();
    }

    [Fact]
    public async Task TheRefusal_NamesADoorThatWorks_Async()
    {
        // Before TASK-215 this message named `DeleteAll()`, which an async store does not have: the caller
        // following the refusal would have hit a compile error. It now names the async spelling.
        var store = await SeededAsync();

        var thrown = (await ((Func<Task>)(async () => await store.DeleteAsync(x => !Empty.Contains(x.Amount))))
            .Should().ThrowAsync<WholeTableWriteException>()).Which;

        thrown.Message.Should().Contain("DeleteAllAsync()");
        thrown.Message.Should().NotContain("DeleteAll() ");

        await store.DeleteAllAsync();
        (await RowsAsync(store)).Should().BeEmpty();
    }

    [Fact]
    public async Task TheRefusal_NamesTheAsyncUpdateDoor_AndItWorks()
    {
        var store = await SeededAsync();

        var thrown = (await ((Func<Task>)(async () => await store.UpdateAsync(
                x => !Empty.Contains(x.Amount), r => r.Name = "clobbered")))
            .Should().ThrowAsync<WholeTableWriteException>()).Which;

        thrown.Message.Should().Contain("UpdateAllAsync(updates)");

        await store.UpdateAllAsync(new PropertyUpdate<Row>().Set(r => r.Name, "migrated"));
        (await RowsAsync(store)).Select(r => r.Name).Should().AllBe("migrated");
    }

    [Fact]
    public async Task AnExplicitTruePredicate_IsStillTheAllRowsSynonym()
    {
        // `x => true` normalizes to a single ConstantExpression and is the documented DeleteAll() synonym.
        // It must survive the guard — otherwise the guard is a wall, and SQL and the portable path would
        // disagree about what the same predicate means.
        var store = await SeededAsync();

        await store.DeleteAsync(x => true);

        (await RowsAsync(store)).Should().BeEmpty();
    }

    [Fact]
    public async Task AnExplicitTruePredicate_IsStillTheAllRowsSynonym_ForUpdate()
    {
        var store = await SeededAsync();

        await store.UpdateAsync(x => true, r => r.Name = "migrated");

        (await RowsAsync(store)).Select(r => r.Name).Should().AllBe("migrated");
    }

    // ── the false-positive direction: what must NOT be refused ───────────────────────────────────────

    [Fact]
    public void ANonEmptyNegatedContains_IsBounded_AndStillDeletes()
    {
        var store = Seeded();

        store.Delete(x => !Some.Contains(x.Amount));

        store.Read().Select(r => r.Name).Should().BeEquivalentTo(new[] { "r1" });
    }

    [Fact]
    public void AnEmptyUnNegatedContains_MatchesNothing_AndIsNotRefused()
    {
        // The mirror image of the defect: `empty.Contains(x)` is always-FALSE, so it constrains everything
        // away rather than nothing. Refusing it would break working code.
        var store = Seeded();

        store.Delete(x => Empty.Contains(x.Amount));

        store.Read().Should().HaveCount(3);
    }

    [Fact]
    public void AnOrdinaryBoundedFilter_IsUntouched()
    {
        var store = Seeded();

        store.Delete(x => x.Amount > 10);

        store.Read().Select(r => r.Name).Should().BeEquivalentTo(new[] { "r1" });
    }

    [Fact]
    public void APerEntityCollection_IsNotClaimedUnbounded()
    {
        // The collection operand references the entity, so its emptiness says nothing about scope and the
        // analyser must decline rather than guess. Refusing here would be a false positive on working code.
        var store = Seeded();

        store.Delete(x => !new[] { x.Amount }.Contains(99));

        store.Read().Should().BeEmpty("every row's own single-element set excludes 99 — a real, if odd, filter");
    }
}
