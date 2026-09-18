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
/// SH-H041 / SH-H042 / SH-H043 / SH-H044 (TASK-116) — every one of these is the same root cause:
/// <b>a leaf degrades to a constant, and the constant then widens to match every row.</b>
///
/// <para>Why this is severe rather than cosmetic: <c>ToExpression()</c> is the store filter for
/// <c>Read</c>/<c>Count</c> <b>and</b> for bulk <c>Update(filter, …)</c> / <c>Delete(filter)</c>. A leaf
/// that widens to match-all on the destructive path empties the table. Worse, a whole specification that
/// reduces to <c>Constant(true)</c> produces literally <c>x =&gt; true</c>, which is exactly the node
/// TASK-109's <c>IsExplicitAllRows</c> whitelists as a <i>deliberate</i> whole-table request — so the
/// guard added for SH-H002 does not catch this; it waves it through the explicit all-rows door.</para>
///
/// <para>The fix is not per-site constants but a tracked degradation flag (<c>Leaf.Unevaluable()</c>):
/// a leaf that could not be evaluated matches nothing, and <b>stays</b> matching nothing under negation.
/// That is what makes the invariant hold for degradation sites nobody has written yet — two sites beyond
/// the four filed findings (<c>BuildComparison</c>, <c>BuildBetween</c>) had already forgotten it.</para>
/// </summary>
public class RuleSpecificationMatchAllTests
{
    private class Widget : Birko.Data.Models.AbstractModel
    {
        public string? Name { get; set; }
        public int Quantity { get; set; }
        public Status State { get; set; }
    }

    public enum Status { Draft, Active, Archived }

    private static Func<Widget, bool> Compile(IRule rule)
        => new RuleSpecification<Widget>(rule).ToExpression().Compile();

    private static List<Widget> Sample() =>
    [
        new() { Guid = Guid.NewGuid(), Name = "alpha", Quantity = 1, State = Status.Draft },
        new() { Guid = Guid.NewGuid(), Name = "beta", Quantity = 21, State = Status.Active },
        new() { Guid = Guid.NewGuid(), Name = "gamma", Quantity = 3, State = Status.Archived },
    ];

    private static int Matches(IRule rule) => Sample().Count(Compile(rule));

    // ── SH-H041 — Like / In / NotIn were declared operators with no arm ──────────────────────────

    [Fact]
    public void In_filters_instead_of_matching_every_row()
    {
        // The headline defect: ComparisonOperator.In is declared, had no arm, and fell to
        // `_ => Expression.Constant(true)`. `Status In [Draft, Active]` matched all three rows.
        var rule = new Rule(nameof(Widget.State), ComparisonOperator.In,
            new object[] { Status.Draft, Status.Active });

        Matches(rule).Should().Be(2, "only Draft and Active are in the set — not the Archived row");
    }

    [Fact]
    public void NotIn_is_the_complement_of_In()
    {
        var rule = new Rule(nameof(Widget.State), ComparisonOperator.NotIn,
            new object[] { Status.Draft, Status.Active });

        Matches(rule).Should().Be(1, "only the Archived row is outside the set");
    }

    [Fact]
    public void In_over_an_empty_set_matches_nothing_and_NotIn_over_it_matches_everything()
    {
        // Set-faithful, mirroring the SQL empty-IN / empty-NOT-IN decision (2026-07-27): "in the empty
        // set" is false for every row, so its negation is legitimately true for every row. This is the
        // one all-rows outcome in this file that is CORRECT — which is why BuildIn marks it a real
        // predicate rather than Unevaluable: only a real predicate may be inverted.
        Matches(new Rule(nameof(Widget.State), ComparisonOperator.In, Array.Empty<object>()))
            .Should().Be(0);
        Matches(new Rule(nameof(Widget.State), ComparisonOperator.NotIn, Array.Empty<object>()))
            .Should().Be(3);
    }

    [Theory]
    [InlineData("alpha", 1)]   // no wildcard — exact
    [InlineData("al%", 1)]     // prefix
    [InlineData("%ta", 1)]     // suffix
    [InlineData("%m%", 1)]     // contains
    public void Like_translates_its_anchors(string pattern, int expected)
    {
        Matches(new Rule(nameof(Widget.Name), ComparisonOperator.Like, pattern))
            .Should().Be(expected);
    }

