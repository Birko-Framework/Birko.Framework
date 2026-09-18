using System;
using System.Linq.Expressions;
using Birko.Data.Expressions;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Core.Tests;

/// <summary>
/// TASK-222 — <see cref="ExpressionNormalizer"/>'s ternary and <c>??</c> expansions routinely produce
/// <c>X &amp;&amp; true</c>, <c>X &amp;&amp; false</c> and their Or duals; a ternary whose branches are
/// literals (<c>c ? true : false</c>) yields nothing else. Those are semantically trivial and a parser is
/// entitled to be surprised by them: RavenDB renders <c>X &amp;&amp; true</c> as the <b>malformed</b> RQL
/// <c>where (Score = $p0 and)</c> — measured — rather than rejecting it.
///
/// Reducing them keeps the normalizer's contract ("semantically identical, only the shape changes") while
/// handing every downstream parser a tree with no dangling constant operands. SQL and ElasticSearch have
/// consumed this normalizer all along, so these are also the pin that the reduction did not change what
/// they receive for any non-trivial predicate.
/// </summary>
public class BooleanConstantSimplificationTests
{
    public class Model { public bool Active { get; set; } public int Amount { get; set; } public int? Score { get; set; } }

    private static Expression Norm(Expression<Func<Model, bool>> e) => ExpressionNormalizer.Normalize(e.Body)!;

    [Fact]
    public void A_ternary_with_literal_branches_collapses_to_its_test()
    {
        // c ? true : false  ->  (c && true) || (!c && false)  ->  c
        // Unreduced, RavenDB rendered this as `where (Score = $p0 and)`.
        Expression<Func<Model, bool>> e = x => x.Score == null ? true : false;

        var normalized = Norm(e);

        normalized.ToString().Should().NotContain("True").And.NotContain("False");
        normalized.NodeType.Should().Be(ExpressionType.Equal, "only the test survives");
    }

    [Fact]
    public void A_ternary_with_inverted_literal_branches_collapses_to_the_negated_test()
    {
        Expression<Func<Model, bool>> e = x => x.Active ? false : true;

        var normalized = Norm(e);

        normalized.ToString().Should().NotContain("True").And.NotContain("False");
        Expression.Lambda<Func<Model, bool>>(normalized, e.Parameters).Compile()(new Model { Active = true })
            .Should().BeFalse();
        Expression.Lambda<Func<Model, bool>>(normalized, e.Parameters).Compile()(new Model { Active = false })
            .Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(MeaningPreservingCases))]
    public void Simplification_preserves_meaning(string label, Expression<Func<Model, bool>> e)
    {
        // The reduction must be a pure shape change. Oracle: the ORIGINAL lambda compiled, compared
        // against the normalized one over every relevant input — shape assertions alone would not catch
        // an inverted branch.
        var original = e.Compile();
        var normalized = Expression.Lambda<Func<Model, bool>>(Norm(e), e.Parameters).Compile();

        foreach (var m in new[]
        {
            new Model { Active = true,  Amount = 9, Score = 5 },
            new Model { Active = false, Amount = 9, Score = null },
            new Model { Active = true,  Amount = 1, Score = null },
            new Model { Active = false, Amount = 1, Score = 5 },
        })
        {
            normalized(m).Should().Be(original(m), $"{label} on Active={m.Active} Amount={m.Amount} Score={m.Score}");
        }
    }

    public static TheoryData<string, Expression<Func<Model, bool>>> MeaningPreservingCases() => new()
    {
        { "literal branches", x => x.Score == null ? true : false },
        { "inverted literals", x => x.Active ? false : true },
        { "true branch only", x => x.Amount > 4 ? true : x.Active },
        { "false branch only", x => x.Amount > 4 ? x.Active : false },
        { "real branches", x => x.Amount > 4 ? x.Active : x.Score == null },
        { "nested", x => x.Amount > 4 ? (x.Active ? x.Score == null : x.Active) : x.Active },
        { "as a conjunct", x => x.Active && (x.Amount > 4 ? x.Active : x.Score == null) },
        { "negated", x => !(x.Amount > 4 ? x.Active : x.Score == null) },
    };

    [Fact]
    public void A_predicate_with_real_branches_still_expands_to_boolean_algebra()
    {
        // The pin that the reduction did NOT swallow the expansion the normalizer exists for — SQL and
        // ElasticSearch depend on receiving AND/OR/NOT here, not a ConditionalExpression.
        Expression<Func<Model, bool>> e = x => x.Amount > 4 ? x.Active : x.Score == null;

        Norm(e).NodeType.Should().Be(ExpressionType.OrElse);
    }
}
