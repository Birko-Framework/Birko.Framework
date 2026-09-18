namespace Birko.Validation;

/// <summary>
/// A single validation rule that checks a condition on a property value.
/// </summary>
public interface IValidationRule
{
    string PropertyName { get; }
    string ErrorCode { get; }
    string ErrorMessage { get; }
    bool IsValid(object? value, ValidationContext context);
}
