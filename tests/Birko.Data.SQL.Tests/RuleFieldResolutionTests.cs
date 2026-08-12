using System;
using System.Linq;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Conditions;
using Birko.Rules;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests;

/// <summary>
/// SH-H023 (TASK-111). <c>RuleConditionConverter</c> constructed <c>new Condition(rule.Field, …)</c> with no
/// resolution and no validation, and every condition strategy interpolates <c>Condition.Name</c> straight
/// into <c>CommandText</c> — <c>EqualConditionStrategy</c> is <c>$"{condition.Name}{op}{value}"</c>. A rule
/// tree is configuration data and <c>docs/rules.md</c> advertises this path as producing "a direct WHERE
/// clause", so caller text reached the statement.
///
/// These are the unit-level checks: what <c>Condition.Name</c> ends up holding, and which fields are
/// refused. The proof that the payloads were *executable* is end-to-end, against a real database, in
/// <c>Birko.Data.SQL.SqLite.Tests.RuleFieldResolutionEndToEndTests</c> — an assertion on emitted text alone
/// would have accepted the pre-fix clause as "valid SQL", and what the database then did with it is the
/// entire finding.
///
/// Note the fix does NOT quote the resolved identifier, matching the ORDER BY sink (TASK-110): this
/// codebase emits column identifiers bare everywhere and quotes only table names, so quoting here would
/// break a working filter on PostgreSQL, where an unquoted DDL identifier folds to lower case. The
/// resolution is the whitelist; quoting was never what closed the injection.
/// </summary>
public class RuleFieldResolutionTests
{
    [Table("RuleRows")]
    public class RuleRow : AbstractModel
    {
        [NamedField("label_col")]
        public string? Label { get; set; }

        public int Rank { get; set; }
    }

    /// <summary>
    /// The payloads measured against SQLite while verifying the finding. Each one reached CommandText
    /// verbatim before the fix; the first two returned every row for a filter that matched none, the third
    /// created a table, the fourth evaluated a subquery as the left operand.
    /// </summary>
    public static TheoryData<string> Payloads => new()
    {
        "Rank OR 1=1 --",
        "Rank = 1 OR 1=1 --",
        "Rank; CREATE TABLE Pwned (x INTEGER); --",
        "(SELECT count(*) FROM sqlite_master)",
        "1=1 OR 1=1 --",
        "Rank' OR 'a'='a",
        "Rank/*comment*/",
        // Trailing newline: .NET's `$` matches before it, so a `$`-anchored guard would have let this
        // through. Harmless in itself, but the pattern must admit only the characters it lists — hence \z.
        "Rank\n",
        "Rank\n; DROP TABLE T; --",
        "Rank Rank",
        "Rank.Table.Column",
    };

    // ── The type-aware overloads resolve ──

    [Fact]
    public void A_remapped_property_resolves_to_its_column()
    {
        // Pre-fix this emitted `Label`, and the database answered "no such column: Label" — a remapped
        // property could not be filtered at all.
        var rule = new Rule("Label", ComparisonOperator.Equal, "a");

        var conditions = RuleConditionConverter.ToConditions<RuleRow>(rule).ToList();

        conditions.Should().HaveCount(1);
        conditions[0].Name.Should().Be("RuleRows.label_col");
    }

    [Fact]
    public void An_unmapped_property_resolves_table_qualified()
    {
        // Table-qualified matches what the expression path already emits for WHERE
        // (ResolveColumnName(exprType, name, withTableName: true)) and closes the finding's "nothing
        // qualifies the name, making it ambiguous in a join".
        var rule = new Rule("Rank", ComparisonOperator.Equal, 1);

        var conditions = RuleConditionConverter.ToConditions<RuleRow>(rule).ToList();

        conditions[0].Name.Should().Be("RuleRows.Rank");
    }

    [Fact]
    public void The_column_name_itself_still_resolves()
    {
        // A caller passing the mapped column name worked before any guard existed and has to keep working;
        // it is drawn from the same metadata, so it is equally safe.
        var rule = new Rule("label_col", ComparisonOperator.Equal, "a");

        var conditions = RuleConditionConverter.ToConditions<RuleRow>(rule).ToList();

        conditions[0].Name.Should().Be("RuleRows.label_col");
    }

    [Fact]
    public void The_non_generic_type_overload_resolves_identically()
    {
        var rule = new Rule("Label", ComparisonOperator.Equal, "a");

        RuleConditionConverter.ToConditions(typeof(RuleRow), rule).Single()
            .Name.Should().Be("RuleRows.label_col");
    }

    // ── The type-aware overloads refuse ──

    [Theory]
    [MemberData(nameof(Payloads))]
    public void A_payload_field_is_refused_by_the_type_aware_overload(string payload)
    {
        var rule = new Rule(payload, ComparisonOperator.Equal, 999);

        var act = () => RuleConditionConverter.ToConditions<RuleRow>(rule).ToList();

        act.Should().Throw<ArgumentException>().WithMessage($"*{payload}*");
    }

