using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.MSSql.Tests;

/// <summary>
/// TASK-874 — SQL Server's one LIKE pattern limit, offline: the no-argument forms and TASK-869's ordinal comparisons
/// are refused through the same <see cref="MSSqlValueFragmentDialect.RequireLikePattern"/>. The live contract
/// (<c>StringComparisonFilterContract</c>) measures the boundary against the server.
/// </summary>
public class LikePatternLimitTests
{
    private static readonly MSSqlValueFragmentDialect Dialect = new((value, digits, _) => $"ROUND({value}, {digits})");

    [Fact]
    public void A_pattern_at_the_limit_is_accepted()
    {
        var accept = () => Dialect.RequireLikePattern(new string('a', MSSqlValueFragmentDialect.MaxLikePatternLength), "a Contains on T.Text");
        accept.Should().NotThrow();
    }

    [Fact]
    public void A_pattern_past_the_limit_is_refused_by_name()
    {
        var refuse = () => Dialect.RequireLikePattern(new string('a', MSSqlValueFragmentDialect.MaxLikePatternLength + 1), "a Contains on T.Text");
        refuse.Should().Throw<NotSupportedException>()
            .WithMessage("Cannot translate a Contains on T.Text to SQL Server: its LIKE pattern would be 4001 characters, past SQL Server's limit of 4000 (Msg 8152)*");
    }

    [Fact]
    public void An_ordinal_comparison_is_refused_through_the_same_limit()
    {
        var bound = 0;
        var match = () => Dialect.StringMatch(ConditionType.Like, "Text", new string('a', 3999), StringComparison.Ordinal, _ => { bound++; return "@p"; });
        match.Should().Throw<NotSupportedException>()
            .WithMessage("Cannot translate a string comparison with StringComparison.Ordinal to SQL Server: its LIKE pattern would be 4001 characters*");
        bound.Should().Be(0, "nothing was bound");
    }
}
