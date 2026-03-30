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

        /// <summary>
        /// Bare boolean member access: s.IsTest (without == true)
        /// Should produce a condition equivalent to IsTest = 1
        /// </summary>
        [Fact]
        public void ParseBareBooleanExpression()
        {
            Expression<Func<DateModel, bool>> expr = (x) => x.IsTest;
            var data = Birko.Data.SQL.DataBase.ParseConditionExpression(expr);
            Assert.NotNull(data);
            Assert.Single(data);
            Assert.NotNull(data.First().Name);
            Assert.Contains("IsTest", data.First().Name!, StringComparison.OrdinalIgnoreCase);
            Assert.NotNull(data.First().Values);
            Assert.Contains(true, data.First().Values!.Cast<object>());
        }

        /// <summary>
        /// Negated bare boolean: !s.IsTest → NOT (IsTest = 1)
        /// </summary>
        [Fact]
        public void ParseNegatedBareBooleanExpression()
        {
            Expression<Func<DateModel, bool>> expr = (x) => !x.IsTest;
            var data = Birko.Data.SQL.DataBase.ParseConditionExpression(expr);
            Assert.NotNull(data);
            Assert.Single(data);
            Assert.True(data.First().IsNot);
        }

        /// <summary>
        /// Multi-condition with bare boolean: s.Count == 1 && s.IsTest && s.Text == "a"
        /// All three conditions must be parsed — IsTest must not short-circuit the AND.
        /// </summary>
        [Fact]
        public void ParseMultiConditionWithBareBooleanExpression()
        {
            Expression<Func<DateModel, bool>> expr = (x) => x.Count == 1 && x.IsTest && x.Text == "a";
            var data = Birko.Data.SQL.DataBase.ParseConditionExpression(expr);
            Assert.NotNull(data);
            // Should produce conditions (possibly nested) — the key is it doesn't produce empty/false
            Assert.NotEmpty(data);
            // Flatten subconditions to check all 3 operands are present
            var allConditions = FlattenConditions(data);
            Assert.True(allConditions.Count >= 3, $"Expected at least 3 conditions, got {allConditions.Count}");
        }

        /// <summary>
        /// Explicit boolean comparison should still work: s.IsTest == true
        /// </summary>
        [Fact]
        public void ParseExplicitBooleanComparisonStillWorks()
        {
            Expression<Func<DateModel, bool>> expr = (x) => x.Count == 1 && x.IsTest == true;
            var data = Birko.Data.SQL.DataBase.ParseConditionExpression(expr);
            Assert.NotNull(data);
            Assert.NotEmpty(data);
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
