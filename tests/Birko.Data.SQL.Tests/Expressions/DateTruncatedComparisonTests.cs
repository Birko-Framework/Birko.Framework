using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Conditions;
using FluentAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Xunit;

namespace Birko.Data.SQL.Tests.Expressions;

[Table("DateProbes")]
public class DateProbeEntity : Birko.Data.Models.AbstractLogModel
{
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Translation guard for <c>x.DateColumn.Date &lt;op&gt; value</c> (Symbio TASK-355).
///
/// The parser used to render this as <c>DATE(col) &lt;op&gt; @param</c> and bind the right-hand side as
/// a <b>DateTime</b>. Under Microsoft.Data.Sqlite a DateTime column is stored as the text
/// <c>yyyy-MM-dd HH:mm:ss.FFFFFFF</c>, so <c>DATE(col)</c> evaluates to the 10-character
/// <c>yyyy-MM-dd</c> while the parameter serialises to the full <c>yyyy-MM-dd 00:00:00</c>. Measured
/// against a real database: equality matched <b>0</b> rows where 4 matched, and <c>&lt;</c> matched
/// <b>14</b> where 4 should have — the shorter prefix sorts first, so it over-matched by a whole day.
/// Both silent: the query ran and returned a plausible wrong answer.
///
/// It is now rewritten to a half-open range over the RAW column, which is correct, sargable (a function
/// on the column defeats an index), and free of <c>DATE()</c> — not a function in T-SQL at all, so the
/// old form was a hard syntax error on MSSql.
///
/// These assert on the parsed <see cref="Condition"/> tree rather than on executed SQL, so they pin the
/// shape independently of any one dialect. The end-to-end proof against real SQLite lives in Symbio's
/// <c>DatePredicateTranslationTests</c>.
/// </summary>
public class DateTruncatedComparisonTests
{
    private static readonly DateTime Day = new(2026, 8, 7, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ColumnDate_Equals_ProducesHalfOpenRange_NotADateFunction()
    {
        var date = Day.AddHours(15);   // a non-midnight instant, as a request-supplied date would be
        Expression<Func<DateProbeEntity, bool>> expr = x => x.OpenedAt.Date == date.Date;

        var conditions = Birko.Data.SQL.DataBase.ParseConditionExpression(expr).ToList();
        var leaves = Leaves(conditions).ToList();

        leaves.Should().HaveCount(2, "the day becomes a lower and an upper bound");
        AssertNoDateFunction(conditions);

        var lower = leaves.Single(c => c.Type == ConditionType.GreatherAndEqual);
        var upper = leaves.Single(c => c.Type == ConditionType.Less);
        IsColumn(lower, "OpenedAt").Should().BeTrue("the range is over the raw column, not DATE(col)");
        IsColumn(upper, "OpenedAt").Should().BeTrue();
        Value(lower).Should().Be(Day, "the lower bound is the day's midnight, with the time truncated");
        Value(upper).Should().Be(Day.AddDays(1), "the upper bound is exclusive — the next midnight");
    }

    [Fact]
    public void ColumnDate_NotEquals_ProducesAnOrPair_NotAnUnsatisfiableAnd()
    {
        Expression<Func<DateProbeEntity, bool>> expr = x => x.OpenedAt.Date != Day.Date;

        var conditions = Birko.Data.SQL.DataBase.ParseConditionExpression(expr).ToList();

        // Regression pin: an earlier revision attached this through a helper that overwrote the child's
        // IsOr with the enclosing node's flag, turning `col < d OR col >= d+1` into an AND that matches
        // nothing. Both bounds present with AND semantics is the specific bug being excluded here.
        var pair = FindGroupWithTwoLeaves(conditions);
        pair.Should().NotBeNull("the complement is a two-bound group");
        pair!.IsOr.Should().BeTrue("outside a day means before it OR after it — an AND matches no row");

        var leaves = Leaves(conditions).ToList();
        leaves.Single(c => c.Type == ConditionType.Less).Should().NotBeNull();
        leaves.Single(c => c.Type == ConditionType.GreatherAndEqual).Should().NotBeNull();
        AssertNoDateFunction(conditions);
    }

    [Theory]
    [InlineData(ExpressionType.LessThan)]
    [InlineData(ExpressionType.LessThanOrEqual)]
    [InlineData(ExpressionType.GreaterThan)]
    [InlineData(ExpressionType.GreaterThanOrEqual)]
    public void ColumnDate_Inequalities_UseASingleBoundOnTheRawColumn(ExpressionType op)
    {
        var d = Day.Date;
        Expression<Func<DateProbeEntity, bool>> expr = op switch
        {
            ExpressionType.LessThan => x => x.OpenedAt.Date < d,
            ExpressionType.LessThanOrEqual => x => x.OpenedAt.Date <= d,
            ExpressionType.GreaterThan => x => x.OpenedAt.Date > d,
            _ => x => x.OpenedAt.Date >= d,
        };

        var conditions = Birko.Data.SQL.DataBase.ParseConditionExpression(expr).ToList();
        var leaves = Leaves(conditions).ToList();

        leaves.Should().ContainSingle("an inequality needs one bound, not a range");
        AssertNoDateFunction(conditions);
        IsColumn(leaves[0], "OpenedAt").Should().BeTrue();

        // `<= d` and `> d` shift to the NEXT midnight; `< d` and `>= d` use the day itself. Getting this
        // wrong is an off-by-one-day that no `==` test would catch.
        var (expectedType, expectedValue) = op switch
        {
            ExpressionType.LessThan => (ConditionType.Less, Day),
            ExpressionType.LessThanOrEqual => (ConditionType.Less, Day.AddDays(1)),
            ExpressionType.GreaterThan => (ConditionType.GreatherAndEqual, Day.AddDays(1)),
            _ => (ConditionType.GreatherAndEqual, Day),
        };
        leaves[0].Type.Should().Be(expectedType);
        Value(leaves[0]).Should().Be(expectedValue);
    }

    [Fact]
    public void ValueOnTheLeft_MirrorsTheOperator()
    {
        var d = Day.Date;
        Expression<Func<DateProbeEntity, bool>> expr = x => d < x.OpenedAt.Date;   // ⟺ Date > d

        var conditions = Birko.Data.SQL.DataBase.ParseConditionExpression(expr).ToList();
        var leaf = Leaves(conditions).Single();

        leaf.Type.Should().Be(ConditionType.GreatherAndEqual, "`d < col.Date` is `col.Date > d` mirrored");
        Value(leaf).Should().Be(Day.AddDays(1));
    }

    [Fact]
    public void NullableColumn_ValueDate_ResolvesTheSameColumn()
    {
        Expression<Func<DateProbeEntity, bool>> expr = x => x.ClosedAt!.Value.Date == Day.Date;

        var conditions = Birko.Data.SQL.DataBase.ParseConditionExpression(expr).ToList();
        var leaves = Leaves(conditions).ToList();

        leaves.Should().HaveCount(2);
        leaves.Should().OnlyContain(c => IsColumn(c, "ClosedAt"), "the longer member chain is still one column");
        AssertNoDateFunction(conditions);
    }

    [Fact]
    public void DateComparison_CombinesWithOtherPredicates()
    {
        // The shape DailySummaryService actually issues: the range must nest inside the AND without
        // swallowing or being swallowed by its siblings.
        Expression<Func<DateProbeEntity, bool>> expr =
            x => x.Name == "probe" && x.OpenedAt.Date == Day.Date;

        var conditions = Birko.Data.SQL.DataBase.ParseConditionExpression(expr).ToList();
        var leaves = Leaves(conditions).ToList();

        leaves.Should().Contain(c => IsColumn(c, "Name"), "the sibling predicate survives");
        leaves.Count(c => IsColumn(c, "OpenedAt")).Should().Be(2, "both bounds survive");
        AssertNoDateFunction(conditions);
    }

    [Fact]
    public void DateComparison_NestedUnderAnOr_KeepsItsOwnAndSemantics()
    {
        // The range's two bounds must stay ANDed with each other even when the enclosing node is an OR.
        // This is the same nesting hazard that a first cut of the fix got wrong for `!=` (the child's
        // IsOr was overwritten by the parent's), so it is pinned from the other direction too.
        Expression<Func<DateProbeEntity, bool>> expr =
            x => x.Name == "other" || x.OpenedAt.Date == Day.Date;

        var conditions = Birko.Data.SQL.DataBase.ParseConditionExpression(expr).ToList();
        var leaves = Leaves(conditions).ToList();

        leaves.Should().Contain(c => IsColumn(c, "Name"));
        leaves.Count(c => IsColumn(c, "OpenedAt")).Should().Be(2, "both bounds survive the OR nesting");

        var rangeGroup = All(conditions).Single(c =>
            c.SubConditions != null && c.SubConditions.Count(s => IsColumn(s, "OpenedAt")) == 2);
        rangeGroup.IsOr.Should().BeFalse(
            "a day is a lower AND an upper bound; ORing them would match every row");
        AssertNoDateFunction(conditions);
    }

    /// <summary>Column names arrive table-qualified (<c>DateProbes.OpenedAt</c>), so match the suffix.</summary>
    private static bool IsColumn(Condition c, string name)
        => c.Name != null
            && (string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)
                || c.Name.EndsWith("." + name, StringComparison.OrdinalIgnoreCase));

    private static void AssertNoDateFunction(IEnumerable<Condition> conditions)
        => All(conditions).Should().NotContain(
            c => c.Name != null && c.Name.StartsWith("DATE(", StringComparison.OrdinalIgnoreCase),
            "DATE() is not portable — T-SQL has no such function, and it also makes the predicate non-sargable");

    private static IEnumerable<Condition> All(IEnumerable<Condition> conditions)
    {
        foreach (var c in conditions)
        {
            yield return c;
            if (c.SubConditions != null)
                foreach (var s in All(c.SubConditions)) yield return s;
        }
    }

    /// <summary>Conditions that carry a column + value, i.e. everything that is not a pure group node.</summary>
    private static IEnumerable<Condition> Leaves(IEnumerable<Condition> conditions)
        => All(conditions).Where(c => !string.IsNullOrEmpty(c.Name) && c.Values != null);

    private static Condition? FindGroupWithTwoLeaves(IEnumerable<Condition> conditions)
        => All(conditions).FirstOrDefault(c => c.SubConditions != null
            && c.SubConditions.Count(s => !string.IsNullOrEmpty(s.Name)) == 2);

    private static object? Value(Condition c) => c.Values!.Cast<object?>().Single();
}
