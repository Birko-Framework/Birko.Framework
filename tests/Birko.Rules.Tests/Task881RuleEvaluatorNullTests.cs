using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Birko.Rules.Tests;

/// <summary>
/// TASK-881 — <see cref="RuleEvaluator"/> answers a rule on a NULL value the way C# does, as the rule compiled by
/// <see cref="RuleExpressionConverter"/> and (since TASK-864) the same rule in SQL answer it. TASK-859's decision: C#
/// semantics, always.
/// <para>
/// Before (measured, both contexts alike): 21 of TASK-864's 117 shapes differed from the compiled rule. The evaluator
/// ordered NULL below every value (<c>Score LessThan 5</c> selected the NULL rows, and its negation lost them), ordered
/// every value above a null bound (<c>Score GreaterThan null</c> selected every non-null row, where C# selects none), and
/// never matched NULL from an <c>In</c> list (<c>In [null, 5]</c> missed the NULL rows, <c>NotIn [null, 5]</c> selected
/// them).
/// </para>
/// <para>
/// The shapes and rows are TASK-864's (<c>tests/Shared/StoreContract/RuleNullContract.cs</c>), copied: that file links the
/// SQL connector and cannot be compiled here.
/// </para>
/// </summary>
public class Task881RuleEvaluatorNullTests
{
    public class Row
    {
        public int Num { get; set; }
        public int? Score { get; set; }
        public string? Name { get; set; }
        public int Amount { get; set; }
    }

    private static Row[] Rows() =>
    [
        new() { Num = 1, Score = null, Name = null, Amount = 5 },
        new() { Num = 2, Score = 3, Name = "abc", Amount = 3 },
        new() { Num = 3, Score = 5, Name = "xyz", Amount = 5 },
        new() { Num = 4, Score = 7, Name = null, Amount = 7 },
        new() { Num = 5, Score = null, Name = "bcd", Amount = 1 },
    ];

    private static Rule R(string field, ComparisonOperator op, object? value) => new(field, op, value);

    private static readonly IReadOnlyDictionary<string, Func<Rule>> Shapes = new Dictionary<string, Func<Rule>>
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
        // TASK-881's own: orderings against a null bound, a Between with a null bound, and a nullable column ordered.
        ["Score GreaterThan null"] = () => R("Score", ComparisonOperator.GreaterThan, null),
        ["Score GreaterThanOrEqual null"] = () => R("Score", ComparisonOperator.GreaterThanOrEqual, null),
        ["Score LessThan null"] = () => R("Score", ComparisonOperator.LessThan, null),
        ["Score LessThanOrEqual null"] = () => R("Score", ComparisonOperator.LessThanOrEqual, null),
        ["Score Between null..5"] = () => new Rule("Score", ComparisonOperator.Between, null) { UpperValue = 5 },
        ["Score Between 3..null"] = () => new Rule("Score", ComparisonOperator.Between, 3) { UpperValue = null },
        ["Name Equal null"] = () => R("Name", ComparisonOperator.Equal, null),
        ["Name NotEqual null"] = () => R("Name", ComparisonOperator.NotEqual, null),
        ["Name NotIn [abc,null]"] = () => R("Name", ComparisonOperator.NotIn, new object?[] { "abc", null }),
    };

    public static IEnumerable<object[]> Cases()
    {
        foreach (var name in Shapes.Keys)
        {
            yield return new object[] { name, "plain" };
            yield return new object[] { name, "negated" };
            yield return new object[] { name, "NOT group" };
        }
    }

    private static IRule Build(string shape, string form)
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
    private static int[] Compiled(IRule rule)
    {
        var predicate = RuleExpressionConverter.ToExpression<Row>(rule).Compile();
        return Rows().Where(predicate).Select(r => r.Num).OrderBy(n => n).ToArray();
    }

    private static int[] Evaluated(IRule rule, Func<Row, IRuleContext> context)
    {
        var evaluator = new RuleEvaluator();
        return Rows().Where(r => evaluator.Evaluate(rule, context(r)).IsMatch).Select(r => r.Num).OrderBy(n => n).ToArray();
    }

    private static IRuleContext Typed(Row row) => new ObjectRuleContext<Row>(row);

    private static IRuleContext Open(Row row) => DictionaryRuleContext.From(
        ("Num", row.Num), ("Score", row.Score), ("Name", row.Name), ("Amount", row.Amount));

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_evaluator_selects_the_rows_the_compiled_rule_selects_on_a_typed_context(string shape, string form)
    {
        var rule = Build(shape, form);
        Evaluated(rule, Typed).Should().Equal(Compiled(rule), $"{form} {shape}");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_evaluator_selects_the_rows_the_compiled_rule_selects_on_a_dictionary_context(string shape, string form)
    {
        var rule = Build(shape, form);
        Evaluated(rule, Open).Should().Equal(Compiled(rule), $"{form} {shape}");
    }

    /// <summary>
    /// The reference itself, pinned against hand-written C# for the shapes that moved: the compiled rule is only the
    /// reference because it is C#.
    /// </summary>
    [Fact]
    public void The_reference_is_CSharp()
    {
        int? nul = null;
        int[] Where(Func<Row, bool> p) => Rows().Where(p).Select(r => r.Num).ToArray();

        Compiled(Shapes["Score LessThan 5"]()).Should().Equal(Where(x => x.Score < 5));
        Compiled(Shapes["Score GreaterThan null"]()).Should().Equal(Where(x => x.Score > nul));
        Compiled(new Rule("Score", ComparisonOperator.LessThan, 5) { IsNegated = true }).Should().Equal(Where(x => !(x.Score < 5)));
        Compiled(Shapes["Score In [null,5]"]()).Should().Equal(Where(x => x.Score == null || x.Score == 5));
        Compiled(Shapes["Score NotIn [null,5]"]()).Should().Equal(Where(x => !(x.Score == null || x.Score == 5)));
        Compiled(Shapes["Score Between 3..5"]()).Should().Equal(Where(x => x.Score >= 3 && x.Score <= 5));
        Compiled(new Rule("Score", ComparisonOperator.GreaterThanOrEqual, null) { IsNegated = true })
            .Should().Equal(Where(x => !(x.Score >= nul)));
    }
}
