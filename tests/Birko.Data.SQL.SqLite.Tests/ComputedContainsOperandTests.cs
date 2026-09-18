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
/// TASK-213 — a COMPUTED operand inside a set <c>Contains</c> was silently replaced by a different predicate.
///
/// <para><b>The mechanism.</b> The non-string <c>Contains</c> arm looped <i>every</i> argument through
/// <c>ParseConditionExpression</c>. The collection argument set <c>Values</c> as intended, but a computed
/// value operand (<c>x.Amount + 1</c>, <c>x.Score ?? 0</c>) was parsed as though it were a nested
/// <b>predicate</b>, so it took the binary-comparison path and fabricated a <b>subcondition</b>
/// (<c>Amount = 1</c>) on the very condition being built. The renderer branches on <c>SubConditions</c>
/// <b>before</b> it looks at <c>Type</c>, so the <c>In</c> and its values were then ignored entirely and only
/// the fabricated equality was emitted.</para>
///
/// <para>That is why this was worse than a broken predicate — it was a <i>different</i> one, silently.
/// Measured against SQLite before the fix (seed amounts 1/5/5/9): <c>Ids.Contains(x.Amount + 1)</c> with
/// <c>Ids = {1,5}</c> returned <b>1</b> row where C# returns <b>0</b>, and the negated form <b>3</b> where C#
/// returns <b>4</b>. Wrong in both directions.</para>
///
/// <para><b>Row-set parity lives in <see cref="SqlExpressionParityTests"/></b>, against the compiled-delegate
/// oracle — that is the instrument that found this and it is where the answers belong. This suite asserts the
/// three things the oracle cannot see: the parsed <i>shape</i> (so the malformed condition cannot come back),
/// the <i>refusal</i> for an operand that cannot be translated, and the composition with TASK-137's
/// whole-table write guard.</para>
/// </summary>
public class ComputedContainsOperandTests : IDisposable
{
    private readonly string _root;

    public ComputedContainsOperandTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-computedin-{Guid.NewGuid():N}");
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    public class Row : AbstractModel
    {
        public string? Name { get; set; }
        public int Amount { get; set; }
        public int? Score { get; set; }
    }

