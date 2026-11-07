using System;
using System.Collections.Generic;
using System.Data.Common;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests;

/// <summary>
/// TASK-861 — the condition tree reduces with three-valued logic, so an always-false term (the empty <c>IN</c>) under a
/// NOT makes its group always-true. <c>NOT (1 = 0 AND A)</c> used to render as written, a non-empty WHERE true of every
/// row, so the whole-table guard let a DELETE through (measured on all four SQL providers: 6 of 6 rows). The row-set
/// consequences are pinned by the shared <c>AlwaysFalseGroupContract</c>; this suite pins the reduction and rendering.
/// </summary>
public class AlwaysFalseGroupReductionTests
{
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

    private static Condition False() => new("Widgets.Count", Array.Empty<object?>(), ConditionType.In);

    private static Condition True() => new("Widgets.Count", Array.Empty<object?>(), ConditionType.In, isNot: true);

    private static Condition Real(string name = "Widgets.Count", int value = 5)
        => Condition.CreateValue(name, value, ConditionType.Greather);

    private static Condition And(params Condition[] children) => Group(false, false, children);
    private static Condition Or(params Condition[] children) => Group(true, false, children);
    private static Condition NotAnd(params Condition[] children) => Group(false, true, children);
    private static Condition NotOr(params Condition[] children) => Group(true, true, children);

    private static Condition Group(bool isOr, bool isNot, params Condition[] children)
        => new(null, null, ConditionType.Equal, isNot: isNot, isOr: isOr, subConditions: children);

    private static string Render(Condition condition)
        => new FakeConnector().ConditionDefinition(condition, new TestDbCommand());

    public static IEnumerable<object?[]> Truths() => new[]
    {
        new object?[] { "FALSE", False(), false },
        new object?[] { "TRUE", True(), true },
        new object?[] { "A", Real(), null },
        new object?[] { "A AND FALSE", And(Real(), False()), false },
        new object?[] { "A AND TRUE", And(Real(), True()), null },
        new object?[] { "A OR TRUE", Or(Real(), True()), true },
        new object?[] { "A OR FALSE", Or(Real(), False()), null },
        new object?[] { "NOT (FALSE AND A)", NotAnd(False(), Real()), true },
        new object?[] { "NOT (A AND (B AND FALSE))", NotAnd(Real(), And(Real("Widgets.Name"), False())), true },
        new object?[] { "NOT (A OR FALSE)", NotOr(Real(), False()), null },
        new object?[] { "NOT (FALSE OR FALSE)", NotOr(False(), False()), true },
        new object?[] { "A OR NOT (FALSE AND B)", Or(Real(), NotAnd(False(), Real("Widgets.Name"))), true },
        new object?[] { "NOT (NOT (FALSE AND A))", Group(false, true, NotAnd(False(), Real())), false },
        new object?[] { "NOT (A OR TRUE)", NotOr(Real(), True()), false },
    };

    [Theory]
    [MemberData(nameof(Truths))]
    public void A_tree_reduces_with_three_valued_logic(string label, Condition condition, bool? expected)
        => AbstractConnectorBase.ConstantTruth(condition).Should().Be(expected, label);

    [Theory]
    [MemberData(nameof(Truths))]
    public void The_guard_refuses_exactly_the_trees_that_reduce_to_true(string label, Condition condition, bool? expected)
        => AbstractConnectorBase.WouldTargetEveryRow(new[] { condition }).Should().Be(expected == true, label);

    [Fact]
    public void A_negated_always_false_group_renders_no_where()
    {
        Render(NotAnd(False(), Real())).Should().BeEmpty();
        Render(NotAnd(Real(), And(Real("Widgets.Name"), False()))).Should().BeEmpty();
    }

    [Fact]
    public void An_always_false_group_renders_the_constant_alone()
    {
        Render(And(Real(), False())).Should().Be(AbstractConnectorBase.AlwaysFalseSql);
        Render(Group(false, true, NotAnd(False(), Real()))).Should().Be(AbstractConnectorBase.AlwaysFalseSql);
    }

    [Fact]
    public void An_always_false_OR_term_is_dropped()
    {
        Render(Or(Real(), False())).Should().Be("Widgets.Count > @WHEREWidgetsCount0_0");
        Render(NotOr(Real(), False())).Should().Be("NOT (Widgets.Count > @WHEREWidgetsCount0_0)");
    }

    [Fact]
    public void A_flat_chain_that_reduces_to_true_is_refused()
    {
        // In a flat list a term's IsOr is its separator from the previous term, so the OR-joined term is a one-child
        // group around the negated AND.
        var chain = new[] { Real(), Group(true, false, NotAnd(False(), Real("Widgets.Name"))) };
        AbstractConnectorBase.WouldTargetEveryRow(chain).Should().BeTrue("A OR NOT (FALSE AND B) is every row");

        var command = new TestDbCommand { CommandText = "DELETE FROM Widgets" };
        var act = () => new FakeConnector().AddRequiredWhere(chain, command, "delete", "Widgets");
        act.Should().Throw<Birko.Data.Exceptions.WholeTableWriteException>();
    }
}
