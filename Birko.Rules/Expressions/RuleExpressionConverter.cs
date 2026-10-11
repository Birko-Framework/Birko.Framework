using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Birko.Rules;

/// <summary>
/// Converts Birko.Rules rule trees into LINQ Expression&lt;Func&lt;T, bool&gt;&gt; predicates.
/// Works with any store that accepts LINQ expressions (SQL, Elasticsearch, MongoDB, JSON, etc.).
/// <para>
/// <b>FIELD-025 (TASK-558).</b> A rule this converter cannot turn into a predicate MATCHES NOTHING — the answer
/// <c>RuleSpecification</c> and <c>RuleEvaluator</c> give for the same tree. That covers a field that resolves to no
/// property, a disabled rule, an empty or all-disabled group and a disabled <see cref="RuleSet"/>. Such a child used
/// to be dropped, so <c>AND(Status == "Open", NonExistent == "x")</c> became <c>Status == "Open"</c> — wider than
/// the rule — and at the root the converter returned <c>null</c>, which a caller reads as "no filter".
/// The entry points therefore never return <c>null</c>: nothing-to-match is <c>x =&gt; false</c>.
/// </para>
/// <para>
/// Negation is pushed down to the leaves by De Morgan (as <c>RuleSpecification</c> does since TASK-320), never
/// wrapped around a combined group: match-nothing is a fact about the rule, not a predicate, so an enclosing
/// negated group must not invert it into match-all. Disabled members of an enabled group or set are skipped.
/// </para>
/// </summary>
public static class RuleExpressionConverter
{
    /// <summary>
    /// Converts a single rule (leaf or group) into a LINQ predicate expression. Never null: a disabled rule, an
    /// empty group or an unresolved field yields <c>x =&gt; false</c>.
    /// </summary>
    public static Expression<Func<T, bool>> ToExpression<T>(IRule rule) where T : class
    {
        var param = Expression.Parameter(typeof(T), "x");
        return Lambda<T>(BuildExpression(rule, param, typeof(T), negate: false), param);
    }

    /// <summary>
    /// Converts all enabled rules in a RuleSet into a single AND-combined predicate. Never null: a disabled set,
    /// a set with no enabled rules, or one whose rules match nothing yields <c>x =&gt; false</c>.
    /// </summary>
    public static Expression<Func<T, bool>> ToExpression<T>(RuleSet ruleSet) where T : class
    {
        var param = Expression.Parameter(typeof(T), "x");
        if (!ruleSet.IsEnabled)
            return Lambda<T>(null, param);

        return Lambda<T>(BuildConjunction(ruleSet.Rules, param, typeof(T)), param);
    }

    /// <summary>
    /// Converts multiple rules into a single AND-combined predicate. Never null: no enabled rules, or a rule that
    /// matches nothing, yields <c>x =&gt; false</c>.
    /// </summary>
    public static Expression<Func<T, bool>> ToExpression<T>(IEnumerable<IRule> rules) where T : class
    {
        var param = Expression.Parameter(typeof(T), "x");
        return Lambda<T>(BuildConjunction(rules, param, typeof(T)), param);
    }

    private static Expression<Func<T, bool>> Lambda<T>(Expression? body, ParameterExpression param)
        => Expression.Lambda<Func<T, bool>>(body ?? Expression.Constant(false), param);

    /// <summary>An AND of the enabled rules; null (match-nothing) when there are none or any matches nothing.</summary>
    private static Expression? BuildConjunction(IEnumerable<IRule> rules, ParameterExpression param, Type entityType)
    {
        // Every enabled rule is built before any is discarded, so each one's own errors surface.
        var built = rules
            .Where(r => r.IsEnabled)
            .Select(r => BuildExpression(r, param, entityType, negate: false))
            .ToList();

        if (built.Count == 0 || built.Any(e => e is null))
            return null;

        return built.Aggregate((a, b) => Expression.AndAlso(a!, b!));
    }

