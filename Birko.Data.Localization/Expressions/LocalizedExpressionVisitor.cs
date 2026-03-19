using Birko.Data.Localization.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Birko.Data.Localization.Expressions;

/// <summary>
/// Result of splitting an expression into localized and non-localized parts.
/// </summary>
public class LocalizedFilterSplit<T> where T : Data.Models.AbstractModel, ILocalizable
{
    /// <summary>
    /// The remaining filter with localized field conditions removed.
    /// Null if the entire expression was localized.
    /// </summary>
    public Expression<Func<T, bool>>? RemainingFilter { get; init; }

    /// <summary>
    /// Extracted conditions on localized fields: FieldName → list of conditions on the translation Value.
    /// Each condition is an expression over <c>string</c> (the translation value).
    /// </summary>
    public IReadOnlyList<LocalizedFieldCondition> LocalizedConditions { get; init; } = Array.Empty<LocalizedFieldCondition>();

    /// <summary>
    /// Whether the original expression contained any conditions on localized fields.
    /// </summary>
    public bool HasLocalizedConditions => LocalizedConditions.Count > 0;
}

/// <summary>
/// A single condition extracted from a filter that targets a localized field.
/// </summary>
public class LocalizedFieldCondition
{
    /// <summary>The property name on the entity (e.g., "Name", "Description").</summary>
    public string FieldName { get; init; } = string.Empty;

    /// <summary>
    /// A predicate that tests a translation value string.
    /// Built from the original expression (e.g., x.Name == "Foo" → value == "Foo").
    /// </summary>
    public Func<string, bool> ValuePredicate { get; init; } = _ => true;
}

/// <summary>
/// Analyzes an expression tree to split conditions on localizable fields from non-localizable conditions.
/// Supports: ==, !=, Contains, StartsWith, EndsWith on localizable string properties.
/// Combined with &amp;&amp; (AndAlso) at the top level.
/// </summary>
public static class LocalizedExpressionAnalyzer
{
    /// <summary>
    /// Splits a filter expression into localized and non-localized parts.
    /// </summary>
    /// <param name="filter">The original filter expression.</param>
    /// <param name="localizableFields">The set of property names that are localizable.</param>
    /// <returns>A split result with remaining filter and extracted localized conditions.</returns>
    public static LocalizedFilterSplit<T> Split<T>(
        Expression<Func<T, bool>>? filter,
        IReadOnlyList<string> localizableFields)
        where T : Data.Models.AbstractModel, ILocalizable
    {
        if (filter == null || localizableFields.Count == 0)
        {
            return new LocalizedFilterSplit<T> { RemainingFilter = filter };
        }

        var fieldSet = new HashSet<string>(localizableFields);
        var param = filter.Parameters[0];
        var localizedConditions = new List<LocalizedFieldCondition>();
        var remainingParts = new List<Expression>();

        // Flatten top-level AndAlso chain
        var parts = FlattenAndAlso(filter.Body);

        foreach (var part in parts)
        {
            var extracted = TryExtractLocalizedCondition(part, param, fieldSet);
            if (extracted != null)
            {
                localizedConditions.Add(extracted);
            }
            else
            {
                remainingParts.Add(part);
            }
        }

        Expression<Func<T, bool>>? remaining = null;
        if (remainingParts.Count > 0)
        {
            var combined = remainingParts.Aggregate(Expression.AndAlso);
            remaining = Expression.Lambda<Func<T, bool>>(combined, param);
        }

        return new LocalizedFilterSplit<T>
        {
            RemainingFilter = remaining,
            LocalizedConditions = localizedConditions
        };
    }

    /// <summary>
    /// Flattens a chain of AndAlso expressions into individual parts.
    /// </summary>
    private static List<Expression> FlattenAndAlso(Expression expression)
    {
        var parts = new List<Expression>();
        FlattenAndAlsoRecursive(expression, parts);
        return parts;
    }

    private static void FlattenAndAlsoRecursive(Expression expression, List<Expression> parts)
    {
        if (expression is BinaryExpression binary && binary.NodeType == ExpressionType.AndAlso)
        {
            FlattenAndAlsoRecursive(binary.Left, parts);
            FlattenAndAlsoRecursive(binary.Right, parts);
        }
        else
        {
            parts.Add(expression);
        }
    }

