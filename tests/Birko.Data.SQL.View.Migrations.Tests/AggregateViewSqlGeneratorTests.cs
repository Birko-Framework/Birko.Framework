using System;
using System.Linq;
using Birko.Data.SQL.Attributes;
using Birko.Data.SQL.View.Migrations;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.View.Migrations.Tests;

/// <summary>
/// TASK-129 — the migrations caller of the shared view-SELECT builder.
///
/// <para>
/// The defect is in <c>ViewSelectSqlBuilder</c>, which all three callers share, so it reached
/// <c>ViewSqlGenerator.GenerateCreateViewSql</c> too: an aggregate arrived with two aliases
/// (<c>COUNT(Orders.Guid) as COUNT AS "OrderCount"</c>) and the statement was a syntax error on every
/// provider, so no persistent aggregate view could be migrated into existence.
/// </para>
///
/// <para>
/// These assert the generated statement rather than executing it, because <c>GenerateCreateViewSql</c>
/// emits <c>CREATE OR REPLACE VIEW</c>, which SQLite does not accept — the same reason
/// <see cref="ViewMigrationExtensionsTests"/> uses a recording fake. The executed-against-SQLite half of
/// TASK-129 lives in <c>Birko.Data.SQL.Views.Tests.AggregateViewDdlTests</c>, which drives the SQLite
/// connector's own <c>CreateView</c> over the identical builder output.
/// </para>
/// </summary>
public class AggregateViewSqlGeneratorTests
{
    [Table("AggCustomers")]
    public class AggCustomer : Birko.Data.Models.AbstractModel
    {
        public string Name { get; set; } = null!;
    }

    [Table("AggOrders")]
    public class AggOrder : Birko.Data.Models.AbstractModel
    {
        public Guid CustomerId { get; set; }
        public decimal Total { get; set; }
        public decimal Tax { get; set; }
    }

    [View(typeof(AggCustomer), typeof(AggOrder), nameof(AggCustomer.Guid), nameof(AggOrder.CustomerId), name: "AggTotals")]
    public class AggTotalsView
    {
        [ViewField(typeof(AggCustomer), nameof(AggCustomer.Name))]
        public string CustomerName { get; set; } = null!;

        [CountField(typeof(AggOrder), nameof(AggOrder.Guid))]
        public int OrderCount { get; set; }

        [SumField(typeof(AggOrder), nameof(AggOrder.Total))]
        public decimal TotalSpent { get; set; }

        // Same function as TotalSpent, different source column: before the fix both were keyed "SUM" and
        // View.AddField dropped this one silently, so it never reached the DDL at all.
        [SumField(typeof(AggOrder), nameof(AggOrder.Tax))]
        public decimal TotalTax { get; set; }
    }

    [Fact]
    public void GenerateCreateViewSql_gives_each_aggregate_exactly_one_alias()
    {
        var sql = ViewSqlGenerator.GenerateCreateViewSql(typeof(AggTotalsView));

        var projection = sql.Substring(
            sql.IndexOf(" AS SELECT ", StringComparison.Ordinal) + " AS SELECT ".Length,
            sql.IndexOf(" FROM ", StringComparison.Ordinal) - (sql.IndexOf(" AS SELECT ", StringComparison.Ordinal) + " AS SELECT ".Length));

        foreach (var item in projection.Split(',').Select(x => x.Trim()))
        {
            // Pre-fix these items were `COUNT(AggOrders.Guid) as COUNT AS "OrderCount"` — two aliases.
            item.Split(' ').Count(t => t.Equals("as", StringComparison.OrdinalIgnoreCase))
                .Should().BeLessThanOrEqualTo(1, $"'{item}' must carry at most one alias");
        }
    }

    [Fact]
    public void GenerateCreateViewSql_aliases_every_aggregate_to_the_column_the_persistent_read_asks_for()
    {
        var view = DataBase.LoadView(typeof(AggTotalsView));
        view.Should().NotBeNull();

        var sql = ViewSqlGenerator.GenerateCreateViewSql(typeof(AggTotalsView));

        // Asserted against the metadata, not against literals: the alias and the persistent read have to
        // name the same column or the view is created and then cannot be queried.
        foreach (var column in view!.GetPersistentViewSelectFields().Values)
        {
            sql.Should().Contain(column);
        }
        sql.Should().Contain("AS OrderCount");
        sql.Should().Contain("AS TotalSpent");
        sql.Should().Contain("AS TotalTax");
    }

    [Fact]
    public void GenerateCreateViewSql_keeps_both_aggregates_of_the_same_function()
    {
        var view = DataBase.LoadView(typeof(AggTotalsView));

        view!.GetPersistentViewSelectFields().Values.Should().Contain(new[] { "TotalSpent", "TotalTax" });
        ViewSqlGenerator.GenerateCreateViewSql(typeof(AggTotalsView))
            .Split(',').Count(x => x.Contains("SUM(", StringComparison.Ordinal)).Should().Be(2);
    }
}
