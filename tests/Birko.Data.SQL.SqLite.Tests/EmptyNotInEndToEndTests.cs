using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Birko.Data.Models;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Data.Stores;
using Birko.Models.SQL.Mapping;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-137 — end-to-end proof against a real on-disk SQLite database that an empty <c>NOT IN</c> no longer
/// reaches a whole-table write, and that every read it appears in returns the same rows as before.
///
/// <para><b>What was measured before the fix</b>, on this exact fixture (3 rows, amounts 10/20/30, an empty
/// <c>ids</c> list): <c>DeleteAsync(x =&gt; !ids.Contains(x.Amount))</c> threw nothing and left <b>0 of 3</b>
/// rows; <c>UpdateAsync</c> with the same filter rewrote <b>3 of 3</b>. The empty <c>NOT IN</c> rendered
/// <c>WHERE 1 = 1</c>, which is a non-empty WHERE, so <c>AddRequiredWhere</c>'s whole-table guard (SH-H002 /
/// TASK-109) was satisfied by a tautology.</para>
///
/// <para><b>Why row counts and not SQL text.</b> The statement text is asserted in
/// <c>Birko.Data.SQL.Tests.EmptyNotInReductionTests</c>. Here the consequence is the point: SQLite accepts a
/// conditionless DELETE happily, so only the surviving rows can tell a refused delete from a performed one.
/// The read half matters just as much — the reduction must not silently narrow a result set, which no
/// assertion about the absence of <c>1 = 1</c> would catch.</para>
/// </summary>
public class EmptyNotInEndToEndTests : IDisposable
{
    private readonly string _root;

    public EmptyNotInEndToEndTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-emptynotin-{Guid.NewGuid():N}");
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
            map.ToTable("EmptyNotInRows").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
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

