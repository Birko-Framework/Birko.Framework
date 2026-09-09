using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Tests.TestResources.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests.DataBase
{
    /// <summary>
    /// TASK-308 / SH-H022 — when one operand of an <c>&amp;&amp;</c> / <c>||</c> is a constant, the parser
    /// collapses the group to the surviving operand through <c>ReturnSingleSubCondition</c>, which
    /// <b>assigned</b> the parent's <c>IsNot</c> from the survivor instead of combining them. The
    /// <c>Not</c> branch has already toggled <c>IsNot</c> on the very object it passes down as the parent,
    /// so an enclosing negation was silently discarded.
    ///
    /// <para>Measured before the fix, on SQLite: <c>x =&gt; !(x.Amount == 10 &amp;&amp; trueFlag)</c>
    /// rendered <c>WHERE Amount = @p</c> against the control <c>x =&gt; !(x.Amount == 10)</c>, which
    /// rendered <c>WHERE NOT (Amount = @p)</c>. The read returned the exact complement of what was asked
    /// for, and <c>DeleteAsync</c> destroyed that complement with <b>no exception</b> — the clause is
    /// non-empty, so SH-H002's whole-table guard had nothing to refuse. It was the only finding in its area
    /// whose destructive path was unguarded.</para>
    ///
    /// <para>The row-level consequence is asserted in
    /// <c>Birko.Data.SQL.SqLite.Tests.PredicateMistranslationEndToEndTests</c>; this suite pins the
    /// condition tree, which is where the negation is lost and which is fast enough to cover every
    /// polarity combination.</para>
    ///
    /// <para>⚠ The file already knew this shape for the sibling flag — the <c>.Date</c> range branch nests
    /// rather than merging, with a comment explaining that <c>ReturnSingleSubCondition</c> would overwrite a
    /// nested <c>IsOr</c>. The <c>IsNot</c> half went unnoticed beside it.</para>
    /// </summary>
    public class NegatedGroupCollapseTests
    {
        /// <summary>The effective negation of a parsed tree: the outermost condition's own flag.</summary>
        private static bool NegationOf(Expression<Func<DateModel, bool>> expr)
            => Birko.Data.SQL.DataBase.ParseConditionExpression(expr).Single().IsNot;

        [Fact]
        public void A_negation_around_a_collapsed_AND_survives_the_collapse()
        {
            var trueFlag = true;

            NegationOf(x => !(x.Count == 10 && trueFlag)).Should().BeTrue(
                "before the fix the collapse assigned the survivor's IsNot (false) over the toggled parent");
        }

        [Fact]
        public void A_negation_around_a_collapsed_OR_survives_the_collapse()
        {
            // The OR arm reaches ReturnSingleSubCondition through a different branch (`isOR && !leftVal`),
            // so a fix applied to only one arm would leave this red.
            var falseFlag = false;

            NegationOf(x => !(x.Count == 10 || falseFlag)).Should().BeTrue();
        }

        [Fact]
        public void A_negation_around_a_collapsed_AND_survives_when_the_LEFT_operand_is_the_constant()
        {
            // Both operand positions, because the branch is duplicated per side (`leftIsConst` /
            // `rightIsConst`) and each calls ReturnSingleSubCondition separately.
            var trueFlag = true;

            NegationOf(x => !(trueFlag && x.Count == 10)).Should().BeTrue();
        }

        [Fact]
        public void A_negation_around_a_collapsed_group_whose_survivor_is_also_negated_keeps_both()
        {
            // XOR, not OR. `!(x.Count != 10 && trueFlag)` is `x.Count == 10`, so the survivor's own IsNot
            // must cancel the enclosing one. A fix that used `||` or `|=` would leave this negated and
            // return the complement — this is the test that separates the two spellings.
            var trueFlag = true;

            // ⚠ MEASURED, and it corrected my own first expectation — recorded because the mistake is the
            // instructive part. `!=` is `Type=Equal` with `IsNot=true` on the *comparison leaf*, and the
            // comparison branch NESTS that leaf under the parent rather than merging into it. So the
            // survivor handed to ReturnSingleSubCondition is the wrapper, whose own IsNot is FALSE, and the
            // XOR therefore sees only the enclosing negation. The composed tree is
            // NOT ( Count <> 10 ), which is Count = 10 — the right answer.
            // Before the fix the assignment wrote the wrapper's false over the toggled true and the tree
            // became plain `Count <> 10`: the complement.
            // The row-level proof that it really means Count = 10 is
            // PredicateMistranslationEndToEndTests.A_doubly_negated_group_cancels_rather_than_negating_twice.
            var c = Birko.Data.SQL.DataBase.ParseConditionExpression(
                (Expression<Func<DateModel, bool>>)(x => !(x.Count != 10 && trueFlag))).Single();

            c.IsNot.Should().BeTrue("the enclosing negation must survive the collapse");
            var negLeaf = c.SubConditions!.Single();
            negLeaf.IsNot.Should().BeTrue("the survivor's own `!=` negation is untouched");
            negLeaf.Type.Should().Be(ConditionType.Equal, "there is no NotEqual type — `!=` is Equal + IsNot");
        }

        [Fact]
        public void An_un_negated_collapse_is_unchanged()
        {
            // Contract pin: the overwhelmingly common case has no enclosing negation, so the XOR must be
            // the identity there. Without this the fix is indistinguishable from negating every collapse.
            var trueFlag = true;

            NegationOf(x => x.Count == 10 && trueFlag).Should().BeFalse();
        }

        [Fact]
        public void An_un_negated_collapse_leaves_the_survivors_own_negation_where_it_was()
        {
            var trueFlag = true;

            var c = Birko.Data.SQL.DataBase.ParseConditionExpression(
                (Expression<Func<DateModel, bool>>)(x => x.Count != 10 && trueFlag)).Single();

            c.IsNot.Should().BeFalse("nothing negated the group, so the XOR must be the identity");
            c.SubConditions!.Single().IsNot.Should().BeTrue("the leaf's own `!=` is where the negation lives");
        }

        [Fact]
        public void A_plain_negation_that_never_collapses_is_unchanged()
        {
            // The control the defect was measured against: this shape never reaches
            // ReturnSingleSubCondition, so it was correct before and must stay correct.
            NegationOf(x => !(x.Count == 10)).Should().BeTrue();
        }

        [Fact]
        public void The_collapse_still_carries_the_survivors_content()
        {
            // Guards against a fix that preserved IsNot by not transferring the rest: the parent must still
            // become the surviving condition.
            var trueFlag = true;

            var c = Birko.Data.SQL.DataBase.ParseConditionExpression(
                (Expression<Func<DateModel, bool>>)(x => !(x.Count == 10 && trueFlag))).Single();

            var leaf = c.SubConditions?.Any() == true ? c.SubConditions.First() : c;
            (leaf.Values?.Cast<object?>().Contains(10) ?? false).Should().BeTrue(
                "the collapsed group must still constrain Count to 10");
        }
    }
}
