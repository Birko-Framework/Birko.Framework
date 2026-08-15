using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.Expressions;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Core.Tests;

/// <summary>
/// TASK-212 — the portable "how much does this predicate constrain?" analyser that the destructive guards
/// consult before a backend translates anything.
///
/// <para>It exists because a guard on the <i>translated</i> query cannot see an unbounded filter. Measured on
/// MongoDB.Driver 3.2.0, <c>x =&gt; !empty.Contains(x.Amount)</c> renders <c>{ "Amount": { "$nin": [] } }</c> —
/// a one-element document that looks like an ordinary field predicate and matches every document. The C#
/// expression is unambiguous where that rendering is not.</para>
///
/// <para>Two questions, deliberately separate: <see cref="PredicateScope.IsExplicitAllRows"/> ("did the caller
/// say every row out loud") and <see cref="PredicateScope.ReducesToAllRows"/> ("does this happen to cover
/// every row"). A destructive guard refuses the second while honouring the first, so conflating them would
/// either break the documented <c>DeleteAll()</c> synonym or bless the accident.</para>
///
/// <para><b>The false-positive direction is the dangerous one</b> and gets the most cases below: a refusal
/// that fires on a predicate which does constrain something breaks working code, which is worse than the hole
/// it closes.</para>
/// </summary>
public class PredicateScopeTests
{
    private class Row
    {
        public string? Name { get; set; }
        public int Amount { get; set; }
        public bool Active { get; set; }
        public List<int>? Tags { get; set; }
    }

    private static readonly List<int> Empty = new();
    private static readonly List<int> Some = new() { 1, 5 };
    private static readonly int[] EmptyArray = Array.Empty<int>();
    private static readonly List<int>? NullSet = null;

    private static bool Reduces(Expression<Func<Row, bool>> e) => PredicateScope.ReducesToAllRows(e);
    private static bool Explicit(Expression<Func<Row, bool>> e) => PredicateScope.IsExplicitAllRows(e);

    // ── the explicit door ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void A_constant_true_is_explicit_and_also_covers_everything()
    {
        Explicit(x => true).Should().BeTrue();
        Reduces(x => true).Should().BeTrue("the explicit door is a special case of covering everything");
    }

    [Fact]
    public void Normalization_folds_the_indirect_spellings_into_the_same_one_node()
    {
        var flag = true;

        Explicit(x => 1 == 1).Should().BeTrue();
        Explicit(x => flag).Should().BeTrue();
    }

    [Fact]
    public void A_constant_false_is_neither()
    {
        Explicit(x => false).Should().BeFalse();
        Reduces(x => false).Should().BeFalse("matching nothing is not matching everything");
    }

    [Fact]
    public void A_real_predicate_is_neither()
    {
        Explicit(x => x.Amount > 4).Should().BeFalse();
        Reduces(x => x.Amount > 4).Should().BeFalse();
    }

    [Fact]
    public void A_null_predicate_is_neither()
    {
        PredicateScope.IsExplicitAllRows(null).Should().BeFalse();
        PredicateScope.ReducesToAllRows(null).Should().BeFalse("the null case belongs to RequireFilter");
    }

    // ── the defect shape: an empty negated Contains ──────────────────────────────────────────────────────

    [Fact]
    public void An_empty_negated_Contains_covers_everything_but_is_not_explicit()
    {
        // The whole point: it must be caught (Reduces) and must NOT be mistaken for the deliberate door.
        Reduces(x => !Empty.Contains(x.Amount)).Should().BeTrue();
        Explicit(x => !Empty.Contains(x.Amount)).Should().BeFalse();
    }

    [Fact]
    public void Both_the_instance_and_the_extension_Contains_forms_are_recognised()
    {
        // A List<T> binds the instance method; an array binds Enumerable.Contains, putting the collection in
        // argument 0 instead of Object. Missing one form would leave half the callers unguarded.
        Reduces(x => !Empty.Contains(x.Amount)).Should().BeTrue("List<T>.Contains — instance form");
        Reduces(x => !EmptyArray.Contains(x.Amount)).Should().BeTrue("Enumerable.Contains — extension form");
    }