    private async Task<AsyncSQLiteStore<Row>> SeededStore()
    {
        Register();
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "emptynotin.db" });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(Row) });

        var store = new AsyncSQLiteStore<Row>();
        store.SetSettings(new SqLiteSettings(_root, "emptynotin.db"));
        for (var i = 1; i <= 3; i++)
        {
            await store.CreateAsync(new Row { Guid = Guid.NewGuid(), Name = $"r{i}", Amount = i * 10 });
        }
        (await store.ReadAsync(CancellationToken.None)).Should().HaveCount(3, "seed");
        return store;
    }

    private static readonly List<int> Empty = new();

    // ── the destructive half: this is the defect ─────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_WithASoleEmptyNotIn_IsRefusedAndLeavesEveryRow()
    {
        var store = await SeededStore();

        var act = async () => await store.DeleteAsync(x => !Empty.Contains(x.Amount), CancellationToken.None);

        await act.Should().ThrowAsync<Birko.Data.Exceptions.WholeTableWriteException>();
        (await store.ReadAsync(CancellationToken.None)).Should().HaveCount(3,
            "the rows are the proof — before the fix this call left none of them and threw nothing");
    }

    [Fact]
    public async Task Update_WithASoleEmptyNotIn_IsRefusedAndRewritesNothing()
    {
        var store = await SeededStore();

        var act = async () => await store.UpdateAsync(
            x => !Empty.Contains(x.Amount),
            new PropertyUpdate<Row>().Set(x => x.Name, "WIPED"),
            CancellationToken.None);

        await act.Should().ThrowAsync<Birko.Data.Exceptions.WholeTableWriteException>();
        (await store.ReadAsync(CancellationToken.None)).Should().NotContain(r => r.Name == "WIPED",
            "before the fix this rewrote 3 of 3 rows");
    }

    [Fact]
    public async Task Delete_WithACollapsedOrChain_IsAlsoRefused()
    {
        // `A OR TRUE` is TRUE, so this targets every row just as surely as the sole term does.
        var store = await SeededStore();

        var act = async () => await store.DeleteAsync(
            x => x.Amount > 20 || !Empty.Contains(x.Amount), CancellationToken.None);

        await act.Should().ThrowAsync<Birko.Data.Exceptions.WholeTableWriteException>();
        (await store.ReadAsync(CancellationToken.None)).Should().HaveCount(3);
    }

    [Fact]
    public async Task TheRefusalIsWholeTableWriteException_NotTheBareExceptionTheTransactionWrapperWouldProduce()
    {
        // The refusal has to fire in the pre-check, BEFORE DoCommandWithTransaction — its InitException
        // re-wraps anything thrown inside the callback in a bare Exception that no
        // `catch (WholeTableWriteException)` could select, which is an unhandled 500 for a request-shaped
        // problem. Asserting the concrete type here is what proves the placement.
        var store = await SeededStore();

        var act = async () => await store.DeleteAsync(x => !Empty.Contains(x.Amount), CancellationToken.None);

        var thrown = await act.Should().ThrowAsync<Birko.Data.Exceptions.WholeTableWriteException>();
        thrown.Which.Should().BeOfType<Birko.Data.Exceptions.WholeTableWriteException>();
        thrown.Which.Message.Should().Contain("DeleteAll()", "a guard that only says no gets reached around");
    }

    // ── the doors that must stay open ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteAll_StillEmptiesTheTable()
    {
        // § SH-H037: fail-fast is legitimate only where an opt-out exists and is checked first. Executed, not
        // reasoned about — a refusal whose escape hatch throws is a wall wearing a door's label.
        var store = await SeededStore();

        await store.DeleteAllAsync(CancellationToken.None);

        (await store.ReadAsync(CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task AnExplicitTruePredicate_StillDeletesEveryRow()
    {
        var store = await SeededStore();

        await store.DeleteAsync(x => true, CancellationToken.None);

        (await store.ReadAsync(CancellationToken.None)).Should().BeEmpty(
            "the one-node explicit constant is the documented DeleteAll synonym and is unaffected");
    }

    [Fact]
    public async Task ABoundedDeleteBesideAnAlwaysTrueTerm_StillDeletesItsRows()
    {
        // The reduction must not make a bounded delete look unbounded — the refusal fires on "everything",
        // never on "everything except the term I dropped".
        var store = await SeededStore();

        await store.DeleteAsync(x => x.Amount > 20 && !Empty.Contains(x.Amount), CancellationToken.None);

        var remaining = await store.ReadAsync(CancellationToken.None);
        remaining.Should().HaveCount(2);
        remaining.Should().OnlyContain(r => r.Amount <= 20);
    }

    [Fact]
    public async Task ADeleteThatMatchesNothing_StillDeletesNothingRatherThanBeingRefused()
    {
        // `NOT (A OR TRUE)` is always FALSE. It must render, and it must not be mistaken for "everything" in
        // either direction: not refused, and not executed as a whole-table delete.
        var store = await SeededStore();

        await store.DeleteAsync(x => !(x.Amount > 20 || !Empty.Contains(x.Amount)), CancellationToken.None);

        (await store.ReadAsync(CancellationToken.None)).Should().HaveCount(3,
            "an always-false filter deletes nothing");
    }

    // ── the read half: the oracle that must not move ─────────────────────────────────────────────────────

    public static IEnumerable<object[]> ReadShapes()
    {
        // Every value here was measured against the UNFIXED code and is unchanged by the fix. That is the
        // point of the table: the reduction rewrites the SQL for all seven shapes, so a silent narrowing
        // anywhere in the AND/OR/negation algebra shows up as a changed count.
        yield return new object[] { "sole empty NOT IN", (Expression<Func<Row, bool>>)(x => !Empty.Contains(x.Amount)), 3 };
        yield return new object[] { "AND with a real term", (Expression<Func<Row, bool>>)(x => x.Amount > 10 && !Empty.Contains(x.Amount)), 2 };
        yield return new object[] { "OR with a real term", (Expression<Func<Row, bool>>)(x => x.Amount > 20 || !Empty.Contains(x.Amount)), 3 };
        yield return new object[] { "negated OR group", (Expression<Func<Row, bool>>)(x => !(x.Amount > 20 || !Empty.Contains(x.Amount))), 0 };
        yield return new object[] { "negated AND group", (Expression<Func<Row, bool>>)(x => !(x.Amount > 20 && !Empty.Contains(x.Amount))), 2 };
        yield return new object[] { "AND nested inside OR", (Expression<Func<Row, bool>>)(x => x.Amount > 20 || x.Name == "r1" && !Empty.Contains(x.Amount)), 2 };
        yield return new object[] { "three-term AND", (Expression<Func<Row, bool>>)(x => x.Amount > 10 && x.Name != null && !Empty.Contains(x.Amount)), 2 };
        yield return new object[] { "empty IN (always false)", (Expression<Func<Row, bool>>)(x => Empty.Contains(x.Amount)), 0 };
    }

    [Theory]
    [MemberData(nameof(ReadShapes))]
    public async Task Read_ReturnsTheSameRowsAsBeforeTheReduction(string label, Expression<Func<Row, bool>> filter, int expected)
    {
        var store = await SeededStore();

        var rows = await store.ReadAsync(filter, null, null, null, CancellationToken.None);

        rows.Should().HaveCount(expected, label);
    }

    [Fact]
    public async Task Read_WithANonEmptyNotIn_IsUnaffected()
    {
        var store = await SeededStore();
        var ids = new List<int> { 20 };

        var rows = await store.ReadAsync(x => !ids.Contains(x.Amount), null, null, null, CancellationToken.None);

        rows.Should().HaveCount(2, "a real NOT IN still filters");
        rows.Should().OnlyContain(r => r.Amount != 20);
    }

    // ── the leaf shape this fix is built on ─────────────────────────────────────────────────────────────

    [Fact]
    public void TheParserProducesTheEmptyNotInLeafShape()
    {
        // Guards the whole design against being built on a condition the parser never emits. Lives here rather
        // than in Birko.Data.SQL.Tests because resolving the column name needs an IModelMapping registration.
        Register();

        var conditions = Birko.Data.SQL.DataBase
            .ParseConditionExpression((Expression<Func<Row, bool>>)(x => !Empty.Contains(x.Amount)))
            .ToList();

        conditions.Should().HaveCount(1, "the parser yields one root; precedence is expressed by nesting");
        var leaf = conditions[0];
        leaf.Type.Should().Be(ConditionType.In);
        leaf.IsNot.Should().BeTrue();
        leaf.Values.Should().NotBeNull();
        leaf.Values!.Cast<object?>().Should().BeEmpty();
        AbstractConnectorBase.IsAlwaysTrueCondition(leaf).Should().BeTrue();
    }
}
