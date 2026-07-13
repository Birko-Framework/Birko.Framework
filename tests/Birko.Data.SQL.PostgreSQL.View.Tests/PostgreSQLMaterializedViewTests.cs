using System;
using System.Threading.Tasks;
using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.PostgreSQL.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.PostgreSQL.View.Tests;

/// <summary>
/// CR-M143: the PostgreSQL materialized-view surface had no .Tests sibling. The DDL execution needs a
/// live PostgreSQL, but the SQL-text composition (extracted into internal Build* helpers) and the
/// argument-validation guards are unit-testable offline.
/// </summary>
public class PostgreSQLMaterializedViewTests
{
    private static PostgreSQLConnector NewConnector()
        => new(new PostgreSqlSettings("localhost", "db", "user", "pass"));

    // ── DDL composition ──

    [Fact]
    public void BuildCreateMaterializedViewSql_ComposesIfNotExists()
    {
        NewConnector().BuildCreateMaterializedViewSql("mv_sales", "SELECT 1")
            .Should().Be("CREATE MATERIALIZED VIEW IF NOT EXISTS \"mv_sales\" AS SELECT 1");
    }

    [Fact]
    public void BuildRefreshMaterializedViewSql_HonorsConcurrently()
    {
        var c = NewConnector();
        c.BuildRefreshMaterializedViewSql("mv", false).Should().Be("REFRESH MATERIALIZED VIEW \"mv\"");
        c.BuildRefreshMaterializedViewSql("mv", true).Should().Be("REFRESH MATERIALIZED VIEW CONCURRENTLY \"mv\"");
    }

    [Fact]
    public void BuildDropMaterializedViewSql_ComposesIfExists()
    {
        NewConnector().BuildDropMaterializedViewSql("mv")
            .Should().Be("DROP MATERIALIZED VIEW IF EXISTS \"mv\"");
    }

    // ── argument-validation guards (throw before any DB access) ──

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ViewName_Guards_Sync_Throw(string name)
    {
        var c = NewConnector();

        c.Invoking(x => x.ViewExists(name)).Should().Throw<ArgumentException>();
        c.Invoking(x => x.MaterializedViewExists(name)).Should().Throw<ArgumentException>();
        c.Invoking(x => x.RefreshMaterializedView(name)).Should().Throw<ArgumentException>();
        c.Invoking(x => x.DropMaterializedView(name)).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ViewName_Guards_Async_ThrowSynchronously()
    {
        var c = NewConnector();

        // The async variants validate before Task.Run, so the guard throws synchronously (the Task is
        // never created) — a block-bodied Action discards the Task so the sync throw is asserted.
        FluentActions.Invoking(() => { c.RefreshMaterializedViewAsync(""); }).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => { c.DropMaterializedViewAsync(""); }).Should().Throw<ArgumentException>();
        FluentActions.Invoking(() => { c.MaterializedViewExistsAsync(""); }).Should().Throw<ArgumentException>();
    }

    [Fact]
    public void CreateMaterializedView_NonViewType_ThrowsInvalidOperation()
    {
        var c = NewConnector();

        c.Invoking(x => x.CreateMaterializedView(typeof(object))).Should().Throw<InvalidOperationException>();
        // Async validates before Task.Run → throws synchronously too.
        FluentActions.Invoking(() => { c.CreateMaterializedViewAsync(typeof(object)); }).Should().Throw<InvalidOperationException>();
    }
}
