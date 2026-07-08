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

    [Fact]
    public void FormatCurrency_HonorsCurrencyCode_UnderSameCulture()
    {
        // CR-H118: the currencyCode parameter must affect output — two different codes
        // formatted under one culture must be distinguishable.
        var formatter = new NumberFormatter();
        var de = CultureInfo.GetCultureInfo("de-DE");

        var usd = formatter.FormatCurrency(99.99m, "USD", de);
        var gbp = formatter.FormatCurrency(99.99m, "GBP", de);

        usd.Should().NotBe(gbp);
        usd.Should().Contain("$");
        gbp.Should().Contain("£");
        // Locale layout (comma decimal separator) still comes from the culture.
        usd.Should().Contain("99,99");
        gbp.Should().Contain("99,99");
    }

    [Fact]
    public void FormatCurrency_UsesExpectedSymbol_ForKnownCode()
    {
        var formatter = new NumberFormatter();
        var result = formatter.FormatCurrency(99.99m, "EUR", CultureInfo.GetCultureInfo("en-US"));
        result.Should().Contain("€");
        result.Should().Contain("99.99");
    }

    [Fact]
    public void FormatCurrency_FallsBackToCode_ForUnknownCurrency()
    {
        var formatter = new NumberFormatter();
        var result = formatter.FormatCurrency(99.99m, "xyz", CultureInfo.GetCultureInfo("en-US"));
        // Unknown ISO code renders the (upper-cased) code itself as the symbol.
        result.Should().Contain("XYZ");
    }
}
