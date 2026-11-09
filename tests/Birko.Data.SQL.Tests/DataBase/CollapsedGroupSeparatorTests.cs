using System;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.SQL.Conditions;
using Birko.Data.SQL.Tests.TestResources.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests.DataBase
{
    /// <summary>
    /// TASK-866 — when `&amp;&amp;` / `||` collapses onto a surviving operand that is itself a group, the group keeps its
    /// own separator. ReturnSingleSubCondition wrote the enclosing operator's flag over it, so
    /// `!((A || B) &amp;&amp; t)` rendered NOT (A AND B) (measured on SQLite, PostgreSQL, MySQL, SQL Server).
    /// </summary>
    public class CollapsedGroupSeparatorTests
    {
        /// <summary>The group whose two children are the survivor's two comparisons.</summary>
        private static Condition PairGroup(Expression<Func<DateModel, bool>> expr)
        {
            static Condition? Find(Condition c)
            {
                var subs = c.SubConditions?.ToList();
                if (subs == null || subs.Count == 0) return null;
                if (subs.Count == 2 && subs.All(s => s.SubConditions?.Count() <= 1)) return c;
                return subs.Select(Find).FirstOrDefault(f => f != null);
            }
            return Find(Birko.Data.SQL.DataBase.ParseConditionExpression(expr).Single())!;
        }

        public static TheoryData<string, Expression<Func<DateModel, bool>>, bool> Shapes()
        {
            var t = true; var f = false;
            return new()
            {
                { "!((A || B) && t)", x => !((x.Count == 10 || x.Text == "a") && t), true },
                { "!(t && (A || B))", x => !(t && (x.Count == 10 || x.Text == "a")), true },
                { "C || ((A || B) && t)", x => x.Count == 30 || ((x.Count == 10 || x.Text == "a") && t), true },
                { "C && ((A && B) || f)", x => x.Count >= 10 && ((x.Count == 10 && x.Text == "a") || f), false },
                { "C && (f || (A && B))", x => x.Count >= 10 && (f || (x.Count == 10 && x.Text == "a")), false },
                { "!((A && B) || f)", x => !((x.Count == 10 && x.Text == "a") || f), false },
                { "!(!(A || B) && t)", x => !(!(x.Count == 10 || x.Text == "a") && t), true },
            };
        }

        [Theory]
        [MemberData(nameof(Shapes))]
        public void A_collapsed_group_keeps_its_own_separator(string shape, Expression<Func<DateModel, bool>> expr, bool childrenOred)
        {
            PairGroup(expr).IsOr.Should().Be(childrenOred, shape);
        }
    }
}
