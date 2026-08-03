using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// SH-H002 / SH-M023 (TASK-109) — end-to-end proof against a real on-disk SQLite database that a refused
/// destructive operation leaves the rows **present**.
///
/// <para>The statement text is asserted in <c>Birko.Data.SQL.Tests.DestructiveFilterGuardTests</c>; this suite
/// asserts the consequence, which is the only part that actually matters to a caller. Note that SQLite's
/// grammar accepts a conditionless <c>DELETE</c> happily — that is exactly why the defect was invisible to a
/// SQLite-backed suite before the guard existed, and why these tests count rows rather than inspect SQL.</para>
/// </summary>
public class DestructiveFilterEndToEndTests : IDisposable
{
    private readonly string _root;

    public DestructiveFilterEndToEndTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-destructive-{Guid.NewGuid():N}");
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
            map.ToTable("Rows").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
            map.Property(x => x.Amount);
        }
    }

    private AsyncSQLiteStore<Row> NewStore()
    {
        var registry = new ModelMapRegistry();
        registry.Register(new RowMapping());
        registry.ApplyToDatabase();
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "destructive.db" });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(Row) });
        var store = new AsyncSQLiteStore<Row>();
        store.SetSettings(new SqLiteSettings(_root, "destructive.db"));
        return store;
    }

    private async Task<AsyncSQLiteStore<Row>> SeededStore()
    {
        var store = NewStore();
        for (var i = 1; i <= 3; i++)
        {
            await store.CreateAsync(new Row { Guid = Guid.NewGuid(), Name = $"r{i}", Amount = i * 10 });
        }
        (await store.ReadAsync(CancellationToken.None)).Should().HaveCount(3, "seed");
        return store;
    }

    private static async Task<int> CountAsync(AsyncSQLiteStore<Row> store)
        => (await store.ReadAsync(CancellationToken.None)).Count();

    // ---- the defect: rows must SURVIVE a refused delete ----

    [Fact]
    public async Task NullFilter_DeleteAsync_LeavesEveryRowInPlace()
    {
        var store = await SeededStore();

        var act = async () => await store.DeleteAsync((Expression<Func<Row, bool>>)null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
        (await CountAsync(store)).Should().Be(3, "SH-H002: a null filter rendered `DELETE FROM \"Rows\"`");
    }

    [Fact]
    public async Task UntranslatableFilter_DeleteAsync_LeavesEveryRowInPlace()
    {
        var store = await SeededStore();
        Func<Row, bool> pred = r => r.Amount > 10;

        // An InvocationExpression the parser cannot express. Before the guard this silently dropped the
        // filter and deleted all three rows while reporting success.
        var act = async () => await store.DeleteAsync(x => pred(x));

        await act.Should().ThrowAsync<Birko.Data.Exceptions.WholeTableWriteException>();
        (await CountAsync(store)).Should().Be(3);
    }

    [Fact]
    public async Task UntranslatableFilter_UpdateAsync_LeavesEveryRowUnchanged()
    {
        var store = await SeededStore();
        Func<Row, bool> pred = r => r.Amount > 10;

        var act = async () => await store.UpdateAsync(
            x => pred(x), new Birko.Data.Stores.PropertyUpdate<Row>().Set(r => r.Name, "clobbered"));

        await act.Should().ThrowAsync<Birko.Data.Exceptions.WholeTableWriteException>();
        (await store.ReadAsync(CancellationToken.None)).Select(r => r.Name)
            .Should().BeEquivalentTo(new[] { "r1", "r2", "r3" });
    }

    [Fact]
    public async Task NullFilter_UpdateAsync_WithAnAction_LeavesEveryRowUnchanged()
    {
        // The read-then-loop overload: no conditionless SQL is ever emitted, so only the store-boundary
        // guard can catch this. Before it, Read(null) returned every row and each was mutated.
        var store = await SeededStore();

        var act = async () => await store.UpdateAsync((Expression<Func<Row, bool>>)null!, r => r.Name = "clobbered");

        await act.Should().ThrowAsync<ArgumentNullException>();
        (await store.ReadAsync(CancellationToken.None)).Select(r => r.Name)
            .Should().BeEquivalentTo(new[] { "r1", "r2", "r3" });
    }

    [Fact]
    public async Task TheRefusal_IsNotWrappedByTheTransactionHandler()
    {
        // Found by these tests: the first version of the guard threw from inside the command-building callback
        // of DoCommandWithTransaction, whose catch funnels everything through InitException and re-wraps it in
        // a bare System.Exception. The rows survived, but the exception a host could catch was
        // `Exception` — an unhandled 500 for what is a request-shaped problem, exactly the failure
        // TenantScopeRequiredException was introduced to avoid. The guard now runs BEFORE the wrapper.
        var store = await SeededStore();
        Func<Row, bool> pred = r => r.Amount > 10;

        var thrown = await Record.ExceptionAsync(() => store.DeleteAsync(x => pred(x)));

        thrown.Should().BeOfType<Birko.Data.Exceptions.WholeTableWriteException>(
            "the refusal must reach the caller with its own type, not wrapped");
        thrown!.InnerException.Should().BeNull();
    }

    // ---- what must keep working ----

    [Fact]
    public async Task ATranslatingFilter_DeletesExactlyTheMatchingRows()
    {
        var store = await SeededStore();

        await store.DeleteAsync(x => x.Amount > 10);

        (await store.ReadAsync(CancellationToken.None)).Select(r => r.Name)
            .Should().BeEquivalentTo(new[] { "r1" }, "the guard must not disturb a filter that translates");
    }

    [Fact]
    public async Task AFilterMatchingNothing_DeletesNothing()
    {
        // The distinction the whole task turns on: "matches nothing" must not become "matches everything".
        var store = await SeededStore();

        await store.DeleteAsync(x => x.Amount > 9999);

        (await CountAsync(store)).Should().Be(3);
    }

    [Fact]
    public async Task ConstantFalseFilter_DeletesNothing()
    {
        var store = await SeededStore();

        await store.DeleteAsync(x => false);

        (await CountAsync(store)).Should().Be(3, "`x => false` renders an always-false WHERE, not an absent one");
    }

    // ---- the explicit all-rows door still works ----

    [Fact]
    public async Task DeleteAllAsync_EmptiesTheTable()
    {
        var store = await SeededStore();

        await store.DeleteAllAsync();

        (await CountAsync(store)).Should().Be(0);
    }

    [Fact]
    public async Task ExplicitTruePredicate_IsASynonymForDeleteAll()
    {
        // Kept working deliberately: it is an explicit statement of intent, and 4 existing call sites in the
        // family use it. Recognised as ONE node type after ExpressionNormalizer, not as a shape whitelist.
        var store = await SeededStore();

        await store.DeleteAsync(x => true);

        (await CountAsync(store)).Should().Be(0);
    }

    [Fact]
    public async Task UpdateAllAsync_TouchesEveryRow()
    {
        // The migration shape the guard would otherwise have left with only an O(n) workaround.
        var store = await SeededStore();

        await store.UpdateAllAsync(new Birko.Data.Stores.PropertyUpdate<Row>().Set(r => r.Name, "migrated"));

        (await store.ReadAsync(CancellationToken.None)).Select(r => r.Name)
            .Should().AllBe("migrated");
    }

    [Fact]
    public async Task ExplicitTruePredicate_UpdateAsync_IsASynonymForUpdateAll()
    {
        var store = await SeededStore();

        await store.UpdateAsync(x => true, new Birko.Data.Stores.PropertyUpdate<Row>().Set(r => r.Name, "migrated"));

        (await store.ReadAsync(CancellationToken.None)).Select(r => r.Name).Should().AllBe("migrated");
    }

    [Fact]
    public async Task OrWithAConstantTrueSide_IsRefused_NotTreatedAsAllRows()
    {
        // The parser reduces this to "everything", but it is not the explicit door — recognising it would
        // mean whitelisting shapes. Refused, with a message naming the explicit API.
        var store = await SeededStore();

        var act = async () => await store.DeleteAsync(x => true || x.Amount > 10);

        await act.Should().ThrowAsync<Birko.Data.Exceptions.WholeTableWriteException>()
            .WithMessage("*DeleteAll()*");
        (await CountAsync(store)).Should().Be(3);
    }
}
