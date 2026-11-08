using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.Expressions;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Core.Tests;

/// <summary>
/// TASK-867 — <see cref="NullOrderingFold"/> folds a lifted ordering against null to the <c>false</c> C# defines it as and
/// reduces the constants that leaves, so a translator never renders one. Every case is checked against the compiled C#
/// delegate over rows with and without a Score.
/// </summary>
public class NullOrderingFoldTests
{
    public class Row { public int A { get; set; } public int? Score { get; set; } public bool Flag { get; set; } }

    private static readonly int? Nul = null;
    private static readonly int? Five = 5;

    private static readonly Row[] Rows =
    {
        new() { A = 1, Score = null }, new() { A = 2, Score = 5, Flag = true }, new() { A = 5, Score = null, Flag = true },
        new() { A = 6, Score = 2 },
    };

    public static IEnumerable<object[]> Cases() => new (string, Expression<Func<Row, bool>>, string)[]
    {
        ("Score > nul", x => x.Score > Nul, "False"),
        ("nul < Score", x => Nul < x.Score, "False"),
        ("Score <= (int?)null", x => x.Score <= (int?)null, "False"),
        ("A >= nul", x => x.A >= Nul, "False"),
        ("!(Score > nul)", x => !(x.Score > Nul), "True"),
        ("Score > nul || A > 4", x => x.Score > Nul || x.A > 4, "(x.A > 4)"),
        ("!(Score > nul) && A > 4", x => !(x.Score > Nul) && x.A > 4, "(x.A > 4)"),
        ("!(Score > nul || A > 3)", x => !(x.Score > Nul || x.A > 3), "Not((x.A > 3))"),
        ("!(Score < nul && A > 1)", x => !(x.Score < Nul && x.A > 1), "True"),
        ("Flag == (Score > nul)", x => x.Flag == (x.Score > Nul), "(x.Flag == False)"),
        ("A > 0 ? Score > nul : Flag", x => x.A > 0 ? x.Score > Nul : x.Flag, "IIF((x.A > 0), False, x.Flag)"),
        ("(Score > nul) == (A < nul)", x => (x.Score > Nul) == (x.A < Nul), "True"),
    }.Select(c => new object[] { c.Item1, c.Item2, c.Item3 });

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_null_ordering_folds_to_false_and_the_result_matches_CSharp(string name, Expression<Func<Row, bool>> filter, string expected)
    {
        var folded = NullOrderingFold.Rewrite(filter)!;

        folded.Body.ToString().Should().Be(expected, name);
        Rows.Select(folded.Compile()).Should().Equal(Rows.Select(filter.Compile()), name);
    }

    public static IEnumerable<object[]> Untouched() => new (string, Expression<Func<Row, bool>>)[]
    {
        ("Score == nul", x => x.Score == Nul),
        ("Score != nul", x => x.Score != Nul),
        ("Score > five", x => x.Score > Five),
        ("Score > 3", x => x.Score > 3),
        ("A > 3 && Flag", x => x.A > 3 && x.Flag),
        ("Score > Score", x => x.Score > x.Score),
    }.Select(c => new object[] { c.Item1, c.Item2 });

    [Theory]
    [MemberData(nameof(Untouched))]
    public void A_filter_with_no_null_ordering_is_returned_by_reference(string name, Expression<Func<Row, bool>> filter)
        => NullOrderingFold.Rewrite(filter).Should().BeSameAs(filter, name);

    [Fact]
    public void An_operand_that_throws_when_evaluated_is_not_claimed_null()
    {
        Func<int?> boom = () => throw new InvalidOperationException();
        Expression<Func<Row, bool>> filter = x => x.Score > boom();
        NullOrderingFold.Rewrite(filter).Should().BeSameAs(filter);
    }

    [Fact]
    public void Only_a_filter_folded_to_false_matches_nothing()
    {
        NullOrderingFold.MatchesNothing(NullOrderingFold.Rewrite<Row>(x => x.Score > Nul && x.A > 1)).Should().BeTrue();
        NullOrderingFold.MatchesNothing(NullOrderingFold.Rewrite<Row>(x => !(x.Score > Nul))).Should().BeFalse();
        NullOrderingFold.MatchesNothing(NullOrderingFold.Rewrite<Row>(x => x.A > 1)).Should().BeFalse();
        NullOrderingFold.MatchesNothing(null).Should().BeFalse();
    }
}
