using System.Collections.Generic;
using System.Linq;
using Birko.Data.Patterns.Specification;
using FluentAssertions;
using Xunit;

namespace Birko.Rules.Tests;

/// <summary>
/// FIELD-025 (TASK-558) — <c>RuleExpressionConverter</c> dropped a child it could not translate (a field that resolves
/// to no property, a disabled rule, an empty group), so <c>AND(A, NonExistent == "x")</c> became <c>A</c>, wider than
/// the rule. It negated a group with <c>Expression.Not</c> around the combined children, so <c>NOT (A AND
/// unresolved)</c> became <c>NOT A</c>, wider again. At the root it returned <c>null</c>, which a caller reads as "no
/// filter". It now answers match-nothing for all of those, as <c>RuleSpecification</c> and <c>RuleEvaluator</c> do, and
/// negates by De Morgan down to the leaves.
///
/// <para>Observed state: the rows each engine selects from one shared table — the converter's predicate, the
/// specification's expression, and the evaluator (via <c>IsSatisfiedBy</c>) — never the expression's shape.</para>
/// </summary>
public class RuleExpressionConverterAgreementTests
{
    public class Row
    {
        public int Rank { get; set; }
        public string Status { get; set; } = "";
    }

    private static readonly Row[] Table =
    [
        new() { Rank = 1, Status = "Open" },
        new() { Rank = 2, Status = "Open" },
        new() { Rank = 3, Status = "Closed" },
    ];

    private static Rule Rank(int value) => new("Rank", ComparisonOperator.Equal, value);
    private static Rule Unresolved() => new("NonExistent", ComparisonOperator.Equal, "x");
    private static RuleGroup Empty() => new(LogicOperator.And, new List<IRule>());

    private static int[] Ranks(IEnumerable<Row> rows) => rows.Select(r => r.Rank).OrderBy(r => r).ToArray();

    public static TheoryData<string, IRule, int[]> Trees => new()
    {
        { "unresolved leaf", Unresolved(), [] },
        { "negated unresolved leaf", new Rule("NonExistent", ComparisonOperator.Equal, "x") { IsNegated = true }, [] },
        { "A AND unresolved", RuleGroup.And(Rank(1), Unresolved()), [] },
        { "A OR unresolved", RuleGroup.Or(Rank(1), Unresolved()), [1] },
        { "NOT (A AND unresolved)", new RuleGroup(LogicOperator.And, Rank(1), Unresolved()) { IsNegated = true }, [2, 3] },
        { "NOT (A OR unresolved)", new RuleGroup(LogicOperator.Or, Rank(1), Unresolved()) { IsNegated = true }, [] },
        { "NOT ((A OR unresolved) AND B)",
            new RuleGroup(LogicOperator.And, RuleGroup.Or(Rank(1), Unresolved()), Rank(2)) { IsNegated = true }, [1, 3] },
        { "disabled leaf", new Rule("Rank", ComparisonOperator.Equal, 1) { IsEnabled = false }, [] },
        { "empty group", Empty(), [] },
        { "A AND empty", RuleGroup.And(Rank(1), Empty()), [] },
        { "negated empty group", new RuleGroup(LogicOperator.Or, new List<IRule>()) { IsNegated = true }, [] },
        { "NOT (A OR B)", new RuleGroup(LogicOperator.Or, Rank(1), Rank(2)) { IsNegated = true }, [3] },
        { "NOT (A AND B)", new RuleGroup(LogicOperator.And, Rank(1), new Rule("Status", ComparisonOperator.Equal, "Open")) { IsNegated = true }, [2, 3] },
    };

    [Theory]
    [MemberData(nameof(Trees))]
    public void The_converter_selects_what_RuleSpecification_and_RuleEvaluator_select(string shape, IRule rule, int[] expected)
    {
        var spec = new RuleSpecification<Row>(rule);
        var bySpecExpression = Ranks(Table.Where(spec.ToExpression().Compile()));
        var byEvaluator = Ranks(Table.Where(spec.IsSatisfiedBy));
        bySpecExpression.Should().Equal(expected, $"reference answer (RuleSpecification) for {shape}");
        byEvaluator.Should().Equal(expected, $"reference answer (RuleEvaluator) for {shape}");

        var byConverter = Ranks(Table.Where(RuleExpressionConverter.ToExpression<Row>(rule).Compile()));

        byConverter.Should().Equal(expected, $"RuleExpressionConverter must agree with the other engines for {shape}");
    }

    [Fact]
    public void A_rule_set_holding_an_unresolved_rule_matches_nothing()
    {
        var set = new RuleSet("s", Rank(1), Unresolved());

        Ranks(Table.Where(RuleExpressionConverter.ToExpression<Row>(set).Compile())).Should().BeEmpty();
        Ranks(Table.Where(new RuleSpecification<Row>(set).IsSatisfiedBy)).Should().BeEmpty();
    }

    [Fact]
    public void A_rule_list_holding_an_unresolved_rule_matches_nothing()
    {
        IEnumerable<IRule> rules = [Rank(1), Unresolved()];

        Ranks(Table.Where(RuleExpressionConverter.ToExpression<Row>(rules).Compile())).Should().BeEmpty();
    }

    [Fact]
    public void An_empty_rule_list_matches_nothing()
    {
        Ranks(Table.Where(RuleExpressionConverter.ToExpression<Row>(new List<IRule>()).Compile())).Should().BeEmpty();
    }

    [Fact]
    public void An_enabled_rule_set_skips_its_disabled_members()
    {
        // Contract pin: a disabled member of an enabled set is skipped, not turned into match-nothing.
        var set = new RuleSet("s",
            new Rule("Status", ComparisonOperator.Equal, "Open"),
            new Rule("Rank", ComparisonOperator.Equal, 99) { IsEnabled = false });

        Ranks(Table.Where(RuleExpressionConverter.ToExpression<Row>(set).Compile())).Should().Equal(1, 2);
    }
}
