using Birko.Validation;
using Birko.Validation.Rules;
using FluentAssertions;
using System;
using Xunit;

namespace Birko.Validation.Tests.Rules;

public class CustomRuleTests
{
    private readonly ValidationContext _context = new(new object());

    #region Untyped CustomRule

    [Fact]
    public void Constructor_NullPredicate_Throws()
    {
        var act = () => new CustomRule("Name", null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("predicate");
    }

    [Fact]
    public void IsValid_PredicateReturnsTrue_ReturnsTrue()
    {
        var rule = new CustomRule("Name", (_, _) => true);
        rule.IsValid("test", _context).Should().BeTrue();
    }

    [Fact]
    public void IsValid_PredicateReturnsFalse_ReturnsFalse()
    {
        var rule = new CustomRule("Name", (_, _) => false);
        rule.IsValid("test", _context).Should().BeFalse();
    }

    [Fact]
    public void IsValid_ReceivesValueAndContext()
    {
        object? receivedValue = null;
        ValidationContext? receivedContext = null;
        var rule = new CustomRule("Name", (v, c) => { receivedValue = v; receivedContext = c; return true; });

        rule.IsValid("hello", _context);

        receivedValue.Should().Be("hello");
        receivedContext.Should().BeSameAs(_context);
    }

    [Fact]
    public void DefaultErrorCode_IsCUSTOM_VALIDATION()
    {
        var rule = new CustomRule("Name", (_, _) => true);
        rule.ErrorCode.Should().Be("CUSTOM_VALIDATION");
    }

    [Fact]
    public void CustomErrorCode_IsUsed()
    {
        var rule = new CustomRule("Name", (_, _) => true, errorCode: "MY_CODE");
        rule.ErrorCode.Should().Be("MY_CODE");
    }

    #endregion

    #region Typed CustomRule<T>

    private class TestModel
    {
        public string Name { get; set; } = string.Empty;
        public int Value { get; set; }
    }

    [Fact]
    public void Typed_Constructor_NullPredicate_Throws()
    {
        var act = () => new CustomRule<TestModel>("Name", null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("predicate");
    }

    [Fact]
    public void Typed_IsValid_WhenContextHasTypedInstance_CallsPredicate()
    {
        var model = new TestModel { Name = "Test", Value = 5 };
        var context = new ValidationContext(model);
        var rule = new CustomRule<TestModel>("Value", m => m.Value > 0);

        rule.IsValid(null, context).Should().BeTrue();
    }

    [Fact]
    public void Typed_IsValid_PredicateReturnsFalse_ReturnsFalse()
    {
        var model = new TestModel { Name = "Test", Value = -1 };
        var context = new ValidationContext(model);
        var rule = new CustomRule<TestModel>("Value", m => m.Value > 0);

        rule.IsValid(null, context).Should().BeFalse();
    }

    [Fact]
    public void Typed_IsValid_WhenContextHasWrongType_ReturnsTrue()
    {
        var context = new ValidationContext("not a TestModel");
        var rule = new CustomRule<TestModel>("Value", m => m.Value > 0);

        rule.IsValid(null, context).Should().BeTrue();
    }

    #endregion
}
