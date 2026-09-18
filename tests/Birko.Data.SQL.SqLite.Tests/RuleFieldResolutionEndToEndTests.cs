using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.SqLite.Stores;
using Birko.Rules;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// SH-H023 (TASK-111), end-to-end against a real SQLite file.
///
/// <c>RuleConditionConverter.ConvertLeaf</c> built <c>new Condition(rule.Field, …)</c> with no resolution
/// and no validation, and every condition strategy interpolates <c>Condition.Name</c> straight into
/// <c>CommandText</c> (<c>EqualConditionStrategy</c> is <c>$"{condition.Name}{op}{value}"</c>). A rule tree
/// is configuration data and <c>docs/rules.md</c> advertises this path as producing "a direct WHERE clause".
/// Measured here before the fix, on a 3-row table, with rules whose value (999) matches nothing — so a
/// correct filter returns 0 rows:
///
/// <list type="table">
/// <item><term><c>Rank OR 1=1 --</c></term><description>returned <b>3 rows of 3</b>, no exception</description></item>
/// <item><term><c>Rank = 1 OR 1=1 --</c></term><description>returned <b>3 rows of 3</b></description></item>
/// <item><term><c>Rank; CREATE TABLE Pwned (x INTEGER); --</c></term><description><b>created the table</b></description></item>
/// <item><term><c>(SELECT count(*) FROM sqlite_master)</c></term><description>evaluated the subquery as the left operand — a blind-boolean oracle</description></item>
/// </list>
///
/// The trailing <c> = @param</c> the strategy appends is not a mitigation: <c>--</c> comments it out, and a
/// bare column name absorbs it. The parameter <i>name</i> is sanitised
/// (<c>SqlBuilderContext.GenerateParameterName</c>), which is what made this look safe on a skim — the
/// sanitisation was on the wrong string.
///
/// These run against a real database on purpose. An emitted-SQL assertion alone accepts the pre-fix clause
/// as "valid text", and what the database then does with it is the whole finding.
/// </summary>
public class RuleFieldResolutionEndToEndTests : IDisposable
{
    private readonly string _root;
    private readonly List<string> _executed = new();

    public RuleFieldResolutionEndToEndTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"birko-rulefield-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Table("RRows")]
    public class RRow : AbstractModel
    {
        [NamedField("label_col")]
        public string? Label { get; set; }

        public int Rank { get; set; }
    }

    private string DbPath => Path.Combine(_root, "rulefield.db");

