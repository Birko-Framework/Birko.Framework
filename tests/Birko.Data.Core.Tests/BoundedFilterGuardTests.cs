using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Birko.Data.Expressions;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Core.Tests;

/// <summary>
/// TASK-329 — the single producer for "refuse a destructive write whose filter covers every row".
///
/// <para><b>Why it exists.</b> The rule had two implementations — one on <c>AbstractBulkStore</c>, one on
/// <c>AbstractAsyncBulkStore</c>, differing only in which all-rows door the refusal names — and the SQL bulk
/// stores derive from neither hierarchy, so they had none. Measured on SQLite before the extraction:
/// <c>Update(x =&gt; !empty.Contains(x.Name), r =&gt; r.Name = "OVR")</c> gave
/// <c>thrown=NONE | overwritten=3 of 3</c>, sync and async. Adding copies three and four was the cheap diff
/// and is the shape § Conventions keeps recording as the cause, so the rule moved here instead.</para>
///
/// <para><b>Why these tests are in THIS project.</b> § TASK-255: <i>a guard declared in `Birko.Data.SQL` is
/// tested in `Birko.Data.SQL.Tests`, not only from its consumer's suite</i> — a guard whose only coverage
/// lives downstream is one a downstream cleanup can delete silently. `BoundedFilterGuard` is declared in
/// `Birko.Data.Core`, so it is tested here, beside <see cref="PredicateScopeTests"/>, in addition to the
/// end-to-end row-count suites in the store projects. This file was added at TASK-329's close gate, which
/// caught the omission.</para>
///
/// <para><b>What these tests do NOT establish.</b> That any particular store <i>calls</i> the guard — that
/// is a wiring question and only a row count on a real store can answer it
/// (`Birko.Data.SQL.SqLite.Tests.BulkStoreActionUpdateBoundedFilterTests`,
/// `Birko.Data.InMemory.Tests.PortableUnboundedFilterGuardTests`,
/// `Birko.Data.JSON.Tests.BaseBulkUnboundedFilterGuardTests`). These pin the producer's own contract, which
/// is the half that used to be duplicated.</para>
/// </summary>
public class BoundedFilterGuardTests
{
    private class Row
    {
        public string? Name { get; set; }
        public int Amount { get; set; }
    }

    private static readonly List<int> Empty = new();
    private static readonly List<int> Some = new() { 1, 5 };

    private static Action Require(Expression<Func<Row, bool>>? filter, string operation = "update",
        string door = "UpdateAll(updates)")
        => () => BoundedFilterGuard.Require(filter, operation, nameof(Row), door);

    // ── refuses a predicate that REDUCES to every row ──────────────────────────────────────────────────

    [Fact]
    public void A_sole_empty_negated_Contains_is_refused()
    {
        Require(x => !Empty.Contains(x.Amount)).Should()
            .Throw<Birko.Data.Exceptions.WholeTableWriteException>();
    }

    [Fact]
    public void A_collapsed_Or_chain_is_refused()
    {
        // `A || TRUE` is TRUE. Included because a partial implementation that only recognised a LONE
        // always-true predicate would pass every other test in this file.
        Require(x => x.Amount > 20 || !Empty.Contains(x.Amount)).Should()
            .Throw<Birko.Data.Exceptions.WholeTableWriteException>();
    }

    [Fact]
    public void The_refusal_carries_the_operation_and_the_entity_name()
    {
        var thrown = Require(x => !Empty.Contains(x.Amount), "delete", "DeleteAllAsync()").Should()
            .Throw<Birko.Data.Exceptions.WholeTableWriteException>();

        thrown.Which.Operation.Should().Be("delete");
        thrown.Which.TableName.Should().Be(nameof(Row));
    }

    // ── the door name is the ONLY thing a caller supplies, and it is not cosmetic ───────────────────────

