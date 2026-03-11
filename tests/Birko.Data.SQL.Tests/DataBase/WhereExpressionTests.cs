using Birko.Data.SQL.Tests.TestResources.Models;
using System;
using System.Linq.Expressions;
using Xunit;

namespace Birko.Data.SQL.Tests.DataBase
{
    public class WhereExpressionTests
    {
        [Fact]
        public void ParseValueExpression()
        {
            Expression<Func<DateModel, object>> expr = (x) => x.Count == 3;
            var data = Birko.Data.SQL.DataBase.ParseConditionExpression(expr);
        }

        [Fact]
        public void ParseValueExpression2()
        {
            int var = 3;
            Expression<Func<DateModel, object>> expr = (x) => x.Count == var;
            var data = Birko.Data.SQL.DataBase.ParseConditionExpression(expr);
        }

        [Fact]
        public void ParseValueExpression3()
        {
            ParseValueFunc(Guid.Empty);
        }

        private void ParseValueFunc(Guid id)
        {
            Expression<Func<DateModel, object>> expr = (x) => x.Guid != id;
            var data = Birko.Data.SQL.DataBase.ParseConditionExpression(expr);
        }

        [Fact]
        public void ParseStartsWithExpression()
        {
            Expression<Func<DateModel, object>> expr = (x) => x.Text.StartsWith("a");
            var data = Birko.Data.SQL.DataBase.ParseConditionExpression(expr);
        }

        [Fact]
        public void ParseCombinedExpression()
        {
            Expression<Func<DateModel, object>> expr = (x) => x.Guid != Guid.Empty && x.IsTest == true;
            var data = Birko.Data.SQL.DataBase.ParseConditionExpression(expr);
        }

        [Fact]
        public void ParseParameterExpression()
        {
            DateModel model = new DateModel()
            {
                Guid = Guid.NewGuid()
            };

            Expression<Func<DateModel, object>> expr = (x) => x.Guid != model.Guid;
            var data = Birko.Data.SQL.DataBase.ParseConditionExpression(expr);
        }
    }
}