    [Fact]
    public void An_empty_UN_negated_Contains_does_not_cover_everything()
    {
        // `$in: []` matches nothing. Reporting it as unbounded would refuse a harmless no-op delete.
        Reduces(x => Empty.Contains(x.Amount)).Should().BeFalse();
    }

    [Fact]
    public void A_NON_empty_negated_Contains_does_not_cover_everything()
        => Reduces(x => !Some.Contains(x.Amount)).Should().BeFalse();

    // ── boolean algebra ──────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void An_OR_covers_everything_when_either_side_does()
    {
        Reduces(x => x.Amount > 4 || !Empty.Contains(x.Amount)).Should().BeTrue();
        Reduces(x => !Empty.Contains(x.Amount) || x.Amount > 4).Should().BeTrue();
        Reduces(x => x.Amount > 4 || x.Active).Should().BeFalse();
    }

    [Fact]
    public void An_AND_covers_everything_only_when_both_sides_do()
    {
        Reduces(x => x.Amount > 4 && !Empty.Contains(x.Amount)).Should().BeFalse("A && TRUE is A, which constrains");
        Reduces(x => !Empty.Contains(x.Amount) && !EmptyArray.Contains(x.Amount)).Should().BeTrue();
    }

    [Fact]
    public void A_negated_always_false_term_covers_everything()
    {
        // `!(empty.Contains(x))` — the inner term matches nothing, so its negation matches everything. This
        // is the same shape as the headline case, written the long way round.
        Reduces(x => !(Empty.Contains(x.Amount))).Should().BeTrue();
    }

    [Fact]
    public void A_negated_bounded_term_does_not_cover_everything()
        => Reduces(x => !(x.Amount > 4)).Should().BeFalse();

    [Fact]
    public void Nested_grouping_is_respected()
    {
        // `(A && B) || TRUE` is TRUE; `(A || TRUE) && B` is B, which constrains.
        Reduces(x => (x.Amount > 4 && x.Active) || !Empty.Contains(x.Amount)).Should().BeTrue();
        Reduces(x => (x.Amount > 4 || !Empty.Contains(x.Amount)) && x.Active).Should().BeFalse();
    }

    // ── the false-positive guards: shapes that must NEVER be reported as unbounded ───────────────────────

    [Fact]
    public void A_string_Contains_is_a_substring_test_and_is_never_unbounded()
    {
        // `!x.Name.Contains("")` is NOT always true — and even where it is, it is a substring test with
        // completely different semantics. Treating it as set membership would refuse working text filters.
        Reduces(x => !x.Name!.Contains("a")).Should().BeFalse();
        Reduces(x => !x.Name!.Contains("")).Should().BeFalse();
    }

    [Fact]
    public void A_per_entity_collection_says_nothing_about_scope()
    {
        // `x.Tags` is a property of the entity, so its emptiness varies per row and cannot make the whole
        // predicate unbounded. Evaluating it is impossible anyway — it references the lambda parameter.
        Reduces(x => !x.Tags!.Contains(1)).Should().BeFalse();
    }

    [Fact]
    public void A_null_collection_is_not_claimed_to_be_empty()
    {
        // `null.Contains(x)` throws when evaluated and a driver rejects it at translation, so the operation
        // fails either way. Modelling it would be modelling a case that never reaches a write.
        Reduces(x => !NullSet!.Contains(x.Amount)).Should().BeFalse();
    }

    [Fact]
    public void A_collection_that_cannot_be_evaluated_offline_is_not_claimed_to_be_empty()
    {
        // The analyser compiles the collection sub-expression to inspect it. When that throws, it must decline
        // rather than guess — "when in doubt, do not refuse".
        Func<List<int>> boom = () => throw new InvalidOperationException("nope");

        Reduces(x => !boom().Contains(x.Amount)).Should().BeFalse();
    }

    [Fact]
    public void A_bare_boolean_column_is_not_unbounded()
        => Reduces(x => x.Active).Should().BeFalse();
}
