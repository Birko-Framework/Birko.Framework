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

        if (filter == null)
        {
            return notDeleted;
        }

        var parameter = filter.Parameters[0];
        var body = Expression.AndAlso(
            filter.Body,
            Expression.Invoke(notDeleted, parameter)
        );
        return Expression.Lambda<Func<T, bool>>(body, parameter);
    }
}
