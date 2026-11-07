using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading.Tasks;
using Birko.Data.CosmosDB.Views;
using Birko.Data.Models;
using Birko.Data.Stores;
using Birko.Data.Views;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;

namespace Birko.Data.CosmosDB.Views.Tests;

/// <summary>
/// Regressions for CR-H044 / CR-H045: the aggregate SQL path emitted WHERE and ORDER BY against
/// VIEW property names, but the query runs against the raw documents (FROM c) whose fields use
/// SOURCE names. A renamed field therefore matched nothing / mis-ordered. WHERE now maps view→source
/// and ORDER BY maps group keys to source fields. A sort by an aggregate is refused (TASK-848).
/// </summary>
public class CosmosViewAggregateSqlTests
{
    private class ReviewSource : AbstractModel
    {
        public string? Category { get; set; }
        public decimal Amount { get; set; }
    }

    // View renames Category -> CategoryName and projects Sum(Amount) -> Total.
    private class ReviewView
    {
        public string? CategoryName { get; set; }
        public decimal Total { get; set; }
    }

    private static ViewDefinition BuildDefinition() => new(
        name: "review-by-category",
        queryMode: ViewQueryMode.OnTheFly,
        primarySource: typeof(ReviewSource),
        viewType: typeof(ReviewView),
        fields: new List<FieldSelector> { new(typeof(ReviewSource), "Category", "CategoryName") },
        joins: new List<JoinClause>(),
        aggregates: new List<AggregateClause> { new(AggregateFunction.Sum, typeof(ReviewSource), "Amount", "Total") },
        groupBy: new List<GroupByClause> { new(typeof(ReviewSource), "Category") },
        hints: new Dictionary<string, object>());

    private static string BuildSql(Expression<Func<ReviewView, bool>>? filter, OrderBy<ReviewView>? orderBy)
    {
        // Offline Cosmos container (lazy — no network until a query is executed).
        var client = new CosmosClient("AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;");
        var container = client.GetContainer("db", "reviews");
        var store = new CosmosViewStore<ReviewView>(container, BuildDefinition());

        var method = typeof(CosmosViewStore<ReviewView>)
            .GetMethod("BuildAggregateSql", BindingFlags.NonPublic | BindingFlags.Instance)!;
        return ((QueryDefinition)method.Invoke(store, new object?[] { filter, orderBy, null, null })!).QueryText;
    }

    [Fact]
    public void Where_UsesSourceFieldName_NotViewName()
    {
        var sql = BuildSql(v => v.CategoryName == "books", orderBy: null);

        sql.Should().Contain("c[\"Category\"] ", "CR-H045: the WHERE must target the source field");
        sql.Should().NotContain("c[\"CategoryName\"]", "the renamed view name does not exist on the source document");
    }

    [Fact]
    public void OrderBy_GroupKey_UsesSourceField()
    {
        var sql = BuildSql(filter: null, orderBy: OrderBy<ReviewView>.ByDescending(v => v.CategoryName!));

        var orderByClause = sql.Substring(sql.IndexOf("ORDER BY", StringComparison.Ordinal));
        orderByClause.Should().Be("ORDER BY c[\"Category\"] DESC", "CR-H044: the group key orders by its source field");
    }

    /// <summary>
    /// TASK-848: Cosmos DB refuses every form of a sort by an aggregate (SC2001 / 2206, measured), so the store refuses
    /// it by name before any request. The container is offline: a request would fail with a network error, not this.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrderBy_Aggregate_IsRefusedByName_BeforeAnyRequest(bool afterGroupKey)
    {
        var store = OfflineStore();
        var orderBy = afterGroupKey
            ? OrderBy<ReviewView>.ByName("CategoryName").ThenBy(v => v.Total)
            : OrderBy<ReviewView>.By(v => v.Total);

        var read = () => store.QueryAsync(null, orderBy, limit: 2, offset: 1);

        (await read.Should().ThrowExactlyAsync<NotSupportedException>())
            .WithMessage("*aggregate*ReviewView.Total*TASK-848*");
    }

    private static CosmosViewStore<ReviewView> OfflineStore()
    {
        var client = new CosmosClient("AccountEndpoint=https://localhost:1/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;",
            new CosmosClientOptions { RequestTimeout = TimeSpan.FromSeconds(2) });
        return new CosmosViewStore<ReviewView>(client.GetContainer("db", "reviews"), BuildDefinition());
    }
}