    /// <param name="negate">Whether an enclosing negated group inverts this rule — pushed down to the leaves.</param>
    /// <returns>The predicate, or null when the rule matches nothing under any negation.</returns>
    private static Expression? BuildExpression(IRule rule, ParameterExpression param, Type entityType, bool negate)
    {
        if (!rule.IsEnabled)
            return null;

        return rule switch
        {
            Rule leaf => BuildLeafExpression(leaf, param, entityType, negate),
            RuleGroup group => BuildGroupExpression(group, param, entityType, negate),
            _ => null
        };
    }

    private static Expression? BuildLeafExpression(Rule rule, ParameterExpression param, Type entityType, bool negate)
    {
        var property = ResolveProperty(entityType, rule.Field);
        if (property is null)
            return null;

        var member = BuildMemberAccess(param, rule.Field);
        var propertyType = GetMemberType(member);

        Expression? body = rule.Operator switch
        {
            ComparisonOperator.IsNull => BuildIsNull(member, propertyType),
            ComparisonOperator.IsNotNull => Expression.Not(BuildIsNull(member, propertyType)),
            ComparisonOperator.Equal => BuildComparison(member, propertyType, rule.Value, ExpressionType.Equal),
            ComparisonOperator.NotEqual => BuildComparison(member, propertyType, rule.Value, ExpressionType.NotEqual),
            ComparisonOperator.GreaterThan => BuildComparison(member, propertyType, rule.Value, ExpressionType.GreaterThan),
            ComparisonOperator.GreaterThanOrEqual => BuildComparison(member, propertyType, rule.Value, ExpressionType.GreaterThanOrEqual),
            ComparisonOperator.LessThan => BuildComparison(member, propertyType, rule.Value, ExpressionType.LessThan),
            ComparisonOperator.LessThanOrEqual => BuildComparison(member, propertyType, rule.Value, ExpressionType.LessThanOrEqual),
            ComparisonOperator.Between => BuildBetween(member, propertyType, rule.Value, rule.UpperValue),
            ComparisonOperator.Contains => BuildStringMethod(member, propertyType, rule.Value, nameof(string.Contains)),
            ComparisonOperator.NotContains => Expression.Not(BuildStringMethod(member, propertyType, rule.Value, nameof(string.Contains))),
            ComparisonOperator.StartsWith => BuildStringMethod(member, propertyType, rule.Value, nameof(string.StartsWith)),
            ComparisonOperator.EndsWith => BuildStringMethod(member, propertyType, rule.Value, nameof(string.EndsWith)),
            ComparisonOperator.Like => BuildLike(member, propertyType, rule.Value),
            ComparisonOperator.In => BuildIn(member, propertyType, rule.Value),
            ComparisonOperator.NotIn => Expression.Not(BuildIn(member, propertyType, rule.Value)),
            // An operator this converter does not know is match-nothing, not a `false` a negation could invert.
            _ => null
        };

        if (body is null)
            return null;

        if (rule.IsNegated != negate)
            body = Expression.Not(body);

        // Wrap with null guards for nested properties (e.g., Address.City). Applied after the negation, so a null
        // intermediate never matches — negated or not.
        return WrapWithNullGuards(param, rule.Field, body);
    }

    private static Expression? BuildGroupExpression(RuleGroup group, ParameterExpression param, Type entityType, bool negate)
    {
        var negated = group.IsNegated != negate;
        // NOT (A AND B) is NOT A OR NOT B: under negation the group's connective flips.
        var conjunction = (group.Logic == LogicOperator.And) != negated;

        // Every enabled child is built before any is discarded, so each one's own errors surface.
        var built = group.Rules
            .Where(r => r.IsEnabled)
            .Select(r => BuildExpression(r, param, entityType, negated))
            .ToList();

        // A AND <nothing> matches nothing; A OR <nothing> is A.
        if (conjunction && built.Any(e => e is null))
            return null;

        var children = built.Where(e => e is not null).Select(e => e!).ToList();
        if (children.Count == 0)
            return null;

        return children.Aggregate((a, b) => conjunction ? Expression.AndAlso(a, b) : Expression.OrElse(a, b));
    }

