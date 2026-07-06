using Birko.Xaml.Core.Localization;
using FluentAssertions;
using Xunit;

namespace Birko.Xaml.Core.Tests;

public class FormatterTests
{
    private static (Formatter fmt, I18n i18n) Make(string locale = "en-US")
    {
        var i18n = new I18n();
        i18n.SetLocale(locale);
        return (new Formatter(i18n), i18n);
    }

    // ── Duration — locale-independent, exact parity with Birko.Web fmt.ts ──────

    [Theory]
    [InlineData(0, false, "0:00")]
    [InlineData(5, false, "0:05")]
    [InlineData(65, false, "1:05")]
    [InlineData(3599, false, "59:59")]
    [InlineData(3600, false, "1:00:00")]
    [InlineData(3661, false, "1:01:01")]
    [InlineData(90, true, "0:01:30")]   // alwaysHours forces h:mm:ss under an hour
    public void Duration_matches_web_output(double seconds, bool alwaysHours, string expected)
    {
        Make().fmt.Duration(seconds, alwaysHours).Should().Be(expected);
    }

    [Fact]
    public void Duration_clamps_negative_and_floors_fractions()
    {
        var (fmt, _) = Make();
        fmt.Duration(-42).Should().Be("0:00");
        fmt.Duration(65.9).Should().Be("1:05");
    }

    [Fact]
    public void Duration_is_locale_independent()
    {
        Make("en-US").fmt.Duration(3661).Should().Be(Make("de-DE").fmt.Duration(3661));
    }

    // ── Number / Currency — culture-driven separators ─────────────────────────

    [Fact]
    public void Number_uses_active_culture_separators()
    {
        Make("en-US").fmt.Number(1234.5, 2).Should().Be("1,234.50");
        Make("de-DE").fmt.Number(1234.5, 2).Should().Be("1.234,50");
    }

    [Fact]
    public void Number_without_decimals_trims_trailing_fraction()
    {
        var (fmt, _) = Make("en-US");
        fmt.Number(1234).Should().Be("1,234");
        fmt.Number(1234.5).Should().Be("1,234.5");
    }

    [Fact]
    public void Currency_symbol_is_driven_by_code_not_culture()
    {
        // en-US number shape, but the symbol follows the requested currency code.
        Make("en-US").fmt.Currency(1234.5, "EUR").Should().Contain("€").And.Contain("1,234");
        Make("en-US").fmt.Currency(10, "GBP").Should().Contain("£");
        // Unknown code falls back to showing the code.
        Make("en-US").fmt.Currency(10, "SEK").Should().Contain("SEK");
    }

    [Fact]
    public void Percent_takes_0_to_100_input()
    {
        Make("en-US").fmt.Percent(85).Should().Contain("85").And.Contain("%");
    }

    // ── Date — style differences ──────────────────────────────────────────────

    [Fact]
    public void Full_date_includes_weekday_long_does_not()
    {
        var (fmt, _) = Make("en-US");
        var monday = new DateTime(2026, 1, 5); // a Monday
        fmt.Date(monday, DateStyle.Full).Should().Contain("Monday");
        fmt.Date(monday, DateStyle.Long).Should().NotContain("Monday");
        fmt.Date(monday, DateStyle.Long).Should().Contain("2026").And.Contain("January");
        fmt.Date(monday, DateStyle.Short).Should().Contain("2026");
    }

    // ── Live locale switching ─────────────────────────────────────────────────

    [Fact]
    public void Reflows_when_the_bound_locale_changes()
    {
        var (fmt, i18n) = Make("en-US");
        fmt.Number(1234.5, 1).Should().Be("1,234.5");
        i18n.SetLocale("de-DE");
        fmt.Number(1234.5, 1).Should().Be("1.234,5");
    }

    [Fact]
    public void Unknown_locale_falls_back_to_invariant()
    {
        // Should not throw for a bare/unknown locale tag.
        var (fmt, _) = Make("xx-not-a-culture");
        fmt.Duration(65).Should().Be("1:05");
        fmt.Number(1234, 0).Should().NotBeNull();
    }
}
