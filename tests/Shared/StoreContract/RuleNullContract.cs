using System;
using System.Collections.Generic;
using System.Linq;
using Birko.Data.Exceptions;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Rules;
using FluentAssertions;
using Xunit;

namespace Birko.Tests.Shared.StoreContract;

/// <summary>TASK-864's row: nullable int and string columns, a non-nullable int, and <see cref="Num"/> to name each row.</summary>
[Table("T864RuleNullRows")]
public class RuleNullRow : AbstractModel
{
    public int Num { get; set; }

    public int? Score { get; set; }

    public string? Name { get; set; }

    public int Amount { get; set; }
}

/// <summary>
/// TASK-864 — a rule converted by <see cref="RuleConditionConverter"/> selects, counts and deletes the rows the same rule
/// selects in memory, on rows whose columns are NULL. The reference is the rule compiled to C#
/// (<see cref="RuleExpressionConverter"/>), which is what TASK-859 made every SQL filter answer.
/// <para>
/// Before (measured on SQLite, PostgreSQL, MySQL and SQL Server): the converter built its conditions without TASK-859's
/// nullable operands, so every comparison that C# answers <c>true</c> on NULL — <c>NotEqual</c>, <c>NotIn</c>,
/// <c>NotContains</c>, and any negated <c>Equal</c>, ordering, <c>Between</c>, <c>In</c> or string rule — rendered SQL that is
/// UNKNOWN on a NULL row and dropped it, and <c>In</c> with a <c>null</c> element bound the null, so it never matched a NULL
/// row and its <c>NotIn</c> matched no row at all. 53 of 117 read shapes differed from C#, identically on all four; a null
/// <c>In</c> set read every row under <c>NotIn</c>, where both in-memory engines match nothing.
/// </para>
/// <para>
/// Not the reference: <c>RuleEvaluator</c>, which orders NULL below every value (<c>Score LessThan 5</c> selects the NULL
/// rows) and never matches NULL from an <c>In</c> list — 21 of 117 shapes apart from C#.
/// </para>
/// <para>Shared source, linked into each SQL provider's test project, which supplies a seeded connector.</para>
/// </summary>
public abstract class RuleNullContract
{
    protected virtual bool Ready() => true;

    /// <summary>A connector over a fresh table of <see cref="RuleNullRow"/> holding exactly <paramref name="rows"/>.</summary>
    protected abstract AbstractConnector Seeded(IReadOnlyList<RuleNullRow> rows);

    private static RuleNullRow[] Rows() => new[]
    {
        new RuleNullRow { Num = 1, Score = null, Name = null, Amount = 5 },
        new RuleNullRow { Num = 2, Score = 3, Name = "abc", Amount = 3 },
        new RuleNullRow { Num = 3, Score = 5, Name = "xyz", Amount = 5 },
        new RuleNullRow { Num = 4, Score = 7, Name = null, Amount = 7 },
        new RuleNullRow { Num = 5, Score = null, Name = "bcd", Amount = 1 },
    };

    private static Rule R(string field, ComparisonOperator op, object? value) => new(field, op, value);

