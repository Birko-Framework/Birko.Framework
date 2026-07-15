using System;
using System.Linq.Expressions;
using Birko.Data.Expressions;

namespace Birko.Models.Users.Filters
{
    /// <summary>
    /// CR-L323: shared AND-combining helper for the filter classes, replacing the identical per-class
    /// private static Combine wrapper that all 8 filters duplicated. Use via
    /// <c>using static Birko.Models.Users.Filters.FilterExpressions;</c> so call sites stay unchanged.
    /// </summary>
    internal static class FilterExpressions
    {
        public static Expression<Func<T, bool>> Combine<T>(
            Expression<Func<T, bool>>? left,
            Expression<Func<T, bool>> right)
            => ExpressionParameterReplacer.AndAlso(left, right);
    }
}
