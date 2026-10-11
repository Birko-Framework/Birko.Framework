using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Birko.Data.Exceptions;
using Birko.Data.Models;
using Birko.Data.Patterns.Specification;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Rules;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// FIELD-026 (TASK-559), against a real SQLite file. <c>RuleConditionConverter</c> turned a disabled rule, an empty
/// or all-disabled group and a disabled <c>RuleSet</c> into NO condition, and an empty condition list is no
/// <c>WHERE</c>: a read returned the whole table where <c>RuleSpecification</c> and <c>RuleEvaluator</c> match
/// nothing. An empty group nested in an AND was dropped (<c>A AND &lt;empty&gt;</c> selected A), and a negated group
/// was rendered as SQL <c>NOT</c> around its children, so it could not keep a match-none child from being inverted.
/// The converter now emits an always-false term and pushes negation to the leaves by De Morgan.
///
/// <para>Observed state: the ranks the database returns, compared with what the two in-memory engines select from
/// the same three rows — never the shape of the condition list alone.</para>
/// </summary>
public class RuleConverterMatchNoneTests : IDisposable
{
    private readonly string _root;

    public RuleConverterMatchNoneTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-rulematchnone-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Table("MRows")]
    public class MRow : AbstractModel
    {
        public int Rank { get; set; }
    }

    private static readonly int[] AllRanks = [1, 2, 3];

    private SqLiteConnector SeededConnector()
    {
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "matchnone.db" });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(MRow) });
        var store = new SQLiteStore<MRow>();
        store.SetSettings(new SqLiteSettings(_root, "matchnone.db"));
        store.Create(AllRanks.Select(r => new MRow { Rank = r }).ToArray());
        return connector;
    }

    private static Rule Rank(int value) => new("Rank", ComparisonOperator.Equal, value);

    private static RuleGroup Empty() => new(LogicOperator.And, new List<IRule>());

    private static int[] Sql(SqLiteConnector connector, IEnumerable<Condition> conditions)
        => connector.Select(typeof(MRow), conditions).Cast<MRow>().Select(r => r.Rank).OrderBy(r => r).ToArray();

    private static int[] Spec(RuleSpecification<MRow> spec)
    {
        var rows = AllRanks.Select(r => new MRow { Rank = r }).ToList();
        var byExpression = rows.Where(spec.ToExpression().Compile()).Select(r => r.Rank).ToArray();
        var byEvaluator = rows.Where(spec.IsSatisfiedBy).Select(r => r.Rank).ToArray();
        byExpression.Should().Equal(byEvaluator, "the two in-memory engines are the reference and must agree first");
        return byExpression;
    }

    public static TheoryData<string, IRule, int[]> Trees => new()
    {
        { "disabled leaf", new Rule("Rank", ComparisonOperator.Equal, 1) { IsEnabled = false }, [] },
        { "empty group", Empty(), [] },
        { "all-disabled OR group", RuleGroup.Or(new Rule("Rank", ComparisonOperator.Equal, 1) { IsEnabled = false }), [] },
        { "negated empty group", new RuleGroup(LogicOperator.Or, new List<IRule>()) { IsNegated = true }, [] },
        { "A AND empty", RuleGroup.And(Rank(1), Empty()), [] },
        { "A OR empty", RuleGroup.Or(Rank(1), Empty()), [1] },
        { "NOT (A OR empty)", new RuleGroup(LogicOperator.Or, Rank(1), Empty()) { IsNegated = true }, [] },
        { "NOT ((A OR empty) AND B)",
            new RuleGroup(LogicOperator.And, RuleGroup.Or(Rank(1), Empty()), Rank(2)) { IsNegated = true }, [1, 3] },
        { "NOT (A AND empty)", new RuleGroup(LogicOperator.And, Rank(1), Empty()) { IsNegated = true }, [2, 3] },
        { "NOT (A OR B)", new RuleGroup(LogicOperator.Or, Rank(1), Rank(2)) { IsNegated = true }, [3] },
        { "A OR B", RuleGroup.Or(Rank(1), Rank(2)), [1, 2] },
        { "A OR (B AND C)", RuleGroup.Or(Rank(1), RuleGroup.And(Rank(2), new Rule("Rank", ComparisonOperator.GreaterThan, 1))), [1, 2] },
        { "(A OR B) AND (B OR C)", RuleGroup.And(RuleGroup.Or(Rank(1), Rank(2)), RuleGroup.Or(Rank(2), Rank(3))), [2] },
    };

    [Theory]
    [MemberData(nameof(Trees))]
    public void The_sql_converter_selects_what_the_other_engines_select(string shape, IRule rule, int[] expected)
    {
        var connector = SeededConnector();

        var sql = Sql(connector, RuleConditionConverter.ToConditions<MRow>(rule));

        Spec(new RuleSpecification<MRow>(rule)).Should().Equal(expected, $"reference answer for {shape}");
        sql.Should().Equal(expected, $"SQL must agree with RuleSpecification / RuleEvaluator for {shape}");
    }

    [Fact]
    public void A_disabled_rule_set_selects_nothing()
    {
        var connector = SeededConnector();
        var ruleSet = new RuleSet("Off", Rank(1)) { IsEnabled = false };

        Sql(connector, RuleConditionConverter.ToConditions<MRow>(ruleSet)).Should().BeEmpty();
        Spec(new RuleSpecification<MRow>(ruleSet)).Should().BeEmpty();
    }

    [Fact]
    public void An_enabled_rule_set_still_ands_its_enabled_rules()
    {
        // Contract pin: disabled members of an enabled set are skipped, not turned into match-none.
        var connector = SeededConnector();
        var ruleSet = new RuleSet("On",
            new Rule("Rank", ComparisonOperator.GreaterThanOrEqual, 2),
            new Rule("Rank", ComparisonOperator.Equal, 2) { IsEnabled = false });

        Sql(connector, RuleConditionConverter.ToConditions<MRow>(ruleSet)).Should().Equal(2, 3);
    }

    [Fact]
    public void A_rule_set_whose_later_member_is_an_or_group_ands_it_with_the_rest()
    {
        // The set's flat chain joins each term by its own IsOr, so an OR group must not leak its OR to its sibling.
        var connector = SeededConnector();
        var ruleSet = new RuleSet("Mixed", new Rule("Rank", ComparisonOperator.GreaterThan, 1), RuleGroup.Or(Rank(1), Rank(3)));

        Sql(connector, RuleConditionConverter.ToConditions<MRow>(ruleSet)).Should().Equal(3);
        Spec(new RuleSpecification<MRow>(ruleSet)).Should().Equal(3);
    }

    [Fact]
    public void A_delete_through_a_disabled_rule_deletes_nothing_instead_of_being_refused()
    {
        // Measured before the fix: the empty condition list reached the connector's whole-table guard, which threw
        // WholeTableWriteException — loud, but the wrong answer to "delete what this disabled rule matches".
        var connector = SeededConnector();
        var rule = new Rule("Rank", ComparisonOperator.Equal, 1) { IsEnabled = false };

        var act = () => connector.Delete(typeof(MRow), RuleConditionConverter.ToConditions<MRow>(rule));

        act.Should().NotThrow<WholeTableWriteException>();
        Sql(connector, Array.Empty<Condition>()).Should().Equal(1, 2, 3);
    }
}
