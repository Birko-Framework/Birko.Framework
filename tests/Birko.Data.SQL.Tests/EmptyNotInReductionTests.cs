using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests;

/// <summary>
/// TASK-137 — an empty <c>NOT IN</c> rendered <c>1 = 1</c>, and that constant did two distinct kinds of harm.
///
/// <para><b>The one that was filed</b> is operational: <c>1 = 1</c> is the signature of <c>' OR 1=1--</c>, so
/// emitting it during normal operation puts a false positive into every query log and audit trail, and trains
/// operators to scroll past the pattern they are supposed to react to.</para>
///
/// <para><b>The one that was not filed</b> is why this suite exists. <c>1 = 1</c> is a NON-EMPTY
/// <c>WHERE</c> that constrains nothing, and <see cref="AbstractConnectorBase.AddRequiredWhere"/>'s whole-table
/// write guard (SH-H002 / TASK-109) tests exactly one thing: whether anything was rendered. So the tautology
/// satisfied the guard, and <c>Delete(x =&gt; !empty.Contains(x.Col))</c> reached a whole-table DELETE with the
/// guard's blessing. Measured against real SQLite before the fix: <b>0 of 3 rows left, no exception</b>, and the
/// UPDATE twin rewrote 3 of 3. The row-set consequence is pinned in
/// <c>Birko.Data.SQL.SqLite.Tests.EmptyNotInEndToEndTests</c>; this suite pins the rendering and the guard.</para>
///
/// <para><b>Why a reduction and not a different constant.</b> `TRUE` is a syntax error in T-SQL and any other
/// always-true comparison is the same lookalike in a different hat. An always-true term does not need a
/// rendering at all: <c>A AND TRUE</c> is <c>A</c>, and a chain that reduces to nothing is a chain with no
/// <c>WHERE</c> — which reads as read-everything and, on a destructive statement, is refused. Note the
/// asymmetry with the empty <c>IN</c>: <c>A AND FALSE</c> is <c>FALSE</c>, not <c>A</c>, so the always-false
/// side cannot be dropped and keeps its <c>1 = 0</c>.</para>
/// </summary>
public class EmptyNotInReductionTests
{
    private class Widget : AbstractModel
    {
        public string? Name { get; set; }
        public int Count { get; set; }
    }

    private sealed class FakeConnector : AbstractConnectorBase
    {
        public FakeConnector() : base(new Birko.Configuration.PasswordSettings()) { }

        public override DbConnection CreateConnection(Birko.Configuration.PasswordSettings settings)
            => throw new NotSupportedException();
        public override string ConvertType(System.Data.DbType type, Birko.Data.SQL.Fields.AbstractField field)
            => throw new NotSupportedException();
        public override string FieldDefinition(Birko.Data.SQL.Fields.AbstractField field)
            => throw new NotSupportedException();
    }

    /// <summary>An empty <c>NOT IN</c> leaf, exactly as the expression parser produces it (verified by dumping
    /// the tree for <c>x =&gt; !empty.Contains(x.Count)</c>: <c>Type=In IsNot=True Values=[]</c>).</summary>
    private static Condition EmptyNotIn(string name = "Widgets.Count")
        => new Condition(name, Array.Empty<object?>(), ConditionType.In, isNot: true);

    private static Condition EmptyIn(string name = "Widgets.Count")
        => new Condition(name, Array.Empty<object?>(), ConditionType.In, isNot: false);

    private static Condition Real(string name = "Widgets.Count", int value = 5)
        => Condition.CreateValue(name, value, ConditionType.Greather);

    private static Condition Group(bool isOr, bool isNot, params Condition[] children)
        => new Condition(null, null, ConditionType.Equal, isNot: isNot, isOr: isOr, subConditions: children);

    private static string Render(Condition condition)
        => new FakeConnector().ConditionDefinition(condition, new TestDbCommand());

    private static string RenderList(params Condition[] conditions)
        => new FakeConnector().ConditionDefinition((IEnumerable<Condition>)conditions, new TestDbCommand());

    // ── the constant is gone, and nothing replaced it ────────────────────────────────────────────────────

    [Fact]
    public void ASoleEmptyNotIn_RendersNoWhereAtAll()
    {
        // Not "renders something harmless" — renders NOTHING, which is what makes the destructive guard see it.
        Render(EmptyNotIn()).Should().BeEmpty();
    }

