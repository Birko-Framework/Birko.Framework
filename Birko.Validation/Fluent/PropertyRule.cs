using System.Reflection;
using System.Linq;
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
        _valueAccessor = BuildAccessor(expression);
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
        // SH-M372 (TASK-156): the whole path, so RuleFor(x => x.Wyrm.MaxParallel) and RuleFor(x => x.Wyvern.MaxParallel)
        // report under different keys; both reported as "MaxParallel" before.
        var chain = MemberChain(expression);
        if (chain is not null)
            return string.Join(".", chain.Select(m => m.Name));
        throw new ArgumentException($"Expression must be a simple property access, got: {expression}", nameof(expression));
    }

    /// <summary>
    /// The members from the parameter to the property (<c>x.Address.City</c>: Address, City), or null for any other shape.
    /// </summary>
    private static List<MemberInfo>? MemberChain(Expression<Func<T, object?>> expression)
    {
        var body = expression.Body;
        // Unwrap Convert (boxing for value types)
        if (body is UnaryExpression { NodeType: ExpressionType.Convert } unary)
            body = unary.Operand;

        var chain = new List<MemberInfo>();
        while (body is MemberExpression member)
        {
            chain.Insert(0, member.Member);
            body = member.Expression!;
        }
        return chain.Count > 0 && body is ParameterExpression ? chain : null;
    }

    /// <summary>
    /// Reads the property, answering null when an intermediate of a nested path is null.
    /// </summary>
    /// <remarks>
    /// SH-M372 (TASK-156): the compiled accessor dereferenced the null intermediate, so RuleFor(x => x.Address.City)
    /// threw NullReferenceException out of Validate whenever Address was null, aborting the whole result. A null value is
    /// what the other rules skip and <c>Required</c> reports.
    /// </remarks>
    private static Func<T, object?> BuildAccessor(Expression<Func<T, object?>> expression)
    {
        var chain = MemberChain(expression);
        if (chain is null || chain.Count == 1)
            return expression.Compile();

        return instance =>
        {
            object? current = instance;
            foreach (var member in chain)
            {
                if (current is null)
                    return null;
                current = member switch
                {
                    PropertyInfo property => property.GetValue(current),
                    FieldInfo field => field.GetValue(current),
                    _ => throw new NotSupportedException($"Cannot read {member.Name}."),
                };
            }
            return current;
        };
    }
}
