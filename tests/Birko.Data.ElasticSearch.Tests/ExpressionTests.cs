using Birko.Data.ElasticSearch.Tests.TestResources.Models;
using System;
using System.Linq;
using System.Linq.Expressions;
using Xunit;

namespace Birko.Data.ElasticSearch.Tests
{
    public class ExpressionTests
    {
        [Fact]
        public void ParseTermExpression()
        {
            var guid = Guid.NewGuid();
            Expression<Func<DateModel, object>> expr = (x) => x.Guid == guid;
            var d = Birko.Data.ElasticSearch.ElasticSearch.ParseExpression(expr);
            Assert.Equal("guid",  (d as Nest.TermQuery).Field.Name);
            Assert.Equal(guid, (d as Nest.TermQuery).Value);
        }

        [Fact]
        public void ParseRangeExpression()
        {
            Expression<Func<DateModel, object>> expr = (x) => x.Amount > 0;
            var d = Birko.Data.ElasticSearch.ElasticSearch.ParseExpression(expr);
            Assert.Equal("amount", (d as Nest.NumericRangeQuery).Field.Name);
            Assert.Equal(0, (d as Nest.NumericRangeQuery).GreaterThan);
        }

        [Fact]
        public void ParseRangeExpression2()
        {
            Expression<Func<DateModel, object>> expr = (x) => x.Amount > 0 && x.Amount <=100;
            var d = Birko.Data.ElasticSearch.ElasticSearch.ParseExpression(expr);
        }

        [Fact]
        public void ParseAnyExpression()
        {
            Expression<Func<NestedDateModel, object>> expr = (x) => x.Nested.Any(y=>y.Amount > 0);
            var d = Birko.Data.ElasticSearch.ElasticSearch.ParseExpression(expr);
            Assert.NotNull(d);
            Assert.IsType<Nest.NestedQuery>(d);
        }
    }
}
