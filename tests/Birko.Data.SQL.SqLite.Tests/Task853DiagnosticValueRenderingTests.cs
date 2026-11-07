using System;
using System.Globalization;
using Birko.Data.SQL;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Birko.Data.SQL.SqLite.Tests;

/// <summary>
/// TASK-853: <c>DataBase.GetGeneratedQuery</c> rendered a value by the parameter's <c>DbType</c> and the current
/// culture. <c>SqliteParameter</c> defaults to <c>DbType.String</c>, so every value was quoted and a null read as
/// <c>''</c>; on sk-SK a decimal read <c>1,5</c>. Each test runs under sk-SK on the current thread only, restored
/// afterwards (rule 61: never process-wide).
/// </summary>
public class Task853DiagnosticValueRenderingTests
{
    private static string Render(object? value)
    {
        using var command = new SqliteCommand("SELECT 1 WHERE A = @a");
        command.Parameters.Add(new SqliteParameter("@a", value ?? DBNull.Value));

        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("sk-SK");
            return DataBase.GetGeneratedQuery(command)["SELECT 1 WHERE A = ".Length..];
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    private enum Colour : short { Red = 3 }

    [Fact]
    public void Null_renders_NULL_not_an_empty_string()
    {
        Render(null).Should().Be("NULL");
        Render(DBNull.Value).Should().Be("NULL");
    }

    [Fact]
    public void A_null_with_a_non_string_DbType_renders_NULL_not_nothing()
    {
        using var command = new SqliteCommand("SELECT 1 WHERE A = @a");
        command.Parameters.Add(new SqliteParameter("@a", DBNull.Value) { DbType = System.Data.DbType.Int32 });

        DataBase.GetGeneratedQuery(command).Should().Be("SELECT 1 WHERE A = NULL");
    }

    [Theory]
    [InlineData(10, "10")]
    [InlineData(-7L, "-7")]
    [InlineData((byte)255, "255")]
    [InlineData(1.5d, "1.5")]
    [InlineData(0.25f, "0.25")]
    public void Numbers_are_unquoted_in_the_invariant_culture(object value, string expected)
    {
        Render(value).Should().Be(expected);
    }

    [Fact]
    public void A_decimal_is_unquoted_in_the_invariant_culture()
    {
        Render(1234.5m).Should().Be("1234.5");
        Render(-0.01m).Should().Be("-0.01");
    }

    [Fact]
    public void A_non_finite_double_is_quoted()
    {
        Render(double.NaN).Should().Be("'NaN'");
    }

    [Fact]
    public void Dates_and_times_are_quoted_ISO_8601()
    {
        Render(new DateTime(2026, 11, 7, 13, 45, 30, DateTimeKind.Unspecified).AddTicks(1234567))
            .Should().Be("'2026-11-07T13:45:30.1234567'");
        Render(new DateTime(2026, 11, 7, 13, 45, 30, DateTimeKind.Utc)).Should().Be("'2026-11-07T13:45:30.0000000Z'");
        Render(new DateTimeOffset(2026, 11, 7, 13, 45, 30, TimeSpan.FromHours(1)))
            .Should().Be("'2026-11-07T13:45:30.0000000+01:00'");
        Render(new DateOnly(2026, 11, 7)).Should().Be("'2026-11-07'");
        Render(new TimeOnly(9, 5)).Should().Be("'09:05:00'");
        Render(new TimeOnly(9, 5, 0, 250)).Should().Be("'09:05:00.25'");
    }

    [Fact]
    public void A_guid_is_quoted()
    {
        var guid = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e");
        Render(guid).Should().Be("'0f8fad5b-d9cb-469f-a165-70867728950e'");
    }

    [Fact]
    public void A_bool_is_one_or_zero()
    {
        Render(true).Should().Be("1");
        Render(false).Should().Be("0");
    }

    [Fact]
    public void An_enum_is_its_underlying_number()
    {
        Render(Colour.Red).Should().Be("3");
    }

    [Fact]
    public void Bytes_are_a_hex_literal()
    {
        Render(new byte[] { 0x00, 0xAB, 0xFF }).Should().Be("0x00ABFF");
        Render(Array.Empty<byte>()).Should().Be("0x");
    }

    [Fact]
    public void Long_bytes_are_truncated_with_the_length_noted()
    {
        var bytes = new byte[1000];
        bytes[0] = 0x12;

        Render(bytes).Should().Be("0x12" + new string('0', 62) + "... (1000 bytes)");
    }

    [Fact]
    public void A_string_keeps_its_quote_escaping()
    {
        Render("O'Brien").Should().Be("'O''Brien'");
        Render("1,5").Should().Be("'1,5'");
    }
}
