using System;
using System.Text.RegularExpressions;
using Birko.Validation.Rules;

namespace Birko.Validation.Fluent;

/// <summary>
/// Fluent builder for adding validation rules to a property.
/// </summary>
public class RuleBuilder<T, TProp>
{
    private readonly PropertyRule<T> _propertyRule;

    internal RuleBuilder(PropertyRule<T> propertyRule)
    {
        _propertyRule = propertyRule;
    }

    public string PropertyName => _propertyRule.PropertyName;

    /// <summary>
    /// Value must not be null, empty, or whitespace.
    /// </summary>
    public RuleBuilder<T, TProp> Required(string? message = null)
    {
        _propertyRule.AddRule(new RequiredRule(_propertyRule.PropertyName, message));
        return this;
    }

    /// <summary>
    /// String value must be a valid email format.
    /// </summary>
    public RuleBuilder<T, TProp> Email(string? message = null)
    {
        _propertyRule.AddRule(new EmailRule(_propertyRule.PropertyName, message));
        return this;
    }

    /// <summary>
    /// String length must be at most <paramref name="max"/> characters.
    /// </summary>
    public RuleBuilder<T, TProp> MaxLength(int max, string? message = null)
    {
        _propertyRule.AddRule(new LengthRule(_propertyRule.PropertyName, maxLength: max, errorMessage: message));
        return this;
    }

    /// <summary>
    /// String length must be at least <paramref name="min"/> characters.
    /// </summary>
    public RuleBuilder<T, TProp> MinLength(int min, string? message = null)
    {
        _propertyRule.AddRule(new LengthRule(_propertyRule.PropertyName, minLength: min, errorMessage: message));
        return this;
    }

    /// <summary>
    /// String length must be between <paramref name="min"/> and <paramref name="max"/>.
    /// </summary>
    public RuleBuilder<T, TProp> Length(int min, int max, string? message = null)
    {
        _propertyRule.AddRule(new LengthRule(_propertyRule.PropertyName, min, max, message));
        return this;
    }

    /// <summary>
    /// Comparable value must be within the specified range.
    /// </summary>
    public RuleBuilder<T, TProp> Range(IComparable min, IComparable max, string? message = null)
    {
        _propertyRule.AddRule(new RangeRule(_propertyRule.PropertyName, min, max, message));
        return this;
    }

    /// <summary>
    /// Comparable value must be at least <paramref name="min"/>.
    /// </summary>
    public RuleBuilder<T, TProp> GreaterThanOrEqual(IComparable min, string? message = null)
    {
        _propertyRule.AddRule(new RangeRule(_propertyRule.PropertyName, min: min, errorMessage: message));
        return this;
    }

    /// <summary>
    /// Comparable value must be at most <paramref name="max"/>.
    /// </summary>
    public RuleBuilder<T, TProp> LessThanOrEqual(IComparable max, string? message = null)
    {
        _propertyRule.AddRule(new RangeRule(_propertyRule.PropertyName, max: max, errorMessage: message));
        return this;
    }

    /// <summary>
    /// String value must match the regex pattern.
    /// </summary>
    public RuleBuilder<T, TProp> Matches(string pattern, string? message = null)
    {
        _propertyRule.AddRule(new RegexRule(_propertyRule.PropertyName, pattern, errorMessage: message));
        return this;
    }

    /// <summary>
    /// String value must match the compiled regex.
    /// </summary>
    public RuleBuilder<T, TProp> Matches(Regex regex, string? message = null)
    {
        _propertyRule.AddRule(new RegexRule(_propertyRule.PropertyName, regex, message));
        return this;
    }

    /// <summary>
    /// Value must satisfy the predicate (receives property value).
    /// <para>
    /// CR-L391: for a reference-type <typeparamref name="TProp"/>, a null value is treated as valid and the
    /// predicate is NOT invoked — null-rejection is <see cref="Required"/>'s job, matching the other rules'
    /// convention. If your predicate is meant to reject null, add a <see cref="Required"/> rule instead of
    /// relying on Must.
    /// </para>
    /// </summary>
    public RuleBuilder<T, TProp> Must(Func<TProp, bool> predicate, string? message = null, string? errorCode = null)
    {
        _propertyRule.AddRule(new CustomRule(
            _propertyRule.PropertyName,
            (value, _) => value is TProp typed ? predicate(typed) : value is null && default(TProp) is null,
            errorCode,
            message));
        return this;
    }

    /// <summary>
    /// Validates using the full model instance (for cross-property validation).
    /// </summary>
    public RuleBuilder<T, TProp> MustSatisfy(Func<T, bool> predicate, string? message = null, string? errorCode = null)
    {
        _propertyRule.AddRule(new CustomRule<T>(_propertyRule.PropertyName, predicate, errorCode, message));
        return this;
    }

    /// <summary>
    /// Value must be one of the specified allowed values.
    /// </summary>
    public RuleBuilder<T, TProp> In(params TProp[] allowedValues)
    {
        // CR-L389: an empty set would make the property unsatisfiable (every value fails NOT_IN_SET with an
        // empty allowed-values list) — a silent misconfiguration trap. Reject it up front instead.
        if (allowedValues == null || allowedValues.Length == 0)
        {
            throw new ArgumentException("At least one allowed value must be provided.", nameof(allowedValues));
        }

        var set = new System.Collections.Generic.HashSet<TProp>(allowedValues);
        _propertyRule.AddRule(new CustomRule(
            _propertyRule.PropertyName,
            (value, _) => value is TProp typed && set.Contains(typed),
            "NOT_IN_SET",
            $"'{_propertyRule.PropertyName}' must be one of: {string.Join(", ", allowedValues)}."));
        return this;
    }

    /// <summary>
    /// Value must not equal the specified value.
    /// </summary>
    public RuleBuilder<T, TProp> NotEqual(TProp value, string? message = null)
    {
        _propertyRule.AddRule(new CustomRule(
            _propertyRule.PropertyName,
            (v, _) => !Equals(v, value),
            "NOT_EQUAL",
            message ?? $"'{_propertyRule.PropertyName}' must not equal '{value}'."));
        return this;
    }
}
