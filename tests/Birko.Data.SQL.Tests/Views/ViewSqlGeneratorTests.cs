using Birko.Data.SQL.View.Migrations;
using Birko.Data.SQL.Tests.TestResources.Views;
using FluentAssertions;
using System;
using Xunit;

namespace Birko.Data.SQL.Tests.Views;

public class ViewSqlGeneratorTests
{
    [Fact]
    public void GenerateDropViewSql_ByName_ProducesCorrectSql()
    {
        var sql = ViewSqlGenerator.GenerateDropViewSql("my_view");

        sql.Should().Be("DROP VIEW IF EXISTS \"my_view\"");
    }

    [Fact]
    public void GenerateDropViewSql_WithCustomQuoteChar_UsesIt()
    {
        var sql = ViewSqlGenerator.GenerateDropViewSql("my_view", '`');

        sql.Should().Be("DROP VIEW IF EXISTS `my_view`");
    }

    [Fact]
    public void GenerateDropViewSql_EmptyName_ThrowsArgumentException()
    {
        var act = () => ViewSqlGenerator.GenerateDropViewSql("");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GenerateDropViewSql_NullName_ThrowsArgumentException()
    {
        var act = () => ViewSqlGenerator.GenerateDropViewSql((string)null!);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void GenerateDropViewSql_ByType_ProducesDropStatement()
    {
        var sql = ViewSqlGenerator.GenerateDropViewSql(typeof(CustomerOrderView));

        sql.Should().StartWith("DROP VIEW IF EXISTS ");
    }

    [Fact]
    public void GenerateDropViewSql_ByTypeWithExplicitName_UsesName()
    {
        var sql = ViewSqlGenerator.GenerateDropViewSql(typeof(CustomerOrderView), "custom_name");

        sql.Should().Be("DROP VIEW IF EXISTS \"custom_name\"");
    }

    [Fact]
    public void GenerateDropViewSql_NullType_ThrowsArgumentNullException()
    {
        var act = () => ViewSqlGenerator.GenerateDropViewSql((Type)null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GetViewName_WithExplicitViewName_ReturnsName()
    {
        // CustomerOrderView does not have an explicit name, so it should fall back to table names or type name
        var name = ViewSqlGenerator.GetViewName(typeof(CustomerOrderView));

        name.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void GetViewName_NullType_ThrowsArgumentNullException()
    {
        var act = () => ViewSqlGenerator.GetViewName(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void GenerateCreateViewSql_CustomerOrderView_ContainsCreateOrReplace()
    {
        var sql = ViewSqlGenerator.GenerateCreateViewSql(typeof(CustomerOrderView));

        sql.Should().StartWith("CREATE OR REPLACE VIEW ");
        sql.Should().Contain(" AS ");
        sql.Should().Contain("SELECT ");
    }

    [Fact]
    public void GenerateCreateViewSql_CustomerOrderView_ContainsJoin()
    {
        var sql = ViewSqlGenerator.GenerateCreateViewSql(typeof(CustomerOrderView));

        sql.Should().Contain("JOIN");
    }

    [Fact]
    public void GenerateCreateViewSql_WithExplicitName_UsesProvidedName()
    {
        var sql = ViewSqlGenerator.GenerateCreateViewSql(typeof(CustomerOrderView), "vw_customer_orders");

        sql.Should().Contain("\"vw_customer_orders\"");
    }

    [Fact]
    public void GenerateCreateViewSql_NullType_ThrowsArgumentNullException()
    {
        var act = () => ViewSqlGenerator.GenerateCreateViewSql(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void DefaultQuoteChar_IsAnsiDoubleQuote()
    {
        ViewSqlGenerator.DefaultQuoteChar.Should().Be('"');
    }
}
