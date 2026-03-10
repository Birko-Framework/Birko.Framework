using System.Threading;
using System.Threading.Tasks;

namespace Birko.Validation;

/// <summary>
/// Validates instances of <typeparamref name="T"/>.
/// </summary>
public interface IValidator<in T>
{
    ValidationResult Validate(T instance);
    Task<ValidationResult> ValidateAsync(T instance, CancellationToken ct = default);
}
