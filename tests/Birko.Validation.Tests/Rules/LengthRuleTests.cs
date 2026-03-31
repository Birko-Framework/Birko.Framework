using Birko.Validation;
using Birko.Validation.Rules;
using FluentAssertions;
using Xunit;

namespace Birko.Validation.Tests.Rules;

public class LengthRuleTests
{
    private readonly ValidationContext _context = new(new object());

    [Fact]
    public void IsValid_Null_ReturnsTrue()
    {
        var rule = new LengthRule("Name", 2, 10);
        rule.IsValid(null, _context).Should().BeTrue();
    }

    [Fact]
    public void IsValid_StringWithinRange_ReturnsTrue()
    {
        var rule = new LengthRule("Name", 2, 10);
        rule.IsValid("hello", _context).Should().BeTrue();
    }

    [Fact]
    public void IsValid_StringBelowMin_ReturnsFalse()
    {
        var rule = new LengthRule("Name", 5, 10);
        rule.IsValid("hi", _context).Should().BeFalse();
    }

    [Fact]
    public void IsValid_StringAboveMax_ReturnsFalse()
    {
        var rule = new LengthRule("Name", 2, 5);
        rule.IsValid("toolongstring", _context).Should().BeFalse();
    }

    [Fact]
    public void IsValid_StringExactlyAtMin_ReturnsTrue()
    {
        var rule = new LengthRule("Name", 3, 10);
        rule.IsValid("abc", _context).Should().BeTrue();
    }

    [Fact]
    public void IsValid_StringExactlyAtMax_ReturnsTrue()
    {
        var rule = new LengthRule("Name", 2, 5);
        rule.IsValid("abcde", _context).Should().BeTrue();
    }

    [Fact]
    public void IsValid_MinOnly_AllowsLonger()
    {
        var rule = new LengthRule("Name", minLength: 2);
        rule.IsValid("a very long string", _context).Should().BeTrue();
    }

    [Fact]
    public void IsValid_MaxOnly_AllowsShorter()
    {
        var rule = new LengthRule("Name", maxLength: 100);
        rule.IsValid("a", _context).Should().BeTrue();
    }

    [Fact]
    public void IsValid_NonString_ReturnsFalse()
    {
        var rule = new LengthRule("Name", 2, 10);
        rule.IsValid(42, _context).Should().BeFalse();
    }

    [Fact]
    public void ErrorCode_IsINVALID_LENGTH()
    {
        var rule = new LengthRule("Name", 2, 10);
        rule.ErrorCode.Should().Be("INVALID_LENGTH");
    }

    [Fact]
    public void DefaultMessage_MinAndMax_ContainsBoth()
    {
        var rule = new LengthRule("Name", 2, 10);
        rule.ErrorMessage.Should().Contain("2").And.Contain("10");
    }

    [Fact]
    public void DefaultMessage_MinOnly_ContainsAtLeast()
    {
        var rule = new LengthRule("Name", minLength: 5);
        rule.ErrorMessage.Should().Contain("at least");
    }

    [Fact]
    public void DefaultMessage_MaxOnly_ContainsAtMost()
    {
        var rule = new LengthRule("Name", maxLength: 10);
        rule.ErrorMessage.Should().Contain("at most");
    }
}
