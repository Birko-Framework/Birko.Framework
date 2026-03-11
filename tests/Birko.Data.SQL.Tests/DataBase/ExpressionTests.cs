using Birko.Data.SQL.Tests.TestResources.Models;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Xunit;

namespace Birko.Data.SQL.Tests.DataBase
{
    public class ExpressionTests
    {
        [Fact]
        public void ParseValueExpression()
        {
            Expression<Func<DateModel, object>> expr = (x) => 3;
            var parameters = new Dictionary<string, object>();
            Assert.Equal("@Constat0",  Birko.Data.SQL.DataBase.ParseExpression(expr, parameters, true));
        }

        [Fact]
        public void ParseFieldExpression()
        {
            Expression<Func<DateModel, object>> expr = (x) => x.Amount;
            var parameters = new Dictionary<string, object>();
            Assert.Equal("DateModels.Amount", Birko.Data.SQL.DataBase.ParseExpression(expr, parameters, true));
        }

        [Fact]
        public void ParseFieldExpression2()
        {
            Expression<Func<DateModel, object>> expr = (x) => x.Guid;
            var parameters = new Dictionary<string, object>();
            Assert.Equal("DateModels.Guid", Birko.Data.SQL.DataBase.ParseExpression(expr, parameters, true));
        }

        [Fact]
        public void ParseFieldExpressionWithoutTableName()
        {
            Expression<Func<DateModel, object>> expr = (x) => x.Amount;
            var parameters = new Dictionary<string, object>();
            Assert.Equal("Amount", Birko.Data.SQL.DataBase.ParseExpression(expr, parameters));
        }

        [Fact]
        public void ParseDateFieldExpressionWithoutTableName()
        {
            Expression<Func<DateModel, object>> expr = (x) => DateTime.UtcNow;
            var parameters = new Dictionary<string, object>();
            Assert.Equal("@Constat0", Birko.Data.SQL.DataBase.ParseExpression(expr, parameters));
        }

        [Fact]
        public void ParseFieldAddExpression()
        {
            Expression<Func<DateModel, object>> expr = (x) => x.Amount + x.Amount;
            var parameters = new Dictionary<string, object>();
            Assert.Equal("(DateModels.Amount + DateModels.Amount)", Birko.Data.SQL.DataBase.ParseExpression(expr, parameters, true));
        }

        [Fact]
        public void ParseFieldAddConstantExpression()
        {
            Expression<Func<DateModel, object>> expr = (x) => x.Count + 3;
            var parameters = new Dictionary<string, object>();
            Assert.Equal("(DateModels.Count + @Constat0)", Birko.Data.SQL.DataBase.ParseExpression(expr, parameters, true));
            Assert.Equal(3, parameters["@Constat0"]);
        }

        [Fact]
        public void ParseFieldSubstractConstantExpression()
        {
            Expression<Func<DateModel, object>> expr = (x) => x.Count - 3;
            var parameters = new Dictionary<string, object>();
            Assert.Equal("(DateModels.Count - @Constat0)", Birko.Data.SQL.DataBase.ParseExpression(expr, parameters, true));
            Assert.Equal(3, parameters["@Constat0"]);
        }

        [Fact]
        public void ParseFielSubstractFunctionExpression()
        {
            Expression<Func<DateModel, object>> expr = (x) => x.Count - int.Parse("3");
            var parameters = new Dictionary<string, object>();
            Assert.Equal("(DateModels.Count - @Constat0)", Birko.Data.SQL.DataBase.ParseExpression(expr, parameters, true));
            Assert.Equal(3, parameters["@Constat0"]);
        }

        [Fact]
        public void ParseFieldReplaceFunctionExpression()
        {
            Expression<Func<DateModel, object>> expr = (x) => x.Text.Replace("original", "replace");
            var parameters = new Dictionary<string, object>();
            Assert.Equal("REPLACE(DateModels.Text, @Constat0, @Constat1)", Birko.Data.SQL.DataBase.ParseExpression(expr, parameters, true));
            Assert.Equal("original", parameters["@Constat0"]);
            Assert.Equal("replace", parameters["@Constat1"]);
        }
    }
}