    /// <summary>
    /// Tries to extract a localized field condition from an expression node.
    /// Returns null if the node doesn't reference a localizable field.
    /// </summary>
    private static LocalizedFieldCondition? TryExtractLocalizedCondition(
        Expression node, ParameterExpression param, HashSet<string> localizableFields)
    {
        // Binary: x.Name == "value", x.Name != "value"
        if (node is BinaryExpression binary)
        {
            var (fieldName, constantValue) = ExtractFieldAndConstant(binary.Left, binary.Right, param, localizableFields);
            if (fieldName == null)
            {
                (fieldName, constantValue) = ExtractFieldAndConstant(binary.Right, binary.Left, param, localizableFields);
            }

            if (fieldName != null && constantValue != null)
            {
                Func<string, bool> predicate = binary.NodeType switch
                {
                    ExpressionType.Equal => v => v == constantValue,
                    ExpressionType.NotEqual => v => v != constantValue,
                    _ => null!
                };

                if (predicate != null)
                {
                    return new LocalizedFieldCondition { FieldName = fieldName, ValuePredicate = predicate };
                }
            }
        }

        // Method call: x.Name.Contains("value"), x.Name.StartsWith("value"), x.Name.EndsWith("value")
        if (node is MethodCallExpression methodCall && methodCall.Object != null)
        {
            var fieldName = ExtractFieldName(methodCall.Object, param, localizableFields);
            if (fieldName != null && methodCall.Arguments.Count >= 1)
            {
                var argValue = EvaluateExpression(methodCall.Arguments[0]);
                if (argValue is string strArg)
                {
                    Func<string, bool>? predicate = methodCall.Method.Name switch
                    {
                        "Contains" => v => v != null && v.Contains(strArg),
                        "StartsWith" => v => v != null && v.StartsWith(strArg),
                        "EndsWith" => v => v != null && v.EndsWith(strArg),
                        _ => null
                    };

                    if (predicate != null)
                    {
                        return new LocalizedFieldCondition { FieldName = fieldName, ValuePredicate = predicate };
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Tries to extract a (fieldName, constantValue) pair from left/right operands.
    /// </summary>
    private static (string? fieldName, string? constantValue) ExtractFieldAndConstant(
        Expression candidate, Expression other, ParameterExpression param, HashSet<string> localizableFields)
    {
        var fieldName = ExtractFieldName(candidate, param, localizableFields);
        if (fieldName == null)
        {
            return (null, null);
        }

        var value = EvaluateExpression(other);
        return (fieldName, value as string);
    }

    /// <summary>
    /// Extracts the property name if the expression is a member access on a localizable field.
    /// </summary>
    private static string? ExtractFieldName(
        Expression expression, ParameterExpression param, HashSet<string> localizableFields)
    {
        if (expression is MemberExpression member &&
            member.Expression == param &&
            member.Member is PropertyInfo prop &&
            prop.PropertyType == typeof(string) &&
            localizableFields.Contains(prop.Name))
        {
            return prop.Name;
        }
        return null;
    }

    /// <summary>
    /// Evaluates a constant or captured-variable expression to get its runtime value.
    /// </summary>
    private static object? EvaluateExpression(Expression expression)
    {
        if (expression is ConstantExpression constant)
        {
            return constant.Value;
        }

        // Handle captured variables (closures): field access on a constant (compiler-generated class)
        if (expression is MemberExpression memberExpr && memberExpr.Expression is ConstantExpression outerConstant)
        {
            if (memberExpr.Member is FieldInfo field)
            {
                return field.GetValue(outerConstant.Value);
            }
            if (memberExpr.Member is PropertyInfo prop)
            {
                return prop.GetValue(outerConstant.Value);
            }
        }

        // Fallback: compile and invoke
        try
        {
            var lambda = Expression.Lambda(expression);
            var compiled = lambda.Compile();
            return compiled.DynamicInvoke();
        }
        catch
        {
            return null;
        }
    }
}
