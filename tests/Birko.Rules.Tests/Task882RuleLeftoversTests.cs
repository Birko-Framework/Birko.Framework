using System;
using System.Linq;
using Birko.Tests.Shared.StoreContract;
using FluentAssertions;
using Xunit;

namespace Birko.Rules.Tests;

/// <summary>
/// TASK-882 — the two in-memory runners of a rule, <see cref="RuleEvaluator"/> and the rule compiled by
/// <see cref="RuleExpressionConverter"/>, answer what the equivalent C# lambda answers, on the shapes TASK-864 left: a null
/// value against a non-nullable column, <c>Like</c>, and values given in another form than the member's. The SQL runner
/// is measured on the same shapes by <c>RuleLeftoversContract</c> in each SQL provider's tests.
/// <para>
/// Before (measured): the compiled rule matched nothing for a null against a non-nullable member (<c>Amount NotEqual
/// null</c>, C# every row) and for an ordering over an enum; <c>Like</c> without a wildcard compared ordinally while the
/// evaluator ignored case; the evaluator compared an enum with a number or with its name's order as text, and a
/// <c>DateTime</c> with text in the host's culture. An interior <c>%</c> matched nothing in memory and the literal in SQL:
/// it is now refused by name by every runner.
/// </para>
/// </summary>
public class Task882RuleLeftoversTests
{
    public static TheoryData<string, string> Cases()
    {
        var data = new TheoryData<string, string>();
        foreach (var c in RuleLeftoverShapes.Cases())
            data.Add((string)c[0], (string)c[1]);
        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_compiled_rule_answers_the_lambda(string shape, string form)
    {
        var rule = RuleLeftoverShapes.Build(shape, form);
        var expected = RuleLeftoverShapes.Expected(shape, form);

        Func<int[]> run = () =>
        {
            var predicate = RuleExpressionConverter.ToExpression<RuleLeftoverRow>(rule).Compile();
            return RuleLeftoverShapes.Nums(RuleLeftoverShapes.Rows().Where(predicate));
        };

        if (expected == null)
        {
            run.Should().Throw<NotSupportedException>($"{form} {shape}").WithMessage("*Like*");
            return;
        }
        run().Should().Equal(expected, $"{form} {shape}");
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void The_evaluator_answers_the_lambda(string shape, string form)
    {
        var rule = RuleLeftoverShapes.Build(shape, form);
        var expected = RuleLeftoverShapes.Expected(shape, form);
        var evaluator = new RuleEvaluator();

        Func<int[]> run = () => RuleLeftoverShapes.Nums(RuleLeftoverShapes.Rows()
            .Where(r => evaluator.Evaluate(rule, new ObjectRuleContext<RuleLeftoverRow>(r)).IsMatch));

        if (expected == null)
        {
            run.Should().Throw<NotSupportedException>($"{form} {shape}").WithMessage("*Like*");
            return;
        }
        run().Should().Equal(expected, $"{form} {shape}");
    }

    /// <summary>A dictionary context (Symbio's IoT rules) sees the same values, so it answers the same.</summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void The_evaluator_answers_the_lambda_on_a_dictionary_context(string shape, string form)
    {
        var rule = RuleLeftoverShapes.Build(shape, form);
        var expected = RuleLeftoverShapes.Expected(shape, form);
        var evaluator = new RuleEvaluator();

        static DictionaryRuleContext Context(RuleLeftoverRow r) => DictionaryRuleContext.From(
            ("Num", r.Num), ("Amount", r.Amount), ("Price", r.Price), ("At", r.At), ("Flag", r.Flag), ("State", r.State),
            ("OptState", r.OptState), ("Ref", r.Ref), ("OptRef", r.OptRef), ("Name", r.Name));

        Func<int[]> run = () => RuleLeftoverShapes.Nums(RuleLeftoverShapes.Rows()
            .Where(r => evaluator.Evaluate(rule, Context(r)).IsMatch));

        if (expected == null)
        {
            run.Should().Throw<NotSupportedException>($"{form} {shape}").WithMessage("*Like*");
            return;
        }
        run().Should().Equal(expected, $"{form} {shape}");
    }

    /// <summary>The refusal depends on the rule alone: a row without the field, or no row, refuses it too.</summary>
    [Fact]
    public void An_interior_wildcard_is_refused_whatever_the_data()
    {
        var rule = new Rule("Name", ComparisonOperator.Like, "a%b");
        var evaluator = new RuleEvaluator();

        Action missingField = () => evaluator.Evaluate(rule, DictionaryRuleContext.From(("Other", (object?)1)));
        missingField.Should().Throw<NotSupportedException>().WithMessage("*'Name'*");
        Action nullValue = () => evaluator.Evaluate(rule, DictionaryRuleContext.From(("Name", (object?)null)));
        nullValue.Should().Throw<NotSupportedException>();
        Action compile = () => RuleExpressionConverter.ToExpression<RuleLeftoverRow>(rule);
        compile.Should().Throw<NotSupportedException>().WithMessage("*'Name'*");
    }
}
