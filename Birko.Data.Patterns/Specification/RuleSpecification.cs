using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Birko.Rules;

namespace Birko.Data.Patterns.Specification;

/// <summary>
/// Adapts a Birko.Rules rule tree into the Specification pattern.
/// Supports in-memory evaluation via ObjectRuleContext and LINQ expression generation for store queries.
/// </summary>
/// <typeparam name="T">The entity type to evaluate.</typeparam>
public class RuleSpecification<T> : Specification<T> where T : class
{
    private readonly IRule _rule;
    private readonly IRuleEvaluator _evaluator;

    public RuleSpecification(IRule rule, IRuleEvaluator? evaluator = null)
    {
        _rule = rule;
        _evaluator = evaluator ?? new RuleEvaluator();
    }

    public RuleSpecification(RuleSet ruleSet, IRuleEvaluator? evaluator = null)
        : this(WrapRuleSet(ruleSet), evaluator)
    {
    }

    public override Expression<Func<T, bool>> ToExpression()
    {
        var param = Expression.Parameter(typeof(T), "x");
        var body = BuildExpression(_rule, param);
        return Expression.Lambda<Func<T, bool>>(body, param);
    }

    /// <summary>
    /// Overrides the base compiled-expression evaluation to use the RuleEvaluator for in-memory
    /// evaluation (more accurate than the compiled expression for complex types). Must be an
    /// override (not `new`) so callers holding an ISpecification&lt;T&gt;/Specification&lt;T&gt;
    /// reference — including And/Or/Not combinators and collections — dispatch here (CR-H074).
    /// </summary>
    public override bool IsSatisfiedBy(T entity)
    {
        var context = new ObjectRuleContext<T>(entity);
        var result = _evaluator.Evaluate(_rule, context);
        return result.IsMatch;
    }

    private static IRule WrapRuleSet(RuleSet ruleSet)
    {
        if (!ruleSet.IsEnabled)
            return new RuleGroup(LogicOperator.And, new List<IRule>());

        return new RuleGroup(LogicOperator.And, ruleSet.Rules) { IsEnabled = ruleSet.IsEnabled };
    }

    private static Expression BuildExpression(IRule rule, ParameterExpression param)
    {
        if (!rule.IsEnabled)
            return Expression.Constant(true);

        return rule switch
        {
            Rules.Rule leaf => BuildLeafExpression(leaf, param),
            RuleGroup group => BuildGroupExpression(group, param),
            _ => Expression.Constant(false)
        };
    }

    private static Expression BuildLeafExpression(Rules.Rule rule, ParameterExpression param)
    {
        var property = typeof(T).GetProperty(rule.Field,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);

        if (property is null)
            return Expression.Constant(false);

        var member = Expression.Property(param, property);

        Expression expr = rule.Operator switch
        {
            ComparisonOperator.IsNull => BuildNullCheck(member, isNull: true),
            ComparisonOperator.IsNotNull => BuildNullCheck(member, isNull: false),
            ComparisonOperator.Equal => BuildComparison(member, rule.Value, Expression.Equal),
            ComparisonOperator.NotEqual => BuildComparison(member, rule.Value, Expression.NotEqual),
            ComparisonOperator.GreaterThan => BuildComparison(member, rule.Value, Expression.GreaterThan),
            ComparisonOperator.GreaterThanOrEqual => BuildComparison(member, rule.Value, Expression.GreaterThanOrEqual),
            ComparisonOperator.LessThan => BuildComparison(member, rule.Value, Expression.LessThan),
            ComparisonOperator.LessThanOrEqual => BuildComparison(member, rule.Value, Expression.LessThanOrEqual),
            ComparisonOperator.Between => BuildBetween(member, rule.Value, rule.UpperValue),
            ComparisonOperator.Contains => BuildStringMethod(member, rule.Value, "Contains"),
            ComparisonOperator.NotContains => Expression.Not(BuildStringMethod(member, rule.Value, "Contains")),
            ComparisonOperator.StartsWith => BuildStringMethod(member, rule.Value, "StartsWith"),
            ComparisonOperator.EndsWith => BuildStringMethod(member, rule.Value, "EndsWith"),
            _ => Expression.Constant(true)
        };

        if (rule.IsNegated)
            expr = Expression.Not(expr);

        return expr;
    }

