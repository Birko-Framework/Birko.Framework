using System.Reflection;
using Birko.Data.SQL;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.MSSql.View.Tests;

/// <summary>
/// CR-M140: BuildSchemaBindingSelectSql used to be a ~140-line copy of the base
/// AbstractConnectorBase.BuildViewSelectSql. It now delegates to the shared
/// ViewSelectSqlBuilder, passing a table-qualifier that emits two-part [dbo].[Table] names.
/// These offline tests lock in that the schema-binding SELECT still (a) two-part-qualifies the
/// FROM/JOIN tables, (b) keeps the identical aggregate AS aliases + GROUP BY as the base builder,
/// and (c) leaves the base (regular-view) SELECT using plain single-part table names.
/// </summary>
public class MSSqlSchemaBindingSelectSqlTests
{
    private static MSSqlConnector NewConnector()
        => new(new MSSqlSettings("localhost", "db", "user", "pass"));

    private static string SchemaBindingSelect(MSSqlConnector connector, Tables.View view)
    {
        var method = typeof(MSSqlConnector).GetMethod(
            "BuildSchemaBindingSelectSql", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        return (string)method!.Invoke(connector, new object[] { view })!;
    }

    private static string BaseViewSelect(MSSqlConnector connector, Tables.View view)
    {
        var method = typeof(AbstractConnectorBase).GetMethod(
            "BuildViewSelectSql", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();
        return (string)method!.Invoke(connector, new object[] { view })!;
    }

    private static Tables.View LoadView()
    {
        var view = DataBase.LoadView(typeof(SbCustomerOrderView));
        view.Should().NotBeNull();
        return view!;
    }

    [Fact]
    public void SchemaBindingSelect_QualifiesTablesWithTwoPartName()
    {
        var sql = SchemaBindingSelect(NewConnector(), LoadView());

        sql.Should().Contain("FROM [dbo].[SbCustomers]");
        sql.Should().Contain("JOIN [dbo].[SbOrders]");
    }

    [Fact]
    public void SchemaBindingSelect_KeepsAggregateAliasesAndGroupBy()
    {
        var sql = SchemaBindingSelect(NewConnector(), LoadView());

        sql.Should().StartWith("SELECT ");
        // Aggregate AS aliases use the aggregate name (bracket-quoted), same as the base builder.
        sql.Should().Contain("COUNT(SbOrders.Guid)");
        sql.Should().Contain("AS [COUNT]");
        sql.Should().Contain("SUM(SbOrders.Total)");
        sql.Should().Contain("AS [SUM]");
        sql.Should().Contain("GROUP BY");
    }

    [Fact]
    public void BaseViewSelect_UsesPlainSinglePartTableNames()
    {
        // The regular (non-indexed) MSSql view path must NOT gain the [dbo]. prefix — the schema
        // qualification is scoped to the SCHEMABINDING builder only.
        var baseSql = BaseViewSelect(NewConnector(), LoadView());

        baseSql.Should().Contain("FROM [SbCustomers]");
        baseSql.Should().NotContain("[dbo].");
    }

    [Fact]
    public void SchemaBindingAndBaseSelect_DifferOnlyInTableQualification()
    {
        var connector = NewConnector();
        var view = LoadView();

        var schemaSql = SchemaBindingSelect(connector, view);
        var baseSql = BaseViewSelect(connector, view);

        // Re-qualifying the base FROM/JOIN tokens to two-part names reproduces the schema-binding SQL,
        // proving the field/aggregate/join-grouping logic is shared and unchanged.
        var reQualified = baseSql
            .Replace("FROM [SbCustomers]", "FROM [dbo].[SbCustomers]")
            .Replace("JOIN [SbOrders]", "JOIN [dbo].[SbOrders]");

        reQualified.Should().Be(schemaSql);
    }
}
