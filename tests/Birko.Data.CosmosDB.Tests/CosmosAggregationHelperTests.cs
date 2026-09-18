using Birko.Data.CosmosDB.Aggregation;
using Birko.Data.Stores;
using FluentAssertions;
using Xunit;

namespace Birko.Data.CosmosDB.Tests;

/// <summary>
/// CR-M084: BuildAggregateSqlParts emitted dotted identifiers (c.Field) built by raw string
/// interpolation of model property names — brittle against a camelCase serializer and unsafe against
/// field names with special characters. Identifiers are now bracket-quoted (c["Field"]).
/// </summary>
public class CosmosAggregationHelperTests
{
    [Fact]
    public void BuildAggregateSqlParts_bracket_quotes_group_by_and_source_identifiers()
    {
        var (select, groupBy) = CosmosAggregationHelper.BuildAggregateSqlParts(
            new[] { ("Category", "Category") },
            new[] { (AggregateFunction.Sum, (string?)"Amount", "Total") });

        select.Should().Contain("c[\"Category\"] AS Category");
        select.Should().Contain("SUM(c[\"Amount\"]) AS Total");
        select.Should().NotContain("c.Category");
        select.Should().NotContain("c.Amount");

        groupBy.Should().Be(" GROUP BY c[\"Category\"]");
    }

    [Fact]
    public void BuildAggregateSqlParts_count_emits_COUNT_1_without_a_field()
    {
        var (select, groupBy) = CosmosAggregationHelper.BuildAggregateSqlParts(
            groupByFields: System.Array.Empty<(string, string)>(),
            aggregates: new[] { (AggregateFunction.Count, (string?)null, "Cnt") });

        select.Should().Be("SELECT COUNT(1) AS Cnt FROM c");
        groupBy.Should().BeNull();
    }

    [Theory]
    [InlineData("Category", "c[\"Category\"]")]
    [InlineData("/Category", "c[\"/Category\"]")]
    [InlineData("weird name", "c[\"weird name\"]")]
    public void FieldRef_bracket_quotes_and_falls_back_to_c(string field, string expected)
    {
        CosmosAggregationHelper.FieldRef(field).Should().Be(expected);
    }

    [Fact]
    public void FieldRef_escapes_embedded_quotes_and_backslashes()
    {
        // A field name containing a double quote can no longer break out of the accessor / inject.
        CosmosAggregationHelper.FieldRef("a\"b").Should().Be("c[\"a\\\"b\"]");
        CosmosAggregationHelper.FieldRef("a\\b").Should().Be("c[\"a\\\\b\"]");
    }

    [Fact]
    public void FieldRef_null_or_empty_returns_bare_c()
    {
        CosmosAggregationHelper.FieldRef(null).Should().Be("c");
        CosmosAggregationHelper.FieldRef("").Should().Be("c");
    }
}