    private void CreateSchema()
    {
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "rulefield.db" });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.CreateTable(new[] { typeof(RRow) });
    }

    private SqLiteConnector Connector()
    {
        var factory = new SqLiteStoreFactory(new SqLiteStoreFactoryOptions { Location = _root, Name = "rulefield.db" });
        var connector = (SqLiteConnector)factory.GetConnector();
        connector.OnExecute += text => _executed.Add(text);
        return connector;
    }

    private SQLiteStore<RRow> SeededStore()
    {
        CreateSchema();
        var store = new SQLiteStore<RRow>();
        store.SetSettings(new SqLiteSettings(_root, "rulefield.db"));
        store.Create(new[]
        {
            new RRow { Label = "a", Rank = 1 },
            new RRow { Label = "b", Rank = 2 },
            new RRow { Label = "c", Rank = 3 },
        });
        return store;
    }

    private bool TableExists(string name)
    {
        using var conn = new SqliteConnection($"Data Source={DbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name=$n";
        cmd.Parameters.AddWithValue("$n", name);
        return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
    }

    public static TheoryData<string> Payloads => new()
    {
        "Rank OR 1=1 --",
        "Rank = 1 OR 1=1 --",
        "Rank; CREATE TABLE Pwned (x INTEGER); --",
        "(SELECT count(*) FROM sqlite_master)",
    };

    // ------------------------------------------------------------------ SH-H023, injection

    [Theory]
    [MemberData(nameof(Payloads))]
    public void A_payload_field_never_reaches_the_statement(string payload)
    {
        SeededStore();
        var connector = Connector();
        _executed.Clear();

        var rule = new Rule(payload, ComparisonOperator.Equal, 999);

        var act = () => RuleConditionConverter.ToConditions<RRow>(rule).ToList();

        act.Should().Throw<ArgumentException>(
            "a rule field is interpolated into the WHERE clause, so it must be a name read out of table "
            + "metadata and never caller text");
        _executed.Should().NotContain(t => t.Contains(payload),
            "the payload must be refused before any statement is built, not rejected by the database");
    }

    [Fact]
    public void The_ddl_payload_no_longer_creates_its_table()
    {
        // Asserted positively AND as an absence: before the fix `Pwned` existed after this call. Checking
        // only that the converter threw would not prove the DDL never ran, because the throw could in
        // principle happen after the statement.
        SeededStore();
        const string payload = "Rank; CREATE TABLE Pwned (x INTEGER); --";

        var act = () => RuleConditionConverter.ToConditions<RRow>(new Rule(payload, ComparisonOperator.Equal, 999)).ToList();

        act.Should().Throw<ArgumentException>();
        TableExists("Pwned").Should().BeFalse("the DDL payload created this table before the fix");
        TableExists("RRows").Should().BeTrue();
    }

    [Fact]
    public void The_type_less_overload_also_refuses_a_payload()
    {
        SeededStore();
        const string payload = "Rank OR 1=1 --";

        var act = () => RuleConditionConverter.ToConditions(new Rule(payload, ComparisonOperator.Equal, 999)).ToList();

        act.Should().Throw<ArgumentException>();
    }

    // ------------------------------------------------------------------ SH-H023, remapped columns

    [Fact]
    public void A_remapped_property_filters_on_the_right_column()
    {
        // Before the fix this emitted `WHERE Label = @p` and SQLite answered "no such column: Label", so a
        // [NamedField]-remapped property could not be filtered at all. Note this differs from how the
        // finding was written ("references a column that does not exist" was right; "wrong column" was not
        // — it threw rather than reading the wrong one).
        SeededStore();
        var connector = Connector();
        _executed.Clear();

        var conditions = RuleConditionConverter.ToConditions<RRow>(
            new Rule("Label", ComparisonOperator.Equal, "b")).ToList();
        var rows = connector.Select(typeof(RRow), conditions).Cast<RRow>().ToList();

        rows.Should().HaveCount(1, "a remapped column must be filterable at all");
        rows[0].Label.Should().Be("b");
        rows[0].Rank.Should().Be(2);
    }

    [Fact]
    public void A_remapped_property_is_emitted_under_its_column_name()
    {
        SeededStore();
        var connector = Connector();
        _executed.Clear();

        var conditions = RuleConditionConverter.ToConditions<RRow>(
            new Rule("Label", ComparisonOperator.Equal, "b")).ToList();
        connector.Select(typeof(RRow), conditions).ToList();

        var where = _executed.First(t => t.Contains("WHERE"));
        where.Should().Contain("RRows.label_col = @",
            "the resolved identifier is table-qualified, matching what the expression path emits");
        where.Should().NotContain("WHERE Label",
            "the CLR property name is what the database rejected before the fix");
    }

    [Fact]
    public void An_unremapped_property_still_filters_correctly()
    {
        SeededStore();
        var connector = Connector();

        var conditions = RuleConditionConverter.ToConditions<RRow>(
            new Rule("Rank", ComparisonOperator.GreaterThan, 1)).ToList();
        var rows = connector.Select(typeof(RRow), conditions).Cast<RRow>().ToList();

        rows.Should().HaveCount(2);
        rows.Select(r => r.Rank).Should().BeEquivalentTo(new[] { 2, 3 });
    }

    [Fact]
    public void A_rule_group_over_a_remapped_and_a_plain_column_filters_correctly()
    {
        // Proves the resolved names survive the group path and still address real columns end-to-end.
        //
        // Deliberately an AND group. The OR version of this test fails — RuleGroup.Or over
        // (Label == "a", Rank == 3) returns 0 rows instead of 2 — but that is SH-M128, a separately filed
        // finding with a different root cause in the same file: ConvertGroup marks children via SetOr and
        // then wraps them with Condition.AndSubCondition, and AppendSubConditionsTo takes its separator
        // from the PARENT's IsOr, so an OR group renders as (a AND b). Asserting the OR result here would
        // have to encode the broken behaviour to stay green, which would bless it. Name resolution through
        // the OR branch is pinned at unit level instead
        // (RuleFieldResolutionTests.An_or_group_of_valid_fields_still_resolves_every_leaf).
        SeededStore();
        var connector = Connector();

        var group = RuleGroup.And(
            new Rule("Label", ComparisonOperator.Equal, "b"),
            new Rule("Rank", ComparisonOperator.Equal, 2));
        var conditions = RuleConditionConverter.ToConditions<RRow>(group).ToList();
        var rows = connector.Select(typeof(RRow), conditions).Cast<RRow>().ToList();

        rows.Should().HaveCount(1, "both leaves must resolve to real columns for the AND to match");
        rows[0].Label.Should().Be("b");
    }

    [Fact]
    public void An_unresolvable_field_throws_instead_of_reaching_the_database()
    {
        SeededStore();
        var connector = Connector();
        _executed.Clear();

        var act = () => RuleConditionConverter.ToConditions<RRow>(
            new Rule("NoSuchProperty", ComparisonOperator.Equal, 1)).ToList();

        act.Should().Throw<ArgumentException>().WithMessage("*NoSuchProperty*");
        _executed.Should().NotContain(t => t.Contains("NoSuchProperty"));
    }
}