    // ── Property resolution (supports nested: "Address.City") ──

    // ConcurrentDictionary: RuleExpressionConverter is a static class whose ToExpression entry points
    // can be called concurrently; a plain Dictionary read+written here without synchronization is a
    // documented corruption hazard. Mirrors ObjectRuleContext.PropertyCache.
    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> PropertyCache = new();

    private static PropertyInfo? ResolveProperty(Type type, string field)
    {
        return PropertyCache.GetOrAdd((type, field), static key =>
        {
            var parts = key.Item2.Split('.');
            PropertyInfo? prop = null;
            var currentType = key.Item1;

            foreach (var part in parts)
            {
                prop = currentType.GetProperty(part, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (prop is null)
                    return null;
                currentType = prop.PropertyType;
            }

            return prop;
        });
    }

    private static MemberExpression BuildMemberAccess(ParameterExpression param, string field)
    {
        var parts = field.Split('.');
        Expression current = param;
        foreach (var part in parts)
        {
            var prop = current.Type.GetProperty(part, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)!;
            current = Expression.Property(current, prop);
        }
        return (MemberExpression)current;
    }

    /// <summary>
    /// For nested properties (e.g., "Address.City"), builds null checks for intermediate members:
    /// x.Address != null &amp;&amp; (inner expression on x.Address.City).
    /// Returns the inner expression wrapped with null guards, or just the inner expression if not nested.
    /// </summary>
    private static Expression WrapWithNullGuards(ParameterExpression param, string field, Expression innerExpression)
    {
        var parts = field.Split('.');
        if (parts.Length <= 1)
            return innerExpression;

        // Build null checks for all intermediate (non-leaf) segments
        Expression? guard = null;
        Expression current = param;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            var prop = current.Type.GetProperty(parts[i], BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)!;
            current = Expression.Property(current, prop);

            if (!current.Type.IsValueType || Nullable.GetUnderlyingType(current.Type) is not null)
            {
                var notNull = Expression.NotEqual(current, Expression.Constant(null, current.Type));
                guard = guard is null ? notNull : Expression.AndAlso(guard, notNull);
            }
        }

        if (guard is null)
            return innerExpression;

        return Expression.AndAlso(guard, innerExpression);
    }

    private static Type GetMemberType(MemberExpression member)
    {
        return member.Type;
    }

    // ── Expression builders ──

    private static Expression BuildIsNull(MemberExpression member, Type propertyType)
    {
        if (IsNullableType(propertyType))
            return Expression.Equal(member, Expression.Constant(null, propertyType));

        // Non-nullable value type is never null
        if (propertyType.IsValueType)
            return Expression.Constant(false);

        return Expression.Equal(member, Expression.Constant(null, propertyType));
    }

    private static Expression BuildComparison(MemberExpression member, Type propertyType, object? value, ExpressionType comparison)
    {
        var convertedValue = ConvertValue(value, propertyType);
        var constant = Expression.Constant(convertedValue, propertyType);

        // For string equality, use case-insensitive comparison
        if (propertyType == typeof(string) && (comparison == ExpressionType.Equal || comparison == ExpressionType.NotEqual))
        {
            var compareMethod = typeof(string).GetMethod(nameof(string.Equals), [typeof(string), typeof(string), typeof(StringComparison)])!;
            var call = Expression.Call(compareMethod, member, constant, Expression.Constant(StringComparison.OrdinalIgnoreCase));

            return comparison == ExpressionType.NotEqual
                ? Expression.Not(call)
                : (Expression)call;
        }

        // For nullable types, compare the value directly
        return Expression.MakeBinary(comparison, member, constant);
    }

