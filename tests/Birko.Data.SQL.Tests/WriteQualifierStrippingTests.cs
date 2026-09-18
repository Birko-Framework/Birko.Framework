using System;
using System.Collections.Generic;
using System.Data;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests;

/// <summary>
/// TASK-216 — a filtered write drops the target table's qualifier, so <c>WHERE Widgets.Name = @p</c> is
/// emitted as <c>WHERE Name = @p</c>. A write targets exactly one table, so the qualifier carries no
/// information and a bare column cannot be ambiguous.
///
/// <para>
/// <b>Shape pins, not evidence of the defect.</b> The defect is only observable on a provider that
/// case-folds an unquoted identifier, and SQLite — which this suite uses — is case-insensitive. The evidence
/// lives in <c>Birko.Data.SQL.PostgreSQL.View.Tests.PostgreSqlFilteredWriteTests</c>, gated on a live
/// server. What these pin is the contract, including the two things easiest to get wrong when someone next
/// touches the rewrite: that it does not reach reads, and that it does not corrupt a longer table name.
/// </para>
/// </summary>
public class WriteQualifierStrippingTests
{
    private static string WriteWhere(IEnumerable<Condition> conditions, string tableName)
    {
        var connector = new FakeConnector();
        var command = new TestDbCommand { CommandText = "DELETE FROM \"" + tableName + "\"" };
        connector.AddRequiredWhere(conditions, command, "delete", tableName);
        return command.CommandText;
    }

    [Fact]
    public void The_target_tables_qualifier_is_dropped()
    {
        var sql = WriteWhere(new[] { new Condition("Widgets.Name", new object[] { "a" }) }, "Widgets");

        sql.Should().StartWith("DELETE FROM \"Widgets\" WHERE Name");
        sql.Should().NotContain("Widgets.Name");
    }

    [Fact]
    public void A_qualifier_inside_a_function_call_is_dropped_too()
    {
        // The shape a rewrite that walks condition names one at a time would miss — and the shapes this
        // parser actually produces: LOWER(T.Col), COALESCE(T.A, T.B), and the .Date rewrite's range pair.
        var sql = WriteWhere(new[] { new Condition("LOWER(Widgets.Name)", new object[] { "a" }) }, "Widgets");

        sql.Should().Contain("LOWER(Name)");
        sql.Should().NotContain("Widgets.Name");
    }

    [Fact]
    public void A_different_table_whose_name_ends_with_the_targets_is_not_corrupted()
    {
        // Target `Person`; the condition names `MyPerson.Col`. A naive replace of "Person." would leave
        // "MyCol" — a column that does not exist, and a silent wrong statement rather than a loud one.
        var sql = WriteWhere(new[] { new Condition("MyPerson.Col", new object[] { 1 }) }, "Person");

        sql.Should().Contain("MyPerson.Col");
        sql.Should().NotContain("MyCol");
    }

    [Fact]
    public void A_read_keeps_its_qualifiers()
    {
        // The regression this fix could most easily cause. Reads go through AddWhere and MUST keep their
        // qualifiers: a multi-table read needs them to disambiguate, and they resolve against the bare
        // alias TASK-211 put in the FROM clause.
        var connector = new FakeConnector();
        var command = new TestDbCommand { CommandText = "SELECT Widgets.Name FROM \"Widgets\" AS Widgets" };

        connector.AddWhere(new[] { new Condition("Widgets.Name", new object[] { "a" }) }, command);

        command.CommandText.Should().Contain("WHERE Widgets.Name");
    }

    [Fact]
    public void An_empty_clause_still_refuses_the_write()
    {
        // The whole-table write guard (SH-H002 / TASK-109) decides on the rendered clause being empty, and
        // the rewrite happens after that decision. Pin, not evidence: it passes either way.
        var connector = new FakeConnector();
        var command = new TestDbCommand { CommandText = "DELETE FROM \"Widgets\"" };

        var act = () => connector.AddRequiredWhere(Array.Empty<Condition>(), command, "delete", "Widgets");

        act.Should().Throw<Birko.Data.Exceptions.WholeTableWriteException>();
    }

    private sealed class FakeConnector : Birko.Data.SQL.Connectors.AbstractConnectorBase
    {
        public FakeConnector() : base(new Birko.Configuration.PasswordSettings()) { }

        public override System.Data.Common.DbConnection CreateConnection(Birko.Configuration.PasswordSettings settings)
            => throw new NotSupportedException();
        public override string ConvertType(DbType type, Birko.Data.SQL.Fields.AbstractField field)
            => throw new NotSupportedException();
        public override string FieldDefinition(Birko.Data.SQL.Fields.AbstractField field)
            => throw new NotSupportedException();
    }
}