    private static Expression BuildNullCheck(MemberExpression member, bool isNull)
    {
        var nullExpr = Expression.Constant(null, member.Type);
        return isNull
            ? Expression.Equal(member, nullExpr)
            : Expression.NotEqual(member, nullExpr);
    }

    private static Expression BuildComparison(
        MemberExpression member,
        object? value,
        Func<Expression, Expression, BinaryExpression> comparison)
    {
        // A value that cannot be converted to the member type (null against a non-nullable
        // value type, or a non-convertible/mistyped value) makes the leaf unsatisfiable rather
        // than throwing when the compiled delegate runs in-memory or the provider translates it.
        if (!TryConvertConstant(value, member.Type, out var constant))
            return Expression.Constant(false);
        return comparison(member, constant);
    }

    private static Expression BuildBetween(MemberExpression member, object? lower, object? upper)
    {
        if (!TryConvertConstant(lower, member.Type, out var lowerConst) ||
            !TryConvertConstant(upper, member.Type, out var upperConst))
            return Expression.Constant(false);
        return Expression.AndAlso(
            Expression.GreaterThanOrEqual(member, lowerConst),
            Expression.LessThanOrEqual(member, upperConst)
        );
    }

    private static Expression BuildStringMethod(MemberExpression member, object? value, string methodName)
    {
        // String methods only apply to string members; a non-string field is never a match.
        if (member.Type != typeof(string))
            return Expression.Constant(false);

        var method = typeof(string).GetMethod(methodName, [typeof(string), typeof(StringComparison)])!;
        var valueExpr = Expression.Constant(value?.ToString() ?? string.Empty, typeof(string));
        var comparisonExpr = Expression.Constant(StringComparison.OrdinalIgnoreCase, typeof(StringComparison));
        var call = Expression.Call(member, method, valueExpr, comparisonExpr);

        // Guard against a null string property: null.Contains(...) would NRE when the compiled
        // delegate runs in-memory. A null field is treated as "does not match".
        var notNull = Expression.NotEqual(member, Expression.Constant(null, typeof(string)));
        return Expression.AndAlso(notNull, call);
    }

    /// <summary>
    /// Attempts to build a constant of <paramref name="targetType"/> from <paramref name="value"/>.
    /// Returns false (leaving <paramref name="constant"/> a placeholder) when the value is null
    /// against a non-nullable value type or cannot be converted, so the caller can degrade to an
    /// unsatisfiable leaf instead of throwing.
    /// </summary>
    private static bool TryConvertConstant(object? value, Type targetType, out Expression constant)
    {
        constant = Expression.Constant(false);
        try
        {
            object? converted;
            if (value is null)
            {
                // null is only representable for reference types and Nullable<T>.
                if (targetType.IsValueType && Nullable.GetUnderlyingType(targetType) is null)
                    return false;
                converted = null;
            }
            else if (targetType.IsInstanceOfType(value))
            {
                // Already the right type (covers enums and exact matches ChangeType can't handle).
                converted = value;
            }
            else
            {
                var underlying = Nullable.GetUnderlyingType(targetType) ?? targetType;
                converted = Convert.ChangeType(value, underlying);
            }

            constant = Expression.Constant(converted, targetType);
            return true;
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException or ArgumentException)
        {
            return false;
        }
    }

    private static Expression BuildGroupExpression(RuleGroup group, ParameterExpression param)
    {
        var enabledRules = group.Rules.Where(r => r.IsEnabled).ToList();
        if (enabledRules.Count == 0)
            return Expression.Constant(false);

        Expression combined = BuildExpression(enabledRules[0], param);

        for (int i = 1; i < enabledRules.Count; i++)
        {
            var next = BuildExpression(enabledRules[i], param);
            combined = group.Logic == LogicOperator.And
                ? Expression.AndAlso(combined, next)
                : Expression.OrElse(combined, next);
        }

        return combined;
    }
}
