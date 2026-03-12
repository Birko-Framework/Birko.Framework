using System;
using System.Linq.Expressions;

namespace Birko.Data.Patterns.Specification;

/// <summary>
/// Combines two specifications with logical OR.
/// At least one specification must be satisfied for the result to be true.
/// </summary>
/// <typeparam name="T">The type of entity to evaluate.</typeparam>
public sealed class OrSpecification<T> : Specification<T>
{
    private readonly ISpecification<T> _left;
    private readonly ISpecification<T> _right;

    public OrSpecification(ISpecification<T> left, ISpecification<T> right)
    {
        _left = left ?? throw new ArgumentNullException(nameof(left));
        _right = right ?? throw new ArgumentNullException(nameof(right));
    }

    public override Expression<Func<T, bool>> ToExpression()
    {
        var leftExpr = _left.ToExpression();
        var rightExpr = _right.ToExpression();

        var parameter = Expression.Parameter(typeof(T), "x");
        var combined = Expression.OrElse(
            Expression.Invoke(leftExpr, parameter),
            Expression.Invoke(rightExpr, parameter));

        return Expression.Lambda<Func<T, bool>>(combined, parameter);
    }
}
