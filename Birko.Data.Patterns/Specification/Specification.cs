using System;
using System.Linq.Expressions;

namespace Birko.Data.Patterns.Specification;

/// <summary>
/// Base class for specifications. Subclass this to define reusable business rules.
/// </summary>
/// <typeparam name="T">The type of entity to evaluate.</typeparam>
public abstract class Specification<T> : ISpecification<T>
{
    private Func<T, bool>? _compiledExpression;

    /// <summary>
    /// Returns the LINQ expression for this specification.
    /// Subclasses must implement this to define their rule.
    /// </summary>
    public abstract Expression<Func<T, bool>> ToExpression();

    /// <inheritdoc />
    public bool IsSatisfiedBy(T entity)
    {
        _compiledExpression ??= ToExpression().Compile();
        return _compiledExpression(entity);
    }

    /// <summary>
    /// Combines this specification with another using logical AND.
    /// </summary>
    public ISpecification<T> And(ISpecification<T> other)
        => new AndSpecification<T>(this, other);

    /// <summary>
    /// Combines this specification with another using logical OR.
    /// </summary>
    public ISpecification<T> Or(ISpecification<T> other)
        => new OrSpecification<T>(this, other);

    /// <summary>
    /// Negates this specification.
    /// </summary>
    public ISpecification<T> Not()
        => new NotSpecification<T>(this);

    public static ISpecification<T> operator &(Specification<T> left, Specification<T> right)
        => left.And(right);

    public static ISpecification<T> operator |(Specification<T> left, Specification<T> right)
        => left.Or(right);

    public static ISpecification<T> operator !(Specification<T> spec)
        => spec.Not();
}