    /// <summary>
    /// § SH-H037 / TASK-215: a refusal must name a door the refused caller actually has. An async store has
    /// no <c>DeleteAll()</c>, so a message naming one is an opt-out that does not compile — a wall wearing a
    /// door's label. That is the whole reason `allRowsDoor` is a parameter rather than derived from
    /// <paramref name="operation"/>, and the reason it has no default.
    /// </summary>
    [Theory]
    [InlineData("delete", "DeleteAll()")]
    [InlineData("delete", "DeleteAllAsync()")]
    [InlineData("update", "UpdateAll(updates)")]
    [InlineData("update", "UpdateAllAsync(updates)")]
    public void The_caller_supplied_door_appears_verbatim_in_the_message(string operation, string door)
    {
        var thrown = Require(x => !Empty.Contains(x.Amount), operation, door).Should()
            .Throw<Birko.Data.Exceptions.WholeTableWriteException>();

        thrown.Which.Message.Should().Contain(door);
    }

    /// <summary>
    /// ⚠ Structural pin, and the reason it is worth its four lines: the previous two copies of this rule
    /// differed <b>only</b> in the door string, and the async one shipped naming <c>DeleteAll()</c>
    /// (TASK-215). A default here would let a caller inherit the wrong door silently, which is precisely
    /// the defect that made the parameter necessary. Do not satisfy a failure here by adding one back.
    /// </summary>
    [Fact]
    public void The_door_parameter_has_no_default_so_a_caller_cannot_inherit_the_wrong_one()
    {
        var p = typeof(BoundedFilterGuard)
            .GetMethod(nameof(BoundedFilterGuard.Require))!
            .GetParameters();

        p.Should().HaveCount(4);
        p[3].Name.Should().Be("allRowsDoor");
        p[3].HasDefaultValue.Should().BeFalse(
            "a default door is how the async stores came to cite DeleteAll(), a method their callers "
            + "do not have");
    }

    // ── the doors that must stay open: without these this is a wall, not a guard ────────────────────────

    [Fact]
    public void An_explicit_all_rows_constant_is_allowed_through()
    {
        // The documented DeleteAll()/UpdateAll() synonym, checked BEFORE the reduction (§ SH-H037).
        Require(x => true).Should().NotThrow();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Every_spelling_the_normalizer_folds_to_that_constant_is_treated_the_same(bool flag)
    {
        // x => capturedFlag folds to a single ConstantExpression. The TRUE case is the synonym and passes;
        // the FALSE case matches nothing and must also pass, because the guard fires on "everything" and
        // never on "nothing".
        Require(x => flag).Should().NotThrow();
    }

    [Fact]
    public void A_null_filter_is_IGNORED_here_rather_than_refused()
    {
        // Deliberate, and both previous copies did the same: each caller has its own RequireFilter whose
        // ArgumentNullException names the same door. Refusing here as well would give one mistake two
        // different messages, and the two guards are kept separate on purpose.
        Require(null).Should().NotThrow();
    }

    [Fact]
    public void A_bounded_filter_is_allowed_through()
    {
        Require(x => x.Amount > 20).Should().NotThrow();
    }

    [Fact]
    public void An_And_chain_containing_an_always_true_term_is_still_bounded()
    {
        // `A && TRUE` is `A`. A false refusal breaks working code, which PredicateScope rates worse than
        // the hole it closes — so this direction gets a test of its own.
        Require(x => x.Amount > 20 && !Empty.Contains(x.Amount)).Should().NotThrow();
    }

    [Fact]
    public void A_negated_Contains_over_a_NON_empty_set_is_allowed_through()
    {
        Require(x => !Some.Contains(x.Amount)).Should().NotThrow();
    }

    // ── the ordering of the two checks is observable, and it is what makes the door a door ──────────────

    /// <summary>
    /// `ReducesToAllRows(x =&gt; true)` is <b>also</b> true, so the explicit-door check has to run first or
    /// the documented synonym would be refused. The assertion pair is what makes that ordering visible:
    /// swapping the two checks in the producer reds this test and nothing else here.
    /// </summary>
    [Fact]
    public void The_explicit_door_check_runs_before_the_reduction_check()
    {
        Expression<Func<Row, bool>> allRows = x => true;

        PredicateScope.IsExplicitAllRows(allRows).Should().BeTrue();
        PredicateScope.ReducesToAllRows(allRows).Should().BeTrue(
            "both answer yes for this predicate, which is exactly why the order matters");

        Require(allRows).Should().NotThrow();
    }
}
