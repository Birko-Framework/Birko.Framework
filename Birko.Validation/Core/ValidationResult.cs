using System.Collections.Generic;
using System.Linq;

namespace Birko.Validation;

public record ValidationError(string PropertyName, string ErrorCode, string Message);

public class ValidationResult
{
    private readonly List<ValidationError> _errors = [];

    public bool IsValid => _errors.Count == 0;
    public IReadOnlyList<ValidationError> Errors => _errors;

    public void AddError(string propertyName, string errorCode, string message)
    {
        _errors.Add(new ValidationError(propertyName, errorCode, message));
    }

    public void AddError(ValidationError error)
    {
        _errors.Add(error);
    }

    public void Merge(ValidationResult other)
    {
        _errors.AddRange(other._errors);
    }

    public IDictionary<string, string[]> ToDictionary()
    {
        return _errors
            .GroupBy(e => e.PropertyName)
            .ToDictionary(g => g.Key, g => g.Select(e => e.Message).ToArray());
    }

    public static ValidationResult Success() => new();

    public static ValidationResult Failure(string propertyName, string errorCode, string message)
    {
        var result = new ValidationResult();
        result.AddError(propertyName, errorCode, message);
        return result;
    }

    public static ValidationResult Failure(params ValidationError[] errors)
    {
        var result = new ValidationResult();
        foreach (var error in errors)
            result.AddError(error);
        return result;
    }
}