    private static Expression BuildBetween(MemberExpression member, Type propertyType, object? lower, object? upper)
    {
        var lowerConst = Expression.Constant(ConvertValue(lower, propertyType), propertyType);
        var upperConst = Expression.Constant(ConvertValue(upper, propertyType), propertyType);

        return Expression.AndAlso(
            Expression.GreaterThanOrEqual(member, lowerConst),
            Expression.LessThanOrEqual(member, upperConst));
    }

    private static Expression BuildStringMethod(MemberExpression member, Type propertyType, object? value, string methodName)
    {
        if (propertyType != typeof(string))
            throw new InvalidOperationException($"String operator '{methodName}' cannot be applied to property of type '{propertyType.Name}'.");

        var strValue = value?.ToString() ?? string.Empty;
        var method = typeof(string).GetMethod(methodName, [typeof(string), typeof(StringComparison)])!;

        // Guard against null: (x.Prop != null && x.Prop.Contains(...))
        var notNull = Expression.NotEqual(member, Expression.Constant(null, typeof(string)));
        var call = Expression.Call(member, method, Expression.Constant(strValue), Expression.Constant(StringComparison.OrdinalIgnoreCase));

        return Expression.AndAlso(notNull, call);
    }

    private static Expression BuildLike(MemberExpression member, Type propertyType, object? value)
    {
        if (propertyType != typeof(string))
            throw new InvalidOperationException($"Like operator cannot be applied to property of type '{propertyType.Name}'.");

        var pattern = value?.ToString() ?? string.Empty;

        // SQL LIKE-style: %text%, %text, text%
        if (pattern.StartsWith('%') && pattern.EndsWith('%') && pattern.Length > 1)
            return BuildStringMethod(member, propertyType, pattern[1..^1], nameof(string.Contains));
        if (pattern.StartsWith('%'))
            return BuildStringMethod(member, propertyType, pattern[1..], nameof(string.EndsWith));
        if (pattern.EndsWith('%'))
            return BuildStringMethod(member, propertyType, pattern[..^1], nameof(string.StartsWith));

        // No wildcards — exact match (case-insensitive)
        return BuildComparison(member, propertyType, pattern, ExpressionType.Equal);
    }

    private static Expression BuildIn(MemberExpression member, Type propertyType, object? value)
    {
        var values = new List<object?>();

        if (value is IEnumerable enumerable and not string)
        {
            foreach (var item in enumerable)
                values.Add(item);
        }
        else if (value is not null)
        {
            values.Add(value);
        }

        if (values.Count == 0)
            return Expression.Constant(false);

        // Build: x.Prop == v1 || x.Prop == v2 || ...
        Expression? combined = null;
        foreach (var v in values)
        {
            var converted = ConvertValue(v, propertyType);
            var constant = Expression.Constant(converted, propertyType);
            var eq = Expression.Equal(member, constant);
            combined = combined is null ? eq : Expression.OrElse(combined, eq);
        }

        return combined!;
    }

    // ── Value conversion ──

    private static object? ConvertValue(object? value, Type targetType)
    {
        if (value is null)
            return null;

        var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlying.IsInstanceOfType(value))
            return value;

        if (underlying == typeof(Guid) && value is string guidStr)
            return Guid.Parse(guidStr);

        if (underlying == typeof(DateTime) && value is string dateStr)
            return DateTime.Parse(dateStr, CultureInfo.InvariantCulture);

        if (underlying == typeof(DateTimeOffset) && value is string dtoStr)
            return DateTimeOffset.Parse(dtoStr, CultureInfo.InvariantCulture);

        if (underlying.IsEnum)
        {
            if (value is string enumStr)
                return Enum.Parse(underlying, enumStr, ignoreCase: true);
            return Enum.ToObject(underlying, value);
        }

        return Convert.ChangeType(value, underlying, CultureInfo.InvariantCulture);
    }

    private static bool IsNullableType(Type type)
    {
        return !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
    }
}