    /// <summary>Every operator the converter maps, over a nullable int, a nullable string and a non-nullable int.</summary>
    public static readonly IReadOnlyDictionary<string, Func<Rule>> Shapes = new Dictionary<string, Func<Rule>>
    {
        ["Score Equal 5"] = () => R("Score", ComparisonOperator.Equal, 5),
        ["Score NotEqual 5"] = () => R("Score", ComparisonOperator.NotEqual, 5),
        ["Score GreaterThan 5"] = () => R("Score", ComparisonOperator.GreaterThan, 5),
        ["Score GreaterThanOrEqual 5"] = () => R("Score", ComparisonOperator.GreaterThanOrEqual, 5),
        ["Score LessThan 5"] = () => R("Score", ComparisonOperator.LessThan, 5),
        ["Score LessThanOrEqual 5"] = () => R("Score", ComparisonOperator.LessThanOrEqual, 5),
        ["Score Between 3..5"] = () => Rule.Between("Score", 3, 5),
        ["Score In [3,5]"] = () => R("Score", ComparisonOperator.In, new object?[] { 3, 5 }),
        ["Score NotIn [3,5]"] = () => R("Score", ComparisonOperator.NotIn, new object?[] { 3, 5 }),
        ["Score In [null,5]"] = () => R("Score", ComparisonOperator.In, new object?[] { null, 5 }),
        ["Score NotIn [null,5]"] = () => R("Score", ComparisonOperator.NotIn, new object?[] { null, 5 }),
        ["Score In [null]"] = () => R("Score", ComparisonOperator.In, new object?[] { null }),
        ["Score NotIn [null]"] = () => R("Score", ComparisonOperator.NotIn, new object?[] { null }),
        ["Score In 5 (scalar)"] = () => R("Score", ComparisonOperator.In, 5),
        ["Score IsNull"] = () => R("Score", ComparisonOperator.IsNull, null),
        ["Score IsNotNull"] = () => R("Score", ComparisonOperator.IsNotNull, null),
        ["Score Equal null"] = () => R("Score", ComparisonOperator.Equal, null),
        ["Score NotEqual null"] = () => R("Score", ComparisonOperator.NotEqual, null),
        ["Name Equal abc"] = () => R("Name", ComparisonOperator.Equal, "abc"),
        ["Name NotEqual abc"] = () => R("Name", ComparisonOperator.NotEqual, "abc"),
        ["Name In [abc,null]"] = () => R("Name", ComparisonOperator.In, new object?[] { "abc", null }),
        ["Name NotIn [abc]"] = () => R("Name", ComparisonOperator.NotIn, new object?[] { "abc" }),
        ["Name Contains b"] = () => R("Name", ComparisonOperator.Contains, "b"),
        ["Name NotContains b"] = () => R("Name", ComparisonOperator.NotContains, "b"),
        ["Name StartsWith a"] = () => R("Name", ComparisonOperator.StartsWith, "a"),
        ["Name EndsWith z"] = () => R("Name", ComparisonOperator.EndsWith, "z"),
        ["Name Like %b%"] = () => R("Name", ComparisonOperator.Like, "%b%"),
        ["Name Like a%"] = () => R("Name", ComparisonOperator.Like, "a%"),
        ["Name Like abc"] = () => R("Name", ComparisonOperator.Like, "abc"),
        ["Score In null"] = () => R("Score", ComparisonOperator.In, null),
        ["Score NotIn null"] = () => R("Score", ComparisonOperator.NotIn, null),
        ["Name Contains null"] = () => R("Name", ComparisonOperator.Contains, null),
        ["Name NotContains null"] = () => R("Name", ComparisonOperator.NotContains, null),
        ["Name StartsWith null"] = () => R("Name", ComparisonOperator.StartsWith, null),
        ["Name Like null"] = () => R("Name", ComparisonOperator.Like, null),
        ["Amount NotEqual 5"] = () => R("Amount", ComparisonOperator.NotEqual, 5),
        ["Amount NotIn [3,5]"] = () => R("Amount", ComparisonOperator.NotIn, new object?[] { 3, 5 }),
        ["Amount GreaterThan 3"] = () => R("Amount", ComparisonOperator.GreaterThan, 3),
    };

    /// <summary>Each shape plain and negated (<see cref="Rule.IsNegated"/>), and inside a negated group.</summary>
    public static IEnumerable<object[]> Cases()
    {
        foreach (var name in Shapes.Keys)
        {
            yield return new object[] { name, "plain" };
            yield return new object[] { name, "negated" };
            yield return new object[] { name, "NOT group" };
        }
    }