    [Fact]
    public void NoShapeContainingAnEmptyNotIn_EmitsAnAlwaysTrueConstant()
    {
        // Asserted positively across every shape rather than by checking one input for the absence of the old
        // string: a "does not contain 1 = 1" assertion on a single case is satisfied by almost any change.
        var shapes = new Dictionary<string, Condition>
        {
            ["sole"] = EmptyNotIn(),
            ["AND with a real term"] = Group(isOr: false, isNot: false, Real(), EmptyNotIn()),
            ["OR with a real term"] = Group(isOr: true, isNot: false, Real(), EmptyNotIn()),
            ["negated OR group"] = Group(isOr: true, isNot: true, Real(), EmptyNotIn()),
            ["negated AND group"] = Group(isOr: false, isNot: true, Real(), EmptyNotIn()),
            ["nested AND inside OR"] = Group(isOr: true, isNot: false, Real(), Group(isOr: false, isNot: false, Real("Widgets.Name"), EmptyNotIn())),
            ["two empty NOT INs"] = Group(isOr: false, isNot: false, EmptyNotIn(), EmptyNotIn()),
            ["empty IN beside empty NOT IN"] = Group(isOr: false, isNot: false, EmptyIn(), EmptyNotIn()),
        };

        foreach (var (label, condition) in shapes)
        {
            Render(condition).Should().NotContain("1 = 1", $"shape: {label}");
        }
    }

    [Fact]
    public void TheEmittedSqlIsWhatTheRemainingConditionsSay()
    {
        // The term is dropped, not neutralised — no leftover separator, no empty parentheses.
        var sql = Render(Group(isOr: false, isNot: false, Real(), EmptyNotIn()));

        sql.Should().Be("Widgets.Count > @WHEREWidgetsCount0_0");
        sql.Should().NotContain("AND");
    }

    [Fact]
    public void ThreeTermAndChain_KeepsBothRealTermsAndTheirSeparator()
    {
        var sql = Render(Group(isOr: false, isNot: false, Real(), EmptyNotIn(), Real("Widgets.Name", 9)));

        sql.Should().Contain(" AND ");
        sql.Should().NotContain("1 = 1");
        sql.Split(" AND ").Should().HaveCount(2, "the always-true term is gone, the two real ones remain");
    }

    // ── OR must COLLAPSE, not drop — this is where a careless fix silently narrows results ───────────────

    [Fact]
    public void AnOrChainContainingAnAlwaysTrueTerm_CollapsesToNoWhere()
    {
        // `A OR TRUE` is TRUE. Dropping the term instead would leave `A` and silently narrow the result set
        // from every row to A's rows — a wrong answer, which is worse than the constant this task removes.
        Render(Group(isOr: true, isNot: false, Real(), EmptyNotIn())).Should().BeEmpty();
    }

    [Fact]
    public void AnOrChainCollapses_RegardlessOfWhereTheAlwaysTrueTermSits()
    {
        Render(Group(isOr: true, isNot: false, EmptyNotIn(), Real())).Should().BeEmpty("first position");
        Render(Group(isOr: true, isNot: false, Real(), EmptyNotIn(), Real("Widgets.Name", 9))).Should().BeEmpty("middle");
    }

    [Fact]
    public void AnAndGroupNestedInsideAnOr_CollapsesOnlyItsOwnRun()
    {
        // `A OR (B AND TRUE)` is `A OR B` — the outer OR must NOT collapse, because its always-true-ness
        // depends on the inner AND run, which still carries B.
        var sql = Render(Group(isOr: true, isNot: false,
            Real(),
            Group(isOr: false, isNot: false, Real("Widgets.Name", 9), EmptyNotIn())));

        sql.Should().NotBeEmpty("the chain is A OR B, which constrains rows");
        sql.Should().Contain(" OR ");
        sql.Should().NotContain("1 = 1");
    }

    [Fact]
    public void AnAndGroupWhoseEveryTermIsAlwaysTrue_IsItselfAlwaysTrue()
        => Render(Group(isOr: false, isNot: false, EmptyNotIn(), EmptyNotIn())).Should().BeEmpty();

    // ── negation flips: NOT (A OR TRUE) is FALSE, and must be RENDERED ───────────────────────────────────

    [Fact]
    public void ANegatedGroupThatReducesToAlwaysTrue_RendersTheAlwaysFalseConstant()
    {
        // Measured reachable off the parser: `!(x.Count > 20 || !empty.Contains(x.Count))` parses to a group
        // with IsNot=true, IsOr=true and correctly returns 0 rows. Reducing the group away instead of flipping
        // it would return EVERY row — the exact inversion the empty-NOT-IN rendering was careful to avoid.
        Render(Group(isOr: true, isNot: true, Real(), EmptyNotIn()))
            .Should().Be(AbstractConnectorBase.AlwaysFalseSql);
    }

