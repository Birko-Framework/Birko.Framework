using System;
using Birko.Data.Models;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.View.Migrations;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.View.Migrations.Tests;

[Table("T272GenEvts", Schema = "t272gen")]
public class T272GenEvt : AbstractModel
{
    public string? Name { get; set; }
}

[Table("T272GenPlain")]
public class T272GenPlain : AbstractModel
{
    public string? Name { get; set; }
}

[View(typeof(T272GenEvt), typeof(T272GenPlain), nameof(T272GenEvt.Name), nameof(T272GenPlain.Name))]
public class T272GenView
{
    [ViewField(typeof(T272GenPlain), nameof(T272GenPlain.Name))]
    public string? Name { get; set; }
}

/// <summary>
/// TASK-272: <c>ViewSqlGenerator</c> has no connector, so it cannot render a table's schema (a qualifier on PostgreSQL and
/// SQL Server, a <c>schema__</c> prefix on MySQL and SQLite). It refuses such a view instead of writing it against the
/// default schema.
/// </summary>
public class Task272SchemaViewGeneratorTests
{
    [Fact]
    public void A_view_over_a_table_in_a_schema_is_refused()
    {
        FluentActions.Invoking(() => ViewSqlGenerator.GenerateCreateViewSql(typeof(T272GenView), "t272_gen_view"))
            .Should().Throw<NotSupportedException>().WithMessage("*T272GenEvts*CreateView*");
    }
}