    internal static IRule Build(string shape, string form)
    {
        var rule = Shapes[shape]();
        switch (form)
        {
            case "negated":
                rule.IsNegated = true;
                return rule;
            case "NOT group":
                // NOT (rule AND Num > 0): by De Morgan, NOT rule OR Num <= 0, so the answer is NOT rule's.
                return new RuleGroup(LogicOperator.And, rule, R("Num", ComparisonOperator.GreaterThan, 0)) { IsNegated = true };
            default:
                return rule;
        }
    }

    /// <summary>The rows the rule compiled to C# selects — the reference.</summary>
    internal static int[] Expected(IRule rule)
    {
        var predicate = RuleExpressionConverter.ToExpression<RuleNullRow>(rule).Compile();
        return Rows().Where(predicate).Select(r => r.Num).OrderBy(n => n).ToArray();
    }

    private static int[] Nums(IEnumerable<object> rows) => rows.Cast<RuleNullRow>().Select(r => r.Num).OrderBy(n => n).ToArray();

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_rule_reads_the_rows_it_selects_in_memory(string shape, string form)
    {
        if (!Ready()) return;
        var rule = Build(shape, form);
        var connector = Seeded(Rows());

        var conditions = RuleConditionConverter.ToConditions<RuleNullRow>(rule).ToList();

        Nums(connector.Select(typeof(RuleNullRow), conditions)).Should().Equal(Expected(rule), $"{form} {shape}: read");
        connector.SelectCount(typeof(RuleNullRow), conditions).Should().Be(Expected(rule).Length, $"{form} {shape}: count");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_rule_deletes_the_rows_it_selects_in_memory(string shape, string form)
    {
        if (!Ready()) return;
        var rule = Build(shape, form);
        var connector = Seeded(Rows());
        var expected = Expected(rule);

        var conditions = RuleConditionConverter.ToConditions<RuleNullRow>(rule).ToList();
        connector.Delete(typeof(RuleNullRow), conditions);

        var left = Nums(connector.Select(typeof(RuleNullRow), (IEnumerable<Condition>?)null));
        left.Should().Equal(Rows().Select(r => r.Num).Except(expected).OrderBy(n => n), $"{form} {shape}: delete");
    }

    /// <summary>
    /// An ordering against a null value is <c>false</c> in C# whatever the row (TASK-859), so negated it means every row.
    /// TASK-861's guard has to see that through a rule exactly as it does through a lambda.
    /// </summary>
    [Theory]
    [InlineData(ComparisonOperator.GreaterThan)]
    [InlineData(ComparisonOperator.LessThanOrEqual)]
    public void A_negated_ordering_against_null_is_refused_as_a_whole_table_write(ComparisonOperator op)
    {
        if (!Ready()) return;
        var connector = Seeded(Rows());
        var negated = new Rule("Score", op, null) { IsNegated = true };

        var conditions = RuleConditionConverter.ToConditions<RuleNullRow>(negated).ToList();
        Expected(negated).Should().HaveCount(5, "C# answers the negated null ordering true on every row");

        Nums(connector.Select(typeof(RuleNullRow), conditions)).Should().Equal(1, 2, 3, 4, 5);
        Action delete = () => connector.Delete(typeof(RuleNullRow), conditions);
        delete.Should().Throw<WholeTableWriteException>();
        Action update = () => connector.Update(typeof(RuleNullRow), new RuleNullRow { Num = 9 }, conditions);
        update.Should().Throw<WholeTableWriteException>();
        Nums(connector.Select(typeof(RuleNullRow), (IEnumerable<Condition>?)null)).Should().Equal(1, 2, 3, 4, 5);

        var plain = new Rule("Score", op, null);
        Expected(plain).Should().BeEmpty();
        var plainConditions = RuleConditionConverter.ToConditions<RuleNullRow>(plain).ToList();
        Nums(connector.Select(typeof(RuleNullRow), plainConditions)).Should().BeEmpty();
        connector.Delete(typeof(RuleNullRow), plainConditions);
        Nums(connector.Select(typeof(RuleNullRow), (IEnumerable<Condition>?)null)).Should().Equal(1, 2, 3, 4, 5);
    }
}