    [Fact]
    public void ANegatedAndGroup_DropsTheAlwaysTrueTermAndKeepsTheNegation()
    {
        // `NOT (A AND TRUE)` is `NOT A` — the group is not always-true, so it renders normally minus the term.
        var sql = Render(Group(isOr: false, isNot: true, Real(), EmptyNotIn()));

        sql.Should().StartWith("NOT (");
        sql.Should().NotContain("1 = 1");
        sql.Should().NotBe(AbstractConnectorBase.AlwaysFalseSql);
    }

    // ── the always-FALSE side is deliberately untouched ──────────────────────────────────────────────────

    [Fact]
    public void AnEmptyIn_StillRendersTheAlwaysFalseConstant()
    {
        // `A AND FALSE` is FALSE, not A, so an always-false term cannot be dropped — and `1 = 0` carries no
        // injection connotation. The asymmetry is the point, not an oversight.
        Render(EmptyIn()).Should().Be("1 = 0");
        Render(Group(isOr: false, isNot: false, Real(), EmptyIn())).Should().Contain("1 = 0");
    }

    [Fact]
    public void AnEmptyInBesideAnEmptyNotIn_MatchesNothing()
        => Render(Group(isOr: false, isNot: false, EmptyIn(), EmptyNotIn())).Should().Be("1 = 0");

    // ── a real IN is untouched ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ANonEmptyNotIn_IsUnaffected()
    {
        var condition = new Condition("Widgets.Count", new object?[] { 1, 2 }, ConditionType.In, isNot: true);

        Render(condition).Should().Contain(" NOT IN (").And.NotBeEmpty();
    }

    // ── the flat multi-condition list (hand-built conditions; the parser always yields one root) ──────────

    [Fact]
    public void FlatList_DropsAnAndJoinedAlwaysTrueTerm()
        => RenderList(Real(), EmptyNotIn()).Should().Be("Widgets.Count > @WHEREWidgetsCount0_0");

    [Fact]
    public void FlatList_CollapsesWhenAnAlwaysTrueTermIsOrJoinedAndClosesItsRun()
    {
        var orJoined = EmptyNotIn();
        orJoined.IsOr = true;

        RenderList(Real(), orJoined).Should().BeEmpty("A OR TRUE is TRUE");
    }

    [Fact]
    public void FlatList_ADroppedOrJoinedTermHandsItsOrToTheNextSurvivor()
    {
        // `A OR TRUE AND B` is `A OR (TRUE AND B)` = `A OR B`. If the dropped term's OR were not inherited,
        // the render would be `A AND B` — the intersection instead of the union, i.e. a silent narrowing
        // introduced BY the fix. (Found by reasoning through the reduction, not by a failing test.)
        var orJoined = EmptyNotIn();
        orJoined.IsOr = true;
        var andJoined = Real("Widgets.Name", 9);
        andJoined.IsOr = false;

        var sql = RenderList(Real(), orJoined, andJoined);

        sql.Should().Contain(" OR ");
        sql.Should().NotContain(" AND ");
    }

    // ── the guard: a reduced-away chain is refused on destructive paths ──────────────────────────────────

    private static DbCommand Destructive(IEnumerable<Condition>? conditions, bool allowAllRows = false)
    {
        var command = new TestDbCommand { CommandText = "DELETE FROM \"Widgets\"" };
        new FakeConnector().AddRequiredWhere(conditions, command, "delete", "Widgets", allowAllRows);
        return command;
    }

    [Fact]
    public void ASoleEmptyNotIn_IsRefusedOnADestructiveStatement()
    {
        // The bypass, stated as the guard now sees it. Before the fix this appended `WHERE 1 = 1` and the
        // DELETE ran.
        var act = () => Destructive(new[] { EmptyNotIn() });

        act.Should().Throw<Birko.Data.Exceptions.WholeTableWriteException>()
            .Which.Should().Match<Birko.Data.Exceptions.WholeTableWriteException>(
                e => e.Operation == "delete" && e.TableName == "Widgets");
    }

    [Fact]
    public void TheRefusalNamesTheDeliberateDoor()
    {
        // A guard that only says "no" gets reached around (§ Conventions).
        ((Action)(() => Destructive(new[] { EmptyNotIn() }))).Should()
            .Throw<Birko.Data.Exceptions.WholeTableWriteException>()
            .WithMessage("*DeleteAll()*");
    }

    [Fact]
    public void AnOrChainThatCollapsed_IsAlsoRefused()
        => ((Action)(() => Destructive(new[] { Group(isOr: true, isNot: false, Real(), EmptyNotIn()) })))
            .Should().Throw<Birko.Data.Exceptions.WholeTableWriteException>();

