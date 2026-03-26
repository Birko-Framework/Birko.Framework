using System;
using System.Linq.Expressions;
using Birko.Data.Expressions;

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
        return ExpressionParameterReplacer.OrElse(_left.ToExpression(), _right.ToExpression());
    }
}
