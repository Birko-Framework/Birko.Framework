using Birko.Data.Expressions;
using Birko.Data.Patterns.Models;
using System;
using System.Linq.Expressions;

namespace Birko.Data.Patterns.Decorators;

/// <summary>
/// Shared filter logic for soft-delete decorators.
/// </summary>
public static class SoftDeleteFilter
{
    /// <summary>
    /// Combines a user filter with a DeletedAt == null condition.
    /// </summary>
    public static Expression<Func<T, bool>> CombineWithNotDeleted<T>(Expression<Func<T, bool>>? filter)
        where T : ISoftDeletable
    {
        Expression<Func<T, bool>> notDeleted = x => x.DeletedAt == null;
        return ExpressionParameterReplacer.AndAlso(filter, notDeleted);
    }
}
