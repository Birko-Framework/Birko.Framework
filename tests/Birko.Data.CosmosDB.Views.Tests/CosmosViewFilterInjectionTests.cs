using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Data.Views;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;

namespace Birko.Data.CosmosDB.Views.Tests;

/// <summary>
/// TASK-447 — a filter's string value must not be able to break out of the statement.
/// </summary>
/// <remarks>
/// <para>
/// <c>CosmosFilterTranslator</c> rendered a string operand as a single-quoted literal escaping only
/// the quote (<c>s.Replace("'", "\\'")</c>). Cosmos NoSQL uses <b>backslash</b> as the escape
/// character inside a literal, so a backslash in the <i>input</i> consumed the escape the code had
/// just added. Measured before the fix:
/// </para>
/// <list type="table">
/// <item><description><c>O'Brien</c> → <c>'O\'Brien'</c> — correct, and the only case the old test covered</description></item>
/// <item><description><c>foo\</c> → <c>'foo\'</c> — unterminated literal</description></item>
/// <item><description><c>a\' OR 1=1 --</c> → <c>'a\\' OR 1=1 --'</c> — the literal ends early and the rest is parsed as SQL</description></item>
/// </list>
/// <para>
/// On an aggregate view the predicate is the only thing scoping the query, so the third row widens it
/// to every document <i>with the caller controlling the predicate</i>.
/// </para>
/// <para>
/// ⚠ <b>What these tests prove and what they do not.</b> They assert that no caller-supplied text
/// reaches the statement at all — the containment is structural, so there is no escaping to get right
/// and no payload list to keep current. They do <b>not</b> execute against Cosmos; that the old
/// rendering <i>parses</i> as an injection is read off Cosmos NoSQL's documented literal grammar
/// (backslash escapes, <c>--</c> comments), not measured, and is stated that way in TASK-447.
/// </para>
/// </remarks>
public class CosmosViewFilterInjectionTests
{
    private class OrderSource : AbstractModel
    {
        public Guid TenantGuid { get; set; }
        public string? Category { get; set; }
        public decimal Amount { get; set; }
    }

    private class OrderView
    {
        public Guid TenantGuid { get; set; }
        public string? CategoryName { get; set; }
        public decimal Total { get; set; }
    }

    private static ViewDefinition BuildDefinition() => new(
        name: "orders-by-category",
        queryMode: ViewQueryMode.OnTheFly,
        primarySource: typeof(OrderSource),
        viewType: typeof(OrderView),
        fields: new List<FieldSelector> { new(typeof(OrderSource), "Category", "CategoryName") },
        joins: new List<JoinClause>(),
        aggregates: new List<AggregateClause> { new(AggregateFunction.Sum, typeof(OrderSource), "Amount", "Total") },
        groupBy: new List<GroupByClause> { new(typeof(OrderSource), "Category") },
        hints: new Dictionary<string, object>());

