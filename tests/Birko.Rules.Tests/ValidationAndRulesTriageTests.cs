using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Birko.Rules.Tests;

/// <summary>
/// TASK-156, the validation-and-rules medium findings on the rules engine: the in-memory evaluator, measured through
/// <see cref="RuleEvaluator"/> on real contexts, against what the translator answers for the same rule.
/// </summary>
public class ValidationAndRulesTriageTests
{
    public class Row
    {
        public string? Name { get; set; }
        public string? Code { get; set; }
        public long Big { get; set; }
        public decimal Price { get; set; }
    }

    private static bool Match(IRule rule, object entity)
        => new RuleEvaluator().Evaluate(rule, (IRuleContext)Activator.CreateInstance(typeof(ObjectRuleContext<>).MakeGenericType(entity.GetType()), entity)!).IsMatch;

    private static bool Store(IRule rule, Row row) => RuleExpressionConverter.ToExpression<Row>(rule).Compile()(row);

    [Fact]
    public void SH_M359_a_lone_percent_matches_any_value_instead_of_throwing()
    {
        var rule = new Rule("Name", ComparisonOperator.Like, "%");
        var row = new Row { Name = "anything" };

        Match(rule, row).Should().BeTrue();
        Store(rule, row).Should().BeTrue();
    }

    [Fact]
    public void SH_M360_distinct_longs_and_decimals_beyond_double_precision_are_not_equal()
    {
        Match(new Rule("Big", ComparisonOperator.Equal, 9007199254740992L), new Row { Big = 9007199254740993L }).Should().BeFalse();
        Match(new Rule("Price", ComparisonOperator.Equal, 1.0m), new Row { Price = 1.0000000000000000001m }).Should().BeFalse();
        Match(new Rule("Big", ComparisonOperator.GreaterThan, 9007199254740992L), new Row { Big = 9007199254740993L }).Should().BeTrue();
    }

    [Fact]
    public void SH_M361_two_string_codes_compare_as_text()
    {
        Match(new Rule("Code", ComparisonOperator.Equal, "7"), new Row { Code = "007" }).Should().BeFalse("\"007\" is not \"7\" as a code");
        Match(new Rule("Code", ComparisonOperator.Equal, "(5)"), new Row { Code = "-5" }).Should().BeFalse();
        Match(new Rule("Big", ComparisonOperator.Equal, "42"), new Row { Big = 42 }).Should().BeTrue("a text value against a number is still read as a number");
    }

    [Fact]
    public void SH_M362_an_interior_wildcard_matches_nothing_in_both_engines_negated_or_not()
    {
        var row = new Row { Name = "A%B" };
        var rule = new Rule("Name", ComparisonOperator.Like, "A%B");
        var negated = new Rule("Name", ComparisonOperator.Like, "A%B") { IsNegated = true };

        Match(rule, row).Should().BeFalse("the literal A%B is not what a pattern means");
        Store(rule, row).Should().BeFalse();
        Match(negated, row).Should().BeFalse();
        Store(negated, row).Should().BeFalse();
    }

    [Fact]
    public void A_negated_null_needle_or_null_set_matches_nothing_in_memory_as_at_the_store()
    {
        var row = new Row { Name = "x" };
        foreach (var rule in new IRule[]
        {
            new Rule("Name", ComparisonOperator.Contains, null) { IsNegated = true },
            new Rule("Name", ComparisonOperator.NotIn, null),
            new Rule("Name", ComparisonOperator.In, null) { IsNegated = true },
        })
        {
            Match(rule, row).Should().BeFalse();
            Store(rule, row).Should().BeFalse();
        }
    }

    [Fact]
    public void SH_M369_a_dictionary_field_resolves_case_insensitively()
    {
        var context = new DictionaryRuleContext(new Dictionary<string, object?> { ["Temp"] = 90 });

        new RuleEvaluator().Evaluate(new Rule("temp", ComparisonOperator.GreaterThan, 80), context).IsMatch.Should().BeTrue();
        DictionaryRuleContext.From(("Temp", 90)).HasField("TEMP").Should().BeTrue();
    }

    [Fact]
    public async Task SH_M370_a_rule_set_edited_during_evaluation_does_not_throw()
    {
        var set = new RuleSet("s", Enumerable.Range(0, 200).Select(i => (IRule)new Rule("Name", ComparisonOperator.NotEqual, "v" + i)).ToArray());
        var context = DictionaryRuleContext.From(("Name", "x"));
        using var stop = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        var editor = Task.Run(() =>
        {
            var i = 0;
            while (!stop.IsCancellationRequested)
            {
                set.Rules.Add(new Rule("Name", ComparisonOperator.NotEqual, "e" + i++));
                set.Rules.RemoveAt(set.Rules.Count - 1);
            }
        });

        var act = () =>
        {
            while (!stop.IsCancellationRequested)
            {
                new RuleEvaluator().Evaluate(set, context);
                RuleExpressionConverter.ToExpression<Row>(set);
            }
        };

        act.Should().NotThrow();
        await editor;
    }
}
