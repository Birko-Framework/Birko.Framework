using Birko.Data.SQL.View.Migrations;
using Birko.Data.SQL.Connectors;
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
    public void GenerateCreateViewSql_CustomerOrderView_IsAPlainCreate()
    {
        // TASK-717: a plain CREATE VIEW, which every provider runs; it was CREATE OR REPLACE VIEW.
        var sql = ViewSqlGenerator.GenerateCreateViewSql(typeof(CustomerOrderView), connector: new PortableConnector());

        sql.Should().StartWith("CREATE VIEW ");
        sql.Should().Contain(" AS ");
        sql.Should().Contain("SELECT ");
    }

    [Fact]
    public void GenerateCreateViewSql_CustomerOrderView_ContainsJoin()
    {
        var sql = ViewSqlGenerator.GenerateCreateViewSql(typeof(CustomerOrderView), connector: new PortableConnector());

        sql.Should().Contain("JOIN");
    }

    [Fact]
    public void GenerateCreateViewSql_WithExplicitName_UsesProvidedName()
    {
        var sql = ViewSqlGenerator.GenerateCreateViewSql(typeof(CustomerOrderView), "vw_customer_orders", connector: new PortableConnector());

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

    [Fact]
    public void GenerateCreateViewSql_SelectBody_DelegatesToSharedBuilder()
    {
        // CR-M151: ViewSqlGenerator no longer duplicates the ~140-line view-SELECT builder; it calls
        // the shared ViewSelectSqlBuilder. This asserts the generated statement equals exactly
        // "CREATE VIEW <name> AS <shared-builder-body>", so any future drift is caught.
        var view = Birko.Data.SQL.DataBase.LoadView(typeof(CustomerOrderView));
        view.Should().NotBeNull();

        static string Quote(string id) => "\"" + id.Replace("\"", "\"\"") + "\"";
        var connector = new PortableConnector();
        var expectedBody = ViewSelectSqlBuilder.BuildViewSelectSql(view!, Quote, dialect: connector.RenderDialect);
        var name = ViewSqlGenerator.GetViewName(typeof(CustomerOrderView));

        var sql = ViewSqlGenerator.GenerateCreateViewSql(typeof(CustomerOrderView), connector: connector);

        sql.Should().Be("CREATE VIEW " + Quote(name) + " AS " + expectedBody);
    }

    /// <summary>
    /// TASK-911 (rule 56, inverting what the tests above asserted connector-less): a view grouped by a string, generated
    /// without a connector, is refused by name. The generator knows no provider, so its GROUP BY would follow the column's
    /// collation and could merge keys C# keeps apart; with a connector it groups exactly.
    /// </summary>
    [Fact]
    public void GenerateCreateViewSql_WithoutConnector_RefusesAStringGroupKey()
    {
        var act = () => ViewSqlGenerator.GenerateCreateViewSql(typeof(CustomerOrderView));

        act.Should().Throw<NotSupportedException>().WithMessage("*CustomerOrderView.CustomerName*TASK-911*");
    }

    /// <summary>
    /// A connector of no provider (the portable dialect): enough for the generator's connector-only checks, rendering what
    /// the connector-less generator rendered before TASK-911.
    /// </summary>
    private sealed class PortableConnector : AbstractConnector
    {
        public PortableConnector() : base(new Birko.Configuration.PasswordSettings()) { }

        public override System.Data.Common.DbConnection CreateConnection(Birko.Configuration.PasswordSettings settings)
            => throw new NotSupportedException();
        public override string ConvertType(System.Data.DbType type, Birko.Data.SQL.Fields.AbstractField field) => "TEXT";
        public override string FieldDefinition(Birko.Data.SQL.Fields.AbstractField field) => field.Name + " TEXT";
    }
}
