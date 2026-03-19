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

        [Fact]
        public void BuildViewSelectSql_NoJoins_ThrowsInvalidOperationException()
        {
            var view = new View();
            var connector = new TestViewConnector(TestSettings);

            var act = () => connector.TestBuildViewSelectSql(view);

            act.Should().Throw<InvalidOperationException>().WithMessage("*join*");
        }

        #region Test Connectors

        private class TestViewConnector : AbstractConnectorBase
        {
            public TestViewConnector(PasswordSettings settings) : base(settings) { }
            public string TestBuildViewSelectSql(View view) => BuildViewSelectSql(view);
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
