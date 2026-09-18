using System.Collections.Generic;
using Birko.Data.Migrations.SQL.Context;
using FluentAssertions;
using Xunit;

namespace Birko.Data.Migrations.SQL.Tests;

/// <summary>
/// CR-M115: the remaining SqlDataMigrator gap — ParseFilterToWhere's JSON-to-WHERE parsing,
/// the $gt/$gte/$lt/$lte/$ne operator mapping, and its use of quoted identifiers + @pN parameters
/// (so values are never interpolated). (Store/runner/schema-builder are covered by the SQLite tests.)
/// </summary>
public class SqlDataMigratorParseFilterTests
{
    private static (string where, List<(string Name, object? Value)> parameters) Parse(string json)
    {
        var parameters = new List<(string Name, object? Value)>();
        var idx = 0;
        var where = SqlDataMigrator.ParseFilterToWhere(json, ref idx, parameters);
        return (where, parameters);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    public void Empty_or_object_filter_yields_no_where(string json)
    {
        Parse(json).where.Should().BeEmpty();
    }

    [Fact]
    public void Equality_uses_quoted_identifier_and_a_parameter()
    {
        var (where, parameters) = Parse("{\"status\":\"active\"}");

        where.Should().Be("\"status\" = @p0");
        parameters.Should().ContainSingle();
        parameters[0].Name.Should().Be("@p0");
        parameters[0].Value.Should().Be("active");
    }

    [Theory]
    [InlineData("$gt", ">")]
    [InlineData("$gte", ">=")]
    [InlineData("$lt", "<")]
    [InlineData("$lte", "<=")]
    [InlineData("$ne", "<>")]
    public void Comparison_operators_map_to_sql(string mongoOp, string sqlOp)
    {
        var (where, parameters) = Parse($"{{\"age\":{{\"{mongoOp}\":18}}}}");

        where.Should().Be($"\"age\" {sqlOp} @p0");
        parameters[0].Value.Should().Be(18L);
    }

    [Fact]
    public void Multiple_conditions_are_anded_with_incrementing_parameters()
    {
        var (where, parameters) = Parse("{\"status\":\"active\",\"age\":{\"$gte\":21}}");

        where.Should().Be("\"status\" = @p0 AND \"age\" >= @p1");
        parameters.Should().HaveCount(2);
        parameters[0].Value.Should().Be("active");
        parameters[1].Value.Should().Be(21L);
    }

    [Fact]
    public void Values_are_parameterized_never_interpolated()
    {
        // An injection-looking value stays a bound parameter, not part of the SQL text.
        var (where, parameters) = Parse("{\"name\":\"x'; DROP TABLE t;--\"}");

        where.Should().Be("\"name\" = @p0");
        parameters[0].Value.Should().Be("x'; DROP TABLE t;--");
    }

    [Fact]
    public void Identifiers_use_the_supplied_dialect_quoter()
    {
        // CR-L150: field identifiers are quoted via the caller's dialect-aware quoter (e.g. SQL Server
        // [brackets]) instead of hardcoded ANSI double quotes.
        var parameters = new List<(string Name, object? Value)>();
        var idx = 0;
        var where = SqlDataMigrator.ParseFilterToWhere(
            "{\"status\":\"active\",\"age\":{\"$gte\":21}}", ref idx, parameters, id => $"[{id}]");

        where.Should().Be("[status] = @p0 AND [age] >= @p1");
    }
}
