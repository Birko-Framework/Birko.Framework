using System;
using System.Linq.Expressions;

namespace Birko.Data.Patterns.Specification;

/// <summary>
/// Defines a reusable, composable business rule that can be evaluated against an entity
/// and converted to an expression for store-level filtering.
/// </summary>
/// <typeparam name="T">The type of entity to evaluate.</typeparam>
public interface ISpecification<T>
{
    /// <summary>
    /// Evaluates this specification against an entity in memory.
    /// </summary>
    /// <param name="entity">The entity to evaluate.</param>
    /// <returns>True if the entity satisfies this specification.</returns>
    bool IsSatisfiedBy(T entity);

    /// <summary>
    /// Returns a LINQ expression representing this specification,
    /// suitable for passing to store Read/Count methods.
    /// </summary>
    Expression<Func<T, bool>> ToExpression();
}
