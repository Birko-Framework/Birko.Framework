using System;
using System.Reflection;
using System.Threading.Tasks;
using Birko.Data.SQL;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.MSSql.View.Tests;

/// <summary>
/// CR-L181: CreateIndexedView rejects aggregate (GROUP BY) views up-front (SQL Server would otherwise fail
/// the clustered-index creation because the SCHEMABINDING SELECT lacks COUNT_BIG(*)); non-aggregate views
/// pass the guard. CR-L180: GetSchemaName is protected virtual so a derived connector can target a non-dbo
/// schema in the SCHEMABINDING two-part names.
/// </summary>
public class MSSqlIndexedViewGuardAndSchemaTests
{
    private static MSSqlConnector NewConnector()
        => new(new MSSqlSettings("localhost", "db", "user", "pass"));

    // CR-L181 — aggregate view rejected before any DDL (offline-safe: the guard runs before the connection).
    [Fact]
    public void CreateIndexedView_AggregateView_ThrowsNotSupported()
    {
        NewConnector().Invoking(c => c.CreateIndexedView(typeof(SbCustomerOrderView)))
            .Should().Throw<NotSupportedException>()
            .WithMessage("*COUNT_BIG*");
    }

    [Fact]
    public async Task CreateIndexedViewAsync_AggregateView_ThrowsNotSupported()
    {
        await NewConnector().Invoking(c => c.CreateIndexedViewAsync(typeof(SbCustomerOrderView)))
            .Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*COUNT_BIG*");
    }

    [Fact]
    public void EnsureIndexedViewSupported_NonAggregateView_DoesNotThrow()
    {
        var view = DataBase.LoadView(typeof(SbCustomerOrderPlainView));
        view.Should().NotBeNull();
        view!.HasAggregateFields().Should().BeFalse();

        var method = typeof(MSSqlConnector).GetMethod(
            "EnsureIndexedViewSupported", BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();

        method!.Invoking(m => m.Invoke(null, new object[] { view! })).Should().NotThrow();
    }

    // CR-L180 — a derived connector overriding GetSchemaName changes the SCHEMABINDING two-part names.
    private sealed class AppSchemaConnector : MSSqlConnector
    {
        public AppSchemaConnector() : base(new MSSqlSettings("localhost", "db", "user", "pass")) { }
        protected override string GetSchemaName() => "app";
    }

    [Fact]
    public void GetSchemaName_Override_ChangesSchemaBindingQualification()
    {
        var connector = new AppSchemaConnector();
        var view = DataBase.LoadView(typeof(SbCustomerOrderPlainView))!;

        var method = typeof(MSSqlConnector).GetMethod(
            "BuildSchemaBindingSelectSql", BindingFlags.NonPublic | BindingFlags.Instance);
        var sql = (string)method!.Invoke(connector, new object[] { view })!;

        sql.Should().Contain("FROM [app].[SbCustomers]");
        sql.Should().NotContain("[dbo].");
    }
}