    private sealed class RowMapping : IModelMapping<Row>
    {
        public void Configure(ModelMap<Row> map)
        {
            map.ToTable("ComputedInRows").HasPrimary(x => x.Guid).HasUnique(x => x.Guid);
            map.Property(x => x.Name).HasPrecision(100);
            map.Property(x => x.Amount);
            map.Property(x => x.Score);
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
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "computedin.db" });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(Row) });

        var store = new AsyncSQLiteStore<Row>();
        store.SetSettings(new SqLiteSettings(_root, "computedin.db"));
        for (var i = 1; i <= 3; i++)
        {
            await store.CreateAsync(new Row { Guid = Guid.NewGuid(), Name = $"r{i}", Amount = i * 10, Score = i });
        }
        // A NULL Score is load-bearing, not filler. The pre-fix code rendered
        // `!NoIds.Contains(x.Score ?? 0)` as `NOT (Score = 0)`, which over non-null scores happens to give
        // the right answer — so without a null row the always-true test below passed against the defect and
        // could not witness the fix. With one, SQL's three-valued logic excludes it (`NULL = 0` is UNKNOWN,
        // and `NOT UNKNOWN` is UNKNOWN) while C# counts it, which is the divergence.
        await store.CreateAsync(new Row { Guid = Guid.NewGuid(), Name = "r4", Amount = 40, Score = null });
        (await store.ReadAsync(CancellationToken.None)).Should().HaveCount(4, "seed");
        return store;
    }

    private static readonly int[] Ids = { 1, 5 };
    private static readonly int[] NoIds = Array.Empty<int>();

    private static Condition ParseSingle(Expression<Func<Row, bool>> filter)
    {
        Register();
        var conditions = Birko.Data.SQL.DataBase.ParseConditionExpression(filter).ToList();
        conditions.Should().HaveCount(1, "the parser yields one root per predicate");
        return conditions[0];
    }

    // ── the parsed shape: the operand becomes the condition's NAME, not a fabricated child ───────────────

    [Fact]
    public void AnArithmeticOperand_BecomesTheConditionName_AsASqlFragment()
    {
        var condition = ParseSingle(x => Ids.Contains(x.Amount + 1));

        condition.Type.Should().Be(ConditionType.In);
        condition.Name.Should().Be("(ComputedInRows.Amount + 1)");
        condition.Values!.Cast<object?>().Should().BeEquivalentTo(new object?[] { 1, 5 },
            "the collection argument still supplies the IN list");
    }

    [Fact]
    public void ACoalesceOperand_BecomesTheConditionName_AsASqlFragment()
    {
        var condition = ParseSingle(x => Ids.Contains(x.Score ?? 0));

        condition.Name.Should().Be("COALESCE(ComputedInRows.Score, 0)");
        condition.Type.Should().Be(ConditionType.In);
    }

    [Fact]
    public void NoComputedOperandShape_ProducesTheMalformedConditionAnyMore()
    {
        // The defect's signature was an `In` condition carrying SubConditions — which the renderer prefers
        // over Type, so the IN was dropped. Asserted across every computed shape rather than for one input:
        // a single "Name is not null" check is satisfied by almost any change.
        var shapes = new Dictionary<string, Expression<Func<Row, bool>>>
        {
            ["arithmetic"] = x => Ids.Contains(x.Amount + 1),
            ["arithmetic, negated"] = x => !Ids.Contains(x.Amount + 1),
            ["multiplication"] = x => Ids.Contains(x.Amount * 2),
            ["subtraction"] = x => Ids.Contains(x.Amount - 1),
            ["coalesce"] = x => Ids.Contains(x.Score ?? 0),
            ["coalesce, negated"] = x => !Ids.Contains(x.Score ?? 0),
            ["coalesce, empty set"] = x => !NoIds.Contains(x.Score ?? 0),
            ["arithmetic, empty set"] = x => NoIds.Contains(x.Amount + 1),
        };

        foreach (var (label, filter) in shapes)
        {
            var condition = ParseSingle(filter);

            condition.SubConditions.Should().BeNullOrEmpty(
                $"shape '{label}': an In condition with children has its Type ignored by the renderer");
            condition.Name.Should().NotBeNullOrEmpty($"shape '{label}': the operand must resolve to a name");
            condition.Type.Should().Be(ConditionType.In, $"shape '{label}'");
        }
    }

    [Fact]
    public void APlainColumnOperand_IsUnchanged()
    {
        // The control. Plain operands were always correct and stay on their existing path — the fix is gated
        // on "not a plain resolvable column" precisely so no working shape moves.
        var condition = ParseSingle(x => Ids.Contains(x.Amount));

        condition.Name.Should().Be("ComputedInRows.Amount");
        condition.Type.Should().Be(ConditionType.In);
        condition.SubConditions.Should().BeNullOrEmpty();
    }

    // ── an operand that cannot be translated REFUSES rather than answering wrongly ───────────────────────

    [Fact]
    public void AnUntranslatableOperand_ThrowsInsteadOfSilentlyBecomingAnotherPredicate()
    {
        // `x.Name.Length` has no fragment translation (no LENGTH() support in RenderValueFragment). Before
        // the fix it went down the same silent-substitution path as the arithmetic cases; now it fails loud.
        // § SH-H037: a mapper that cannot express something refuses, it never drops it quietly.
        var act = () => Birko.Data.SQL.DataBase
            .ParseConditionExpression((Expression<Func<Row, bool>>)(x => Ids.Contains(x.Name!.Length)))
            .ToList();

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*Cannot translate operand*")
            .WithMessage("*x.Name.Length*", "the message must name the operand a caller has to change");
    }

    [Fact]
    public async Task AnUntranslatableOperand_ThrowsOnARead_RatherThanReturningTheWrongRows()
    {
        var store = await SeededStore();

        var act = async () => await store.ReadAsync(
            x => Ids.Contains(x.Name!.Length), null, null, null, CancellationToken.None);

        await act.Should().ThrowAsync<NotSupportedException>();
    }

    // ── composition with TASK-137: a computed operand does not hide an always-true term ──────────────────

    [Fact]
    public async Task AnEmptyNegatedContains_OverAComputedOperand_StillMatchesEveryRow()
    {
        // "Not in the empty set" is true of every row whatever the operand computes. Before the fix this
        // emitted the fabricated `Score = 0` and returned 0 of 3 rows.
        var store = await SeededStore();

        var rows = await store.ReadAsync(
            x => !NoIds.Contains(x.Score ?? 0), null, null, null, CancellationToken.None);

        rows.Should().HaveCount(4, "including the NULL-Score row, which the fabricated `NOT (Score = 0)` "
            + "silently excluded under SQL's three-valued logic");
    }

    [Fact]
    public async Task AnEmptyNegatedContains_OverAComputedOperand_IsRefusedOnADelete()
    {
        // The two fixes have to compose: TASK-213 makes the term a well-formed always-true `In`, and
        // TASK-137's reduction then recognises it and refuses the whole-table write. Before TASK-213 the
        // fabricated subcondition hid the always-true term from that guard entirely, so this call would have
        // issued a DELETE with `WHERE Score = 0` — deleting a row nobody asked about.
        var store = await SeededStore();

        var act = async () => await store.DeleteAsync(
            x => !NoIds.Contains(x.Score ?? 0), CancellationToken.None);

        await act.Should().ThrowAsync<Birko.Data.Exceptions.WholeTableWriteException>();
        (await store.ReadAsync(CancellationToken.None)).Should().HaveCount(4, "no row may be deleted");
    }

    [Fact]
    public async Task ABoundedComputedContains_StillDeletesExactlyItsOwnRows()
    {
        // The refusal must not swallow a legitimate bounded delete built on a computed operand.
        // Amounts are 10/20/30, so `Amount + 1` is 11/21/31 and only the middle row matches.
        var store = await SeededStore();
        var target = new[] { 21 };

        await store.DeleteAsync(x => target.Contains(x.Amount + 1), CancellationToken.None);

        var remaining = await store.ReadAsync(CancellationToken.None);
        remaining.Should().HaveCount(3);
        remaining.Should().NotContain(r => r.Amount == 20);
    }

    [Fact]
    public async Task ABoundedComputedContains_UpdatesExactlyItsOwnRows()
    {
        var store = await SeededStore();
        var target = new[] { 21 };

        await store.UpdateAsync(
            x => target.Contains(x.Amount + 1),
            new PropertyUpdate<Row>().Set(x => x.Name, "hit"),
            CancellationToken.None);

        var rows = (await store.ReadAsync(CancellationToken.None)).ToList();
        rows.Where(r => r.Name == "hit").Should().HaveCount(1);
        rows.Single(r => r.Name == "hit").Amount.Should().Be(20);
    }
}
