using System;
using Birko.Data.Migrations.InfluxDB;
using Birko.Data.Migrations.InfluxDB.Context;
using FluentAssertions;
using InfluxDB.Client.Writes;
using Xunit;

namespace Birko.Data.Migrations.InfluxDB.Tests;

/// <summary>
/// Offline coverage for the InfluxDB migration helpers:
/// CR-M110 (ApplyValue preserves value types instead of coercing to double) and
/// CR-M111 (ConvertFilterToFluxPredicate rejects JSON filters; EscapeFluxString escapes names).
/// </summary>
public class InfluxDBMigratorHelperTests
{
    private static string Line(object? value)
    {
        var point = PointData.Measurement("m").Field("base", 0L);
        point = InfluxDBDataMigrator.ApplyValue(point, "f", value);
        return point.ToLineProtocol();
    }

    [Fact]
    public void ApplyValue_bool_stays_boolean_not_double()
    {
        Line(true).Should().Contain("f=true");
        Line(false).Should().Contain("f=false");
    }

    [Fact]
    public void ApplyValue_integer_types_stay_integer()
    {
        // Integer fields render with an 'i' suffix in line protocol; a double would be "f=42".
        Line(42).Should().Contain("f=42i");
        Line(42L).Should().Contain("f=42i");
    }

    [Fact]
    public void ApplyValue_floating_point_is_a_double_field()
    {
        Line(1.5).Should().Contain("f=1.5");
    }

    [Fact]
    public void ApplyValue_string_becomes_a_tag()
    {
        Line("hello").Should().Contain(",f=hello ", "string values are written as tags");
    }

    [Fact]
    public void ApplyValue_unsupported_type_falls_back_to_string_field_without_throwing()
    {
        // CR-M110: Convert.ToDouble(DateTime) used to throw mid-migration. Now it degrades to a string.
        Action act = () => Line(new DateTime(2026, 1, 1));
        act.Should().NotThrow();
    }

    [Fact]
    public void ApplyValue_null_is_skipped()
    {
        // Only the base field remains — no "f=" token.
        Line(null).Should().NotContain("f=");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{}")]
    public void ConvertFilterToFluxPredicate_empty_or_object_is_empty(string input)
    {
        InfluxDBDataMigrator.ConvertFilterToFluxPredicate(input).Should().BeEmpty();
    }

    [Fact]
    public void ConvertFilterToFluxPredicate_rejects_a_json_filter()
    {
        Action act = () => InfluxDBDataMigrator.ConvertFilterToFluxPredicate("{\"x\":1}");
        act.Should().Throw<NotSupportedException>();
    }

    [Fact]
    public void ConvertFilterToFluxPredicate_passes_through_a_flux_predicate()
    {
        InfluxDBDataMigrator.ConvertFilterToFluxPredicate("r.host == \"a\"").Should().Be("r.host == \"a\"");
    }

    [Fact]
    public void EscapeFluxString_escapes_quotes_and_backslashes()
    {
        InfluxMigrationStore.EscapeFluxString("a\"b").Should().Be("a\\\"b");
        InfluxMigrationStore.EscapeFluxString("a\\b").Should().Be("a\\\\b");
        InfluxMigrationStore.EscapeFluxString(null).Should().BeEmpty();
    }
}
