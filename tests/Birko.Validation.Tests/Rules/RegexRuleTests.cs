using Birko.Validation;
using Birko.Validation.Rules;
using FluentAssertions;
using System.Text.RegularExpressions;
using Xunit;

namespace Birko.Validation.Tests.Rules;

public class RegexRuleTests
{
    private readonly ValidationContext _context = new(new object());

    [Fact]
    public void IsValid_Null_ReturnsTrue()
    {
        var rule = new RegexRule("Code", @"^[A-Z]+$");
        rule.IsValid(null, _context).Should().BeTrue();
    }

    [Fact]
    public void IsValid_EmptyString_ReturnsTrue()
    {
        var rule = new RegexRule("Code", @"^[A-Z]+$");
        rule.IsValid(string.Empty, _context).Should().BeTrue();
    }

    [Fact]
    public void IsValid_MatchingString_ReturnsTrue()
    {
        var rule = new RegexRule("Code", @"^[A-Z]+$");
        rule.IsValid("ABC", _context).Should().BeTrue();
    }

    [Fact]
    public void IsValid_NonMatchingString_ReturnsFalse()
    {
        var rule = new RegexRule("Code", @"^[A-Z]+$");
        rule.IsValid("abc", _context).Should().BeFalse();
    }

    [Fact]
    public void IsValid_NonString_ReturnsFalse()
    {
        var rule = new RegexRule("Code", @"^[A-Z]+$");
        rule.IsValid(42, _context).Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithCompiledRegex_Works()
    {
        var regex = new Regex(@"^\d{3}$");
        var rule = new RegexRule("Code", regex);
        rule.IsValid("123", _context).Should().BeTrue();
        rule.IsValid("12", _context).Should().BeFalse();
    }

    [Fact]
    public void ErrorCode_IsINVALID_FORMAT()
    {
        var rule = new RegexRule("Code", @".*");
        rule.ErrorCode.Should().Be("INVALID_FORMAT");
    }
}
