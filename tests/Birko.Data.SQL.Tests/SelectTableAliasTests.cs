using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Tests.TestHelpers;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests;

/// <summary>
/// TASK-211 — a SELECT's <c>FROM</c>/<c>JOIN</c> emits the table QUOTED with a BARE alias equal to its
/// name, so that the bare <c>Table.Column</c> qualifiers this framework emits everywhere else resolve.
///
/// <para>
/// <b>These are contract pins, not evidence of the fix.</b> The defect they describe is only observable on
/// a provider that case-folds an unquoted identifier, and PostgreSQL is the only supported one that does —
/// SQLite (which every offline suite uses), MySQL and MSSql all resolve <c>Table.Column</c> against a
/// quoted <c>"Table"</c> case-insensitively, so no assertion here can distinguish the fix from the defect.
/// The evidence lives in <c>Birko.Data.SQL.PostgreSQL.View.Tests.PostgreSqlOnTheFlyViewTests</c>, gated on
/// a live server. What these pin is the SHAPE, so the next person to touch the emitter finds out here
/// rather than in production on one provider.
/// </para>
/// </summary>
public class SelectTableAliasTests
{
    private static string Sql(
        IEnumerable<string> tables,
        IDictionary<int, string> fields,
        IEnumerable<Join>? joins = null)
    {
        var connector = new FakeConnector();
        var command = new TestDbCommand();
        connector.CreateSelectCommand(command, tables, fields, joins, null, null, null, null, null);
        return command.CommandText;
    }

    [Fact]
    public void The_from_clause_quotes_the_table_and_aliases_it_bare()
    {
        var sql = Sql(new[] { "Widgets" }, new Dictionary<int, string> { { 0, "Widgets.Name" } });

        sql.Should().Be("SELECT Widgets.Name FROM \"Widgets\" AS Widgets");
    }

    [Fact]
    public void Every_table_qualifier_in_the_projection_is_declared_as_an_alias()
    {
        // The property, asserted over the whole projection rather than against a literal: whatever prefix
        // the SELECT list qualifies with must be introduced by the FROM clause under exactly that spelling.
        // (TASK-129's pin passed against `as COUNT AS "OrderCount"` because it asserted a literal substring;
        // asserting the relationship is what catches a producer drifting.)
        var fields = new Dictionary<int, string>
        {
            { 0, "Widgets.Name" },
            { 1, "Parts.Code" },
            { 2, "COUNT(Parts.Id)" },
        };
        var joins = new[]
        {
            Join.Create("Widgets", "Parts", JoinType.Inner,
                new[] { new Condition("Widgets.Id", new object[] { "Parts.WidgetId" }) { IsField = true } }),
        };

        var sql = Sql(new[] { "Widgets" }, fields, joins);

        foreach (var qualifier in new[] { "Widgets", "Parts" })
        {
            sql.Should().Contain($" AS {qualifier}",
                $"the projection qualifies columns with '{qualifier}.', which only resolves if the FROM/JOIN "
                + "clause introduces that exact bare name");
        }
    }

    [Fact]
    public void A_joined_table_is_aliased_the_same_way()
    {
        var joins = new[]
        {
            Join.Create("Widgets", "Parts", JoinType.Inner,
                new[] { new Condition("Widgets.Id", new object[] { "Parts.WidgetId" }) { IsField = true } }),
        };

        var sql = Sql(new[] { "Widgets" }, new Dictionary<int, string> { { 0, "Widgets.Name" } }, joins);

        sql.Should().Contain("INNER JOIN \"Parts\" AS Parts");
    }

    [Fact]
    public void The_alias_is_never_quoted()
    {
        // Quoting the alias would defeat the whole point: a quoted alias is case-sensitive again on a
        // folding provider, and the bare qualifiers would stop matching it.
        var sql = Sql(new[] { "Widgets" }, new Dictionary<int, string> { { 0, "Widgets.Name" } });

        sql.Should().NotContain("AS \"Widgets\"");
    }

    [Theory]
    [InlineData("My Table")]
    [InlineData("weird-name")]
    [InlineData("has.dot")]
    public void A_name_that_cannot_take_a_bare_alias_is_emitted_unaliased(string table)
    {
        // Not a gap: such a table cannot be read through a qualified SELECT on any provider anyway. The
        // point is that the change stays away from the one shape it is not about — an unqualified
        // `SELECT COUNT(*)`, which works for such a table today and must keep working.
        var sql = Sql(new[] { table }, new Dictionary<int, string> { { 0, "COUNT(*)" } });

        sql.Should().Be($"SELECT COUNT(*) FROM \"{table}\"");
        sql.Should().NotContain(" AS ");
    }

    // Minimal concrete connector to reach the base virtual (the base is abstract).
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
