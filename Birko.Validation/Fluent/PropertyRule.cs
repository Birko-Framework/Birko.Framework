using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Birko.Validation.Fluent;

/// <summary>
/// Holds the rules for a single property of a model.
/// </summary>
public class PropertyRule<T>
{
    private readonly Func<T, object?> _valueAccessor;
    private readonly List<IValidationRule> _rules = [];

    public string PropertyName { get; }
    public IReadOnlyList<IValidationRule> Rules => _rules;

    public PropertyRule(Expression<Func<T, object?>> expression)
    {
        PropertyName = GetPropertyName(expression);
        _valueAccessor = expression.Compile();
    }

    public void AddRule(IValidationRule rule) => _rules.Add(rule);

    public object? GetValue(T instance) => _valueAccessor(instance);

    public void Validate(T instance, ValidationContext context, ValidationResult result)
    {
        var value = _valueAccessor(instance);
        context.PropertyName = PropertyName;

        foreach (var rule in _rules)
        {
            if (!rule.IsValid(value, context))
            {
                result.AddError(PropertyName, rule.ErrorCode, rule.ErrorMessage);
            }
        }
    }

    private static string GetPropertyName(Expression<Func<T, object?>> expression)
    {
        var body = expression.Body;

        // Unwrap Convert (boxing for value types)
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } unary)
            body = unary.Operand;

        if (body is MemberExpression member)
            return member.Member.Name;

        throw new ArgumentException($"Expression must be a simple property access, got: {expression}", nameof(expression));
    }
}
