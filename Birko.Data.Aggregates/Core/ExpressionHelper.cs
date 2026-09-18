using System;
using System.Linq.Expressions;
using System.Reflection;

namespace Birko.Data.Aggregates.Core;

/// <summary>
/// Helper for extracting property names from lambda expressions.
/// </summary>
internal static class ExpressionHelper
{
    /// <summary>
    /// Extracts the property name from a member access expression.
    /// </summary>
    public static string GetPropertyName<T, TProp>(Expression<Func<T, TProp>> expression)
    {
        var member = GetMemberExpression(expression.Body);
        if (member == null)
            throw new ArgumentException($"Expression '{expression}' does not refer to a property.", nameof(expression));

        return member.Member.Name;
    }

    private static MemberExpression? GetMemberExpression(Expression expression)
    {
        return expression switch
        {
            MemberExpression me when me.Member is PropertyInfo => me,
            UnaryExpression { Operand: MemberExpression me } when me.Member is PropertyInfo => me,
            _ => null
        };
    }
}
