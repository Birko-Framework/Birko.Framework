using Birko.Validation;
using FluentAssertions;
using Xunit;

namespace Birko.Validation.Tests.Core;

public class ValidationResultTests
{
    [Fact]
    public void Success_IsValid_ReturnsTrue()
    {
        var result = ValidationResult.Success();
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Success_HasNoErrors()
    {
        var result = ValidationResult.Success();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Failure_WithStringParams_IsNotValid()
    {
        var result = ValidationResult.Failure("Name", "REQUIRED", "Name is required.");
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Failure_WithStringParams_ContainsError()
    {
        var result = ValidationResult.Failure("Name", "REQUIRED", "Name is required.");
        result.Errors.Should().ContainSingle()
            .Which.Should().Be(new ValidationError("Name", "REQUIRED", "Name is required."));
    }

    [Fact]
    public void Failure_WithErrors_ContainsAllErrors()
    {
        var result = ValidationResult.Failure(
            new ValidationError("A", "E1", "M1"),
            new ValidationError("B", "E2", "M2"));

        result.Errors.Should().HaveCount(2);
    }

    [Fact]
    public void AddError_ByStringParams_AddsError()
    {
        var result = new ValidationResult();
        result.AddError("Name", "REQUIRED", "Required");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle();
    }

    [Fact]
    public void AddError_ByRecord_AddsError()
    {
        var result = new ValidationResult();
        result.AddError(new ValidationError("Name", "REQUIRED", "Required"));

        result.Errors.Should().ContainSingle();
    }

    [Fact]
    public void Merge_CombinesErrors()
    {
        var result1 = ValidationResult.Failure("A", "E1", "M1");
        var result2 = ValidationResult.Failure("B", "E2", "M2");

        result1.Merge(result2);

        result1.Errors.Should().HaveCount(2);
    }

    [Fact]
    public void Merge_EmptyResult_NoChange()
    {
        var result = ValidationResult.Failure("A", "E1", "M1");
        result.Merge(ValidationResult.Success());

        result.Errors.Should().ContainSingle();
    }

    [Fact]
    public void ToDictionary_GroupsByPropertyName()
    {
        var result = new ValidationResult();
        result.AddError("Name", "REQUIRED", "Name is required.");
        result.AddError("Name", "INVALID_LENGTH", "Name too short.");
        result.AddError("Email", "INVALID_EMAIL", "Invalid email.");

        var dict = result.ToDictionary();

        dict.Should().ContainKey("Name").WhoseValue.Should().HaveCount(2);
        dict.Should().ContainKey("Email").WhoseValue.Should().ContainSingle();
    }
}
