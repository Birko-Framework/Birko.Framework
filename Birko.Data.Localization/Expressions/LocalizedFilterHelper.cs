using Birko.Data.Localization.Models;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Birko.Data.Localization.Expressions;

/// <summary>
/// Shared helpers for building GUID-based filters and combining expressions.
/// Used by all localized store wrappers.
/// </summary>
public static class LocalizedFilterHelper
{
    /// <summary>
    /// Builds a filter expression that matches entities whose Guid is in the given set.
    /// Returns x => false if the set is empty.
    /// </summary>
    public static Expression<Func<T, bool>> BuildGuidFilter<T>(HashSet<Guid> guids)
        where T : Data.Models.AbstractModel, ILocalizable
    {
        var param = Expression.Parameter(typeof(T), "x");
        var guidProp = Expression.Property(param, nameof(Data.Models.AbstractModel.Guid));

        if (guids.Count == 0)
        {
            return Expression.Lambda<Func<T, bool>>(Expression.Constant(false), param);
        }

        // Build: x.Guid != null && guids.Contains(x.Guid.Value)
        var guidValue = Expression.Property(guidProp, "Value");
        var guidsConstant = Expression.Constant(guids);
        var containsMethod = typeof(HashSet<Guid>).GetMethod("Contains", new[] { typeof(Guid) })!;
        var containsCall = Expression.Call(guidsConstant, containsMethod, guidValue);
        var nullCheck = Expression.NotEqual(guidProp, Expression.Constant(null, typeof(Guid?)));
        var combined = Expression.AndAlso(nullCheck, containsCall);

        return Expression.Lambda<Func<T, bool>>(combined, param);
    }

    /// <summary>
    /// Combines two filter expressions with AndAlso, rebinding the right expression's parameter.
    /// </summary>
    public static Expression<Func<T, bool>> CombineFilters<T>(
        Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
        where T : Data.Models.AbstractModel, ILocalizable
    {
        var param = left.Parameters[0];
        var rightBody = new ParameterReplacer(right.Parameters[0], param).Visit(right.Body);
        var combined = Expression.AndAlso(left.Body, rightBody);
        return Expression.Lambda<Func<T, bool>>(combined, param);
    }
}

/// <summary>
/// Replaces a parameter expression with another in an expression tree.
/// Used to combine two lambda expressions with different parameters.
/// </summary>
internal class ParameterReplacer : ExpressionVisitor
{
    private readonly ParameterExpression _oldParam;
    private readonly ParameterExpression _newParam;

    public ParameterReplacer(ParameterExpression oldParam, ParameterExpression newParam)
    {
        _oldParam = oldParam;
        _newParam = newParam;
    }

    protected override Expression VisitParameter(ParameterExpression node)
        => node == _oldParam ? _newParam : base.VisitParameter(node);
}