    private static QueryDefinition Aggregate(Expression<Func<OrderView, bool>>? filter)
    {
        var client = new CosmosClient(
            "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;");
        var store = new CosmosViewStore<OrderView>(client.GetContainer("db", "orders"), BuildDefinition());
        var m = typeof(CosmosViewStore<OrderView>)
            .GetMethod("BuildAggregateSql", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (QueryDefinition)m.Invoke(store, new object?[] { filter, null, null, null })!;
    }

    private static QueryDefinition Count(Expression<Func<OrderView, bool>>? filter)
    {
        var client = new CosmosClient(
            "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;");
        var store = new CosmosViewStore<OrderView>(client.GetContainer("db", "orders"), BuildDefinition());
        var m = typeof(CosmosViewStore<OrderView>)
            .GetMethod("BuildCountAggregateSql", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return (QueryDefinition)m.Invoke(store, new object?[] { filter })!;
    }

    /// <summary>The four inputs measured against the old renderer, plus two control characters.</summary>
    public static TheoryData<string> Payloads() => new()
    {
        "O'Brien",
        @"foo\",
        @"a\' OR 1=1 --",
        "' OR 1=1 --",
        "x\nY",
        "tab\there",
        @"\\\\' OR c.TenantGuid != null OR '",
    };

    [Theory]
    [MemberData(nameof(Payloads))]
    public void No_payload_reaches_the_statement_and_the_value_arrives_intact(string payload)
    {
        var q = Aggregate(v => v.CategoryName == payload);

        // The containment: the statement carries a placeholder, so there is nothing to escape.
        q.QueryText.Should().Contain("c.Category = @p0");
        q.QueryText.Should().NotContain(payload,
            "no caller-supplied text may appear in the statement at all — that is the whole point of "
            + "binding rather than escaping");

        // And the value is not mangled on the way: escaping schemes routinely corrupt what they contain.
        q.GetQueryParameters().Should().ContainSingle();
        q.GetQueryParameters().Single().Value.Should().Be(payload);
    }

    [Theory]
    [MemberData(nameof(Payloads))]
    public void The_count_path_contains_the_same_payloads(string payload)
    {
        // Guard the whole verb family or none of it: BuildCountAggregateSql builds its own statement.
        var q = Count(v => v.CategoryName == payload);

        q.QueryText.Should().Contain("c.Category = @p0");
        q.QueryText.Should().NotContain(payload);
        q.GetQueryParameters().Single().Value.Should().Be(payload);
    }

    [Fact]
    public void The_OR_1_eq_1_payload_cannot_widen_the_predicate()
    {
        // The finding's own example, asserted as the thing that matters rather than as a string shape:
        // whatever the caller supplies, the emitted predicate still constrains exactly one column.
        var q = Aggregate(v => v.CategoryName == @"a\' OR 1=1 --");

        q.QueryText.Should().NotContain("OR");
        q.QueryText.Should().NotContain("--");
        q.QueryText.Should().Contain("WHERE c.Category = @p0");
    }

    [Fact]
    public void Several_values_bind_to_distinct_placeholders_in_order()
    {
        var q = Aggregate(v => v.CategoryName == "books" && v.Total > 10m);

        q.QueryText.Should().Contain("@p0").And.Contain("@p1");
        var ps = q.GetQueryParameters().ToList();
        ps.Should().HaveCount(2);
        ps[0].Name.Should().Be("@p0");
        ps[0].Value.Should().Be("books");
        ps[1].Name.Should().Be("@p1");
        ps[1].Value.Should().Be(10m);
    }

    [Fact]
    public void A_Contains_call_binds_its_argument_too()
    {
        // CONTAINS(field, value) is the other place a caller value reached the statement.
        var q = Aggregate(v => v.CategoryName!.Contains(@"x\' OR 1=1 --"));

        q.QueryText.Should().Contain("CONTAINS(c.Category, @p0)");
        q.QueryText.Should().NotContain("OR 1=1");
        q.GetQueryParameters().Single().Value.Should().Be(@"x\' OR 1=1 --");
    }

    [Fact]
    public void A_filter_that_binds_nothing_carries_no_parameters()
    {
        // ⚠ CONTRACT PIN. A null filter and an explicit `x => true` both mean "no restriction"
        // (TASK-322), and neither should leave a stray binding behind.
        Aggregate(null).GetQueryParameters().Should().BeEmpty();
        Aggregate(v => true).GetQueryParameters().Should().BeEmpty();
    }

    [Fact]
    public void The_placeholder_name_is_not_derived_from_caller_text()
    {
        // The parameter NAME is generated from a counter, never from the column or the value — the
        // mistake that would reintroduce the sink one layer along. CLAUDE.md § SH-H023 records the
        // SQL layer's equivalent (GenerateParameterName sanitises, which is what made the injection
        // there look safe on a skim).
        var q = Aggregate(v => v.CategoryName == "'; DROP");

        q.GetQueryParameters().Single().Name.Should().Be("@p0");
    }
}
