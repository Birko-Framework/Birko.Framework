using Birko.Validation;
using Birko.Validation.Rules;
using FluentAssertions;
using Xunit;

namespace Birko.Validation.Tests.Rules;

public class RangeRuleTests
{
    private readonly ValidationContext _context = new(new object());

    [Fact]
    public void IsValid_Null_ReturnsTrue()
    {
        var rule = new RangeRule("Age", 0, 150);
        rule.IsValid(null, _context).Should().BeTrue();
    }

    [Fact]
    public void IsValid_WithinRange_ReturnsTrue()
    {
        var rule = new RangeRule("Age", 0, 150);
        rule.IsValid(25, _context).Should().BeTrue();
    }

    [Fact]
    public void IsValid_BelowMin_ReturnsFalse()
    {
        var rule = new RangeRule("Age", 0, 150);
        rule.IsValid(-1, _context).Should().BeFalse();
    }

    [Fact]
    public void IsValid_AboveMax_ReturnsFalse()
    {
        var rule = new RangeRule("Age", 0, 150);
        rule.IsValid(200, _context).Should().BeFalse();
    }

    [Fact]
    public void IsValid_ExactlyAtMin_ReturnsTrue()
    {
        var rule = new RangeRule("Age", 0, 150);
        rule.IsValid(0, _context).Should().BeTrue();
    }

    [Fact]
    public void IsValid_ExactlyAtMax_ReturnsTrue()
    {
        var rule = new RangeRule("Age", 0, 150);
        rule.IsValid(150, _context).Should().BeTrue();
    }

    [Fact]
    public void IsValid_MinOnly_AllowsHigher()
    {
        var rule = new RangeRule("Price", min: 0.01m);
        rule.IsValid(99999.99m, _context).Should().BeTrue();
    }

    [Fact]
    public void IsValid_MaxOnly_AllowsLower()
    {
        var rule = new RangeRule("Price", max: 100m);
        rule.IsValid(0.01m, _context).Should().BeTrue();
    }

    [Fact]
    public void IsValid_NonComparable_ReturnsFalse()
    {
        var rule = new RangeRule("Age", 0, 150);
        rule.IsValid(new object(), _context).Should().BeFalse();
    }

    [Fact]
    public void ErrorCode_IsOUT_OF_RANGE()
    {
        var rule = new RangeRule("Age", 0, 150);
        rule.ErrorCode.Should().Be("OUT_OF_RANGE");
    }

    [Fact]
    public void DefaultMessage_MinAndMax_ContainsBoth()
    {
        var rule = new RangeRule("Age", 0, 150);
        rule.ErrorMessage.Should().Contain("0").And.Contain("150");
    }
}
