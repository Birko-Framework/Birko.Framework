using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace Birko.Data.Stores
{
    /// <summary>
    /// Describes a set of property assignments to apply to entities matching a filter.
    /// Platforms can translate these to native operations (SQL SET, MongoDB $set, etc.)
    /// instead of the read-modify-save pattern.
    /// </summary>
    /// <typeparam name="T">The type of entity, must inherit from <see cref="Models.AbstractModel"/>.</typeparam>
    public class PropertyUpdate<T> where T : Models.AbstractModel
    {
        internal List<(LambdaExpression Property, object? Value)> Assignments { get; } = new();

        /// <summary>
        /// Sets a property to the specified value.
        /// </summary>
        /// <typeparam name="TProperty">The type of the property.</typeparam>
        /// <param name="property">Expression selecting the property to update.</param>
        /// <param name="value">The new value for the property.</param>
        /// <returns>This instance for fluent chaining.</returns>
        public PropertyUpdate<T> Set<TProperty>(Expression<Func<T, TProperty>> property, TProperty value)
        {
            Assignments.Add((property, value));
            return this;
        }

        /// <summary>
        /// Applies the property assignments to an entity instance using reflection.
        /// Used as a fallback when native platform translation is not available.
        /// </summary>
        /// <param name="entity">The entity to apply assignments to.</param>
        internal void ApplyTo(T entity)
        {
            foreach (var (property, value) in Assignments)
            {
                var memberExpr = property.Body is UnaryExpression unary
                    ? (MemberExpression)unary.Operand
                    : (MemberExpression)property.Body;

                var prop = memberExpr.Member as PropertyInfo;
                prop?.SetValue(entity, value);
            }
        }
    }
}
