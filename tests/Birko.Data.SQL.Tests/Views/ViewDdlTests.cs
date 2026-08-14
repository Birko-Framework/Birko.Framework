using Birko.Data.SQL.Connectors;
using Birko.Data.SQL.Tables;
using Birko.Data.SQL.Tests.TestResources.Views;
using FluentAssertions;
using System;
using System.Data;
using System.Data.Common;
using System.Linq;
using Xunit;
using PasswordSettings = Birko.Configuration.PasswordSettings;

namespace Birko.Data.SQL.Tests.Views
{
    public class ViewDdlTests
    {
        private static readonly PasswordSettings TestSettings = new() { Location = "test.db", Name = "test" };

        [Fact]
        public void LoadView_CustomerOrderView_LoadsCorrectly()
        {
            var view = SQL.DataBase.LoadView(typeof(CustomerOrderView));

            view.Should().NotBeNull();
            view.Tables.Should().NotBeNull();
            view.Tables.Count().Should().BeGreaterThanOrEqualTo(2);
            view.Join.Should().NotBeNull();
            view.Join!.Count().Should().BeGreaterThanOrEqualTo(1);
        }

        [Fact]
        public void LoadView_CustomerOrderView_HasAggregateFields()
        {
            var view = SQL.DataBase.LoadView(typeof(CustomerOrderView));

            view.HasAggregateFields().Should().BeTrue();
        }

        [Fact]
        public void LoadView_InnerJoinView_HasInnerJoinType()
        {
            var view = SQL.DataBase.LoadView(typeof(CustomerOrderView));

            var join = view.Join!.First();
            join.JoinType.Should().Be(Conditions.JoinType.Inner);
        }

        [Fact]
        public void LoadView_LeftJoinView_HasLeftOuterJoinType()
        {
            var view = SQL.DataBase.LoadView(typeof(CustomerOrderLeftJoinView));

            var join = view.Join!.First();
            join.JoinType.Should().Be(Conditions.JoinType.LeftOuter);
        }

        [Fact]
        public void BuildViewSelectSql_CustomerOrderView_GeneratesSelectWithJoin()
        {
            var view = SQL.DataBase.LoadView(typeof(CustomerOrderView));
            var connector = new TestViewConnector(TestSettings);

            var sql = connector.TestBuildViewSelectSql(view);

            sql.Should().Contain("SELECT ");
            sql.Should().Contain("FROM ");
            sql.Should().Contain("INNER JOIN");
            sql.Should().Contain("ON (");
            sql.Should().Contain("GROUP BY ");
        }

        [Fact]
        public void BuildViewSelectSql_LeftJoinView_GeneratesLeftOuterJoin()
        {
            var view = SQL.DataBase.LoadView(typeof(CustomerOrderLeftJoinView));
            var connector = new TestViewConnector(TestSettings);

            var sql = connector.TestBuildViewSelectSql(view);

            sql.Should().Contain("LEFT OUTER JOIN");
        }

        [Fact]
        public void BuildCreateViewSql_DefaultConnector_UsesCreateOrReplace()
        {
            var connector = new TestViewConnector(TestSettings);

            var sql = connector.TestBuildCreateViewSql("my_view", "SELECT 1");

            sql.Should().Be("CREATE OR REPLACE VIEW \"my_view\" AS SELECT 1");
        }

        [Fact]
        public void BuildCreateViewSql_MSSqlStyle_UsesCreateOrAlter()
        {
            var connector = new TestMSSqlViewConnector(TestSettings);

            var sql = connector.TestBuildCreateViewSql("my_view", "SELECT 1");

            sql.Should().Be("CREATE OR ALTER VIEW [my_view] AS SELECT 1");
        }

        [Fact]
        public void BuildCreateViewSql_SQLiteStyle_UsesIfNotExists()
        {
            var connector = new TestSqLiteViewConnector(TestSettings);

            var sql = connector.TestBuildCreateViewSql("my_view", "SELECT 1");

            sql.Should().Be("CREATE VIEW IF NOT EXISTS my_view AS SELECT 1");
        }

        [Fact]
        public void BuildViewSelectSql_CustomerOrderView_ContainsCountAndSum()
        {
            var view = SQL.DataBase.LoadView(typeof(CustomerOrderView));
            var connector = new TestViewConnector(TestSettings);

            var sql = connector.TestBuildViewSelectSql(view);

            sql.Should().Contain("COUNT(");
            sql.Should().Contain("SUM(");
        }

