using Xunit;
using System.Globalization;
using FluentAssertions;

namespace Birko.Localization.Tests;

public class NumberFormatterTests
{
    [Fact]
    public void Format_UsesCultureFormatting()
    {
        var formatter = new NumberFormatter();
        var result = formatter.Format(1234.56m, CultureInfo.GetCultureInfo("en-US"));
        result.Should().Contain("1,234.56");
    }

    [Fact]
    public void FormatPercent_UsesCultureFormatting()
    {
        var formatter = new NumberFormatter();
        var result = formatter.FormatPercent(0.75m, CultureInfo.GetCultureInfo("en-US"));
        result.Should().Contain("75");
        result.Should().Contain("%");
    }

    [Fact]
    public void FormatCurrency_UsesCultureFormatting()
    {
        var formatter = new NumberFormatter();
        var result = formatter.FormatCurrency(99.99m, "USD", CultureInfo.GetCultureInfo("en-US"));
        result.Should().Contain("99.99");
    }

    [Fact]
    public void FormatCurrency_ThrowsForNullCurrencyCode()
    {
        var formatter = new NumberFormatter();
        var act = () => formatter.FormatCurrency(100m, null!, CultureInfo.GetCultureInfo("en-US"));
        act.Should().Throw<ArgumentException>();
    }
}
