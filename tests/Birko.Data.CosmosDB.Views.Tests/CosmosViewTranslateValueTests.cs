using System;
using System.Linq.Expressions;
using Birko.Data.CosmosDB.Views;
using FluentAssertions;
using Xunit;

namespace Birko.Data.CosmosDB.Views.Tests;

/// <summary>
/// CR-M086: TranslateValue only quoted string/Guid and fell back to a raw ToString() for everything
/// else — so a DateTime filter produced a culture-dependent, unquoted, invalid SQL literal and an enum
/// emitted its unquoted member name. DateTime/DateTimeOffset are now ISO-8601 quoted and enums emit
/// their numeric value (matching System.Text.Json's default serialization).
/// </summary>
public class CosmosViewTranslateValueTests
{
    private enum Status
    {
        Draft = 0,
        Published = 2,
    }

    private static string Translate(object? value, Type? type = null)
    {
        var expr = value is null && type != null
            ? Expression.Constant(null, type)
            : Expression.Constant(value);
        return CosmosFilterTranslator.TranslateValue(expr);
    }

    [Fact]
    public void DateTime_is_quoted_iso8601()
    {
        var dt = new DateTime(2026, 6, 17, 8, 30, 0, DateTimeKind.Utc);

        Translate(dt).Should().Be($"'{dt.ToString("o", System.Globalization.CultureInfo.InvariantCulture)}'");
    }

    [Fact]
    public void DateTimeOffset_is_quoted_iso8601()
    {
        var dto = new DateTimeOffset(2026, 6, 17, 8, 30, 0, TimeSpan.Zero);

        Translate(dto).Should().Be($"'{dto.ToString("o", System.Globalization.CultureInfo.InvariantCulture)}'");
    }

    [Fact]
    public void Enum_emits_its_numeric_value_unquoted()
    {
        Translate(Status.Published).Should().Be("2");
    }

    [Fact]
    public void String_is_single_quoted_and_escaped()
    {
        Translate("O'Brien").Should().Be("'O\\'Brien'");
    }

    [Fact]
    public void Bool_number_guid_and_null_render_as_expected()
    {
        Translate(true).Should().Be("true");
        Translate(42).Should().Be("42");
        var g = Guid.NewGuid();
        Translate(g).Should().Be($"'{g}'");
        Translate(null, typeof(string)).Should().Be("null");
    }
}
