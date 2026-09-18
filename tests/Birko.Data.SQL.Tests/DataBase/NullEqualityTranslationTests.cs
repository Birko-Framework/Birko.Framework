using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Tests.TestResources.Models;
using Xunit;

namespace Birko.Data.SQL.Tests.DataBase
{
    /// <summary>
    /// Regression: a predicate `x.Col == v` where `v` is a captured nullable variable that HOLDS null
    /// must translate to `Col IS NULL`, exactly as a literal `x.Col == null` does. Before the fix the
    /// closure-member branch set Values=[null] with Type=Equal, so EqualConditionStrategy emitted
    /// `Col = NULL` — always UNKNOWN in SQL → silent zero rows. Surfaced by a Symbio audit (TASK-166):
    /// `x.VariantId == variantId` for variant-less products returned no rows.
    /// </summary>
    public class NullEqualityTranslationTests
    {
        private static Condition? FindLeaf(IEnumerable<Condition> conditions, string name)
        {
            foreach (var c in conditions)
            {
                if (c.Name != null && c.Name.EndsWith(name, StringComparison.OrdinalIgnoreCase))
                    return c;
                if (c.SubConditions != null)
                {
                    var found = FindLeaf(c.SubConditions, name);
                    if (found != null) return found;
                }
            }
            return null;
        }

        [Fact]
        public void NullValuedVariableEquality_BecomesIsNull()
        {
            decimal? amount = null;
            Expression<Func<DateModel, bool>> expr = x => x.Amount == amount;

            var leaf = FindLeaf(Birko.Data.SQL.DataBase.ParseConditionExpression(expr).ToList(), "Amount");

            Assert.NotNull(leaf);
            Assert.Equal(ConditionType.IsNull, leaf!.Type);
            Assert.False(leaf.IsNot);
        }

        [Fact]
        public void NullValuedVariableInequality_BecomesIsNotNull()
        {
            decimal? amount = null;
            Expression<Func<DateModel, bool>> expr = x => x.Amount != amount;

            var leaf = FindLeaf(Birko.Data.SQL.DataBase.ParseConditionExpression(expr).ToList(), "Amount");

            Assert.NotNull(leaf);
            Assert.Equal(ConditionType.IsNull, leaf!.Type);
            Assert.True(leaf.IsNot); // emits IS NOT NULL
        }

        [Fact]
        public void NonNullValuedVariableEquality_StaysEqual()
        {
            decimal? amount = 5m;
            Expression<Func<DateModel, bool>> expr = x => x.Amount == amount;

            var leaf = FindLeaf(Birko.Data.SQL.DataBase.ParseConditionExpression(expr).ToList(), "Amount");

            Assert.NotNull(leaf);
            Assert.Equal(ConditionType.Equal, leaf!.Type);
        }
    }
}