    [Fact]
    public void An_unresolvable_field_names_the_field_and_the_entity()
    {
        var rule = new Rule("NoSuchProperty", ComparisonOperator.Equal, 1);

        var act = () => RuleConditionConverter.ToConditions<RuleRow>(rule).ToList();

        act.Should().Throw<ArgumentException>()
            .WithMessage("*NoSuchProperty*").And.Message.Should().Contain("RuleRow");
    }

    [Fact]
    public void A_blank_field_is_refused()
    {
        var rule = new Rule("   ", ComparisonOperator.Equal, 1);

        var act = () => RuleConditionConverter.ToConditions<RuleRow>(rule).ToList();

        act.Should().Throw<ArgumentException>();
    }

    // ── The type-less overloads refuse the payloads too ──

    [Theory]
    [MemberData(nameof(Payloads))]
    public void A_payload_field_is_refused_by_the_type_less_overload(string payload)
    {
        // The type-less path has no metadata to resolve against, so it cannot fix a remapping — but it can
        // insist on a bare identifier, and every measured payload carries a space, an operator, a
        // parenthesis, a quote or a statement separator.
        var rule = new Rule(payload, ComparisonOperator.Equal, 999);

        var act = () => RuleConditionConverter.ToConditions(rule).ToList();

        act.Should().Throw<ArgumentException>().WithMessage($"*{payload}*");
    }

    [Theory]
    [InlineData("Rank")]
    [InlineData("label_col")]
    [InlineData("_private")]
    [InlineData("Col9")]
    [InlineData("RuleRows.Rank")]
    public void A_bare_identifier_still_passes_the_type_less_overload(string field)
    {
        // The 20 pre-existing RuleConditionConverterTests all use plain names like "Name"/"A"/"Status", and
        // all of them still pass — this fix must not break a caller whose rule fields are already columns.
        var rule = new Rule(field, ComparisonOperator.Equal, 1);

        RuleConditionConverter.ToConditions(rule).Single().Name.Should().Be(field);
    }

    // ── The guard reaches every rule in a tree, not just a bare leaf ──

    [Fact]
    public void A_payload_nested_in_an_and_group_is_refused()
    {
        var group = RuleGroup.And(
            new Rule("Rank", ComparisonOperator.Equal, 1),
            new Rule("Rank OR 1=1 --", ComparisonOperator.Equal, 999));

        var act = () => RuleConditionConverter.ToConditions<RuleRow>(group).ToList();

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_payload_nested_in_an_or_group_is_refused()
    {
        // The OR branch runs conditions through SetOr, which rebuilds each Condition from its Name. A guard
        // that only covered the AND path would leave the rebuilt name unchecked.
        var group = RuleGroup.Or(
            new Rule("Rank", ComparisonOperator.Equal, 1),
            new Rule("Rank; CREATE TABLE Pwned (x INTEGER); --", ComparisonOperator.Equal, 999));

        var act = () => RuleConditionConverter.ToConditions<RuleRow>(group).ToList();

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void A_payload_in_a_ruleset_is_refused_when_the_conditions_are_requested()
    {
        // Materialised rather than lazy on purpose: a deferred throw would surface from inside the
        // connector's statement builder, where it reads as a database fault rather than a bad rule.
        var ruleSet = new RuleSet("Test",
            new Rule("Rank", ComparisonOperator.Equal, 1),
            new Rule("1=1 OR 1=1 --", ComparisonOperator.Equal, 999));

        var act = () => RuleConditionConverter.ToConditions<RuleRow>(ruleSet);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void An_or_group_of_valid_fields_still_resolves_every_leaf()
    {
        // SetOr rebuilds each Condition from its Name; this pins that the resolved name survives that
        // rebuild rather than being replaced by the raw field.
        var group = RuleGroup.Or(
            new Rule("Label", ComparisonOperator.Equal, "a"),
            new Rule("Rank", ComparisonOperator.Equal, 1));

        var children = RuleConditionConverter.ToConditions<RuleRow>(group)
            .Single().SubConditions!.ToList();

        children.Select(c => c.Name).Should().Equal("RuleRows.label_col", "RuleRows.Rank");
    }

    // ── A disabled rule is not a hole ──

    [Fact]
    public void A_disabled_rule_carrying_a_payload_is_skipped_not_emitted()
    {
        // Disabled rules are dropped before conversion, so the guard never sees them. That is correct —
        // nothing reaches CommandText — but it is worth pinning, because "the payload did not throw" and
        // "the payload was emitted" would otherwise look the same from the outside.
        var rule = new Rule("Rank OR 1=1 --", ComparisonOperator.Equal, 999) { IsEnabled = false };

        RuleConditionConverter.ToConditions<RuleRow>(rule).Should().BeEmpty();
    }

    [Fact]
    public void A_null_entity_type_is_rejected_rather_than_silently_skipping_resolution()
    {
        var rule = new Rule("Label", ComparisonOperator.Equal, "a");

        var act = () => RuleConditionConverter.ToConditions((Type)null!, rule).ToList();

        act.Should().Throw<ArgumentNullException>();
    }
}
