using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.InMemory.Stores;
using Birko.Data.Patterns.Specification;
using Birko.Rules;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Patterns.Tests;

/// <summary>
/// SH-H045 (TASK-320) — <c>RuleGroup.IsNegated</c> ("negate the entire group result") was ignored by both
/// <c>RuleSpecification.ToExpression()</c> and <c>RuleEvaluator</c>, so <c>NOT(a AND b)</c> was answered as
/// <c>(a AND b)</c>: exactly the complement of the intended rows. Through <c>Delete(spec.ToExpression())</c> that
/// deletes the rows the caller meant to keep.
///
/// <para>The fix pushes negation to the leaves (De Morgan) instead of wrapping the combined group in a
/// <c>Not</c>, because a naive <c>Not</c> would read an unevaluable leaf's match-none as a real <c>false</c> and
/// invert it to match-all — TASK-116's hole, reopened one level up. Every case runs through <b>both</b> engines
/// (the compiled expression and <c>IsSatisfiedBy</c>, which routes through <c>RuleEvaluator</c>), and the two
/// must agree.</para>
/// </summary>
public class RuleGroupNegationTests
{
    private class Widget : Birko.Data.Models.AbstractModel
    {
        public string? Name { get; set; }
        public int Quantity { get; set; }
        public Status State { get; set; }
    }

    public enum Status { Draft, Active, Archived }

    private static List<Widget> Sample() =>
    [
        new() { Guid = Guid.NewGuid(), Name = "alpha", Quantity = 1, State = Status.Draft },
        new() { Guid = Guid.NewGuid(), Name = "beta", Quantity = 21, State = Status.Active },
        new() { Guid = Guid.NewGuid(), Name = "gamma", Quantity = 3, State = Status.Archived },
    ];

    private static Rule Leaf(string field, ComparisonOperator op, object? value, bool negated = false)
        => new(field, op, value) { IsNegated = negated };

    private static RuleGroup Group(LogicOperator logic, bool negated, params IRule[] rules)
        => new(logic, rules.ToList()) { IsNegated = negated };

    /// The names a rule selects, through both engines — which must agree.
    private static string[] Names(IRule rule)
    {
        var spec = new RuleSpecification<Widget>(rule);
        var compiled = spec.ToExpression().Compile();
        var byExpression = Sample().Where(compiled).Select(w => w.Name!).OrderBy(n => n).ToArray();
        var byEvaluator = Sample().Where(spec.IsSatisfiedBy).Select(w => w.Name!).OrderBy(n => n).ToArray();
        byEvaluator.Should().Equal(byExpression, "ToExpression() and IsSatisfiedBy must agree");
        return byExpression;
    }

    [Fact]
    public void Negated_and_group_is_its_complement()
    {
        // (Quantity > 2 AND State == Active) selects beta; NOT of it is everything else.
        var rule = Group(LogicOperator.And, negated: true,
            Leaf(nameof(Widget.Quantity), ComparisonOperator.GreaterThan, 2),
            Leaf(nameof(Widget.State), ComparisonOperator.Equal, Status.Active));

        Names(rule).Should().Equal("alpha", "gamma");
    }

    [Fact]
    public void Negated_or_group_is_its_complement()
    {
        var rule = Group(LogicOperator.Or, negated: true,
            Leaf(nameof(Widget.Name), ComparisonOperator.Equal, "alpha"),
            Leaf(nameof(Widget.Name), ComparisonOperator.Equal, "beta"));

        Names(rule).Should().Equal("gamma");
    }

    [Fact]
    public void A_negated_leaf_inside_a_negated_group_cancels_out()
    {
        // NOT( NOT(State == Draft) ) is State == Draft.
        var rule = Group(LogicOperator.And, negated: true,
            Leaf(nameof(Widget.State), ComparisonOperator.Equal, Status.Draft, negated: true));

        Names(rule).Should().Equal("alpha");
    }

    [Fact]
    public void Nested_negated_groups_compose()
    {
        // NOT( Name == alpha OR NOT(Quantity > 2) )  =  Name != alpha AND Quantity > 2  →  beta, gamma
        var rule = Group(LogicOperator.Or, negated: true,
            Leaf(nameof(Widget.Name), ComparisonOperator.Equal, "alpha"),
            Group(LogicOperator.And, negated: true,
                Leaf(nameof(Widget.Quantity), ComparisonOperator.GreaterThan, 2)));

        Names(rule).Should().Equal("beta", "gamma");
    }

    [Fact]
    public void An_unevaluable_leaf_in_a_negated_group_does_not_widen_to_every_row()
    {
        // `Contains` on an int cannot be evaluated: it matches nothing, and stays matching nothing under
        // negation. NOT(Quantity > 0 AND Quantity Contains "1") is therefore NOT(Quantity > 0) OR <nothing> —
        // no row. A `Not` wrapped around the whole group would read the unevaluable leaf as a real `false`
        // and return all three.
        var rule = Group(LogicOperator.And, negated: true,
            Leaf(nameof(Widget.Quantity), ComparisonOperator.GreaterThan, 0),
            Leaf(nameof(Widget.Quantity), ComparisonOperator.Contains, "1"));

        Names(rule).Should().BeEmpty();
    }

    [Fact]
    public void A_negated_empty_group_matches_nothing()
    {
        Names(Group(LogicOperator.And, negated: true)).Should().BeEmpty();
        Names(Group(LogicOperator.Or, negated: true)).Should().BeEmpty();
    }

    [Fact]
    public void Delete_with_a_negated_group_keeps_the_rows_the_group_selects()
    {
        // The destructive path the finding names, asserted on the rows left — not on "no exception".
        var store = new InMemoryStore<Widget>();
        store.Create(Sample());
        var keepArchived = Group(LogicOperator.Or, negated: true,
            Leaf(nameof(Widget.State), ComparisonOperator.Equal, Status.Archived));

        store.Delete(new RuleSpecification<Widget>(keepArchived).ToExpression());

        store.Read().Select(w => w.Name).Should().Equal("gamma");
    }
}