    [Fact]
    public void TheExplicitAllRowsOptIn_IsCheckedFirst_SoTheGuardHasADoor()
    {
        // Fail-fast is legitimate only where an opt-out exists AND is checked first (§ SH-H037). Asserted, not
        // assumed: `allowAllRows` must survive the new reduction.
        var command = Destructive(new[] { EmptyNotIn() }, allowAllRows: true);

        command.CommandText.Should().Be("DELETE FROM \"Widgets\"");
        command.CommandText.Should().NotContain("1 = 1", "the deliberate door emits clean SQL, not a marker");
    }

    [Fact]
    public void ANegatedGroupReducingToAlwaysFalse_IsNotRefused_BecauseItTargetsNoRows()
    {
        // `NOT (A OR TRUE)` matches nothing, so a DELETE built from it is harmless and must still be allowed —
        // the refusal must fire on "everything", never on "nothing". Guards the inverse mistake.
        var command = Destructive(new[] { Group(isOr: true, isNot: true, Real(), EmptyNotIn()) });

        command.CommandText.Should().Contain("WHERE " + AbstractConnectorBase.AlwaysFalseSql);
    }

    [Fact]
    public void ARealTermBesideAnAlwaysTrueTerm_IsNotRefused()
    {
        // The reduction must not make a bounded delete look unbounded.
        var command = Destructive(new[] { Group(isOr: false, isNot: false, Real(), EmptyNotIn()) });

        command.CommandText.Should().Contain("WHERE Widgets.Count > ");
    }

    // ── the shared verdict: one producer for the guard and the renderer ──────────────────────────────────

    [Fact]
    public void IsAlwaysTrueCondition_AndTheRenderer_CannotDisagree()
    {
        // § Conventions: the "means everything" verdict has ONE producer. If a shape is judged always-true it
        // must render nothing, and if it renders nothing it must be judged always-true — otherwise the guard
        // and the emitted SQL drift apart, which is how the bypass existed in the first place.
        var shapes = new Condition[]
        {
            EmptyNotIn(),
            EmptyIn(),
            Real(),
            Group(isOr: false, isNot: false, Real(), EmptyNotIn()),
            Group(isOr: true, isNot: false, Real(), EmptyNotIn()),
            Group(isOr: true, isNot: true, Real(), EmptyNotIn()),
            Group(isOr: false, isNot: true, Real(), EmptyNotIn()),
            Group(isOr: false, isNot: false, EmptyNotIn(), EmptyNotIn()),
            Group(isOr: true, isNot: false, Real(), Group(isOr: false, isNot: false, Real("Widgets.Name", 9), EmptyNotIn())),
        };

        foreach (var shape in shapes)
        {
            AbstractConnectorBase.IsAlwaysTrueCondition(shape)
                .Should().Be(Render(shape).Length == 0,
                    "the verdict and the rendering are produced from the same reduction");
        }
    }

    [Fact]
    public void IsAlwaysTrueCondition_IsNotIsExplicitAllRows()
    {
        // Two different questions, deliberately: "the caller said every row" (one normalized constant node,
        // the DeleteAll synonym) versus "this tree happens to reduce to every row" (refused, per TASK-109).
        Expression<Func<Widget, bool>> constantTrue = x => true;

        Birko.Data.SQL.DataBase.IsExplicitAllRows(constantTrue).Should().BeTrue();
        AbstractConnectorBase.IsAlwaysTrueCondition(EmptyNotIn()).Should().BeTrue();

        // ...and the empty NOT IN is NOT the explicit door, which is what makes it refusable.
        Expression<Func<Widget, bool>> reducesToEverything = BuildEmptyNotInPredicate();
        Birko.Data.SQL.DataBase.IsExplicitAllRows(reducesToEverything).Should().BeFalse();
    }

    private static Expression<Func<Widget, bool>> BuildEmptyNotInPredicate()
    {
        var empty = new List<int>();
        return x => !empty.Contains(x.Count);
    }

    // The claim that the parser really produces the `Type=In, IsNot=true, Values=[]` leaf this suite is built
    // on — i.e. that none of the above tests a hand-built condition the parser never emits, the failure mode
    // that let TASK-129's DDL assertion pass against broken SQL — is asserted in
    // Birko.Data.SQL.SqLite.Tests.EmptyNotInEndToEndTests.TheParserProducesTheEmptyNotInLeafShape. It needs an
    // IModelMapping registration to resolve the column name, which this project deliberately avoids (see the
    // same note in DestructiveFilterGuardTests).
}
