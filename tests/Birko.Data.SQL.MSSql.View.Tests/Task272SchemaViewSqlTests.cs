using System;
using System.Reflection;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.MSSql.View.Tests;

[Table("T272SbCustomers", Schema = "t272sb")]
public class T272SbCustomer : AbstractLogModel
{
    public string Name { get; set; } = null!;
}

[Table("T272SbOrders")]
public class T272SbOrder : AbstractLogModel
{
    public Guid CustomerId { get; set; }
}

[View(typeof(T272SbCustomer), typeof(T272SbOrder), nameof(T272SbCustomer.Guid), nameof(T272SbOrder.CustomerId), connect: ViewConnect.CheckExisting)]
public class T272SbView
{
    [ViewField(typeof(T272SbCustomer), nameof(T272SbCustomer.Name))]
    public string Name { get; set; } = null!;

    [ViewField(typeof(T272SbOrder), nameof(T272SbOrder.Guid))]
    public Guid? OrderId { get; set; }
}

/// <summary>
/// TASK-272: SQL Server view DDL names a table in its declared schema. SCHEMABINDING took every table as <c>[dbo].[T]</c>
/// (<c>GetSchemaName</c>, hard-coded), so a view over <c>t272sb.T272SbCustomers</c> bound the wrong table or none.
/// </summary>
public class Task272SchemaViewSqlTests
{
    private static string Invoke(Type owner, string method, MSSqlConnector connector, Tables.View view)
        => (string)owner.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(connector, new object[] { view })!;

    private static Tables.View View() => DataBase.LoadView(typeof(T272SbView))!;

    [Fact]
    public void Schemabinding_names_each_table_in_its_own_schema()
    {
        var sql = Invoke(typeof(MSSqlConnector), "BuildSchemaBindingSelectSql", new MSSqlConnector(new MSSqlSettings("h", "db", "u", "p")), View());

        sql.Should().Contain("[t272sb].[T272SbCustomers]");
        sql.Should().Contain("[dbo].[T272SbOrders]", "a table without a schema keeps the default two-part name");
    }

    [Fact]
    public void Schemabinding_takes_the_settings_default_schema_before_dbo()
    {
        var connector = new MSSqlConnector(new MSSqlSettings("h", "db", "u", "p") { Schema = "t272def" });
        var sql = Invoke(typeof(MSSqlConnector), "BuildSchemaBindingSelectSql", connector, View());

        sql.Should().Contain("[t272sb].[T272SbCustomers]", "the entity's own schema wins");
        sql.Should().Contain("[t272def].[T272SbOrders]");
    }

    [Fact]
    public void A_plain_view_aliases_a_schema_table_by_its_quoted_logical_name()
    {
        var sql = Invoke(typeof(AbstractConnectorBase), "BuildViewSelectSql", new MSSqlConnector(new MSSqlSettings("h", "db", "u", "p")), View());

        sql.Should().Contain("[t272sb].[T272SbCustomers] AS [T272SbCustomers]");
        sql.Should().Contain("JOIN [T272SbOrders]", "a table with no schema renders exactly as before");
    }
}
