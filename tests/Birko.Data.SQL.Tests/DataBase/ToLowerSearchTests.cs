using Birko.Data.SQL.Tests.TestResources.Models;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Xunit;

namespace Birko.Data.SQL.Tests.DataBase
{
    public class ToLowerSearchTests
    {
        [Fact]
        public void ToLower_Contains_With_Flag()
        {
            var hasSearch = true;
            var searchVal = "xia";
            Expression<Func<DateModel, bool>> expr = (x) =>
                !hasSearch || x.Text.ToLower().Contains(searchVal);

            var data = Birko.Data.SQL.DataBase.ParseConditionExpression(expr);
            Assert.NotNull(data);
            Assert.NotEmpty(data);

            // Flatten conditions
            var all = FlattenConditions(data);
            Assert.NotEmpty(all);

            var cond = all.FirstOrDefault(c => c.Type == Birko.Data.SQL.Conditions.ConditionType.Like);
            Assert.NotNull(cond);
            Assert.StartsWith("LOWER(", cond!.Name);
            Assert.Contains("Text", cond.Name);

            // Check value
            Assert.NotNull(cond.Values);
            var val = cond.Values.Cast<object>().First();
            Assert.Equal("xia", val);
        }

        private static List<Birko.Data.SQL.Conditions.Condition> FlattenConditions(
            IEnumerable<Birko.Data.SQL.Conditions.Condition> conditions)
        {
            var result = new List<Birko.Data.SQL.Conditions.Condition>();
            foreach (var c in conditions)
            {
                if (c.SubConditions?.Any() == true)
                    result.AddRange(FlattenConditions(c.SubConditions));
                else if (!string.IsNullOrEmpty(c.Name))
                    result.Add(c);
            }
            return result;
        }
    }
}