        // CR-L195: aggregate columns are aliased by the unique view-property name (not the aggregate
        // function name, which would collide across two same-function aggregates), and those aliases must
        // be exactly the columns GetPersistentViewSelectFields queries back.
        //
        // TASK-129 strengthened the assertions without changing the rule. Contain("AS \"OrderCount\"") alone
        // passes on `COUNT(Orders.Guid) as COUNT AS "OrderCount"` — two aliases on one column and a syntax
        // error on every provider — which is how the defect shipped green. Aliases are now COUNTED, so a
        // second one fails, and the inner `as COUNT` is asserted absent by name.
        //
        // The alias stays QUOTED: this identifier is being created, and its reader
        // (CreatePersistentViewSelectCommand) quotes it too, so on PostgreSQL a bare alias would create
        // `ordercount` while the read asks for `"OrderCount"`.
        [Fact]
        public void BuildViewSelectSql_AggregateAliases_UsePropertyNames_AndMatchPersistentSelect()
        {
            var view = SQL.DataBase.LoadView(typeof(CustomerOrderView));
            var connector = new TestViewConnector(TestSettings);

            var sql = connector.TestBuildViewSelectSql(view);

            sql.Should().Contain("AS \"OrderCount\"");
            sql.Should().Contain("AS \"TotalSpent\"");
            sql.Should().NotContain(" as COUNT");
            sql.Should().NotContain(" as SUM");

            var projection = sql.Substring("SELECT ".Length, sql.IndexOf(" FROM ", StringComparison.Ordinal) - "SELECT ".Length);
            foreach (var item in projection.Split(',').Select(x => x.Trim()))
            {
                item.Split(' ').Count(t => t.Equals("as", StringComparison.OrdinalIgnoreCase))
                    .Should().BeLessThanOrEqualTo(1, $"'{item}' must carry at most one alias");
            }

            var cols = view!.GetPersistentViewSelectFields().Values;
            cols.Should().Contain("OrderCount");
            cols.Should().Contain("TotalSpent");
            // The plain (non-aggregate) columns still use the source column names.
            cols.Should().Contain("Name");
        }

        [Fact]
        public void BuildViewSelectSql_NoJoins_ThrowsInvalidOperationException()
        {
            var view = new Tables.View();
            var connector = new TestViewConnector(TestSettings);

            var act = () => connector.TestBuildViewSelectSql(view);

            act.Should().Throw<InvalidOperationException>().WithMessage("*join*");
        }

        #region Test Connectors

        private class TestViewConnector : AbstractConnectorBase
        {
            public TestViewConnector(PasswordSettings settings) : base(settings) { }
            public string TestBuildViewSelectSql(Tables.View view) => BuildViewSelectSql(view);
            public string TestBuildCreateViewSql(string name, string select) => BuildCreateViewSql(name, select);

            public override string QuoteIdentifier(string identifier) => "\"" + identifier + "\"";
            public override string FieldDefinition(Fields.AbstractField field) => field.Name;
            public override string ConvertType(DbType type, Fields.AbstractField field) => "TEXT";
            public override DbConnection CreateConnection(PasswordSettings settings) => throw new NotImplementedException();
        }

        private class TestMSSqlViewConnector : AbstractConnectorBase
        {
            public TestMSSqlViewConnector(PasswordSettings settings) : base(settings) { }
            public string TestBuildCreateViewSql(string name, string select) => BuildCreateViewSql(name, select);

            public override string QuoteIdentifier(string identifier) => "[" + identifier + "]";
            protected override string BuildCreateViewSql(string viewName, string selectSql)
                => "CREATE OR ALTER VIEW " + QuoteIdentifier(viewName) + " AS " + selectSql;

            public override string FieldDefinition(Fields.AbstractField field) => field.Name;
            public override string ConvertType(DbType type, Fields.AbstractField field) => "NVARCHAR(MAX)";
            public override DbConnection CreateConnection(PasswordSettings settings) => throw new NotImplementedException();
        }

        private class TestSqLiteViewConnector : AbstractConnectorBase
        {
            public TestSqLiteViewConnector(PasswordSettings settings) : base(settings) { }
            public string TestBuildCreateViewSql(string name, string select) => BuildCreateViewSql(name, select);

            public override string QuoteIdentifier(string identifier) => identifier;
            protected override string BuildCreateViewSql(string viewName, string selectSql)
                => "CREATE VIEW IF NOT EXISTS " + QuoteIdentifier(viewName) + " AS " + selectSql;

            public override string FieldDefinition(Fields.AbstractField field) => field.Name;
            public override string ConvertType(DbType type, Fields.AbstractField field) => "TEXT";
            public override DbConnection CreateConnection(PasswordSettings settings) => throw new NotImplementedException();
        }

        #endregion
    }
}