    [Fact]
    public void Like_with_an_interior_wildcard_matches_nothing_rather_than_guessing()
    {
        // "a%a" has no single string-method equivalent. Unevaluable — and the point of this test is the
        // NEGATED form below, which must not become match-all.
        Matches(new Rule(nameof(Widget.Name), ComparisonOperator.Like, "a%a")).Should().Be(0);
        Matches(new Rule(nameof(Widget.Name), ComparisonOperator.Like, "a%a") { IsNegated = true })
            .Should().Be(0, "a degraded leaf stays match-none under negation");
    }

    // ── SH-H043 / SH-H044 — a non-string member reaching Expression.Not ─────────────────────────

    [Fact]
    public void NotContains_on_a_non_string_member_does_not_match_every_row()
    {
        // SH-H043: BuildStringMethod returned Constant(false) for a non-string member and the
        // NotContains arm wrapped it in Expression.Not — `!false` — so every row matched, with no
        // IsNegated needed.
        Matches(new Rule(nameof(Widget.Quantity), ComparisonOperator.NotContains, "1"))
            .Should().Be(0, "a string operator on an int is unevaluable, and unevaluable never widens");
    }

    [Fact]
    public void Negated_Contains_on_a_non_string_member_does_not_match_every_row()
    {
        // SH-H044's real trigger — the same Constant(false), reaching `if (rule.IsNegated)`.
        Matches(new Rule(nameof(Widget.Quantity), ComparisonOperator.Contains, "1") { IsNegated = true })
            .Should().Be(0);
    }

    // ── The fifth site, found while fixing the four ─────────────────────────────────────────────

    [Fact]
    public void A_negated_comparison_with_an_unconvertible_value_does_not_match_every_row()
    {
        // Not among the filed findings. BuildComparison degrades to "unsatisfiable" when the value
        // cannot be converted to the member type, and that constant reached the same negation.
        Matches(new Rule(nameof(Widget.Quantity), ComparisonOperator.Equal, "not-a-number") { IsNegated = true })
            .Should().Be(0);
    }

    [Fact]
    public void A_negated_Between_with_unconvertible_bounds_does_not_match_every_row()
    {
        var rule = new Rule(nameof(Widget.Quantity), ComparisonOperator.Between, "x") { IsNegated = true };
        rule.UpperValue = "y";

        Matches(rule).Should().Be(0);
    }

    // ── SH-H042 — a disabled rule ───────────────────────────────────────────────────────────────

    [Fact]
    public void A_disabled_root_rule_matches_nothing_and_agrees_with_IsSatisfiedBy()
    {
        // Was Constant(true): a disabled root became `x => true`, so Delete(spec.ToExpression()) emptied
        // the table while IsSatisfiedBy returned false for every entity. Both now say match-none.
        var rule = new Rule(nameof(Widget.Quantity), ComparisonOperator.Equal, 1) { IsEnabled = false };
        var spec = new RuleSpecification<Widget>(rule);
        var predicate = spec.ToExpression().Compile();

        foreach (var w in Sample())
        {
            predicate(w).Should().BeFalse();
            spec.IsSatisfiedBy(w).Should().Be(predicate(w), "the two engines must agree");
        }
    }

    [Fact]
    public void A_disabled_child_inside_a_group_is_still_skipped_not_treated_as_match_none()
    {
        // CONTRACT PIN, not evidence of this fix: BuildGroupExpression already filtered disabled
        // children before TASK-116, which is why SH-H042 was reachable only for a disabled ROOT. This
        // asserts the fix did not change in-group behaviour into `false && …` (which would silently
        // empty every group containing a disabled rule).
        var group = new RuleGroup(LogicOperator.And, new List<IRule>
        {
            new Rule(nameof(Widget.Quantity), ComparisonOperator.Equal, 1) { IsEnabled = false },
            new Rule(nameof(Widget.Name), ComparisonOperator.Equal, "alpha"),
        });

        Matches(group).Should().Be(1, "the disabled child is dropped, leaving the enabled one to filter");
    }

    // ── The unresolved-field case SH-H044 was originally filed against ──────────────────────────

    [Fact]
    public void An_unresolved_field_matches_nothing_even_when_negated()
    {
        // SH-H044's filed claim named this branch; the task's own verification narrowed it, because the
        // `property is null` return precedes the negation. Pinned so the fix cannot regress it — the
        // ordering that made it safe is now backed by the degradation flag rather than by luck.
        Matches(new Rule("NoSuchProperty", ComparisonOperator.Equal, 1)).Should().Be(0);
        Matches(new Rule("NoSuchProperty", ComparisonOperator.Equal, 1) { IsNegated = true }).Should().Be(0);
    }

    // ── An operator with no arm is a code gap, not data ─────────────────────────────────────────

