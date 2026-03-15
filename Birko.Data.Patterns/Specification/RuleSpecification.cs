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
    /// Override to use RuleEvaluator for in-memory evaluation (more accurate than compiled expression for complex types).
    /// </summary>
    public new bool IsSatisfiedBy(T entity)
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
        var memberAsObject = Expression.Convert(member, typeof(object));

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
        var constant = Expression.Constant(Convert.ChangeType(value, member.Type), member.Type);
        return comparison(member, constant);
    }

    private static Expression BuildBetween(MemberExpression member, object? lower, object? upper)
    {
        var lowerConst = Expression.Constant(Convert.ChangeType(lower, member.Type), member.Type);
        var upperConst = Expression.Constant(Convert.ChangeType(upper, member.Type), member.Type);
        return Expression.AndAlso(
            Expression.GreaterThanOrEqual(member, lowerConst),
            Expression.LessThanOrEqual(member, upperConst)
        );
    }

    private static Expression BuildStringMethod(MemberExpression member, object? value, string methodName)
    {
        var method = typeof(string).GetMethod(methodName, [typeof(string), typeof(StringComparison)])!;
        var valueExpr = Expression.Constant(value?.ToString() ?? string.Empty, typeof(string));
        var comparisonExpr = Expression.Constant(StringComparison.OrdinalIgnoreCase, typeof(StringComparison));
        return Expression.Call(member, method, valueExpr, comparisonExpr);
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
