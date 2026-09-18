using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

    /// <summary>
    /// Translates a filter and returns the clause plus the values it bound.
    /// </summary>
    /// <remarks>
    /// TASK-450: <c>ParseFilterToSql</c> used to render values into the clause as SQL literals, and
    /// the assertions below pinned that rendering. Values are now bound as <c>@pN</c> parameters, so
    /// each test is <b>inverted, not deleted</b> (CLAUDE.md § TASK-211) - it still covers its own
    /// operator or field-name shape, now asserting the placeholder and the bound value separately.
    /// </remarks>
    private static (string Clause, List<KeyValuePair<string, object?>> Parameters) Parse(string? filterJson)
    {
        var parameters = new List<KeyValuePair<string, object?>>();
        var clause = CosmosDBDataMigrator.ParseFilterToSql(filterJson, parameters);
        return (clause, parameters);
    }

    private static object? OnlyValue(string? filterJson)
    {
        var (_, parameters) = Parse(filterJson);
        parameters.Should().ContainSingle("each of these filters compares against exactly one value");
        return parameters[0].Value;
    }

    [Fact]
    public void ParseFilterToSql_Empty_ReturnsEmpty()
    {
        Parse(null).Clause.Should().BeEmpty();
        Parse("{}").Clause.Should().BeEmpty();

        // And an empty filter binds nothing, so the caller's QueryDefinition carries no stray parameter.
        Parse("{}").Parameters.Should().BeEmpty();
    }

    [Fact]
    public void ParseFilterToSql_Equality_And_Operators()
    {
        // CR-M104: identifiers are bracket-quoted (c["field"]) rather than dotted (c.field). That is
        // unchanged -- an identifier cannot be parameterised. Only the VALUE side moved.
        Parse("{\"status\":\"active\"}").Clause.Should().Be("c[\"status\"] = @p0");
        Parse("{\"age\":{\"$gt\":18}}").Clause.Should().Be("c[\"age\"] > @p0");
        Parse("{\"age\":{\"$gte\":18}}").Clause.Should().Be("c[\"age\"] >= @p0");
        Parse("{\"age\":{\"$lt\":65}}").Clause.Should().Be("c[\"age\"] < @p0");
        Parse("{\"age\":{\"$lte\":65}}").Clause.Should().Be("c[\"age\"] <= @p0");
        Parse("{\"state\":{\"$ne\":\"x\"}}").Clause.Should().Be("c[\"state\"] != @p0");

        // The operator is what each row is about; the value must still arrive.
        OnlyValue("{\"status\":\"active\"}").Should().Be("active");
        OnlyValue("{\"age\":{\"$gt\":18}}").Should().Be(18L);
    }

    [Fact]
    public void ParseFilterToSql_does_not_escape_a_quote_because_it_no_longer_renders_the_value()
    {
        // ⚠ INVERTED, and this test WAS the defect's alibi. It asserted `'O''Brien'` -- SQL-standard
        // quote doubling, which Cosmos NoSQL does not use at all: it escapes with a backslash, so that
        // rendering lexes as two adjacent literals and the query is a syntax error. The test made a
        // wrong escaping scheme look like a considered decision.
        var (clause, parameters) = Parse("{\"name\":\"O'Brien\"}");

        clause.Should().Be("c[\"name\"] = @p0");
        parameters.Should().ContainSingle().Which.Value.Should().Be("O'Brien",
            "the value is data now, so it arrives verbatim and nothing needs escaping");
    }

    [Theory]
    [InlineData("O'Brien")]
    [InlineData("foo\\")]
    [InlineData("a\\' OR 1=1 --")]
    [InlineData("' OR 1=1 --")]
    [InlineData("x\nY")]
    public void No_payload_reaches_the_statement(string payload)
    {
        // TASK-450. Measured against the old renderer: `a\' OR 1=1 --` produced
        // `'a\'' OR 1=1 --'`, which lexes as the string `a'` followed by ` OR 1=1 ` and a comment --
        // the backslash was never escaped at all, so it closed the literal early. Asserting that the
        // payload is ABSENT from the clause is stronger than asserting any particular escaping, and it
        // cannot rot as the grammar changes.
        var json = JsonSerializer.Serialize(new Dictionary<string, string> { ["name"] = payload });
        var (clause, parameters) = Parse(json);

        clause.Should().Be("c[\"name\"] = @p0");
        clause.Should().NotContain(payload);
        parameters.Should().ContainSingle().Which.Value.Should().Be(payload);
    }

    [Fact]
    public void Several_values_bind_to_distinct_placeholders()
    {
        var (clause, parameters) = Parse("{\"status\":\"active\",\"age\":{\"$gt\":18}}");

        clause.Should().Be("c[\"status\"] = @p0 AND c[\"age\"] > @p1");
        parameters.Select(p => p.Key).Should().Equal("@p0", "@p1");
        parameters.Select(p => p.Value).Should().Equal("active", 18L);
    }

    [Fact]
    public void ParseFilterToSql_bracket_quotes_field_names_with_special_chars()
    {
        // CR-M104: a field name with whitespace or an embedded quote can no longer produce malformed
        // or injectable SQL — it is bracket-quoted and the embedded quote is escaped.
        Parse("{\"first name\":\"x\"}").Clause.Should().Be("c[\"first name\"] = @p0");
        Parse("{\"a\\\"b\":1}").Clause.Should().Be("c[\"a\\\"b\"] = @p0");

        // TASK-450 moved this rule into QuoteFieldPath so CosmosDBSchemaBuilder shares it -- it used to
        // interpolate a field name with no escaping at all. Behaviour here is unchanged.
        CosmosDBDataMigrator.QuoteFieldPath("first name").Should().Be("c[\"first name\"]");
        CosmosDBDataMigrator.QuoteFieldPath(@"a""b").Should().Be(@"c[""a\""b""]");
        CosmosDBDataMigrator.QuoteFieldPath(@"back\slash").Should().Be(@"c[""back\\slash""]");
    }

    [Theory]
    [InlineData("\"active\"", "active")]
    [InlineData("42", 42L)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("null", null)]
    public void A_value_is_BOUND_by_type_rather_than_formatted(object? _, object? expected)
    {
        // ⚠ INVERTED from FormatSqlValue_FormatsByType, which asserted the SQL literal each type
        // rendered as. There is no rendering any more: the CLR value reaches the parameter and the SDK
        // serializes it with the same serializer that wrote the documents.
        var raw = expected switch
        {
            string s => "\"" + s + "\"",
            null => "null",
            bool b => b ? "true" : "false",
            _ => expected.ToString()!,
        };

        var value = CosmosDBDataMigrator.ExtractValue(Json(raw));
        var parameters = new List<KeyValuePair<string, object?>>();
        CosmosDBDataMigrator.BindValue(value, parameters).Should().Be("@p0");
        parameters[0].Value.Should().Be(expected);
    }

    [Fact]
    public void Bind_attaches_every_collected_parameter_to_the_query()
    {
        var (clause, parameters) = Parse("{\"status\":\"active\",\"age\":{\"$gt\":18}}");

        var queryDef = CosmosDBDataMigrator.Bind($"SELECT * FROM c WHERE {clause}", parameters);

        queryDef.QueryText.Should().Be("SELECT * FROM c WHERE c[\"status\"] = @p0 AND c[\"age\"] > @p1");
        queryDef.GetQueryParameters().Select(p => p.Name).Should().Equal("@p0", "@p1");
        queryDef.GetQueryParameters().Select(p => p.Value).Should().Equal("active", 18L);
    }

    /// <summary>
    /// TASK-450 -- every query the migrator issues must go through <c>Bind</c>, not a bare
    /// <c>new QueryDefinition(query)</c>, or the collected values are silently never attached.
    /// </summary>
    /// <remarks>
    /// ⚠ A source scan for the same reason as the schema-builder one below: all three sites sit
    /// inside methods that call a live container first, so they cannot be reached offline. Measured:
    /// without this, making <c>Bind</c> drop its parameters left all 21 other tests green -- the
    /// statement would have shipped placeholders with nothing bound to them.
    /// </remarks>
    [Fact]
    public void Every_migrator_query_is_built_through_Bind()
    {
        var source = File.ReadAllText(MigratorSource());

        // The one legitimate bare QueryDefinition is the whole-container copy, which has no filter.
        var bare = source.Split('\n')
            .Where(l => l.Contains("new QueryDefinition(", StringComparison.Ordinal))
            .Select(l => l.Trim())
            .Where(l => !l.Contains("\"SELECT * FROM c\"", StringComparison.Ordinal))
            .Where(l => !l.Contains("var queryDef = new QueryDefinition(sql);", StringComparison.Ordinal))
            .ToList();

        bare.Should().BeEmpty("a filtered query built with a bare QueryDefinition carries @pN "
                              + "placeholders with no values attached");

        source.Split("Bind(query, parameters)").Length.Should().Be(4,
            "the two SELECTs and the COUNT all take a filter — three call sites, so four splits");
    }

    private static string MigratorSource()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            foreach (var root in new[] { dir.FullName, Path.Combine(dir.FullName, "Framework") })
            {
                var path = Path.Combine(root, "Birko.Data.Migrations.CosmosDB", "Context", "CosmosDBDataMigrator.cs");
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        throw new FileNotFoundException("CosmosDBDataMigrator.cs was not findable from the test binary.");
    }

    /// <summary>
    /// TASK-450 -- <c>CosmosDBSchemaBuilder.RenameField</c> must route its field name through
    /// <see cref="CosmosDBDataMigrator.QuoteFieldPath"/> rather than interpolating it raw.
    /// </summary>
    /// <remarks>
    /// ⚠ A source scan because <c>RenameField</c> cannot be reached offline -- it calls
    /// <c>ReadContainerAsync</c> on a live container before it builds the query, so there is no seam
    /// to assert the emitted SQL through. Without this, the schema-builder half of the fix has no
    /// test at all: measured, reverting it alone left all 20 other tests green.
    /// </remarks>
    [Fact]
    public void The_schema_builder_does_not_interpolate_a_field_name_into_a_path()
    {
        var source = File.ReadAllText(SchemaBuilderSource());

        source.Should().Contain("QuoteFieldPath(oldName)",
            "the rename query's field name is an identifier and cannot be parameterised, so it must "
            + "use the shared escaper rather than reaching the statement raw");

        // The raw shape it used to emit: an interpolation hole immediately inside a bracket-quoted path.
        source.Should().NotContain("c[\\\"{",
            "interpolating a field name straight into c[\"...\"] lets a quote or backslash in the name "
            + "break out of the identifier");
    }

    private static string SchemaBuilderSource()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            foreach (var root in new[] { dir.FullName, Path.Combine(dir.FullName, "Framework") })
            {
                var path = Path.Combine(root, "Birko.Data.Migrations.CosmosDB", "Context", "CosmosDBSchemaBuilder.cs");
                if (File.Exists(path))
                {
                    return path;
                }
            }
        }

        throw new FileNotFoundException(
            "CosmosDBSchemaBuilder.cs was not findable from the test binary; this scan is the only "
            + "cover that half of TASK-450 has, so do not weaken it into a silent skip.");
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