    [Fact]
    public void An_untranslatable_operator_throws_instead_of_widening()
    {
        // The old `_ => Constant(true)` is what made SH-H041 a table-emptier. A ComparisonOperator with
        // no arm is a gap in this translator, so it fails loudly rather than producing a filter that
        // means the opposite of what was asked.
        var rule = new Rule(nameof(Widget.Quantity), (ComparisonOperator)9999, 1);

        FluentActions.Invoking(() => Compile(rule))
            .Should().Throw<NotSupportedException>()
            .WithMessage("*9999*", "the message must name the operator a maintainer has to add");
    }

    // ── Criterion 7: the two engines agree, case by case ────────────────────────────────────────

    public static TheoryData<string, ComparisonOperator, object?, bool> AgreementCases() => new()
    {
        // field,                    operator,                          value,        negated
        { nameof(Widget.State),      ComparisonOperator.In,             null,         false },
        { nameof(Widget.State),      ComparisonOperator.NotIn,          null,         false },
        { nameof(Widget.Name),       ComparisonOperator.Like,           "al%",        false },
        { nameof(Widget.Name),       ComparisonOperator.Contains,       "et",         false },
        { nameof(Widget.Name),       ComparisonOperator.Contains,       "et",         true  },
        // The case the decision settled: a string operator on an int. Both engines now say match-none,
        // in both polarities — the evaluator used to stringify and answer true for the positive form.
        { nameof(Widget.Quantity),   ComparisonOperator.Contains,       "1",          false },
        { nameof(Widget.Quantity),   ComparisonOperator.NotContains,    "1",          false },
        { nameof(Widget.Quantity),   ComparisonOperator.Contains,       "1",          true  },
        { nameof(Widget.Quantity),   ComparisonOperator.StartsWith,     "2",          false },
        { nameof(Widget.Quantity),   ComparisonOperator.Like,           "%1",         false },
        { nameof(Widget.Quantity),   ComparisonOperator.Equal,          1,            false },
        { nameof(Widget.Quantity),   ComparisonOperator.Equal,          1,            true  },
        { "NoSuchProperty",          ComparisonOperator.Equal,          1,            false },
    };

    [Theory]
    [MemberData(nameof(AgreementCases))]
    public void ToExpression_and_IsSatisfiedBy_agree(
        string field, ComparisonOperator op, object? value, bool negated)
    {
        // Two answers from one specification is its own defect, and it is what let SH-H041…SH-H044 sit
        // unnoticed: whichever engine a reader checked, the other one disagreed somewhere else.
        var ruleValue = value ?? new object[] { Status.Draft, Status.Active };
        var rule = new Rule(field, op, ruleValue) { IsNegated = negated };
        var spec = new RuleSpecification<Widget>(rule);
        var predicate = spec.ToExpression().Compile();

        foreach (var w in Sample())
        {
            spec.IsSatisfiedBy(w).Should().Be(predicate(w),
                $"the expression and in-memory engines must agree for {field} {op} '{ruleValue}' (negated: {negated}) on Quantity={w.Quantity}");
        }
    }

    // ── End to end: the destructive path actually narrows ───────────────────────────────────────

    [Fact]
    public void Delete_with_an_In_specification_removes_only_the_matching_rows()
    {
        // Criterion 2, and the whole reason this task outranked the rest of the pool. Before the fix
        // this specification produced `x => true`, and TASK-109's guard classifies that as a deliberate
        // all-rows request — so the store would have emptied itself through the explicit door, with no
        // exception and a success return.
        var store = new InMemoryStore<Widget>();
        var rows = Sample();
        store.Create(rows);
        store.Count(null).Should().Be(3, "premise: all three rows are stored");

        var spec = new RuleSpecification<Widget>(
            new Rule(nameof(Widget.State), ComparisonOperator.In, new object[] { Status.Draft }));

        store.Delete(spec.ToExpression());

        var survivors = store.Read(null).ToList();
        survivors.Should().HaveCount(2, "only the single Draft row matched the specification");
        survivors.Select(w => w.State).Should().NotContain(Status.Draft);
    }

    [Fact]
    public void Delete_with_a_disabled_specification_removes_nothing()
    {
        // The SH-H042 half of the same consequence: a disabled root used to be `x => true`.
        var store = new InMemoryStore<Widget>();
        store.Create(Sample());

        var spec = new RuleSpecification<Widget>(
            new Rule(nameof(Widget.Quantity), ComparisonOperator.Equal, 1) { IsEnabled = false });

        store.Delete(spec.ToExpression());

        store.Count(null).Should().Be(3, "a disabled rule is not an instruction to delete everything");
    }
}
