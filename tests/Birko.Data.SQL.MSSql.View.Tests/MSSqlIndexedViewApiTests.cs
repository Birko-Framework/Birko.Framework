using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.MSSql.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.MSSql.View.Tests;

/// <summary>
/// CR-M141: the indexed-view API surface (CreateIndexedView / DropIndexedView / IndexedViewExists,
/// the key-column ladder, and BuildCreateViewSql) had no offline coverage. The DDL itself needs a
/// live SQL Server, but the SQL-shape builders, the key-column fallback and the argument guards are
/// unit-testable here.
/// </summary>
public class MSSqlIndexedViewApiTests
{
    private static MSSqlConnector NewConnector()
        => new(new MSSqlSettings("localhost", "db", "user", "pass"));

    [Fact]
    public void BuildCreateViewSql_ProducesCreateOrAlterView()
    {
        var connector = NewConnector();
        var method = typeof(MSSqlConnector).GetMethod("BuildCreateViewSql", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        var sql = (string)method!.Invoke(connector, new object[] { "MyView", "SELECT 1 AS X" })!;

        sql.Should().Be("CREATE OR ALTER VIEW [MyView] AS SELECT 1 AS X");
    }

    [Fact]
    public void GetIndexedViewKeyColumns_ReturnsTableQualifiedColumns()
    {
        var connector = NewConnector();
        var view = DataBase.LoadView(typeof(SbCustomerOrderView));
        view.Should().NotBeNull();

        var method = typeof(MSSqlConnector).GetMethod("GetIndexedViewKeyColumns", BindingFlags.NonPublic | BindingFlags.Instance);
        method.Should().NotBeNull();

        var columns = ((IEnumerable<string>)method!.Invoke(connector, new object[] { view! })!).ToList();

        // The fallback ladder (primary -> unique -> first-field) must yield at least one
        // Table.Field-qualified key column for a well-formed view.
        columns.Should().NotBeEmpty();
        columns.Should().OnlyContain(c => c.Contains('.'));
    }

    [Fact]
    public void CreateIndexedView_NonViewType_ThrowsInvalidOperation()
    {
        var connector = NewConnector();

        connector.Invoking(c => c.CreateIndexedView(typeof(object)))
            .Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void DropIndexedView_EmptyName_ThrowsArgumentException(string name)
    {
        var connector = NewConnector();

        connector.Invoking(c => c.DropIndexedView(name)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task DropIndexedViewAsync_EmptyName_ThrowsArgumentException()
    {
        var connector = NewConnector();

        await connector.Invoking(c => c.DropIndexedViewAsync(""))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void IndexedViewExists_EmptyName_ThrowsArgumentException()
    {
        var connector = NewConnector();

        connector.Invoking(c => c.IndexedViewExists("")).Should().Throw<ArgumentException>();
    }

    // CR-M139: the async indexed-view methods are now genuine async (not Task.Run(sync)); their guards
    // fault the returned task rather than throwing synchronously.

    [Fact]
    public async Task IndexedViewExistsAsync_EmptyName_ThrowsArgumentException()
    {
        var connector = NewConnector();

        await connector.Invoking(c => c.IndexedViewExistsAsync(""))
            .Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CreateIndexedViewAsync_NonViewType_ThrowsInvalidOperation()
    {
        var connector = NewConnector();

        await connector.Invoking(c => c.CreateIndexedViewAsync(typeof(object)))
            .Should().ThrowAsync<InvalidOperationException>();
    }
}
