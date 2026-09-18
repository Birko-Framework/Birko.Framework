using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Birko.Validation.Fluent;

/// <summary>
/// Base class for building fluent validators.
/// Define rules in the constructor using RuleFor().
/// </summary>
/// <example>
/// public class DeviceValidator : AbstractValidator&lt;Device&gt;
/// {
///     public DeviceValidator()
///     {
///         RuleFor(x => x.Name).Required().MaxLength(100);
///         RuleFor(x => x.SerialNumber).Required().Matches(@"^[A-Z0-9-]+$");
///         RuleFor(x => x.Temperature).Range(-50, 150);
///     }
/// }
/// </example>
public abstract class AbstractValidator<T> : IValidator<T>
{
    private readonly List<PropertyRule<T>> _propertyRules = [];

    /// <summary>
    /// Starts a rule chain for the specified property.
    /// </summary>
    protected RuleBuilder<T, TProp> RuleFor<TProp>(Expression<Func<T, TProp>> expression)
    {
        // Wrap expression to return object? for PropertyRule
        var converted = Expression.Lambda<Func<T, object?>>(
            Expression.Convert(expression.Body, typeof(object)),
            expression.Parameters);

        var propertyRule = new PropertyRule<T>(converted);
        _propertyRules.Add(propertyRule);
        return new RuleBuilder<T, TProp>(propertyRule);
    }

    public ValidationResult Validate(T instance)
    {
        if (instance is null)
            throw new ArgumentNullException(nameof(instance));

        var result = new ValidationResult();
        var context = new ValidationContext<T>(instance);

        foreach (var propertyRule in _propertyRules)
        {
            propertyRule.Validate(instance, context, result);
        }

        return result;
    }

    public Task<ValidationResult> ValidateAsync(T instance, CancellationToken ct = default)
    {
        return Task.FromResult(Validate(instance));
    }
}
