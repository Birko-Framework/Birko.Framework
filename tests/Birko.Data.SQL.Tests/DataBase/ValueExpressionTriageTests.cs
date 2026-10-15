using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Birko.Data.SQL.Tests.TestResources.Models;
using FluentAssertions;
using Xunit;

namespace Birko.Data.SQL.Tests.DataBase
{
    /// <summary>
    /// TASK-153, the value-position findings of <c>filter-expression-translation</c>: what <c>ParseExpression</c>
    /// renders for an UPDATE SET right-hand side, and what a CASE fragment may inline.
    /// </summary>
    public class ValueExpressionTriageTests
    {
        public class Holder
        {
            public DateModel Sub { get; set; } = new();
        }

        private static string? Render<T>(Expression<Func<T, object?>> expr)
            => Birko.Data.SQL.DataBase.ParseExpression(expr, new Dictionary<string, object>(), true);

        /// <summary>
        /// SH-M136. A bare bool column is no predicate on SQL Server (BIT, error 4145): the test is rendered as the
        /// comparison the WHERE-side renderer uses, which all four providers accept (measured in the live suites).
        /// </summary>
        [Fact]
        public void A_bool_column_as_a_CASE_test_is_rendered_as_a_comparison()
            => Render<DateModel>(x => x.IsTest ? x.Count : 0).Should().StartWith("CASE WHEN (DateModels.IsTest = '1') THEN");

        /// <summary>SH-M149. A member of a member is no column; it rendered `( = @Const0)`, a syntax error at execution.</summary>
        [Fact]
        public void A_nested_member_is_refused_rather_than_rendered_as_nothing()
        {
            var act = () => Render<NestedHolder>(x => x.Sub.Count);
            act.Should().Throw<NotSupportedException>().WithMessage("*x.Sub.Count*");
        }

        /// <summary>A closure chain is still a value: the SH-M149 refusal applies only to the row's own members.</summary>
        [Fact]
        public void A_closure_chain_is_still_bound_as_a_value()
        {
            var holder = new Holder { Sub = new DateModel { Count = 7 } };
            var parameters = new Dictionary<string, object>();
            Expression<Func<DateModel, object?>> expr = x => holder.Sub.Count;

            Birko.Data.SQL.DataBase.ParseExpression(expr, parameters, true).Should().Be("@Const0");
            parameters["@Const0"].Should().Be(7);
        }

        public class NestedHolder : DateModel
        {
            public DateModel Sub { get; set; } = new();
        }
    }
}
