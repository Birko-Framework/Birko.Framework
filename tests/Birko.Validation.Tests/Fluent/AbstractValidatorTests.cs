using Birko.Validation;
using Birko.Validation.Fluent;
using FluentAssertions;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Birko.Validation.Tests.Fluent;

public class AbstractValidatorTests
{
    #region Test Infrastructure

    private class TestModel
    {
        public string Name { get; set; } = string.Empty;
        public string? Email { get; set; }
        public int Age { get; set; }
        public string? Description { get; set; }
        public decimal Price { get; set; }
        public string? Status { get; set; }
        public string? Code { get; set; }
    }

    private class BasicValidator : AbstractValidator<TestModel>
    {
        public BasicValidator()
        {
            RuleFor(x => x.Name).Required().MaxLength(50);
            RuleFor(x => x.Email).Email();
            RuleFor(x => x.Age).Range(0, 150);
        }
    }

    #endregion

    #region Basic Validation

    [Fact]
    public void Validate_ValidModel_ReturnsSuccess()
    {
        var validator = new BasicValidator();
        var model = new TestModel { Name = "Test", Email = "test@example.com", Age = 25 };

        var result = validator.Validate(model);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_NullName_ReturnsError()
    {
        var validator = new BasicValidator();
        var model = new TestModel { Name = "", Age = 25 };

        var result = validator.Validate(model);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name" && e.ErrorCode == "REQUIRED");
    }

    [Fact]
    public void Validate_NameTooLong_ReturnsError()
    {
        var validator = new BasicValidator();
        var model = new TestModel { Name = new string('x', 51), Age = 25 };

        var result = validator.Validate(model);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Name" && e.ErrorCode == "INVALID_LENGTH");
    }

    [Fact]
    public void Validate_InvalidEmail_ReturnsError()
    {
        var validator = new BasicValidator();
        var model = new TestModel { Name = "Test", Email = "not-an-email", Age = 25 };

        var result = validator.Validate(model);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Email");
    }

    [Fact]
    public void Validate_AgeOutOfRange_ReturnsError()
    {
        var validator = new BasicValidator();
        var model = new TestModel { Name = "Test", Age = -5 };

        var result = validator.Validate(model);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Age" && e.ErrorCode == "OUT_OF_RANGE");
    }

    [Fact]
    public void Validate_MultipleViolations_ReturnsAllErrors()
    {
        var validator = new BasicValidator();
        var model = new TestModel { Name = "", Email = "bad", Age = 200 };

        var result = validator.Validate(model);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterOrEqualTo(3);
    }

    [Fact]
    public void Validate_NullInstance_ThrowsArgumentNull()
    {
        var validator = new BasicValidator();
        var act = () => validator.Validate(null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("instance");
    }

    [Fact]
    public async Task ValidateAsync_ValidModel_ReturnsSuccess()
    {
        var validator = new BasicValidator();
        var model = new TestModel { Name = "Test", Email = "a@b.com", Age = 30 };

        var result = await validator.ValidateAsync(model);

        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region RuleBuilder Methods

    [Fact]
    public void RuleFor_MinLength_RejectsShort()
    {
        var validator = new MinLengthValidator();
        var model = new TestModel { Name = "A" };

        var result = validator.Validate(model);

        result.IsValid.Should().BeFalse();
    }

    private class MinLengthValidator : AbstractValidator<TestModel>
    {
        public MinLengthValidator() { RuleFor(x => x.Name).MinLength(3); }
    }

    [Fact]
    public void RuleFor_Length_RejectsOutOfRange()
    {
        var validator = new LengthValidator();
        var model = new TestModel { Name = "A" };

        var result = validator.Validate(model);

        result.IsValid.Should().BeFalse();
    }

    private class LengthValidator : AbstractValidator<TestModel>
    {
        public LengthValidator() { RuleFor(x => x.Name).Length(2, 10); }
    }

    [Fact]
    public void RuleFor_GreaterThanOrEqual_RejectsBelowMin()
    {
        var validator = new GteValidator();
        var model = new TestModel { Price = -1m };

        var result = validator.Validate(model);

        result.IsValid.Should().BeFalse();
    }

    private class GteValidator : AbstractValidator<TestModel>
    {
        public GteValidator() { RuleFor(x => x.Price).GreaterThanOrEqual(0m); }
    }

    [Fact]
    public void RuleFor_LessThanOrEqual_RejectsAboveMax()
    {
        var validator = new LteValidator();
        var model = new TestModel { Price = 1001m };

        var result = validator.Validate(model);

        result.IsValid.Should().BeFalse();
    }

    private class LteValidator : AbstractValidator<TestModel>
    {
        public LteValidator() { RuleFor(x => x.Price).LessThanOrEqual(1000m); }
    }

    [Fact]
    public void RuleFor_Matches_RejectsNonMatching()
    {
        var validator = new MatchesValidator();
        var model = new TestModel { Code = "abc" };

        var result = validator.Validate(model);

        result.IsValid.Should().BeFalse();
    }

    private class MatchesValidator : AbstractValidator<TestModel>
    {
        public MatchesValidator() { RuleFor(x => x.Code).Matches(@"^[A-Z0-9]+$"); }
    }

    [Fact]
    public void RuleFor_Must_RejectsWhenPredicateFails()
    {
        var validator = new MustValidator();
        var model = new TestModel { Description = "short" };

        var result = validator.Validate(model);

        result.IsValid.Should().BeFalse();
    }

    private class MustValidator : AbstractValidator<TestModel>
    {
        public MustValidator()
        {
            RuleFor(x => x.Description).Must(d => d != null && d.Length >= 20, "Must be at least 20 chars.");
        }
    }

    [Fact]
    public void RuleFor_MustSatisfy_RejectsWithCrossPropertyCheck()
    {
        var validator = new MustSatisfyValidator();
        var model = new TestModel { Price = 600m, Description = null };

        var result = validator.Validate(model);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void RuleFor_MustSatisfy_PassesWhenSatisfied()
    {
        var validator = new MustSatisfyValidator();
        var model = new TestModel { Price = 600m, Description = "Has description" };

        var result = validator.Validate(model);

        result.IsValid.Should().BeTrue();
    }

    private class MustSatisfyValidator : AbstractValidator<TestModel>
    {
        public MustSatisfyValidator()
        {
            RuleFor(x => x.Description)
                .MustSatisfy(m => m.Price < 500m || !string.IsNullOrEmpty(m.Description), "Expensive items need description.");
        }
    }

    [Fact]
    public void RuleFor_In_RejectsValueNotInSet()
    {
        var validator = new InValidator();
        var model = new TestModel { Status = "Unknown" };

        var result = validator.Validate(model);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void RuleFor_In_AcceptsValueInSet()
    {
        var validator = new InValidator();
        var model = new TestModel { Status = "Active" };

        var result = validator.Validate(model);

        result.IsValid.Should().BeTrue();
    }

    private class InValidator : AbstractValidator<TestModel>
    {
        public InValidator() { RuleFor(x => x.Status).In("Active", "Inactive", "Pending"); }
    }

    [Fact]
    public void RuleFor_NotEqual_RejectsEqualValue()
    {
        var validator = new NotEqualValidator();
        var model = new TestModel { Status = "NONE" };

        var result = validator.Validate(model);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void RuleFor_NotEqual_AcceptsDifferentValue()
    {
        var validator = new NotEqualValidator();
        var model = new TestModel { Status = "Active" };

        var result = validator.Validate(model);

        result.IsValid.Should().BeTrue();
    }

    private class NotEqualValidator : AbstractValidator<TestModel>
    {
        public NotEqualValidator() { RuleFor(x => x.Status).NotEqual("NONE"); }
    }

    [Fact]
    public void RuleFor_In_EmptyAllowedValues_Throws()
    {
        // CR-L389: In() with no allowed values would make the property unsatisfiable; it must reject up front.
        var act = () => new EmptyInValidator();

        act.Should().Throw<ArgumentException>().WithParameterName("allowedValues");
    }

    private class EmptyInValidator : AbstractValidator<TestModel>
    {
        public EmptyInValidator() { RuleFor(x => x.Status).In(); }
    }

    [Fact]
    public void RuleFor_Must_NullReferenceValue_TreatedAsValid_PredicateNotInvoked()
    {
        // CR-L391: for a reference-type property, Must treats null as valid and never invokes the predicate
        // (null-rejection is Required's job). The predicate here would throw on null if it were called.
        var validator = new MustNeverNullDerefValidator();
        var model = new TestModel { Description = null };

        var result = validator.Validate(model);

        result.IsValid.Should().BeTrue();
    }

    private class MustNeverNullDerefValidator : AbstractValidator<TestModel>
    {
        public MustNeverNullDerefValidator()
        {
            // No null-guard in the predicate: d! would NRE at runtime if invoked with a null Description,
            // proving Must skips the predicate for null (the ! only silences the compile-time warning).
            RuleFor(x => x.Description).Must(d => d!.Length > 3, "Too short.");
        }
    }

    #endregion
}
