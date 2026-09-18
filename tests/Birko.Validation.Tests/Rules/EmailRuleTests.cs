using Birko.Validation;
using Birko.Validation.Rules;
using FluentAssertions;
using Xunit;

namespace Birko.Validation.Tests.Rules;

public class EmailRuleTests
{
    private readonly EmailRule _rule = new("Email");
    private readonly ValidationContext _context = new(new object());

    #region Passthrough (null/empty)

    [Fact]
    public void IsValid_Null_ReturnsTrue()
    {
        _rule.IsValid(null, _context).Should().BeTrue();
    }

    [Fact]
    public void IsValid_EmptyString_ReturnsTrue()
    {
        _rule.IsValid(string.Empty, _context).Should().BeTrue();
    }

    [Fact]
    public void IsValid_WhitespaceString_ReturnsTrue()
    {
        _rule.IsValid("   ", _context).Should().BeTrue();
    }

    #endregion

    #region Valid emails

    [Fact]
    public void IsValid_SimpleEmail_ReturnsTrue()
    {
        _rule.IsValid("user@example.com", _context).Should().BeTrue();
    }

    [Fact]
    public void IsValid_EmailWithSubdomain_ReturnsTrue()
    {
        _rule.IsValid("user@sub.example.com", _context).Should().BeTrue();
    }

    #endregion

    #region Invalid formats

    [Fact]
    public void IsValid_MissingAt_ReturnsFalse()
    {
        _rule.IsValid("userexample.com", _context).Should().BeFalse();
    }

    [Fact]
    public void IsValid_MissingDomain_ReturnsFalse()
    {
        _rule.IsValid("user@", _context).Should().BeFalse();
    }

    [Fact]
    public void IsValid_MissingTld_ReturnsFalse()
    {
        _rule.IsValid("user@example", _context).Should().BeFalse();
    }

    [Fact]
    public void IsValid_NonStringType_ReturnsFalse()
    {
        _rule.IsValid(42, _context).Should().BeFalse();
    }

    #endregion

    #region Properties

    [Fact]
    public void ErrorCode_IsINVALID_EMAIL()
    {
        _rule.ErrorCode.Should().Be("INVALID_EMAIL");
    }

    #endregion
}
