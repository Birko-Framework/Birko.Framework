using System;
using System.Linq.Expressions;

namespace Birko.Data.Patterns.Specification;

/// <summary>
/// Negates a specification.
/// The result is true when the inner specification is not satisfied.
/// </summary>
/// <typeparam name="T">The type of entity to evaluate.</typeparam>
public sealed class NotSpecification<T> : Specification<T>
{
    private readonly ISpecification<T> _inner;

    public NotSpecification(ISpecification<T> inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    }

    public override Expression<Func<T, bool>> ToExpression()
    {
        var innerExpr = _inner.ToExpression();
        var negated = Expression.Not(innerExpr.Body);

        return Expression.Lambda<Func<T, bool>>(negated, innerExpr.Parameters);
    }
}
