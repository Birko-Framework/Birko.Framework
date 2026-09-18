using Xunit;
using System.Globalization;
using FluentAssertions;

namespace Birko.Localization.Tests;

public class CldrPluralizerTests
{
    private readonly CldrPluralizer _pluralizer = new();

    [Theory]
    [InlineData(1, 0)]  // one
    [InlineData(0, 1)]  // other
    [InlineData(2, 1)]  // other
    [InlineData(5, 1)]  // other
    [InlineData(100, 1)] // other
    public void English_TwoForms(int count, int expectedForm)
    {
        _pluralizer.GetPluralForm(count, CultureInfo.GetCultureInfo("en")).Should().Be(expectedForm);
    }

    [Fact]
    public void English_HasTwoForms()
    {
        _pluralizer.GetPluralFormCount(CultureInfo.GetCultureInfo("en")).Should().Be(2);
    }

    [Theory]
    [InlineData(1, 0)]   // one: "1 deň"
    [InlineData(2, 1)]   // few: "2 dni"
    [InlineData(3, 1)]   // few: "3 dni"
    [InlineData(4, 1)]   // few: "4 dni"
    [InlineData(5, 2)]   // other: "5 dní"
    [InlineData(0, 2)]   // other: "0 dní"
    [InlineData(10, 2)]  // other
    [InlineData(100, 2)] // other
    public void Slovak_ThreeForms(int count, int expectedForm)
    {
        _pluralizer.GetPluralForm(count, CultureInfo.GetCultureInfo("sk")).Should().Be(expectedForm);
    }

    [Fact]
    public void Slovak_HasThreeForms()
    {
        _pluralizer.GetPluralFormCount(CultureInfo.GetCultureInfo("sk")).Should().Be(3);
    }

    [Theory]
    [InlineData(1, 0)]   // one
    [InlineData(2, 1)]   // few
    [InlineData(3, 1)]   // few
    [InlineData(4, 1)]   // few
    [InlineData(5, 2)]   // other
    [InlineData(12, 2)]  // other (n%100=12, in 12..14 range)
    [InlineData(22, 1)]  // few (n%10=2, n%100=22, not in 12..14)
    [InlineData(23, 1)]  // few
    [InlineData(24, 1)]  // few
    [InlineData(25, 2)]  // other
    [InlineData(112, 2)] // other (n%10=2, but n%100=12)
    [InlineData(122, 1)] // few (n%10=2, n%100=22)
    public void Polish_ThreeForms(int count, int expectedForm)
    {
        _pluralizer.GetPluralForm(count, CultureInfo.GetCultureInfo("pl")).Should().Be(expectedForm);
    }

    [Theory]
    [InlineData(1, 0)]   // one (n%10=1, n%100!=11)
    [InlineData(21, 0)]  // one
    [InlineData(101, 0)] // one
    [InlineData(2, 1)]   // few
    [InlineData(3, 1)]   // few
    [InlineData(4, 1)]   // few
    [InlineData(22, 1)]  // few
    [InlineData(5, 2)]   // other
    [InlineData(11, 2)]  // other (n%100=11)
    [InlineData(12, 2)]  // other (n%100=12)
    [InlineData(111, 2)] // other (n%100=11)
    [InlineData(0, 2)]   // other
    public void Russian_ThreeForms(int count, int expectedForm)
    {
        _pluralizer.GetPluralForm(count, CultureInfo.GetCultureInfo("ru")).Should().Be(expectedForm);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(5, 0)]
    [InlineData(100, 0)]
    public void Chinese_OneForm(int count, int expectedForm)
    {
        _pluralizer.GetPluralForm(count, CultureInfo.GetCultureInfo("zh")).Should().Be(expectedForm);
    }

    [Fact]
    public void Chinese_HasOneForm()
    {
        _pluralizer.GetPluralFormCount(CultureInfo.GetCultureInfo("zh")).Should().Be(1);
    }

    [Theory]
    [InlineData(0, 0)]   // zero
    [InlineData(1, 1)]   // one
    [InlineData(2, 2)]   // two
    [InlineData(3, 3)]   // few (n%100 in 3..10)
    [InlineData(10, 3)]  // few
    [InlineData(11, 4)]  // many (n%100 in 11..99)
    [InlineData(99, 4)]  // many
    [InlineData(100, 5)] // other
    public void Arabic_SixForms(int count, int expectedForm)
    {
        _pluralizer.GetPluralForm(count, CultureInfo.GetCultureInfo("ar")).Should().Be(expectedForm);
    }

    [Fact]
    public void Arabic_HasSixForms()
    {
        _pluralizer.GetPluralFormCount(CultureInfo.GetCultureInfo("ar")).Should().Be(6);
    }

    [Theory]
    [InlineData(0, 0)]  // French: 0 is singular
    [InlineData(1, 0)]  // one
    [InlineData(2, 1)]  // other
    public void French_ZeroIsSingular(int count, int expectedForm)
    {
        _pluralizer.GetPluralForm(count, CultureInfo.GetCultureInfo("fr")).Should().Be(expectedForm);
    }

    [Fact]
    public void UnknownLanguage_FallsBackToEnglishRules()
    {
        // "xx" is not in the CLDR rules, should default to 2-form (en-like)
        var unknown = CultureInfo.GetCultureInfo("af"); // Afrikaans, not explicitly listed
        _pluralizer.GetPluralForm(1, unknown).Should().Be(0);
        _pluralizer.GetPluralForm(2, unknown).Should().Be(1);
        _pluralizer.GetPluralFormCount(unknown).Should().Be(2);
    }

    [Fact]
    public void NegativeCount_UsesAbsoluteValue()
    {
        _pluralizer.GetPluralForm(-1, CultureInfo.GetCultureInfo("en")).Should().Be(0);
        _pluralizer.GetPluralForm(-5, CultureInfo.GetCultureInfo("sk")).Should().Be(2);
    }

    [Fact]
    public void NullCulture_ThrowsArgumentNullException()
    {
        var act = () => _pluralizer.GetPluralForm(1, null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
