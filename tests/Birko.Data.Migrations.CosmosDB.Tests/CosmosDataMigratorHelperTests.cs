using System.Text.Json;
using Birko.Data.Migrations.CosmosDB.Context;
using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Xunit;

namespace Birko.Data.Migrations.CosmosDB.Tests;

/// <summary>
/// CR-H057: the CosmosDB migration backend had no tests. These cover the pure filter/value helpers
/// (ParseFilterToSql / FormatSqlValue / ExtractValue) and the CR-H055 partition-key builder, all
/// without a live Cosmos instance.
/// </summary>
public class CosmosDataMigratorHelperTests
{
    private static JsonElement Json(string raw) => JsonDocument.Parse(raw).RootElement;

    [Fact]
    public void ParseFilterToSql_Empty_ReturnsEmpty()
    {
        CosmosDBDataMigrator.ParseFilterToSql(null).Should().BeEmpty();
        CosmosDBDataMigrator.ParseFilterToSql("{}").Should().BeEmpty();
    }

    [Fact]
    public void ParseFilterToSql_Equality_And_Operators()
    {
        // CR-M104: identifiers are bracket-quoted (c["field"]) rather than dotted (c.field).
        CosmosDBDataMigrator.ParseFilterToSql("{\"status\":\"active\"}").Should().Be("c[\"status\"] = 'active'");
        CosmosDBDataMigrator.ParseFilterToSql("{\"age\":{\"$gt\":18}}").Should().Be("c[\"age\"] > 18");
        CosmosDBDataMigrator.ParseFilterToSql("{\"age\":{\"$gte\":18}}").Should().Be("c[\"age\"] >= 18");
        CosmosDBDataMigrator.ParseFilterToSql("{\"age\":{\"$lt\":65}}").Should().Be("c[\"age\"] < 65");
        CosmosDBDataMigrator.ParseFilterToSql("{\"age\":{\"$lte\":65}}").Should().Be("c[\"age\"] <= 65");
        CosmosDBDataMigrator.ParseFilterToSql("{\"state\":{\"$ne\":\"x\"}}").Should().Be("c[\"state\"] != 'x'");
    }

    [Fact]
    public void ParseFilterToSql_EscapesSingleQuotes()
    {
        CosmosDBDataMigrator.ParseFilterToSql("{\"name\":\"O'Brien\"}").Should().Be("c[\"name\"] = 'O''Brien'");
    }

    [Fact]
    public void ParseFilterToSql_bracket_quotes_field_names_with_special_chars()
    {
        // CR-M104: a field name with whitespace or an embedded quote can no longer produce malformed
        // or injectable SQL — it is bracket-quoted and the embedded quote is escaped.
        CosmosDBDataMigrator.ParseFilterToSql("{\"first name\":\"x\"}").Should().Be("c[\"first name\"] = 'x'");
        CosmosDBDataMigrator.ParseFilterToSql("{\"a\\\"b\":1}").Should().Be("c[\"a\\\"b\"] = 1");
    }

    [Theory]
    [InlineData("\"active\"", "'active'")]
    [InlineData("42", "42")]
    [InlineData("true", "true")]
    [InlineData("false", "false")]
    [InlineData("null", "null")]
    public void FormatSqlValue_FormatsByType(string rawJson, string expected)
    {
        var value = CosmosDBDataMigrator.ExtractValue(Json(rawJson));
        CosmosDBDataMigrator.FormatSqlValue(value).Should().Be(expected);
    }

    [Fact]
    public void BuildPartitionKey_StringNumberBool()
    {
        var doc = Json("{\"tenantId\":\"acme\",\"num\":7,\"flag\":true}");

        CosmosDBDataMigrator.BuildPartitionKey(doc, "tenantId").Should().Be(new PartitionKey("acme"));
        CosmosDBDataMigrator.BuildPartitionKey(doc, "num").Should().Be(new PartitionKey(7d));
        CosmosDBDataMigrator.BuildPartitionKey(doc, "flag").Should().Be(new PartitionKey(true));
    }

    [Fact]
    public void BuildPartitionKey_MissingProperty_IsNull()
    {
        var doc = Json("{\"id\":\"x\"}");

        CosmosDBDataMigrator.BuildPartitionKey(doc, "tenantId").Should().Be(PartitionKey.Null);
    }
}
