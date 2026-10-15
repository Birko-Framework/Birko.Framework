using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Rules;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-153, the medium findings in <c>filter-expression-translation</c>. Every SQL answer is checked against the
/// answer C# (or, for rules, the in-memory <c>RuleEvaluator</c>'s semantics) gives over the same rows, on a real
/// database: these defects were wrong answers, never exceptions.
/// </summary>
public class FilterTranslationTriageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"birko-ft153-{Guid.NewGuid():N}");
    private int _db;

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Table("Ft153")]
    public class Row : AbstractModel
    {
        public string? Name { get; set; }
        public string? Code { get; set; }
        public int Age { get; set; }
        public int? Score { get; set; }
        public bool Vip { get; set; }
        public int Premium { get; set; }
    }

    private static Row[] Seed() => new Row[]
    {
        new() { Name = "alpha", Code = "FV2026-1", Age = 10, Score = 5, Vip = true, Premium = 200 },
        new() { Name = "100% beta", Code = "A_1", Age = 20, Score = null, Vip = false, Premium = 50 },
        new() { Name = "", Code = "A21", Age = 30, Score = 150, Vip = false, Premium = 10 },
        new() { Name = null, Code = "XFV2026", Age = 40, Score = 1, Vip = true, Premium = 90 },
        new() { Name = "ABC", Code = null, Age = 50, Score = 7, Vip = false, Premium = 0 },
        new() { Name = "A", Code = "B", Age = 60, Score = 2, Vip = false, Premium = 1 },
        new() { Name = "1000 gamma", Code = "AB", Age = 70, Score = 3, Vip = false, Premium = 2 },
    };

    private Data.SQL.Connectors.SqLiteConnector Connector()
    {
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = $"f{++_db}.db" });
        var connector = (Data.SQL.Connectors.SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(Row) });
        foreach (var row in Seed())
        {
            row.Guid = Guid.NewGuid();
            connector.Insert(row);
        }
        return connector;
    }

    private static int[] Ages(IEnumerable<Row> rows) => rows.Select(r => r.Age).OrderBy(a => a).ToArray();

    /// <summary>The SQL answer must be the C# answer over the same rows.</summary>
    private void Agrees(Expression<Func<Row, bool>> filter)
        => Ages(Connector().Select(typeof(Row), filter).Cast<Row>()).Should().Equal(Ages(Seed().Where(filter.Compile())), filter.ToString());

    private int[] RuleAges(IRule rule) => Ages(Connector().Select(typeof(Row), RuleConditionConverter.ToConditions<Row>(rule)).Cast<Row>());

    private static int[] Expected(Func<Row, bool> predicate) => Ages(Seed().Where(predicate));

    // ---- SH-M123: a value on the left of an ordering comparison ----

    [Fact] public void A_value_on_the_left_of_greater_than_is_flipped() => Agrees(x => 25 > x.Age);
    [Fact] public void A_value_on_the_left_of_less_or_equal_is_flipped() => Agrees(x => 25 <= x.Age);
    [Fact] public void A_value_on_the_left_of_an_equality_still_matches() => Agrees(x => 30 == x.Age);

    // ---- SH-M127: string.IsNullOrEmpty ----

    [Fact] public void IsNullOrEmpty_matches_null_and_empty() => Agrees(x => string.IsNullOrEmpty(x.Name));
    [Fact] public void Not_IsNullOrEmpty_matches_the_rest() => Agrees(x => !string.IsNullOrEmpty(x.Name));
    [Fact] public void IsNullOrEmpty_inside_a_conjunction() => Agrees(x => string.IsNullOrEmpty(x.Name) && x.Age > 35);
    [Fact] public void IsNullOrEmpty_inside_a_disjunction() => Agrees(x => x.Age == 10 || string.IsNullOrEmpty(x.Name));

    // ---- SH-M134: LIKE metacharacters in a search term are literal ----

    [Fact] public void StartsWith_an_underscore_is_literal() => Agrees(x => x.Code != null && x.Code.StartsWith("A_1"));
    [Fact] public void Contains_an_underscore_is_literal() => Agrees(x => x.Code != null && x.Code.Contains("_"));
    [Fact] public void Contains_a_percent_is_literal() => Agrees(x => x.Name != null && x.Name.Contains("0%"));
    [Fact] public void EndsWith_the_escape_character_is_literal() => Agrees(x => x.Code != null && x.Code.EndsWith("!"));

    // ---- SH-M135: a bool column as a CASE test in WHERE ----

    [Fact] public void A_bool_column_tests_a_CASE_in_WHERE() => Agrees(x => (x.Vip ? x.Premium : x.Score) > 100);

    // ---- SH-M125: a pattern method whose pattern is a column ----

    [Fact]
    public void A_pattern_built_from_a_column_is_refused_not_answered_empty()
    {
        var act = () => Connector().Select(typeof(Row), (Expression<Func<Row, bool>>)(x => x.Name!.StartsWith(x.Code!))).Cast<Row>().ToList();
        act.Should().Throw<NotSupportedException>().WithMessage("*pattern is a column*");
    }

    // ---- SH-M129..M132: the rule converter ----

    [Fact]
    public void A_Between_rule_keeps_both_bounds()
        => RuleAges(Rules.Rule.Between("Age", 15, 35)).Should().Equal(Expected(r => r.Age >= 15 && r.Age <= 35));

    [Fact]
    public void A_negated_Between_rule_is_outside_both_bounds()
        => RuleAges(new Rules.Rule("Age", ComparisonOperator.Between, 15) { UpperValue = 35, IsNegated = true })
            .Should().Equal(Expected(r => r.Age < 15 || r.Age > 35));

    [Fact]
    public void A_Between_rule_negated_by_its_group_is_outside_both_bounds()
        => RuleAges(new RuleGroup(LogicOperator.And, new List<IRule> { Rules.Rule.Between("Age", 15, 35) }) { IsNegated = true })
            .Should().Equal(Expected(r => r.Age < 15 || r.Age > 35));

    [Fact]
    public void A_Between_rule_without_an_upper_bound_is_refused()
    {
        var act = () => RuleConditionConverter.ToConditions<Row>(new Rules.Rule("Age", ComparisonOperator.Between, 15));
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void An_Equal_rule_with_a_null_value_is_IS_NULL()
        => RuleAges(new Rules.Rule("Name", ComparisonOperator.Equal, null)).Should().Equal(Expected(r => r.Name == null));

    [Fact]
    public void A_NotEqual_rule_with_a_null_value_is_IS_NOT_NULL()
        => RuleAges(new Rules.Rule("Name", ComparisonOperator.NotEqual, null)).Should().Equal(Expected(r => r.Name != null));

    [Fact]
    public void An_In_rule_with_a_string_is_one_value_not_its_characters()
        => RuleAges(new Rules.Rule("Name", ComparisonOperator.In, "ABC")).Should().Equal(Expected(r => r.Name == "ABC"));

    [Fact]
    public void A_Like_rule_with_a_trailing_percent_is_a_prefix()
        => RuleAges(new Rules.Rule("Code", ComparisonOperator.Like, "FV2026%")).Should().Equal(Expected(r => r.Code?.StartsWith("FV2026") == true));

    [Fact]
    public void A_Like_rule_with_a_leading_percent_is_a_suffix()
        => RuleAges(new Rules.Rule("Code", ComparisonOperator.Like, "%2026")).Should().Equal(Expected(r => r.Code?.EndsWith("2026") == true));

    [Fact]
    public void A_Like_rule_without_a_percent_is_an_equality_and_its_underscore_literal()
        => RuleAges(new Rules.Rule("Code", ComparisonOperator.Like, "A_1")).Should().Equal(Expected(r => r.Code == "A_1"));
}
