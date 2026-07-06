using Birko.Helpers;
using FluentAssertions;
using Xunit;

namespace Birko.Helpers.Tests
{
    public class EnumHelperTests
    {
        public enum Sample
        {
            None = 0,
            First = 1,
            Second = 2,
        }

        [Theory]
        [InlineData("First", Sample.First)]
        [InlineData("first", Sample.First)]
        [InlineData("SECOND", Sample.Second)]
        [InlineData("  Second  ", Sample.Second)]
        [InlineData("None", Sample.None)]
        public void TryParseName_accepts_defined_names_case_insensitively(string value, Sample expected)
        {
            EnumHelper.TryParseName<Sample>(value, out var result).Should().BeTrue();
            result.Should().Be(expected);
        }

        [Theory]
        [InlineData("0")]   // ordinal of None — still rejected: names only
        [InlineData("1")]
        [InlineData("-3")]
        [InlineData("999")] // out-of-range ordinal Enum.TryParse would otherwise accept
        public void TryParseName_rejects_numeric_and_ordinal_input(string value)
        {
            EnumHelper.TryParseName<Sample>(value, out var result).Should().BeFalse();
            result.Should().Be(default(Sample));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("Nope")]
        public void TryParseName_rejects_blank_and_unknown_names(string? value)
        {
            EnumHelper.TryParseName<Sample>(value, out _).Should().BeFalse();
        }
    }
}
