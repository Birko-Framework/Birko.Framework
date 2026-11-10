using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace Birko.Rules.Tests;

/// <summary>
/// TASK-895 (the user's decision, 2026-11-10: a rule's string equality is exact, as the lambda's is) — <see cref="RuleEvaluator"/>
/// answers a string <c>Equal</c> / <c>NotEqual</c> / <c>In</c> / <c>NotIn</c> ordinally, as the rule compiled by
/// <see cref="RuleExpressionConverter"/> (C#'s <c>==</c>) and the same rule in SQL (<c>RuleConditionConverter</c>) answer it.
/// <para>
/// Before (measured): the evaluator compared two strings with <c>OrdinalIgnoreCase</c> (SH-M295's recorded divergence,
/// TASK-144), so 30 of the 42 string cases of the shared <c>StringEqualityLeftoversContract</c> selected other rows than the
/// compiled rule: <c>Status Equal "open"</c> matched <c>Open</c>. The string methods keep ignoring case.
/// </para>
/// </summary>
public class Task895RuleEvaluatorStringEqualityTests
{
    public class Row
    {
        public int Num { get; set; }
        public string? Name { get; set; }
    }

    private static readonly string?[] Names =
        { "abc", "ABC", "Abc", "abc ", "café", "café", "CAFÉ", "ß", "ss", "K", "K", "", " ", null };

    private static Row[] Rows() => Names.Select((name, i) => new Row { Num = i, Name = name }).ToArray();

    private static Rule R(ComparisonOperator op, object? value) => new("Name", op, value);

    private static readonly IReadOnlyDictionary<string, Func<Rule>> Shapes = new Dictionary<string, Func<Rule>>
    {
        ["Equal abc"] = () => R(ComparisonOperator.Equal, "abc"),
        ["Equal ABC"] = () => R(ComparisonOperator.Equal, "ABC"),
        ["Equal 'abc '"] = () => R(ComparisonOperator.Equal, "abc "),
        ["Equal café"] = () => R(ComparisonOperator.Equal, "café"),
        ["Equal ss"] = () => R(ComparisonOperator.Equal, "ss"),
        ["Equal Kelvin"] = () => R(ComparisonOperator.Equal, "K"),
        ["Equal ''"] = () => R(ComparisonOperator.Equal, ""),
        ["NotEqual abc"] = () => R(ComparisonOperator.NotEqual, "abc"),
        ["NotEqual CAFÉ"] = () => R(ComparisonOperator.NotEqual, "CAFÉ"),
        ["In [abc, ß]"] = () => R(ComparisonOperator.In, new object?[] { "abc", "ß" }),
        ["In [ABC, null]"] = () => R(ComparisonOperator.In, new object?[] { "ABC", null }),
        ["In 'k' (scalar)"] = () => R(ComparisonOperator.In, "k"),
        ["NotIn [abc, café]"] = () => R(ComparisonOperator.NotIn, new object?[] { "abc", "café" }),
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
                return new RuleGroup(LogicOperator.And, rule, new Rule("Num", ComparisonOperator.GreaterThanOrEqual, 0)) { IsNegated = true };
            default:
                return rule;
        }
    }

    private static int[] Compiled(IRule rule)
    {
        var predicate = RuleExpressionConverter.ToExpression<Row>(rule).Compile();
        return Rows().Where(predicate).Select(r => r.Num).ToArray();
    }

    private static int[] Evaluated(IRule rule, Func<Row, IRuleContext> context)
    {
        var evaluator = new RuleEvaluator();
        return Rows().Where(r => evaluator.Evaluate(rule, context(r)).IsMatch).Select(r => r.Num).ToArray();
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void A_string_equality_rule_selects_in_memory_what_the_compiled_rule_selects(string shape, string form)
    {
        var rule = Build(shape, form);
        var expected = Compiled(rule);

        Evaluated(rule, r => new ObjectRuleContext<Row>(r)).Should().Equal(expected, $"{form} {shape}, typed context");
        Evaluated(rule, r => DictionaryRuleContext.From(("Num", r.Num), ("Name", r.Name)))
            .Should().Equal(expected, $"{form} {shape}, dictionary context");
    }

    [Fact]
    public void The_string_methods_still_ignore_case()
    {
        var evaluator = new RuleEvaluator();
        var context = new ObjectRuleContext<Row>(new Row { Name = "Open" });

        evaluator.Evaluate(new Rule("Name", ComparisonOperator.Equal, "open"), context).IsMatch.Should().BeFalse();
        evaluator.Evaluate(new Rule("Name", ComparisonOperator.Contains, "pe"), context).IsMatch.Should().BeTrue();
        evaluator.Evaluate(new Rule("Name", ComparisonOperator.StartsWith, "op"), context).IsMatch.Should().BeTrue();
        evaluator.Evaluate(new Rule("Name", ComparisonOperator.Like, "open"), context).IsMatch.Should().BeTrue();
    }

    [Fact]
    public void A_value_of_another_type_against_text_still_compares_its_text()
    {
        var evaluator = new RuleEvaluator();
        var id = Guid.NewGuid();

        evaluator.Evaluate(new Rule("Id", ComparisonOperator.Equal, id.ToString().ToUpperInvariant()),
            DictionaryRuleContext.From(("Id", id))).IsMatch.Should().BeTrue("a Guid against its text is not a string equality");
    }
}
