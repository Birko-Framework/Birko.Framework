using System;
using System.Linq.Expressions;

namespace Birko.Data.Expressions;

/// <summary>
/// Replaces all occurrences of one ParameterExpression with another in an expression tree.
/// Use this instead of Expression.Invoke when combining lambda expressions for SQL-compatible
/// expression trees (Expression.Invoke creates InvocationExpression nodes that Birko's
/// ParseConditionExpression cannot resolve into proper SQL conditions).
/// </summary>
public sealed class ExpressionParameterReplacer : ExpressionVisitor
{
    private readonly ParameterExpression _oldParam;
    private readonly ParameterExpression _newParam;

    public ExpressionParameterReplacer(ParameterExpression oldParam, ParameterExpression newParam)
    {
        _oldParam = oldParam;
        _newParam = newParam;
    }

    protected override Expression VisitParameter(ParameterExpression node)
        => node == _oldParam ? _newParam : base.VisitParameter(node);

    /// <summary>
    /// Combines two lambda expressions with AndAlso, rewriting parameters to share a single parameter.
    /// Returns <paramref name="right"/> unchanged if <paramref name="left"/> is null.
    /// </summary>
    public static Expression<Func<T, bool>> AndAlso<T>(
        Expression<Func<T, bool>>? left,
        Expression<Func<T, bool>> right)
    {
        if (left == null) return right;
        var parameter = left.Parameters[0];
        var rewrittenRight = new ExpressionParameterReplacer(right.Parameters[0], parameter).Visit(right.Body);
        return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(left.Body, rewrittenRight), parameter);
    }

    /// <summary>
    /// Combines two lambda expressions with OrElse, rewriting parameters to share a single parameter.
    /// Returns <paramref name="right"/> unchanged if <paramref name="left"/> is null.
    /// </summary>
    public static Expression<Func<T, bool>> OrElse<T>(
        Expression<Func<T, bool>>? left,
        Expression<Func<T, bool>> right)
    {
        if (left == null) return right;
        var parameter = left.Parameters[0];
        var rewrittenRight = new ExpressionParameterReplacer(right.Parameters[0], parameter).Visit(right.Body);
        return Expression.Lambda<Func<T, bool>>(Expression.OrElse(left.Body, rewrittenRight), parameter);
    }
}
