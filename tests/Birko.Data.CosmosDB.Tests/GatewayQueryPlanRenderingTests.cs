using System;
using System.Linq;
using System.Linq.Expressions;
using Birko.Data.CosmosDB.Aggregation;
using Birko.Data.CosmosDB.Stores;
using Birko.Data.Stores;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Xunit;
using Row = Birko.Data.CosmosDB.Tests.Task899FilterLeftoversLiveTests.Row;

namespace Birko.Data.CosmosDB.Tests;

/// <summary>
/// Offline: the query text a client that takes its query plan from the gateway is safe to send. The SDK plans a query
/// locally only on Windows x64; every other client (Linux, so CI and any container) asks the gateway, and the Cosmos DB
/// emulator's gateway answered two shapes wrongly (measured 2026-11-11 on builds EN20261008 and EN20261019, from Linux and
/// from Windows with <c>AZURE_COSMOS_BYPASS_QUERY_PARSING=true</c>): a <c>COUNT</c> inside a <c>JOIN</c> subquery was
/// reported as the query's own aggregate, so a read with <c>Items.Count(p)</c> failed, and an astral character written as
/// an escaped surrogate pair matched nothing, so a mixed MIN/MAX group was answered instead of refused. The live suites
/// (Task899FilterLeftoversLiveTests, CosmosAggregateStringOrderContractTests) fail on that path without the fix; these
/// pin the rendering so a Windows run catches a regression too.
/// </summary>
public class GatewayQueryPlanRenderingTests
{
    private static readonly CosmosClient Client = new("AccountEndpoint=http://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==");

    private static string Query(Expression<Func<Row, bool>> filter)
        => Client.GetContainer("db", "c").GetItemLinqQueryable<Row>().Where(CosmosFilterRewrite.Prepare(filter)!).ToQueryDefinition().QueryText;

    public static TheoryData<string, Expression<Func<Row, bool>>> NestedCounts => new()
    {
        { "Count(p) > 0", x => x.Items.Count(i => i.Score > 5) > 0 },
        { "Count(!p) == 1", x => x.Items.Count(i => !(i.Score > 5)) == 1 },
        { "LongCount(p) > 0", x => x.Items.LongCount(i => i.Score > 5) > 0L },
        { "Where(p).Count() > 1", x => x.Items.Where(i => i.Score > 5).Count() > 1 },
        { "Where(p).LongCount() > 1", x => x.Items.Where(i => i.Score > 5).LongCount() > 1L },
    };

    [Theory]
    [MemberData(nameof(NestedCounts))]
    public void A_nested_count_is_an_array_length_not_a_COUNT_subquery(string shape, Expression<Func<Row, bool>> filter)
    {
        var text = Query(filter);
        text.Should().NotContain("COUNT(", $"'{shape}' must not put an aggregate in a subquery");
        text.Should().Contain("ARRAY_LENGTH(", $"'{shape}' counts the kept elements as an array");
    }

    [Fact]
    public void A_nested_count_keeps_the_predicate_null_semantics()
    {
        Query(x => x.Items.Count(i => !(i.Score > 5)) == 1).Should().Contain(
            "WHERE (NOT ((IS_DEFINED(i0[\"Score\"]) AND (NOT IS_NULL(i0[\"Score\"]))) AND (i0[\"Score\"] > 5))))) AS v0 WHERE (ARRAY_LENGTH(v0) = 1)");
    }

    [Fact]
    public void Preparing_a_nested_count_twice_renders_the_same()
    {
        Expression<Func<Row, bool>> filter = x => x.Items.Count(i => i.Score > 5) > 0;
        var once = CosmosFilterRewrite.Prepare(filter)!;
        var queryable = Client.GetContainer("db", "c").GetItemLinqQueryable<Row>();
        queryable.Where(CosmosFilterRewrite.Prepare(once)!).ToQueryDefinition().QueryText
            .Should().Be(queryable.Where(once).ToQueryDefinition().QueryText);
    }

    [Fact]
    public void A_count_of_the_member_itself_stays_its_array_length()
    {
        Query(x => x.Items.Count() > 0).Should().EndWith("WHERE (ARRAY_LENGTH(root[\"Items\"]) > 0)");
    }

    [Fact]
    public void The_string_order_checks_hold_the_characters_themselves_not_escaped_surrogates()
    {
        var (highBmp, astral) = CosmosAggregationHelper.StringOrderChecks("root[\"Name\"]");

        astral.Should().Contain(AggregateMath.AstralCharacters, "an escaped surrogate pair is not read as the character on the gateway path");
        highBmp.Should().Contain(AggregateMath.HighBmpCharacters);
        (highBmp + astral).Should().NotContain("\\u", "nothing in these patterns needs escaping");
    }
}
