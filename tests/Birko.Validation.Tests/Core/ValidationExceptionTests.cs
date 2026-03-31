using Birko.Validation;
using FluentAssertions;
using Xunit;

namespace Birko.Validation.Tests.Core;

public class ValidationExceptionTests
{
    [Fact]
    public void Constructor_SetsValidationResult()
    {
        var result = ValidationResult.Failure("Name", "REQUIRED", "Required");
        var ex = new ValidationException(result);

        ex.ValidationResult.Should().BeSameAs(result);
    }

    [Fact]
    public void Message_ContainsErrorCount()
    {
        var result = new ValidationResult();
        result.AddError("A", "E1", "M1");
        result.AddError("B", "E2", "M2");
        var ex = new ValidationException(result);

        ex.Message.Should().Contain("2 error(s)");
    }
}
