using Birko.Validation;
using Birko.Validation.Rules;
using FluentAssertions;
using System;
using System.Collections.Generic;
using Xunit;

namespace Birko.Validation.Tests.Rules;

public class RequiredRuleTests
{
    private readonly RequiredRule _rule = new("Name");
    private readonly ValidationContext _context = new(new object());

    #region Null

    [Fact]
    public void IsValid_Null_ReturnsFalse()
    {
        _rule.IsValid(null, _context).Should().BeFalse();
    }

    #endregion

    #region Strings

    [Fact]
    public void IsValid_EmptyString_ReturnsFalse()
    {
        _rule.IsValid(string.Empty, _context).Should().BeFalse();
    }

    [Fact]
    public void IsValid_WhitespaceString_ReturnsFalse()
    {
        _rule.IsValid("   ", _context).Should().BeFalse();
    }

    [Fact]
    public void IsValid_NonEmptyString_ReturnsTrue()
    {
        _rule.IsValid("hello", _context).Should().BeTrue();
    }

    #endregion

    #region Collections

    [Fact]
    public void IsValid_EmptyCollection_ReturnsFalse()
    {
        _rule.IsValid(new List<int>(), _context).Should().BeFalse();
    }

    [Fact]
    public void IsValid_NonEmptyCollection_ReturnsTrue()
    {
        _rule.IsValid(new List<int> { 1 }, _context).Should().BeTrue();
    }

    #endregion

    #region Guid

    [Fact]
    public void IsValid_EmptyGuid_ReturnsFalse()
    {
        _rule.IsValid(Guid.Empty, _context).Should().BeFalse();
    }

    [Fact]
    public void IsValid_ValidGuid_ReturnsTrue()
    {
        _rule.IsValid(Guid.NewGuid(), _context).Should().BeTrue();
    }

    #endregion

    #region Other types

    [Fact]
    public void IsValid_NonNullObject_ReturnsTrue()
    {
        _rule.IsValid(42, _context).Should().BeTrue();
    }

    #endregion

    #region Properties

    [Fact]
    public void ErrorCode_IsREQUIRED()
    {
        _rule.ErrorCode.Should().Be("REQUIRED");
    }

    [Fact]
    public void DefaultMessage_ContainsPropertyName()
    {
        _rule.ErrorMessage.Should().Contain("Name");
    }

    [Fact]
    public void CustomMessage_IsUsed()
    {
        var rule = new RequiredRule("Name", "Custom error");
        rule.ErrorMessage.Should().Be("Custom error");
    }

    #endregion
}
